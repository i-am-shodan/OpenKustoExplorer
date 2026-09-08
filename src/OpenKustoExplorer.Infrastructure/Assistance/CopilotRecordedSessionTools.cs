using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Infrastructure.Assistance;

/// <summary>
/// Creates bounded read-only Copilot tools pinned to one recorded query session.
/// </summary>
internal static class CopilotRecordedSessionTools
{
    /// <summary>Gets the recorded-session overview tool name.</summary>
    internal const string GetOverviewToolName = "get_recorded_session_overview";

    /// <summary>Gets the exact recorded-query tool name.</summary>
    internal const string GetQueryToolName = "get_recorded_query";

    /// <summary>Gets the bounded recorded-result search tool name.</summary>
    internal const string SearchResultsToolName = "search_recorded_results";

    /// <summary>Gets the bounded recorded-result page tool name.</summary>
    internal const string GetResultPageToolName = "get_recorded_result_page";

    /// <summary>Gets the recorded chain-query generation tool name.</summary>
    internal const string GenerateChainQueryToolName = "generate_recorded_chain_query";
    private const int MaximumCellCount = 100;
    private const int MaximumOutputBytes = 64 * 1024;
    private const int MaximumOverviewExecutionCount = 100;
    private const int MaximumOverviewValueCount = 100;
    private const int MaximumQueryLength = 30_000;
    private const int MaximumRowCount = 50;
    private const int MaximumTableCount = 20;
    private const int MaximumValueLength = 500;

    /// <summary>
    /// Creates the exact tools available to one consented Recorded Sessions conversation.
    /// </summary>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="chainSearcher">The recorded evidence chain searcher.</param>
    /// <param name="relationPlanner">The recorded relation planner.</param>
    /// <param name="queryGenerator">The recorded chain-query generator.</param>
    /// <param name="scope">The recorded session captured for the conversation.</param>
    /// <returns>Five AOT-safe read-only tool declarations.</returns>
    internal static IReadOnlyList<AIFunction> Create(
        IKustoRecordedSessionStore sessionStore,
        IKustoRecordedChainSearcher chainSearcher,
        IKustoRecordedRelationPlanner relationPlanner,
        IKustoRecordedChainQueryGenerator queryGenerator,
        KustoCopilotRecordedSessionScope scope)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(chainSearcher);
        ArgumentNullException.ThrowIfNull(relationPlanner);
        ArgumentNullException.ThrowIfNull(queryGenerator);
        ArgumentNullException.ThrowIfNull(scope);

        return Array.AsReadOnly<AIFunction>(
        [
            CopilotTool.DefineTool(
                (CancellationToken cancellationToken) => GetOverviewAsync(
                    sessionStore,
                    scope.SessionId,
                    cancellationToken),
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = GetOverviewToolName,
                    Description = "Gets bounded query, result-schema, and pertinent-value metadata for the selected recorded session without returning result rows.",
                }),
            CopilotTool.DefineTool(
                (
                    [Description("Exact execution ID from the recorded-session overview.")] string executionId,
                    CancellationToken cancellationToken) => GetQueryAsync(
                        sessionStore,
                        scope.SessionId,
                        executionId,
                        cancellationToken),
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = GetQueryToolName,
                    Description = "Gets the KQL and relation metadata for one exact query in the selected recorded session.",
                }),
            CopilotTool.DefineTool(
                (
                    [Description("Case-insensitive text to find in recorded display values.")] string searchText,
                    [Description("Exact execution ID to search, or an empty string to search the selected session.")] string executionId,
                    [Description("Maximum matching rows to return, from 1 through 50.")] int maximumMatches,
                    CancellationToken cancellationToken) => SearchResultsAsync(
                        sessionStore,
                        scope.SessionId,
                        searchText,
                        executionId,
                        maximumMatches,
                        cancellationToken),
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = SearchResultsToolName,
                    Description = "Searches recorded result values and returns at most 50 matching rows with stable cell coordinates.",
                }),
            CopilotTool.DefineTool(
                (
                    [Description("Exact execution ID from the recorded-session overview.")] string executionId,
                    [Description("Zero-based result-table ordinal.")] int tableOrdinal,
                    [Description("Zero-based first row ordinal.")] int startRow,
                    [Description("Number of rows to return, from 1 through 50.")] int rowCount,
                    CancellationToken cancellationToken) => GetResultPageAsync(
                        sessionStore,
                        scope.SessionId,
                        executionId,
                        tableOrdinal,
                        startRow,
                        rowCount,
                        cancellationToken),
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = GetResultPageToolName,
                    Description = "Gets one page of at most 50 rows from an exact recorded result table.",
                }),
            CopilotTool.DefineTool(
                (
                    [Description("Start cell coordinate returned by a result tool, formatted executionId/table/row/column.")] string startCoordinate,
                    [Description("Required intermediate cell coordinate in the same format, or an empty string for any route.")] string viaCoordinate,
                    [Description("Destination cell coordinate returned by a result tool, formatted executionId/table/row/column.")] string destinationCoordinate,
                    CancellationToken cancellationToken) => GenerateChainQueryAsync(
                        sessionStore,
                        chainSearcher,
                        relationPlanner,
                        queryGenerator,
                        scope,
                        startCoordinate,
                        viaCoordinate,
                        destinationCoordinate,
                        cancellationToken),
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = GenerateChainQueryToolName,
                    Description = "Generates validated KQL that transforms one exact recorded value into another, optionally through a required intermediate value.",
                }),
        ]);
    }

    /// <summary>Gets bounded metadata for one pinned recorded session.</summary>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="sessionId">The pinned session identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Bounded session metadata as JSON.</returns>
    internal static async Task<string> GetOverviewAsync(
        IKustoRecordedSessionStore sessionStore,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        KustoRecordedSession? session = await sessionStore.GetSessionAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return SerializeError("The selected recorded session no longer exists.");
        }

        return Serialize(writer => WriteOverview(writer, session));
    }

    /// <summary>Gets one exact recorded query and its relation metadata.</summary>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="sessionId">The pinned session identifier.</param>
    /// <param name="executionId">The requested execution identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The recorded query as JSON.</returns>
    internal static async Task<string> GetQueryAsync(
        IKustoRecordedSessionStore sessionStore,
        Guid sessionId,
        string executionId,
        CancellationToken cancellationToken)
    {
        KustoRecordedSession? session = await sessionStore.GetSessionAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return SerializeError("The selected recorded session no longer exists.");
        }

        if (!Guid.TryParse(executionId, out Guid parsedExecutionId)
            || FindExecution(session, parsedExecutionId) is not KustoRecordedExecution execution)
        {
            return SerializeError("The execution ID is invalid or does not belong to the selected recorded session.");
        }

        return Serialize(writer => WriteQuery(writer, execution));
    }

    /// <summary>Searches result display values with strict match and output bounds.</summary>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="sessionId">The pinned session identifier.</param>
    /// <param name="searchText">The case-insensitive display text to find.</param>
    /// <param name="executionId">An optional execution filter.</param>
    /// <param name="maximumMatches">The requested match limit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Bounded matching rows as JSON.</returns>
    internal static async Task<string> SearchResultsAsync(
        IKustoRecordedSessionStore sessionStore,
        Guid sessionId,
        string searchText,
        string executionId,
        int maximumMatches,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return SerializeError("Search text is required.");
        }

        KustoRecordedSession? session = await sessionStore.GetSessionAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return SerializeError("The selected recorded session no longer exists.");
        }

        if (!TryResolveExecutions(session, executionId, out IReadOnlyList<KustoRecordedExecution> executions))
        {
            return SerializeError("The execution ID is invalid or does not belong to the selected recorded session.");
        }

        int limit = Math.Clamp(maximumMatches, 1, MaximumRowCount);
        List<RecordedRowMatch> matches = FindMatches(
            executions,
            searchText,
            limit,
            cancellationToken,
            out bool truncated);

        return Serialize(writer => WriteSearchResults(writer, searchText, limit, matches, truncated));
    }

    /// <summary>Gets one bounded page from an exact result table.</summary>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="sessionId">The pinned session identifier.</param>
    /// <param name="executionId">The requested execution identifier.</param>
    /// <param name="tableOrdinal">The requested table ordinal.</param>
    /// <param name="startRow">The first requested row.</param>
    /// <param name="rowCount">The requested row count.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A bounded result page as JSON.</returns>
    internal static async Task<string> GetResultPageAsync(
        IKustoRecordedSessionStore sessionStore,
        Guid sessionId,
        string executionId,
        int tableOrdinal,
        int startRow,
        int rowCount,
        CancellationToken cancellationToken)
    {
        KustoRecordedSession? session = await sessionStore.GetSessionAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return SerializeError("The selected recorded session no longer exists.");
        }

        if (!Guid.TryParse(executionId, out Guid parsedExecutionId)
            || FindExecution(session, parsedExecutionId) is not KustoRecordedExecution execution)
        {
            return SerializeError("The execution ID is invalid or does not belong to the selected recorded session.");
        }

        if (execution.Result is null
            || tableOrdinal < 0
            || tableOrdinal >= execution.Result.Tables.Count)
        {
            return SerializeError("The result-table ordinal is invalid for this execution.");
        }

        if (startRow < 0)
        {
            return SerializeError("The first row ordinal cannot be negative.");
        }

        KustoResultTable table = execution.Result.Tables[tableOrdinal];
        int limit = Math.Clamp(rowCount, 1, MaximumRowCount);
        return Serialize(writer => WriteResultPage(
            writer,
            execution,
            table,
            tableOrdinal,
            startRow,
            limit));
    }

    /// <summary>Generates an evidence-backed KQL chain between exact recorded cells.</summary>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="chainSearcher">The recorded chain searcher.</param>
    /// <param name="relationPlanner">The recorded relation planner.</param>
    /// <param name="queryGenerator">The recorded query generator.</param>
    /// <param name="scope">The pinned recorded-session scope.</param>
    /// <param name="startCoordinate">The start cell coordinate.</param>
    /// <param name="viaCoordinate">The optional required intermediate cell coordinate.</param>
    /// <param name="destinationCoordinate">The destination cell coordinate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The generated chain query or diagnostics as JSON.</returns>
    internal static async Task<string> GenerateChainQueryAsync(
        IKustoRecordedSessionStore sessionStore,
        IKustoRecordedChainSearcher chainSearcher,
        IKustoRecordedRelationPlanner relationPlanner,
        IKustoRecordedChainQueryGenerator queryGenerator,
        KustoCopilotRecordedSessionScope scope,
        string startCoordinate,
        string viaCoordinate,
        string destinationCoordinate,
        CancellationToken cancellationToken)
    {
        KustoRecordedSession? session = await sessionStore.GetSessionAsync(
            scope.SessionId,
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return SerializeError("The selected recorded session no longer exists.");
        }

        if (!TryResolveCoordinate(session, startCoordinate, out KustoRecordedValueCoordinate start))
        {
            return SerializeError("The start coordinate is invalid or does not belong to the selected recorded session.");
        }

        if (!TryResolveCoordinate(session, destinationCoordinate, out KustoRecordedValueCoordinate destination))
        {
            return SerializeError("The destination coordinate is invalid or does not belong to the selected recorded session.");
        }

        KustoRecordedValueCoordinate? via = null;
        if (!string.IsNullOrWhiteSpace(viaCoordinate))
        {
            if (!TryResolveCoordinate(session, viaCoordinate, out KustoRecordedValueCoordinate parsedVia))
            {
                return SerializeError("The intermediate coordinate is invalid or does not belong to the selected recorded session.");
            }

            via = parsedVia;
        }

        if (scope.DatabaseSchema is not KustoDatabaseSchema schema)
        {
            return SerializeError("The recorded query database schema is unavailable, so chain KQL cannot be validated.");
        }

        KustoQueryChain? chain = await FindChainAsync(
            chainSearcher,
            session,
            scope.SessionId,
            schema,
            start,
            via,
            destination,
            cancellationToken).ConfigureAwait(false);

        if (chain is null)
        {
            return SerializeError(via is null
                ? "No recorded evidence chain connects the selected values."
                : "No recorded evidence chain connects both required segments through the intermediate value.");
        }

        KustoRelationalChainPlan? plan = relationPlanner.CreatePlan(session, chain);
        if (plan is null)
        {
            return SerializeError("The recorded evidence lacks sufficient source lineage to generate safe KQL.");
        }

        KustoGeneratedChainQuery generated = queryGenerator.Generate(plan, schema);
        return Serialize(writer => WriteGeneratedQuery(writer, generated, chain, via is not null));
    }

    private static IEnumerable<RecordedRowReference> EnumerateResultRows(
        IReadOnlyList<KustoRecordedExecution> executions)
    {
        foreach (KustoRecordedExecution execution in executions)
        {
            IReadOnlyList<KustoResultTable> tables = execution.Result?.Tables ?? Array.Empty<KustoResultTable>();
            for (int tableOrdinal = 0; tableOrdinal < tables.Count; tableOrdinal++)
            {
                KustoResultTable table = tables[tableOrdinal];
                for (int rowOrdinal = 0; rowOrdinal < table.Rows.Count; rowOrdinal++)
                {
                    yield return new RecordedRowReference(
                        execution,
                        table,
                        tableOrdinal,
                        table.Rows[rowOrdinal],
                        rowOrdinal);
                }
            }
        }
    }

    private static async Task<KustoQueryChain?> FindChainAsync(
        IKustoRecordedChainSearcher chainSearcher,
        KustoRecordedSession session,
        Guid sessionId,
        KustoDatabaseSchema schema,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate? via,
        KustoRecordedValueCoordinate destination,
        CancellationToken cancellationToken)
    {
        if (via is null)
        {
            return await chainSearcher.FindAsync(
                sessionId,
                start,
                destination,
                schema,
                cancellationToken).ConfigureAwait(false);
        }

        KustoQueryChain? first = await chainSearcher.FindAsync(
            sessionId,
            start,
            via,
            schema,
            cancellationToken).ConfigureAwait(false);
        KustoQueryChain? second = await chainSearcher.FindAsync(
            sessionId,
            via,
            destination,
            schema,
            cancellationToken).ConfigureAwait(false);
        if (first is not null && second is not null)
        {
            return new KustoQueryChain(
                sessionId,
                start,
                destination,
                first.Pivots.Concat(second.Pivots),
                checked(first.TotalCost + second.TotalCost));
        }

        KustoQueryChain? direct = await chainSearcher.FindAsync(
            sessionId,
            start,
            destination,
            schema,
            cancellationToken).ConfigureAwait(false);
        return direct is not null && ChainContainsValue(session, direct, via)
            ? direct
            : null;
    }

    private static bool ChainContainsValue(
        KustoRecordedSession session,
        KustoQueryChain chain,
        KustoRecordedValueCoordinate coordinate)
    {
        if (!TryGetCell(session, coordinate, out RecordedCell cell))
        {
            return false;
        }

        KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(
            cell.Column.TypeName,
            cell.Value);
        return chain.Pivots.Any(pivot => pivot.Input.Equals(identity) || pivot.Output.Equals(identity));
    }

    private static List<RecordedRowMatch> FindMatches(
        IReadOnlyList<KustoRecordedExecution> executions,
        string searchText,
        int limit,
        CancellationToken cancellationToken,
        out bool truncated)
    {
        List<RecordedRowMatch> matches = [];
        truncated = false;
        foreach (RecordedRowReference reference in EnumerateResultRows(executions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] matchingColumns = reference.Row.ResultValues
                .Select((value, columnOrdinal) => (value, columnOrdinal))
                .Where(item => item.value.DisplayText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.columnOrdinal)
                .ToArray();
            if (matchingColumns.Length == 0)
            {
                continue;
            }

            if (matches.Count == limit)
            {
                truncated = true;
                break;
            }

            matches.Add(new RecordedRowMatch(
                reference.Execution,
                reference.Table,
                reference.TableOrdinal,
                reference.Row,
                reference.RowOrdinal,
                matchingColumns));
        }

        return matches;
    }

    private static KustoRecordedExecution? FindExecution(KustoRecordedSession session, Guid executionId)
    {
        foreach (KustoRecordedExecution execution in session.Executions)
        {
            if (execution.Id == executionId)
            {
                return execution;
            }
        }

        return null;
    }

    private static string FormatCoordinate(KustoRecordedValueCoordinate coordinate)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{coordinate.ExecutionId:D}/{coordinate.TableOrdinal}/{coordinate.RowOrdinal}/{coordinate.ColumnOrdinal}");
    }

    private static string GetExecutionName(KustoRecordedExecution execution)
    {
        return execution.DisplayName ?? execution.DocumentTitle;
    }

    private static string Serialize(Action<Utf8JsonWriter> writeAction)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writeAction(writer);
        }

        return buffer.WrittenCount <= MaximumOutputBytes
            ? Encoding.UTF8.GetString(buffer.WrittenSpan)
            : "{\"truncated\":true,\"message\":\"Recorded-session tool output exceeded 64 KiB. Request fewer rows or a narrower search.\"}";
    }

    private static string SerializeError(string message)
    {
        return Serialize(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("error", message);
            writer.WriteEndObject();
        });
    }

    private static string Truncate(string value, int maximumLength = MaximumValueLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static bool TryGetCell(
        KustoRecordedSession session,
        KustoRecordedValueCoordinate coordinate,
        out RecordedCell cell)
    {
        cell = null!;
        KustoRecordedExecution? execution = FindExecution(session, coordinate.ExecutionId);
        if (execution?.Result is null
            || coordinate.TableOrdinal >= execution.Result.Tables.Count)
        {
            return false;
        }

        KustoResultTable table = execution.Result.Tables[coordinate.TableOrdinal];
        if (coordinate.RowOrdinal >= table.Rows.Count
            || coordinate.ColumnOrdinal >= table.Columns.Count)
        {
            return false;
        }

        KustoResultRow row = table.Rows[coordinate.RowOrdinal];
        if (coordinate.ColumnOrdinal >= row.ResultValues.Count)
        {
            return false;
        }

        cell = new RecordedCell(
            table.Columns[coordinate.ColumnOrdinal],
            row.ResultValues[coordinate.ColumnOrdinal]);
        return true;
    }

    private static bool TryParseCoordinate(
        string text,
        out KustoRecordedValueCoordinate coordinate)
    {
        coordinate = null!;
        string[] parts = text.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !Guid.TryParse(parts[0], out Guid executionId)
            || executionId == Guid.Empty
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int tableOrdinal)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int rowOrdinal)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int columnOrdinal)
            || tableOrdinal < 0
            || rowOrdinal < 0
            || columnOrdinal < 0)
        {
            return false;
        }

        coordinate = new KustoRecordedValueCoordinate(
            executionId,
            tableOrdinal,
            rowOrdinal,
            columnOrdinal);
        return true;
    }

    private static bool TryResolveCoordinate(
        KustoRecordedSession session,
        string text,
        out KustoRecordedValueCoordinate coordinate)
    {
        return TryParseCoordinate(text, out coordinate)
            && TryGetCell(session, coordinate, out _);
    }

    private static bool TryResolveExecutions(
        KustoRecordedSession session,
        string executionId,
        out IReadOnlyList<KustoRecordedExecution> executions)
    {
        executions = session.Executions;
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return true;
        }

        if (!Guid.TryParse(executionId, out Guid parsedExecutionId)
            || FindExecution(session, parsedExecutionId) is not KustoRecordedExecution execution)
        {
            return false;
        }

        executions = Array.AsReadOnly([execution]);
        return true;
    }

    private static void WriteCell(
        Utf8JsonWriter writer,
        KustoRecordedExecution execution,
        KustoResultTable table,
        int tableOrdinal,
        KustoResultRow row,
        int rowOrdinal,
        int columnOrdinal,
        bool isMatch)
    {
        KustoResultColumn column = table.Columns[columnOrdinal];
        KustoResultValue value = row.ResultValues[columnOrdinal];
        KustoRecordedValueCoordinate coordinate = new(
            execution.Id,
            tableOrdinal,
            rowOrdinal,
            columnOrdinal);
        writer.WriteStartObject();
        writer.WriteString("coordinate", FormatCoordinate(coordinate));
        writer.WriteString("column", Truncate(column.Name));
        writer.WriteString("type", Truncate(column.TypeName));
        if (value.IsNull)
        {
            writer.WriteNull("value");
        }
        else
        {
            writer.WriteString("value", Truncate(value.DisplayText));
        }

        if (isMatch)
        {
            writer.WriteBoolean("matched", true);
        }

        writer.WriteEndObject();
    }

    private static void WriteColumns(Utf8JsonWriter writer, KustoResultTable table)
    {
        writer.WritePropertyName("columns");
        writer.WriteStartArray();
        foreach (KustoResultColumn column in table.Columns.Take(MaximumCellCount))
        {
            writer.WriteStartObject();
            writer.WriteString("name", Truncate(column.Name));
            writer.WriteString("type", Truncate(column.TypeName));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteBoolean("columnsTruncated", table.Columns.Count > MaximumCellCount);
    }

    private static void WriteExecutionSummary(Utf8JsonWriter writer, KustoRecordedExecution execution)
    {
        writer.WriteStartObject();
        writer.WriteString("executionId", execution.Id);
        writer.WriteNumber("sequence", execution.Sequence);
        writer.WriteString("name", Truncate(GetExecutionName(execution)));
        writer.WriteString("status", execution.Status.ToString());
        writer.WriteString("startedAtUtc", execution.StartedAtUtc);
        writer.WriteString("cluster", Truncate(execution.ClusterUri.Host));
        writer.WriteString("database", Truncate(execution.DatabaseName));
        writer.WriteNumber("retainedRowCount", execution.Result?.Tables.Sum(table => table.Rows.Count) ?? 0);
        writer.WritePropertyName("tables");
        writer.WriteStartArray();
        if (execution.Result is not null)
        {
            for (int ordinal = 0; ordinal < Math.Min(execution.Result.Tables.Count, MaximumTableCount); ordinal++)
            {
                KustoResultTable table = execution.Result.Tables[ordinal];
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", ordinal);
                writer.WriteString("name", Truncate(table.Name));
                writer.WriteNumber("rowCount", table.Rows.Count);
                WriteColumns(writer, table);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteBoolean("tablesTruncated", (execution.Result?.Tables.Count ?? 0) > MaximumTableCount);
        writer.WriteEndObject();
    }

    private static void WriteGeneratedQuery(
        Utf8JsonWriter writer,
        KustoGeneratedChainQuery generated,
        KustoQueryChain chain,
        bool usedVia)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("succeeded", generated.Succeeded);
        writer.WriteBoolean("usedRequiredIntermediate", usedVia);
        writer.WriteNumber("pivotCount", chain.Pivots.Count);
        writer.WriteNumber("evidenceCost", chain.TotalCost);
        writer.WriteString("queryText", Truncate(generated.QueryText, MaximumQueryLength));
        writer.WriteBoolean("queryTruncated", generated.QueryText.Length > MaximumQueryLength);
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (string diagnostic in generated.Diagnostics.Take(MaximumCellCount))
        {
            writer.WriteStringValue(Truncate(diagnostic));
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteInterest(Utf8JsonWriter writer, KustoRecordedInterest interest)
    {
        writer.WriteStartObject();
        writer.WriteString("source", interest.Source.ToString());
        writer.WriteString("column", Truncate(interest.ColumnName));
        writer.WriteString("type", Truncate(interest.Identity.TypeName));
        if (interest.Identity.IsNull)
        {
            writer.WriteNull("value");
        }
        else
        {
            writer.WriteString("value", Truncate(interest.Identity.CanonicalValue));
        }

        if (interest.Coordinate is not null)
        {
            writer.WriteString("coordinate", FormatCoordinate(interest.Coordinate));
        }

        writer.WriteEndObject();
    }

    private static void WriteMarkedValue(
        Utf8JsonWriter writer,
        KustoRecordedSession session,
        KustoRecordedValueCoordinate coordinate,
        string kind)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WriteString("coordinate", FormatCoordinate(coordinate));
        if (TryGetCell(session, coordinate, out RecordedCell cell))
        {
            writer.WriteString("column", Truncate(cell.Column.Name));
            writer.WriteString("type", Truncate(cell.Column.TypeName));
            if (cell.Value.IsNull)
            {
                writer.WriteNull("value");
            }
            else
            {
                writer.WriteString("value", Truncate(cell.Value.DisplayText));
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteOverview(Utf8JsonWriter writer, KustoRecordedSession session)
    {
        writer.WriteStartObject();
        writer.WriteString("sessionId", session.Summary.Id);
        writer.WriteString("name", Truncate(session.Summary.Name));
        writer.WriteString("createdAtUtc", session.Summary.CreatedAtUtc);
        writer.WriteString("lastUpdatedAtUtc", session.Summary.LastUpdatedAtUtc);
        writer.WriteNumber("executionCount", session.Executions.Count);
        writer.WriteNumber("pertinentInterestCount", session.Interests.Count(interest => !interest.IsSuppressed));
        writer.WriteNumber("markedValueCount", session.Marks.Count);
        writer.WritePropertyName("executions");
        writer.WriteStartArray();
        foreach (KustoRecordedExecution execution in session.Executions.Take(MaximumOverviewExecutionCount))
        {
            WriteExecutionSummary(writer, execution);
        }

        writer.WriteEndArray();
        writer.WriteBoolean("executionsTruncated", session.Executions.Count > MaximumOverviewExecutionCount);
        writer.WritePropertyName("pertinentValues");
        writer.WriteStartArray();
        foreach (KustoRecordedInterest interest in session.Interests
            .Where(interest => !interest.IsSuppressed)
            .Take(MaximumOverviewValueCount))
        {
            WriteInterest(writer, interest);
        }

        writer.WriteEndArray();
        writer.WriteBoolean(
            "pertinentValuesTruncated",
            session.Interests.Count(interest => !interest.IsSuppressed) > MaximumOverviewValueCount);
        writer.WritePropertyName("markedValues");
        writer.WriteStartArray();
        foreach (KustoRecordedMark mark in session.Marks.Take(MaximumOverviewValueCount))
        {
            WriteMarkedValue(writer, session, mark.Coordinate, mark.Kind.ToString());
        }

        writer.WriteEndArray();
        writer.WriteBoolean("markedValuesTruncated", session.Marks.Count > MaximumOverviewValueCount);
        writer.WritePropertyName("chainEndpoints");
        writer.WriteStartArray();
        foreach (KustoChainEndpoint endpoint in session.Endpoints)
        {
            WriteMarkedValue(writer, session, endpoint.Coordinate, endpoint.Role.ToString());
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteQuery(Utf8JsonWriter writer, KustoRecordedExecution execution)
    {
        writer.WriteStartObject();
        writer.WriteString("executionId", execution.Id);
        writer.WriteNumber("sequence", execution.Sequence);
        writer.WriteString("name", Truncate(GetExecutionName(execution)));
        writer.WriteString("cluster", Truncate(execution.ClusterUri.Host));
        writer.WriteString("database", Truncate(execution.DatabaseName));
        writer.WriteString("status", execution.Status.ToString());
        writer.WriteString("queryText", Truncate(execution.QueryText, MaximumQueryLength));
        writer.WriteBoolean("queryTruncated", execution.QueryText.Length > MaximumQueryLength);
        if (execution.ErrorMessage is not null)
        {
            writer.WriteString("error", Truncate(execution.ErrorMessage));
        }

        if (execution.Relation is KustoRecordedRelationDescriptor relation)
        {
            writer.WritePropertyName("relation");
            writer.WriteStartObject();
            writer.WriteString("sourceTable", Truncate(relation.SourceTableName));
            writer.WriteBoolean("composable", relation.IsComposable);
            writer.WritePropertyName("columns");
            writer.WriteStartArray();
            foreach (KustoSourceColumnLineage column in relation.Columns.Take(MaximumCellCount))
            {
                writer.WriteStartObject();
                writer.WriteString("result", Truncate(column.ResultColumnName));
                writer.WriteString("source", Truncate(column.SourceColumnName));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteResultPage(
        Utf8JsonWriter writer,
        KustoRecordedExecution execution,
        KustoResultTable table,
        int tableOrdinal,
        int startRow,
        int rowCount)
    {
        int endRow = (int)Math.Min(table.Rows.Count, (long)startRow + rowCount);
        writer.WriteStartObject();
        writer.WriteString("executionId", execution.Id);
        writer.WriteString("executionName", Truncate(GetExecutionName(execution)));
        writer.WriteNumber("tableOrdinal", tableOrdinal);
        writer.WriteString("tableName", Truncate(table.Name));
        writer.WriteNumber("totalRowCount", table.Rows.Count);
        writer.WriteNumber("startRow", startRow);
        WriteColumns(writer, table);
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        for (int rowOrdinal = startRow; rowOrdinal < endRow; rowOrdinal++)
        {
            WriteRow(writer, execution, table, tableOrdinal, table.Rows[rowOrdinal], rowOrdinal, []);
        }

        writer.WriteEndArray();
        if (endRow < table.Rows.Count)
        {
            writer.WriteNumber("nextStartRow", endRow);
        }
        else
        {
            writer.WriteNull("nextStartRow");
        }

        writer.WriteEndObject();
    }

    private static void WriteRow(
        Utf8JsonWriter writer,
        KustoRecordedExecution execution,
        KustoResultTable table,
        int tableOrdinal,
        KustoResultRow row,
        int rowOrdinal,
        IReadOnlyCollection<int> matchingColumns)
    {
        int cellCount = Math.Min(
            MaximumCellCount,
            Math.Min(table.Columns.Count, row.ResultValues.Count));
        writer.WriteStartObject();
        writer.WriteNumber("rowOrdinal", rowOrdinal);
        writer.WritePropertyName("cells");
        writer.WriteStartArray();
        for (int columnOrdinal = 0; columnOrdinal < cellCount; columnOrdinal++)
        {
            WriteCell(
                writer,
                execution,
                table,
                tableOrdinal,
                row,
                rowOrdinal,
                columnOrdinal,
                matchingColumns.Contains(columnOrdinal));
        }

        writer.WriteEndArray();
        writer.WriteBoolean(
            "cellsTruncated",
            table.Columns.Count > MaximumCellCount || row.ResultValues.Count > MaximumCellCount);
        writer.WriteEndObject();
    }

    private static void WriteSearchResults(
        Utf8JsonWriter writer,
        string searchText,
        int limit,
        IReadOnlyList<RecordedRowMatch> matches,
        bool truncated)
    {
        writer.WriteStartObject();
        writer.WriteString("searchText", Truncate(searchText));
        writer.WriteNumber("matchLimit", limit);
        writer.WriteBoolean("matchesTruncated", truncated);
        writer.WritePropertyName("matches");
        writer.WriteStartArray();
        foreach (RecordedRowMatch match in matches)
        {
            writer.WriteStartObject();
            writer.WriteString("executionId", match.Execution.Id);
            writer.WriteString("executionName", Truncate(GetExecutionName(match.Execution)));
            writer.WriteNumber("tableOrdinal", match.TableOrdinal);
            writer.WriteString("tableName", Truncate(match.Table.Name));
            writer.WritePropertyName("row");
            WriteRow(
                writer,
                match.Execution,
                match.Table,
                match.TableOrdinal,
                match.Row,
                match.RowOrdinal,
                match.MatchingColumns);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private sealed class RecordedCell
    {
        internal RecordedCell(KustoResultColumn column, KustoResultValue value)
        {
            Column = column;
            Value = value;
        }

        internal KustoResultColumn Column { get; }

        internal KustoResultValue Value { get; }
    }

    private sealed class RecordedRowMatch
    {
        internal RecordedRowMatch(
            KustoRecordedExecution execution,
            KustoResultTable table,
            int tableOrdinal,
            KustoResultRow row,
            int rowOrdinal,
            IReadOnlyList<int> matchingColumns)
        {
            Execution = execution;
            Table = table;
            TableOrdinal = tableOrdinal;
            Row = row;
            RowOrdinal = rowOrdinal;
            MatchingColumns = matchingColumns;
        }

        internal KustoRecordedExecution Execution { get; }

        internal KustoResultTable Table { get; }

        internal int TableOrdinal { get; }

        internal KustoResultRow Row { get; }

        internal int RowOrdinal { get; }

        internal IReadOnlyList<int> MatchingColumns { get; }
    }

    private sealed class RecordedRowReference
    {
        internal RecordedRowReference(
            KustoRecordedExecution execution,
            KustoResultTable table,
            int tableOrdinal,
            KustoResultRow row,
            int rowOrdinal)
        {
            Execution = execution;
            Table = table;
            TableOrdinal = tableOrdinal;
            Row = row;
            RowOrdinal = rowOrdinal;
        }

        internal KustoRecordedExecution Execution { get; }

        internal KustoResultTable Table { get; }

        internal int TableOrdinal { get; }

        internal KustoResultRow Row { get; }

        internal int RowOrdinal { get; }
    }
}
