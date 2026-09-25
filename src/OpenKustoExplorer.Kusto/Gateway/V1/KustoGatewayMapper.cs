using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Domain.Schema;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Kusto.Gateway.V1;

/// <summary>
/// Maps version-one gateway contracts to host-independent application models.
/// </summary>
public static class KustoGatewayMapper
{
    /// <summary>
    /// Creates a bounded gateway request for one Copilot operation.
    /// </summary>
    /// <param name="operationId">The unique operation identifier.</param>
    /// <param name="context">The current workbench context.</param>
    /// <param name="request">The current natural-language request.</param>
    /// <param name="modelId">The selected server model.</param>
    /// <param name="sharedDataText">The complete consented local snapshot.</param>
    /// <param name="history">Prior browser-local conversation turns.</param>
    /// <returns>The gateway request.</returns>
    public static KustoGatewayCopilotRequest ToGatewayCopilotRequest(
        Guid operationId,
        KustoCopilotContext context,
        string request,
        string modelId,
        string sharedDataText,
        IReadOnlyList<KustoGatewayCopilotTurn> history)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(sharedDataText);
        ArgumentNullException.ThrowIfNull(history);
        EnsureOperationId(operationId);
        return new KustoGatewayCopilotRequest
        {
            DocumentTitle = context.DocumentTitle,
            History = history.ToArray(),
            ModelId = modelId.Trim(),
            OperationId = operationId,
            QueryText = context.QueryText,
            Request = request.Trim(),
            SchemaText = context.SchemaText,
            ScopeKind = (int)context.ScopeKind,
            SharedDataText = sharedDataText,
            TargetText = context.TargetText,
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Validates and maps a Copilot gateway request to a store-independent context.
    /// </summary>
    /// <param name="request">The gateway request.</param>
    /// <returns>The application Copilot context.</returns>
    public static KustoCopilotContext ToDomainCopilotContext(KustoGatewayCopilotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureVersion(request.Version);
        EnsureOperationId(request.OperationId);
        EnsureRequiredText(request.DocumentTitle, 512, "Copilot scope title");
        EnsureText(request.QueryText, KustoGatewayRoutes.MaximumCopilotContextCharacterCount, "Copilot query text");
        EnsureText(request.TargetText, 4_096, "Copilot target text");
        EnsureText(request.SchemaText, 131_072, "Copilot schema text");
        EnsureText(
            request.SharedDataText,
            KustoGatewayRoutes.MaximumCopilotSharedDataCharacterCount,
            "Copilot shared data");
        EnsureRequiredText(request.Request, 16_384, "Copilot request");
        EnsureRequiredText(request.ModelId, 256, "Copilot model");
        KustoCopilotScopeKind scopeKind = (KustoCopilotScopeKind)request.ScopeKind;
        if (!Enum.IsDefined(scopeKind))
        {
            throw new InvalidDataException("The Copilot scope kind is invalid.");
        }

        ValidateCopilotHistory(request.History);
        return new KustoCopilotContext(
            request.OperationId,
            scopeKind,
            request.DocumentTitle,
            request.QueryText,
            request.TargetText,
            request.SchemaText,
            request.SharedDataText,
            null,
            null);
    }

    /// <summary>
    /// Maps an application Copilot response to the gateway wire contract.
    /// </summary>
    /// <param name="reply">The assistant reply.</param>
    /// <returns>The gateway response.</returns>
    public static KustoGatewayCopilotResponse ToGatewayCopilotResponse(KustoCopilotReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return new KustoGatewayCopilotResponse
        {
            Message = reply.Message,
            ProposedCypher = reply.ProposedCypher,
            ProposedQuery = reply.ProposedQuery,
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Maps a gateway Copilot response to an application reply.
    /// </summary>
    /// <param name="response">The gateway response.</param>
    /// <returns>The assistant reply.</returns>
    public static KustoCopilotReply ToDomainCopilotReply(KustoGatewayCopilotResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        EnsureVersion(response.Version);
        return new KustoCopilotReply(
            response.Message,
            response.ProposedQuery,
            response.ProposedCypher);
    }

    /// <summary>
    /// Creates a gateway request for a query operation.
    /// </summary>
    /// <param name="operationId">The unique operation identifier.</param>
    /// <param name="request">The application query request.</param>
    /// <returns>The gateway request.</returns>
    public static KustoGatewayQueryRequest ToGatewayRequest(
        Guid operationId,
        KustoQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOperationId(operationId);
        return new KustoGatewayQueryRequest
        {
            ClusterUri = request.ClusterUri.AbsoluteUri,
            DatabaseName = request.DatabaseName,
            OperationId = operationId,
            QueryText = request.QueryText,
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Creates a gateway request for a database catalog operation.
    /// </summary>
    /// <param name="operationId">The unique operation identifier.</param>
    /// <param name="clusterUri">The target cluster URI.</param>
    /// <returns>The gateway request.</returns>
    public static KustoGatewayDatabasesRequest ToGatewayDatabasesRequest(
        Guid operationId,
        Uri clusterUri)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);
        EnsureOperationId(operationId);
        EnsureHttpsClusterUri(clusterUri);
        return new KustoGatewayDatabasesRequest
        {
            ClusterUri = clusterUri.AbsoluteUri,
            OperationId = operationId,
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Creates a gateway request for a database schema operation.
    /// </summary>
    /// <param name="operationId">The unique operation identifier.</param>
    /// <param name="clusterUri">The target cluster URI.</param>
    /// <param name="databaseName">The target database name.</param>
    /// <returns>The gateway request.</returns>
    public static KustoGatewaySchemaRequest ToGatewaySchemaRequest(
        Guid operationId,
        Uri clusterUri,
        string databaseName)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        EnsureOperationId(operationId);
        EnsureHttpsClusterUri(clusterUri);
        return new KustoGatewaySchemaRequest
        {
            ClusterUri = clusterUri.AbsoluteUri,
            DatabaseName = databaseName,
            OperationId = operationId,
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Validates and maps a gateway query request.
    /// </summary>
    /// <param name="request">The gateway request.</param>
    /// <returns>The application query request.</returns>
    public static KustoQueryRequest ToDomainRequest(KustoGatewayQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureVersion(request.Version);
        EnsureOperationId(request.OperationId);
        return new KustoQueryRequest(
            ParseClusterUri(request.ClusterUri),
            request.DatabaseName,
            request.QueryText);
    }

    /// <summary>
    /// Validates and extracts a database catalog target.
    /// </summary>
    /// <param name="request">The gateway request.</param>
    /// <returns>The target cluster URI.</returns>
    public static Uri ToDomainClusterUri(KustoGatewayDatabasesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureVersion(request.Version);
        EnsureOperationId(request.OperationId);
        return ParseClusterUri(request.ClusterUri);
    }

    /// <summary>
    /// Validates and extracts a database schema target.
    /// </summary>
    /// <param name="request">The gateway request.</param>
    /// <returns>The target cluster URI and database name.</returns>
    public static (Uri ClusterUri, string DatabaseName) ToDomainSchemaTarget(
        KustoGatewaySchemaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureVersion(request.Version);
        EnsureOperationId(request.OperationId);
        if (string.IsNullOrWhiteSpace(request.DatabaseName))
        {
            throw new ArgumentException("The database name cannot be null or whitespace.", nameof(request));
        }

        return (ParseClusterUri(request.ClusterUri), request.DatabaseName);
    }

    /// <summary>
    /// Maps an application query result to a gateway response.
    /// </summary>
    /// <param name="result">The application query result.</param>
    /// <returns>The gateway response.</returns>
    public static KustoGatewayQueryResponse ToGatewayQueryResponse(KustoQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new KustoGatewayQueryResponse
        {
            Completeness = (int)result.Completeness,
            DurationTicks = result.Duration.Ticks,
            Tables = result.Tables.Select(ToGatewayTable).ToArray(),
            Version = KustoGatewayRoutes.Version,
            Visualization = result.Visualization is null
                ? null
                : ToGatewayVisualization(result.Visualization),
        };
    }

    /// <summary>
    /// Maps a gateway query response to an application result.
    /// </summary>
    /// <param name="response">The gateway response.</param>
    /// <returns>The application query result.</returns>
    public static KustoQueryResult ToDomainResult(KustoGatewayQueryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        EnsureVersion(response.Version);
        KustoQueryResultCompleteness completeness = (KustoQueryResultCompleteness)response.Completeness;
        if (!Enum.IsDefined(completeness))
        {
            throw new InvalidDataException("The gateway returned an unknown result completeness value.");
        }

        KustoResultTable[] tables = (response.Tables
            ?? throw new InvalidDataException("The gateway returned no result table collection."))
            .Select(ToDomainTable)
            .ToArray();
        KustoVisualization? visualization = response.Visualization is null
            ? null
            : ToDomainVisualization(response.Visualization);
        return new KustoQueryResult(
            tables,
            new TimeSpan(response.DurationTicks),
            visualization,
            completeness);
    }

    /// <summary>
    /// Maps a graph export batch to one gateway stream frame.
    /// </summary>
    /// <param name="batch">The graph export batch.</param>
    /// <returns>The gateway stream frame.</returns>
    public static KustoGatewayGraphFrame ToGatewayGraphBatchFrame(KustoGraphExportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new KustoGatewayGraphFrame
        {
            Columns = batch.Columns
                .Select(column => new KustoGatewayResultColumn
                {
                    Name = column.Name,
                    TypeName = column.TypeName,
                })
                .ToArray(),
            EndsTable = batch.EndsTable,
            FrameKind = KustoGatewayGraphFrameKind.Batch,
            Rows = batch.Rows.Select(row => row.ToArray()).ToArray(),
            StartsTable = batch.StartsTable,
            TableKind = (int)batch.Kind,
            TableName = batch.TableName,
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Maps a gateway stream frame to a graph export batch.
    /// </summary>
    /// <param name="frame">The gateway stream frame.</param>
    /// <returns>The graph export batch.</returns>
    public static KustoGraphExportBatch ToDomainGraphBatch(KustoGatewayGraphFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        EnsureVersion(frame.Version);
        if (frame.FrameKind != KustoGatewayGraphFrameKind.Batch)
        {
            throw new InvalidDataException("The gateway frame does not contain a graph batch.");
        }

        KustoGraphExportTableKind tableKind = (KustoGraphExportTableKind)frame.TableKind;
        if (!Enum.IsDefined(tableKind))
        {
            throw new InvalidDataException("The gateway returned an unknown graph table kind.");
        }

        KustoGatewayResultColumn[] columns = frame.Columns
            ?? throw new InvalidDataException("The gateway returned no graph column collection.");
        if (!frame.StartsTable && (frame.TableName is not null || columns.Length != 0))
        {
            throw new InvalidDataException("A graph continuation batch contains table metadata.");
        }

        IReadOnlyList<KustoResultColumn>? domainColumns = frame.StartsTable
            ? columns.Select(column => new KustoResultColumn(column.Name, column.TypeName)).ToArray()
            : null;
        IReadOnlyList<IReadOnlyList<string>> rows = (frame.Rows
            ?? throw new InvalidDataException("The gateway returned no graph row collection."))
            .Select(row => (IReadOnlyList<string>)(row
                ?? throw new InvalidDataException("The gateway returned a null graph row.")))
            .ToArray();
        return new KustoGraphExportBatch(
            tableKind,
            rows,
            frame.StartsTable,
            frame.EndsTable,
            frame.TableName,
            domainColumns);
    }

    /// <summary>
    /// Maps a completed graph export to one gateway summary frame.
    /// </summary>
    /// <param name="summary">The completed graph export.</param>
    /// <returns>The gateway stream frame.</returns>
    public static KustoGatewayGraphFrame ToGatewayGraphSummaryFrame(KustoGraphExportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new KustoGatewayGraphFrame
        {
            DurationTicks = summary.Duration.Ticks,
            EdgeCount = summary.EdgeCount,
            FrameKind = KustoGatewayGraphFrameKind.Summary,
            NodeCount = summary.NodeCount,
            ResultTable = summary.ResultTable is null ? null : ToGatewayTable(summary.ResultTable),
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Maps a gateway summary frame to a completed graph export.
    /// </summary>
    /// <param name="frame">The gateway stream frame.</param>
    /// <returns>The completed graph export.</returns>
    public static KustoGraphExportSummary ToDomainGraphSummary(KustoGatewayGraphFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        EnsureVersion(frame.Version);
        if (frame.FrameKind != KustoGatewayGraphFrameKind.Summary)
        {
            throw new InvalidDataException("The gateway frame does not contain a graph summary.");
        }

        return new KustoGraphExportSummary(
            frame.NodeCount,
            frame.EdgeCount,
            new TimeSpan(frame.DurationTicks),
            frame.ResultTable is null ? null : ToDomainTable(frame.ResultTable));
    }

    /// <summary>
    /// Maps an application database catalog to a gateway response.
    /// </summary>
    /// <param name="databases">The application database catalog.</param>
    /// <returns>The gateway response.</returns>
    public static KustoGatewayDatabasesResponse ToGatewayDatabasesResponse(
        IReadOnlyList<KustoDatabaseInfo> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);
        return new KustoGatewayDatabasesResponse
        {
            Databases = databases
                .Select(database => new KustoGatewayDatabase
                {
                    DisplayName = database.DisplayName,
                    Name = database.Name,
                })
                .ToArray(),
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Maps a gateway database catalog to application models.
    /// </summary>
    /// <param name="response">The gateway response.</param>
    /// <returns>The application database catalog.</returns>
    public static IReadOnlyList<KustoDatabaseInfo> ToDomainDatabases(
        KustoGatewayDatabasesResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        EnsureVersion(response.Version);
        return (response.Databases
            ?? throw new InvalidDataException("The gateway returned no database collection."))
            .Select(database => new KustoDatabaseInfo(database.Name, database.DisplayName))
            .ToArray();
    }

    /// <summary>
    /// Maps an application database schema to a gateway response.
    /// </summary>
    /// <param name="schema">The application database schema.</param>
    /// <returns>The gateway response.</returns>
    public static KustoGatewaySchemaResponse ToGatewaySchemaResponse(KustoDatabaseSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new KustoGatewaySchemaResponse
        {
            ClusterName = schema.ClusterName,
            DatabaseName = schema.DatabaseName,
            Functions = schema.Functions
                .Select(function => new KustoGatewayFunction
                {
                    Body = function.Body,
                    Documentation = function.Documentation,
                    Folder = function.Folder,
                    Name = function.Name,
                    Parameters = function.Parameters,
                })
                .ToArray(),
            Tables = schema.Tables
                .Select(table => new KustoGatewayTable
                {
                    Columns = table.Columns
                        .Select(column => new KustoGatewayColumn
                        {
                            Name = column.Name,
                            Type = (int)column.Type,
                        })
                        .ToArray(),
                    Name = table.Name,
                })
                .ToArray(),
            Version = KustoGatewayRoutes.Version,
        };
    }

    /// <summary>
    /// Maps a gateway database schema to an application model.
    /// </summary>
    /// <param name="response">The gateway response.</param>
    /// <returns>The application database schema.</returns>
    public static KustoDatabaseSchema ToDomainSchema(KustoGatewaySchemaResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        EnsureVersion(response.Version);
        KustoTableSchema[] tables = (response.Tables
            ?? throw new InvalidDataException("The gateway returned no table collection."))
            .Select(table => new KustoTableSchema(
                table.Name,
                (table.Columns
                    ?? throw new InvalidDataException("The gateway returned no column collection."))
                    .Select(column => new KustoColumnSchema(
                        column.Name,
                        ParseScalarType(column.Type)))))
            .ToArray();
        KustoFunctionSchema[] functions = (response.Functions
            ?? throw new InvalidDataException("The gateway returned no function collection."))
            .Select(function => new KustoFunctionSchema(
                function.Name,
                function.Parameters,
                function.Body,
                function.Folder,
                function.Documentation))
            .ToArray();
        return new KustoDatabaseSchema(
            response.ClusterName,
            response.DatabaseName,
            tables,
            functions);
    }

    private static void EnsureHttpsClusterUri(Uri clusterUri)
    {
        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The cluster URI must be an absolute HTTPS URI.", nameof(clusterUri));
        }
    }

    private static void EnsureRequiredText(string value, int maximumLength, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{description} is required.");
        }

        EnsureText(value, maximumLength, description);
    }

    private static void EnsureText(string value, int maximumLength, string description)
    {
        if (value is null)
        {
            throw new InvalidDataException($"{description} is missing.");
        }

        if (value.Length > maximumLength)
        {
            throw new InvalidDataException($"{description} exceeds {maximumLength:N0} characters.");
        }
    }

    private static void ValidateCopilotHistory(KustoGatewayCopilotTurn[]? history)
    {
        if (history is null)
        {
            throw new InvalidDataException("The Copilot history collection is missing.");
        }

        if (history.Length > KustoGatewayRoutes.MaximumCopilotHistoryTurnCount)
        {
            throw new InvalidDataException(
                $"Copilot history exceeds {KustoGatewayRoutes.MaximumCopilotHistoryTurnCount:N0} turns.");
        }

        foreach (KustoGatewayCopilotTurn turn in history)
        {
            if (turn is null)
            {
                throw new InvalidDataException("The Copilot history contains a null turn.");
            }

            EnsureRequiredText(
                turn.UserPrompt,
                KustoGatewayRoutes.MaximumCopilotContextCharacterCount,
                "Copilot history prompt");
            EnsureRequiredText(turn.AssistantResponse, 131_072, "Copilot history response");
        }
    }

    private static void EnsureOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("The gateway operation identifier cannot be empty.", nameof(operationId));
        }
    }

    private static void EnsureVersion(int version)
    {
        if (version != KustoGatewayRoutes.Version)
        {
            throw new InvalidDataException($"Gateway protocol version {version} is not supported.");
        }
    }

    private static Uri ParseClusterUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? clusterUri))
        {
            throw new InvalidDataException("The gateway cluster URI is invalid.");
        }

        EnsureHttpsClusterUri(clusterUri);
        return clusterUri;
    }

    private static KustoScalarType ParseScalarType(int value)
    {
        KustoScalarType type = (KustoScalarType)value;
        return Enum.IsDefined(type)
            ? type
            : throw new InvalidDataException("The gateway returned an unknown scalar type.");
    }

    private static KustoGatewayResultTable ToGatewayTable(KustoResultTable table)
    {
        return new KustoGatewayResultTable
        {
            Columns = table.Columns
                .Select(column => new KustoGatewayResultColumn
                {
                    Name = column.Name,
                    TypeName = column.TypeName,
                })
                .ToArray(),
            Name = table.Name,
            Rows = table.Rows
                .Select(row => row.ResultValues
                    .Select(value => new KustoGatewayResultValue
                    {
                        DisplayText = value.DisplayText,
                        IsNull = value.IsNull,
                        RawJson = value.RawJson,
                    })
                    .ToArray())
                .ToArray(),
        };
    }

    private static KustoResultTable ToDomainTable(KustoGatewayResultTable table)
    {
        KustoResultColumn[] columns = (table.Columns
            ?? throw new InvalidDataException("The gateway returned no result columns."))
            .Select(column => new KustoResultColumn(column.Name, column.TypeName))
            .ToArray();
        KustoResultRow[] rows = (table.Rows
            ?? throw new InvalidDataException("The gateway returned no result rows."))
            .Select(row => new KustoResultRow(row.Select(value => new KustoResultValue(
                value.DisplayText,
                value.RawJson,
                value.IsNull))))
            .ToArray();
        return new KustoResultTable(table.Name, columns, rows);
    }

    private static KustoGatewayVisualization ToGatewayVisualization(KustoVisualization visualization)
    {
        return new KustoGatewayVisualization
        {
            Accumulate = visualization.Accumulate,
            AnomalyColumns = visualization.AnomalyColumns.ToArray(),
            Kind = (int)visualization.Kind,
            KindOption = visualization.KindOption,
            LegendVisible = visualization.LegendVisible,
            SeriesColumns = visualization.SeriesColumns.ToArray(),
            Title = visualization.Title,
            XAxisLogarithmic = visualization.XAxisLogarithmic,
            XColumn = visualization.XColumn,
            XTitle = visualization.XTitle,
            YAxisLogarithmic = visualization.YAxisLogarithmic,
            YColumns = visualization.YColumns.ToArray(),
            YMaximum = visualization.YMaximum,
            YMinimum = visualization.YMinimum,
            YSplit = visualization.YSplit,
            YTitle = visualization.YTitle,
        };
    }

    private static KustoVisualization ToDomainVisualization(KustoGatewayVisualization visualization)
    {
        KustoVisualizationKind kind = (KustoVisualizationKind)visualization.Kind;
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("The gateway returned an unknown visualization kind.");
        }

        return new KustoVisualization(
            kind,
            visualization.Title,
            visualization.XColumn,
            visualization.XTitle,
            visualization.YTitle,
            visualization.SeriesColumns,
            visualization.YColumns,
            visualization.AnomalyColumns,
            visualization.KindOption,
            visualization.LegendVisible,
            visualization.Accumulate,
            visualization.YMinimum,
            visualization.YMaximum,
            visualization.XAxisLogarithmic,
            visualization.YAxisLogarithmic,
            visualization.YSplit);
    }
}
