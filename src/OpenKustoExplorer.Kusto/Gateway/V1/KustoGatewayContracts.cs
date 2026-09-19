namespace OpenKustoExplorer.Kusto.Gateway.V1;

/// <summary>
/// Contains the version-one Kusto gateway wire contracts.
/// </summary>
public static class KustoGatewayContracts
{
    /// <summary>
    /// Identifies one frame in a streaming graph export response.
    /// </summary>
    public enum KustoGatewayGraphFrameKind
    {
        /// <summary>
        /// Carries a bounded table batch.
        /// </summary>
        Batch,

        /// <summary>
        /// Carries the completed export summary.
        /// </summary>
        Summary,

        /// <summary>
        /// Carries a terminal gateway error.
        /// </summary>
        Error,
    }

    /// <summary>
    /// Requests one stateless Copilot completion using bounded browser-local history.
    /// </summary>
    public sealed class KustoGatewayCopilotRequest
    {
        /// <summary>Gets or initializes the active scope title.</summary>
        public string DocumentTitle { get; init; } = string.Empty;

        /// <summary>Gets or initializes prior browser-local conversation turns.</summary>
        public KustoGatewayCopilotTurn[] History { get; init; } = [];

        /// <summary>Gets or initializes the configured model identifier.</summary>
        public string ModelId { get; init; } = string.Empty;

        /// <summary>Gets or initializes the operation identifier used for cancellation.</summary>
        public Guid OperationId { get; init; }

        /// <summary>Gets or initializes the active KQL or openCypher text.</summary>
        public string QueryText { get; init; } = string.Empty;

        /// <summary>Gets or initializes the current natural-language request.</summary>
        public string Request { get; init; } = string.Empty;

        /// <summary>Gets or initializes the bounded schema or scope metadata.</summary>
        public string SchemaText { get; init; } = string.Empty;

        /// <summary>Gets or initializes explicitly consented browser-local data.</summary>
        public string SharedDataText { get; init; } = string.Empty;

        /// <summary>Gets or initializes the numeric workbench scope kind.</summary>
        public int ScopeKind { get; init; }

        /// <summary>Gets or initializes the concise target description.</summary>
        public string TargetText { get; init; } = string.Empty;

        /// <summary>Gets or initializes the wire-protocol version.</summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Preserves one completed browser-local Copilot turn.
    /// </summary>
    public sealed class KustoGatewayCopilotTurn
    {
        /// <summary>Gets or initializes the canonical assistant response.</summary>
        public string AssistantResponse { get; init; } = string.Empty;

        /// <summary>Gets or initializes the complete prior user prompt.</summary>
        public string UserPrompt { get; init; } = string.Empty;
    }

    /// <summary>
    /// Returns one Copilot completion.
    /// </summary>
    public sealed class KustoGatewayCopilotResponse
    {
        /// <summary>Gets or initializes the user-facing assistant response.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>Gets or initializes an optional complete read-only openCypher proposal.</summary>
        public string? ProposedCypher { get; init; }

        /// <summary>Gets or initializes an optional complete KQL proposal.</summary>
        public string? ProposedQuery { get; init; }

        /// <summary>Gets or initializes the wire-protocol version.</summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Returns the server-configured Copilot provider and models.
    /// </summary>
    public sealed class KustoGatewayCopilotModelsResponse
    {
        /// <summary>Gets or initializes the configured models.</summary>
        public KustoGatewayCopilotModel[] Models { get; init; } = [];

        /// <summary>Gets or initializes the provider display name.</summary>
        public string ProviderDisplayName { get; init; } = string.Empty;

        /// <summary>Gets or initializes the numeric provider kind.</summary>
        public int ProviderKind { get; init; }

        /// <summary>Gets or initializes the wire-protocol version.</summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Describes one server-configured Copilot model.
    /// </summary>
    public sealed class KustoGatewayCopilotModel
    {
        /// <summary>Gets or initializes the model display name.</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Gets or initializes the model identifier.</summary>
        public string Id { get; init; } = string.Empty;
    }

    /// <summary>
    /// Requests one query through the version-one gateway.
    /// </summary>
    public sealed class KustoGatewayQueryRequest
    {
        /// <summary>
        /// Gets or initializes the target cluster URI.
        /// </summary>
        public string ClusterUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the target database name.
        /// </summary>
        public string DatabaseName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the operation identifier used for cancellation.
        /// </summary>
        public Guid OperationId { get; init; }

        /// <summary>
        /// Gets or initializes the query text.
        /// </summary>
        public string QueryText { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Requests accessible databases through the version-one gateway.
    /// </summary>
    public sealed class KustoGatewayDatabasesRequest
    {
        /// <summary>
        /// Gets or initializes the target cluster URI.
        /// </summary>
        public string ClusterUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the operation identifier used for cancellation.
        /// </summary>
        public Guid OperationId { get; init; }

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Requests one database schema through the version-one gateway.
    /// </summary>
    public sealed class KustoGatewaySchemaRequest
    {
        /// <summary>
        /// Gets or initializes the target cluster URI.
        /// </summary>
        public string ClusterUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the target database name.
        /// </summary>
        public string DatabaseName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the operation identifier used for cancellation.
        /// </summary>
        public Guid OperationId { get; init; }

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Returns the current authenticated gateway session.
    /// </summary>
    public sealed class KustoGatewaySessionResponse
    {
        /// <summary>
        /// Gets or initializes the stable account identifier.
        /// </summary>
        public string AccountId { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the account sign-in name.
        /// </summary>
        public string AccountName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the antiforgery request token.
        /// </summary>
        public string AntiforgeryToken { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the account display name.
        /// </summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the opaque partition for browser-local application data.
        /// </summary>
        public string StoragePartition { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Returns databases from the version-one gateway.
    /// </summary>
    public sealed class KustoGatewayDatabasesResponse
    {
        /// <summary>
        /// Gets or initializes the returned databases.
        /// </summary>
        public KustoGatewayDatabase[] Databases { get; init; } = [];

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Describes one database on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayDatabase
    {
        /// <summary>
        /// Gets or initializes the preferred display name.
        /// </summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the database name.
        /// </summary>
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// Returns one query result from the version-one gateway.
    /// </summary>
    public sealed class KustoGatewayQueryResponse
    {
        /// <summary>
        /// Gets or initializes the numeric result-completeness value.
        /// </summary>
        public int Completeness { get; init; }

        /// <summary>
        /// Gets or initializes the total elapsed duration in ticks.
        /// </summary>
        public long DurationTicks { get; init; }

        /// <summary>
        /// Gets or initializes the returned result tables.
        /// </summary>
        public KustoGatewayResultTable[] Tables { get; init; } = [];

        /// <summary>
        /// Gets or initializes optional visualization metadata.
        /// </summary>
        public KustoGatewayVisualization? Visualization { get; init; }

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Carries one ordered frame in a streaming graph export response.
    /// </summary>
    public sealed class KustoGatewayGraphFrame
    {
        /// <summary>
        /// Gets or initializes result columns for a table-start batch.
        /// </summary>
        public KustoGatewayResultColumn[] Columns { get; init; } = [];

        /// <summary>
        /// Gets or initializes the stable gateway error code.
        /// </summary>
        public string? ErrorCode { get; init; }

        /// <summary>
        /// Gets or initializes the terminal gateway error message.
        /// </summary>
        public string? ErrorMessage { get; init; }

        /// <summary>
        /// Gets or initializes the streamed edge-row count for a summary frame.
        /// </summary>
        public long EdgeCount { get; init; }

        /// <summary>
        /// Gets a value indicating whether a batch completes its table.
        /// </summary>
        public bool EndsTable { get; init; }

        /// <summary>
        /// Gets or initializes the frame kind.
        /// </summary>
        public KustoGatewayGraphFrameKind FrameKind { get; init; }

        /// <summary>
        /// Gets or initializes the export duration in ticks for a summary frame.
        /// </summary>
        public long DurationTicks { get; init; }

        /// <summary>
        /// Gets or initializes the streamed node-row count for a summary frame.
        /// </summary>
        public long NodeCount { get; init; }

        /// <summary>
        /// Gets or initializes the bounded Results-view table for a summary frame.
        /// </summary>
        public KustoGatewayResultTable? ResultTable { get; init; }

        /// <summary>
        /// Gets or initializes ordered invariant string rows for a batch frame.
        /// </summary>
        public string[][] Rows { get; init; } = [];

        /// <summary>
        /// Gets a value indicating whether a batch starts its table.
        /// </summary>
        public bool StartsTable { get; init; }

        /// <summary>
        /// Gets or initializes the numeric graph table kind for a batch frame.
        /// </summary>
        public int TableKind { get; init; }

        /// <summary>
        /// Gets or initializes the result-table name for a table-start batch.
        /// </summary>
        public string? TableName { get; init; }

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Describes one result table on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayResultTable
    {
        /// <summary>
        /// Gets or initializes the result columns.
        /// </summary>
        public KustoGatewayResultColumn[] Columns { get; init; } = [];

        /// <summary>
        /// Gets or initializes the result table name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes rows containing values in column order.
        /// </summary>
        public KustoGatewayResultValue[][] Rows { get; init; } = [];
    }

    /// <summary>
    /// Describes one result column on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayResultColumn
    {
        /// <summary>
        /// Gets or initializes the column name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the server-reported type name.
        /// </summary>
        public string TypeName { get; init; } = string.Empty;
    }

    /// <summary>
    /// Preserves one exact result value on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayResultValue
    {
        /// <summary>
        /// Gets or initializes the invariant display text.
        /// </summary>
        public string DisplayText { get; init; } = string.Empty;

        /// <summary>
        /// Gets a value indicating whether the value is null.
        /// </summary>
        public bool IsNull { get; init; }

        /// <summary>
        /// Gets or initializes the optional exact JSON token.
        /// </summary>
        public string? RawJson { get; init; }
    }

    /// <summary>
    /// Preserves query visualization metadata on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayVisualization
    {
        /// <summary>
        /// Gets a value indicating whether values accumulate.
        /// </summary>
        public bool Accumulate { get; init; }

        /// <summary>
        /// Gets or initializes anomaly indicator columns.
        /// </summary>
        public string[] AnomalyColumns { get; init; } = [];

        /// <summary>
        /// Gets or initializes the numeric visualization kind.
        /// </summary>
        public int Kind { get; init; }

        /// <summary>
        /// Gets or initializes the optional visualization kind modifier.
        /// </summary>
        public string? KindOption { get; init; }

        /// <summary>
        /// Gets a value indicating whether the legend is visible.
        /// </summary>
        public bool LegendVisible { get; init; }

        /// <summary>
        /// Gets or initializes columns identifying a series.
        /// </summary>
        public string[] SeriesColumns { get; init; } = [];

        /// <summary>
        /// Gets or initializes the optional title.
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// Gets a value indicating whether the x-axis is logarithmic.
        /// </summary>
        public bool XAxisLogarithmic { get; init; }

        /// <summary>
        /// Gets or initializes the optional x-axis column.
        /// </summary>
        public string? XColumn { get; init; }

        /// <summary>
        /// Gets or initializes the optional x-axis title.
        /// </summary>
        public string? XTitle { get; init; }

        /// <summary>
        /// Gets a value indicating whether the y-axis is logarithmic.
        /// </summary>
        public bool YAxisLogarithmic { get; init; }

        /// <summary>
        /// Gets or initializes measured-value columns.
        /// </summary>
        public string[] YColumns { get; init; } = [];

        /// <summary>
        /// Gets or initializes the optional fixed y-axis maximum.
        /// </summary>
        public double? YMaximum { get; init; }

        /// <summary>
        /// Gets or initializes the optional fixed y-axis minimum.
        /// </summary>
        public double? YMinimum { get; init; }

        /// <summary>
        /// Gets or initializes the optional y-axis split mode.
        /// </summary>
        public string? YSplit { get; init; }

        /// <summary>
        /// Gets or initializes the optional y-axis title.
        /// </summary>
        public string? YTitle { get; init; }
    }

    /// <summary>
    /// Returns one database schema from the version-one gateway.
    /// </summary>
    public sealed class KustoGatewaySchemaResponse
    {
        /// <summary>
        /// Gets or initializes the cluster name.
        /// </summary>
        public string ClusterName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the database name.
        /// </summary>
        public string DatabaseName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes stored functions.
        /// </summary>
        public KustoGatewayFunction[] Functions { get; init; } = [];

        /// <summary>
        /// Gets or initializes database tables.
        /// </summary>
        public KustoGatewayTable[] Tables { get; init; } = [];

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }

    /// <summary>
    /// Describes one table on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayTable
    {
        /// <summary>
        /// Gets or initializes table columns.
        /// </summary>
        public KustoGatewayColumn[] Columns { get; init; } = [];

        /// <summary>
        /// Gets or initializes the table name.
        /// </summary>
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// Describes one schema column on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayColumn
    {
        /// <summary>
        /// Gets or initializes the column name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the numeric scalar type.
        /// </summary>
        public int Type { get; init; }
    }

    /// <summary>
    /// Describes one stored function on the gateway wire.
    /// </summary>
    public sealed class KustoGatewayFunction
    {
        /// <summary>
        /// Gets or initializes the function body.
        /// </summary>
        public string Body { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes optional documentation.
        /// </summary>
        public string? Documentation { get; init; }

        /// <summary>
        /// Gets or initializes the optional folder.
        /// </summary>
        public string? Folder { get; init; }

        /// <summary>
        /// Gets or initializes the function name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the parameter declaration.
        /// </summary>
        public string Parameters { get; init; } = string.Empty;
    }

    /// <summary>
    /// Returns a stable gateway error without exposing server internals.
    /// </summary>
    public sealed class KustoGatewayErrorResponse
    {
        /// <summary>
        /// Gets or initializes the stable error code.
        /// </summary>
        public string Code { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the user-facing error message.
        /// </summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// Gets or initializes the wire-protocol version.
        /// </summary>
        public int Version { get; init; }
    }
}
