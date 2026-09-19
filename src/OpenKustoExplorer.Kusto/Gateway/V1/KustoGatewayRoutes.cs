namespace OpenKustoExplorer.Kusto.Gateway.V1;

/// <summary>
/// Defines the version-one same-origin Kusto gateway surface.
/// </summary>
public static class KustoGatewayRoutes
{
    /// <summary>
    /// Gets the request header carrying the antiforgery token.
    /// </summary>
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    /// <summary>
    /// Gets the form field carrying the antiforgery token.
    /// </summary>
    public const string AntiforgeryFormFieldName = "__RequestVerificationToken";

    /// <summary>
    /// Gets the largest accepted gateway request body in bytes.
    /// </summary>
    public const int MaximumRequestByteCount = 1_048_576;

    /// <summary>
    /// Gets the largest accepted gateway response body in bytes.
    /// </summary>
    public const int MaximumResponseByteCount = 67_108_864;

    /// <summary>
    /// Gets the maximum number of prior Copilot turns accepted from browser-local history.
    /// </summary>
    public const int MaximumCopilotHistoryTurnCount = 10;

    /// <summary>
    /// Gets the maximum character count accepted for one Copilot context field.
    /// </summary>
    public const int MaximumCopilotContextCharacterCount = 262_144;

    /// <summary>
    /// Gets the maximum character count accepted for explicitly shared local data.
    /// </summary>
    public const int MaximumCopilotSharedDataCharacterCount = 65_536;

    /// <summary>
    /// Gets the Copilot completion endpoint.
    /// </summary>
    public const string Copilot = "/api/v1/copilot";

    /// <summary>
    /// Gets the configured Copilot model endpoint.
    /// </summary>
    public const string CopilotModels = "/api/v1/copilot/models";

    /// <summary>
    /// Gets the database catalog endpoint.
    /// </summary>
    public const string Databases = "/api/v1/kusto/databases";

    /// <summary>
    /// Gets the streaming graph export endpoint.
    /// </summary>
    public const string Graph = "/api/v1/kusto/graph";

    /// <summary>
    /// Gets the operation cancellation route prefix.
    /// </summary>
    public const string Operations = "/api/v1/kusto/operations";

    /// <summary>
    /// Gets the query endpoint.
    /// </summary>
    public const string Query = "/api/v1/kusto/query";

    /// <summary>
    /// Gets the database schema endpoint.
    /// </summary>
    public const string Schema = "/api/v1/kusto/schema";

    /// <summary>
    /// Gets the authenticated gateway session endpoint.
    /// </summary>
    public const string Session = "/api/v1/kusto/session";

    /// <summary>
    /// Gets the top-level Web sign-out endpoint.
    /// </summary>
    public const string SignOut = "/auth/signout";

    /// <summary>
    /// Gets the current wire-protocol version.
    /// </summary>
    public const int Version = 1;
}
