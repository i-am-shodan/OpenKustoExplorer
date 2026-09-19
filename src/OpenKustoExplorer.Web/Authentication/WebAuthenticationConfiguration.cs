namespace OpenKustoExplorer.Web.Authentication;

/// <summary>
/// Selects and validates the Web host authentication mode.
/// </summary>
internal static class WebAuthenticationConfiguration
{
    /// <summary>
    /// Gets the managed-identity federation mode name.
    /// </summary>
    internal const string FederatedManagedIdentityMode = "FederatedManagedIdentity";

    /// <summary>
    /// Gets the loopback-only local development mode name.
    /// </summary>
    internal const string LocalDevelopmentMode = "LocalDevelopment";

    /// <summary>
    /// Determines whether the host uses credentialless local development authentication.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns><see langword="true"/> for local development authentication.</returns>
    internal static bool UseLocalDevelopment(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        string mode = configuration["WebAuthentication:Mode"]
            ?? FederatedManagedIdentityMode;
        if (string.Equals(mode, LocalDevelopmentMode, StringComparison.Ordinal))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "LocalDevelopment authentication is permitted only in the Development environment.");
            }

            return true;
        }

        if (!string.Equals(mode, FederatedManagedIdentityMode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"WebAuthentication:Mode must be {LocalDevelopmentMode} or {FederatedManagedIdentityMode}.");
        }

        ValidateFederatedManagedIdentity(configuration);
        return false;
    }

    private static void ValidateFederatedManagedIdentity(IConfiguration configuration)
    {
        RequireValue(configuration, "AzureAd:TenantId");
        RequireValue(configuration, "AzureAd:ClientId");

        if (!string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"])
            || configuration.GetSection("AzureAd:ClientCertificates").GetChildren().Any())
        {
            throw new InvalidOperationException(
                "FederatedManagedIdentity authentication does not permit client secrets or certificates.");
        }

        IConfigurationSection[] credentials = configuration
            .GetSection("AzureAd:ClientCredentials")
            .GetChildren()
            .ToArray();
        if (credentials.Length != 1)
        {
            throw new InvalidOperationException(
                "FederatedManagedIdentity authentication requires exactly one client credential.");
        }

        IConfigurationSection credential = credentials[0];
        string? sourceType = credential["SourceType"];
        if (!string.Equals(
            sourceType,
            "SignedAssertionFromManagedIdentity",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "AzureAd:ClientCredentials:0:SourceType must be SignedAssertionFromManagedIdentity.");
        }

        if (string.IsNullOrWhiteSpace(credential["ManagedIdentityClientId"]))
        {
            throw new InvalidOperationException(
                "The required configuration value 'AzureAd:ClientCredentials:0:ManagedIdentityClientId' is missing.");
        }
    }

    private static void RequireValue(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            throw new InvalidOperationException($"The required configuration value '{key}' is missing.");
        }
    }
}
