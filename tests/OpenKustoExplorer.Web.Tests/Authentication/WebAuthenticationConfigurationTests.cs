using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenKustoExplorer.Web.Authentication;

namespace OpenKustoExplorer.Web.Tests.Authentication;

/// <summary>
/// Verifies Web authentication mode selection and validation.
/// </summary>
public sealed class WebAuthenticationConfigurationTests
{
    /// <summary>
    /// Verifies local authentication needs no confidential-client settings in Development.
    /// </summary>
    [Fact]
    public void LocalDevelopmentNeedsNoApplicationCredential()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["WebAuthentication:Mode"] = "LocalDevelopment",
        });

        bool useLocalDevelopment = WebAuthenticationConfiguration.UseLocalDevelopment(
            configuration,
            new TestHostEnvironment(Environments.Development));

        Assert.True(useLocalDevelopment);
    }

    /// <summary>
    /// Verifies local authentication cannot be enabled in a deployed environment.
    /// </summary>
    [Fact]
    public void LocalDevelopmentIsRejectedOutsideDevelopment()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["WebAuthentication:Mode"] = "LocalDevelopment",
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WebAuthenticationConfiguration.UseLocalDevelopment(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("Development environment", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies managed-identity federation accepts identifiers without a secret or certificate.
    /// </summary>
    [Fact]
    public void FederatedManagedIdentityNeedsNoStoredCredential()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["WebAuthentication:Mode"] = "FederatedManagedIdentity",
            ["AzureAd:TenantId"] = "tenant-id",
            ["AzureAd:ClientId"] = "application-client-id",
            ["AzureAd:ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity",
            ["AzureAd:ClientCredentials:0:ManagedIdentityClientId"] = "managed-identity-client-id",
        });

        bool useLocalDevelopment = WebAuthenticationConfiguration.UseLocalDevelopment(
            configuration,
            new TestHostEnvironment(Environments.Production));

        Assert.False(useLocalDevelopment);
        Assert.Null(configuration["AzureAd:ClientSecret"]);
    }

    /// <summary>
    /// Verifies a missing managed identity is rejected during startup validation.
    /// </summary>
    [Fact]
    public void FederatedManagedIdentityRequiresManagedIdentityClientId()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["WebAuthentication:Mode"] = "FederatedManagedIdentity",
            ["AzureAd:TenantId"] = "tenant-id",
            ["AzureAd:ClientId"] = "application-client-id",
            ["AzureAd:ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity",
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WebAuthenticationConfiguration.UseLocalDevelopment(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("ManagedIdentityClientId", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a legacy client secret cannot override managed-identity federation.
    /// </summary>
    [Fact]
    public void FederatedManagedIdentityRejectsClientSecret()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["WebAuthentication:Mode"] = "FederatedManagedIdentity",
            ["AzureAd:TenantId"] = "tenant-id",
            ["AzureAd:ClientId"] = "application-client-id",
            ["AzureAd:ClientSecret"] = "legacy-secret",
            ["AzureAd:ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity",
            ["AzureAd:ClientCredentials:0:ManagedIdentityClientId"] = "managed-identity-client-id",
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            WebAuthenticationConfiguration.UseLocalDevelopment(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("does not permit", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        internal TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string ApplicationName { get; set; } = "OpenKustoExplorer.Web.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; }
    }
}
