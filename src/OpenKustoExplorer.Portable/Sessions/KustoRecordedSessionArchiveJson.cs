using System.Text.Json;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Portable.Sessions;

/// <summary>
/// Reads and writes the JSON entries in a portable recorded-session archive.
/// </summary>
internal static class KustoRecordedSessionArchiveJson
{
    /// <summary>Gets the stable archive format discriminator.</summary>
    internal const string FormatName = "OpenKustoExplorer.RecordedSession";

    /// <summary>Gets the current archive schema version.</summary>
    internal const int CurrentVersion = 1;

    /// <summary>Writes an archive manifest.</summary>
    /// <param name="stream">The writable manifest stream.</param>
    /// <param name="session">The exported session.</param>
    /// <param name="exportedAtUtc">The archive creation time.</param>
    internal static void WriteManifest(
        Stream stream,
        KustoRecordedSession session,
        DateTimeOffset exportedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(session);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", FormatName);
        writer.WriteNumber("version", CurrentVersion);
        writer.WriteString("exportedAtUtc", exportedAtUtc.ToUniversalTime());
        writer.WriteString("sourceSessionId", session.Summary.Id);
        writer.WriteString("sourceSessionName", session.Summary.Name);
        writer.WriteEndObject();
    }

    /// <summary>Reads and validates an archive manifest.</summary>
    /// <param name="stream">The readable manifest stream.</param>
    /// <returns>The source session identity.</returns>
    internal static (Guid SessionId, string SessionName) ReadManifest(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream, CreateDocumentOptions());
        JsonElement root = RequireKind(document.RootElement, JsonValueKind.Object, "archive manifest");
        string format = ReadRequiredString(root, "format", allowEmpty: false);
        if (!string.Equals(format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported recorded-session archive format '{format}'.");
        }

        int version = ReadRequiredInt32(root, "version");
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported recorded-session archive version {version}.");
        }

        _ = ReadRequiredDateTimeOffset(root, "exportedAtUtc");
        return (
            ReadRequiredGuid(root, "sourceSessionId"),
            ReadRequiredString(root, "sourceSessionName", allowEmpty: false));
    }

    /// <summary>Writes one complete recorded-session payload.</summary>
    /// <param name="stream">The writable payload stream.</param>
    /// <param name="session">The session to write.</param>
    internal static void WriteSession(Stream stream, KustoRecordedSession session)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(session);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("id", session.Summary.Id);
        writer.WriteString("name", session.Summary.Name);
        writer.WriteString("createdAtUtc", session.Summary.CreatedAtUtc);
        writer.WriteString("lastUpdatedAtUtc", session.Summary.LastUpdatedAtUtc);
        WritePeriods(writer, session.Periods);
        WriteExecutions(writer, session.Executions);
        WriteInterests(writer, session.Interests);
        WriteMarks(writer, session.Marks);
        WriteEndpoints(writer, session.Endpoints);
        writer.WriteEndObject();
    }

    /// <summary>Reads one complete recorded-session payload.</summary>
    /// <param name="stream">The readable payload stream.</param>
    /// <returns>The recorded session.</returns>
    internal static KustoRecordedSession ReadSession(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream, CreateDocumentOptions());
        JsonElement root = RequireKind(document.RootElement, JsonValueKind.Object, "session payload");
        Guid sessionId = ReadRequiredGuid(root, "id");
        string name = ReadRequiredString(root, "name", allowEmpty: false);
        DateTimeOffset createdAtUtc = ReadRequiredDateTimeOffset(root, "createdAtUtc");
        DateTimeOffset lastUpdatedAtUtc = ReadRequiredDateTimeOffset(root, "lastUpdatedAtUtc");
        KustoRecordingPeriod[] periods = ReadRequiredArray(root, "periods")
            .EnumerateArray()
            .Select(element => ReadPeriod(element, sessionId))
            .ToArray();
        KustoRecordedExecution[] executions = ReadRequiredArray(root, "executions")
            .EnumerateArray()
            .Select(element => ReadExecution(element, sessionId))
            .ToArray();
        KustoRecordedInterest[] interests = ReadRequiredArray(root, "interests")
            .EnumerateArray()
            .Select(element => ReadInterest(element, sessionId))
            .ToArray();
        KustoRecordedMark[] marks = ReadRequiredArray(root, "marks")
            .EnumerateArray()
            .Select(element => ReadMark(element, sessionId))
            .ToArray();
        KustoChainEndpoint[] endpoints = ReadRequiredArray(root, "endpoints")
            .EnumerateArray()
            .Select(element => ReadEndpoint(element, sessionId))
            .ToArray();
        KustoRecordedSessionSummary summary = new(
            sessionId,
            name,
            createdAtUtc,
            lastUpdatedAtUtc,
            executions.Length,
            0);
        return new KustoRecordedSession(summary, periods, executions, interests, marks, endpoints);
    }

    private static JsonDocumentOptions CreateDocumentOptions()
    {
        return new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
            AllowTrailingCommas = false,
        };
    }

    private static void WritePeriods(Utf8JsonWriter writer, IReadOnlyList<KustoRecordingPeriod> periods)
    {
        writer.WritePropertyName("periods");
        writer.WriteStartArray();
        foreach (KustoRecordingPeriod period in periods)
        {
            writer.WriteStartObject();
            writer.WriteString("id", period.Id);
            writer.WriteString("startedAtUtc", period.StartedAtUtc);
            WriteNullableDateTimeOffset(writer, "stoppedAtUtc", period.StoppedAtUtc);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KustoRecordingPeriod ReadPeriod(JsonElement element, Guid sessionId)
    {
        RequireKind(element, JsonValueKind.Object, "recording period");
        return new KustoRecordingPeriod(
            ReadRequiredGuid(element, "id"),
            sessionId,
            ReadRequiredDateTimeOffset(element, "startedAtUtc"),
            ReadNullableDateTimeOffset(element, "stoppedAtUtc"));
    }

    private static void WriteExecutions(
        Utf8JsonWriter writer,
        IReadOnlyList<KustoRecordedExecution> executions)
    {
        writer.WritePropertyName("executions");
        writer.WriteStartArray();
        foreach (KustoRecordedExecution execution in executions)
        {
            writer.WriteStartObject();
            writer.WriteString("id", execution.Id);
            writer.WriteString("periodId", execution.PeriodId);
            writer.WriteNumber("sequence", execution.Sequence);
            writer.WriteString("documentId", execution.DocumentId);
            writer.WriteString("documentTitle", execution.DocumentTitle);
            WriteNullableString(writer, "displayName", execution.DisplayName);
            writer.WriteString("clusterUri", execution.ClusterUri.AbsoluteUri);
            writer.WriteString("databaseName", execution.DatabaseName);
            writer.WriteString("queryText", execution.QueryText);
            writer.WriteString("startedAtUtc", execution.StartedAtUtc);
            WriteNullableDateTimeOffset(writer, "completedAtUtc", execution.CompletedAtUtc);
            writer.WriteString("status", execution.Status.ToString());
            WriteNullableString(writer, "errorMessage", execution.ErrorMessage);
            WriteResult(writer, execution.Result);
            WriteRelation(writer, execution.Relation);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KustoRecordedExecution ReadExecution(JsonElement element, Guid sessionId)
    {
        RequireKind(element, JsonValueKind.Object, "recorded execution");
        string clusterUriText = ReadRequiredString(element, "clusterUri", allowEmpty: false);
        if (!Uri.TryCreate(clusterUriText, UriKind.Absolute, out Uri? clusterUri))
        {
            throw new InvalidDataException($"Recorded execution cluster URI '{clusterUriText}' is invalid.");
        }

        return new KustoRecordedExecution(
            ReadRequiredGuid(element, "id"),
            sessionId,
            ReadRequiredGuid(element, "periodId"),
            ReadRequiredInt64(element, "sequence"),
            ReadRequiredGuid(element, "documentId"),
            ReadRequiredString(element, "documentTitle", allowEmpty: false),
            clusterUri,
            ReadRequiredString(element, "databaseName", allowEmpty: false),
            ReadRequiredString(element, "queryText", allowEmpty: false),
            ReadRequiredDateTimeOffset(element, "startedAtUtc"),
            ReadNullableDateTimeOffset(element, "completedAtUtc"),
            ReadRequiredEnum<KustoRecordedExecutionStatus>(element, "status"),
            ReadNullableString(element, "errorMessage"),
            ReadResult(element),
            ReadRelation(element),
            ReadNullableString(element, "displayName"));
    }

    private static void WriteResult(Utf8JsonWriter writer, KustoQueryResult? result)
    {
        writer.WritePropertyName("result");
        if (result is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber("durationMilliseconds", result.Duration.TotalMilliseconds);
        writer.WriteString("completeness", result.Completeness.ToString());
        writer.WritePropertyName("tables");
        writer.WriteStartArray();
        foreach (KustoResultTable table in result.Tables)
        {
            WriteResultTable(writer, table);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoQueryResult? ReadResult(JsonElement executionElement)
    {
        JsonElement element = ReadRequiredProperty(executionElement, "result");
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireKind(element, JsonValueKind.Object, "recorded result");
        double durationMilliseconds = ReadRequiredDouble(element, "durationMilliseconds");
        if (!double.IsFinite(durationMilliseconds) || durationMilliseconds < 0)
        {
            throw new InvalidDataException("Recorded result duration must be a finite non-negative number.");
        }

        KustoResultTable[] tables = ReadRequiredArray(element, "tables")
            .EnumerateArray()
            .Select(ReadResultTable)
            .ToArray();
        return new KustoQueryResult(
            tables,
            TimeSpan.FromMilliseconds(durationMilliseconds),
            completeness: ReadRequiredEnum<KustoQueryResultCompleteness>(element, "completeness"));
    }

    private static void WriteResultTable(Utf8JsonWriter writer, KustoResultTable table)
    {
        writer.WriteStartObject();
        writer.WriteString("name", table.Name);
        writer.WritePropertyName("columns");
        writer.WriteStartArray();
        foreach (KustoResultColumn column in table.Columns)
        {
            writer.WriteStartObject();
            writer.WriteString("name", column.Name);
            writer.WriteString("typeName", column.TypeName);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (KustoResultRow row in table.Rows)
        {
            writer.WriteStartArray();
            foreach (KustoResultValue value in row.ResultValues)
            {
                writer.WriteStartObject();
                writer.WriteString("displayText", value.DisplayText);
                WriteNullableString(writer, "rawJson", value.RawJson);
                writer.WriteBoolean("isNull", value.IsNull);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoResultTable ReadResultTable(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "result table");
        KustoResultColumn[] columns = ReadRequiredArray(element, "columns")
            .EnumerateArray()
            .Select(column => new KustoResultColumn(
                ReadRequiredString(column, "name", allowEmpty: false),
                ReadRequiredString(column, "typeName", allowEmpty: false)))
            .ToArray();
        KustoResultRow[] rows = ReadRequiredArray(element, "rows")
            .EnumerateArray()
            .Select(row => ReadResultRow(row, columns.Length))
            .ToArray();
        return new KustoResultTable(
            ReadRequiredString(element, "name", allowEmpty: false),
            columns,
            rows);
    }

    private static KustoResultRow ReadResultRow(JsonElement element, int columnCount)
    {
        RequireKind(element, JsonValueKind.Array, "result row");
        KustoResultValue[] values = element.EnumerateArray()
            .Select(ReadResultValue)
            .ToArray();
        if (values.Length != columnCount)
        {
            throw new InvalidDataException("A recorded result row does not match its column count.");
        }

        return new KustoResultRow(values);
    }

    private static KustoResultValue ReadResultValue(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "result value");
        return new KustoResultValue(
            ReadRequiredString(element, "displayText", allowEmpty: true),
            ReadNullableString(element, "rawJson"),
            ReadRequiredBoolean(element, "isNull"));
    }

    private static void WriteRelation(Utf8JsonWriter writer, KustoRecordedRelationDescriptor? relation)
    {
        writer.WritePropertyName("relation");
        if (relation is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("sourceTableName", relation.SourceTableName);
        writer.WriteBoolean("isComposable", relation.IsComposable);
        writer.WritePropertyName("columns");
        writer.WriteStartArray();
        foreach (KustoSourceColumnLineage column in relation.Columns)
        {
            writer.WriteStartObject();
            writer.WriteString("resultColumnName", column.ResultColumnName);
            writer.WriteString("sourceColumnName", column.SourceColumnName);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoRecordedRelationDescriptor? ReadRelation(JsonElement executionElement)
    {
        JsonElement element = ReadRequiredProperty(executionElement, "relation");
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireKind(element, JsonValueKind.Object, "recorded relation");
        KustoSourceColumnLineage[] columns = ReadRequiredArray(element, "columns")
            .EnumerateArray()
            .Select(column => new KustoSourceColumnLineage(
                ReadRequiredString(column, "resultColumnName", allowEmpty: false),
                ReadRequiredString(column, "sourceColumnName", allowEmpty: false)))
            .ToArray();
        return new KustoRecordedRelationDescriptor(
            ReadRequiredString(element, "sourceTableName", allowEmpty: false),
            ReadRequiredBoolean(element, "isComposable"),
            columns);
    }

    private static void WriteInterests(Utf8JsonWriter writer, IReadOnlyList<KustoRecordedInterest> interests)
    {
        writer.WritePropertyName("interests");
        writer.WriteStartArray();
        foreach (KustoRecordedInterest interest in interests)
        {
            writer.WriteStartObject();
            writer.WriteString("id", interest.Id);
            writer.WriteString("declaredExecutionId", interest.DeclaredExecutionId);
            writer.WriteString("source", interest.Source.ToString());
            writer.WriteString("columnName", interest.ColumnName);
            writer.WritePropertyName("identity");
            writer.WriteStartObject();
            writer.WriteString("typeName", interest.Identity.TypeName);
            writer.WriteString("canonicalValue", interest.Identity.CanonicalValue);
            writer.WriteBoolean("isNull", interest.Identity.IsNull);
            writer.WriteEndObject();
            WriteCoordinate(writer, "coordinate", interest.Coordinate);
            WriteNullableInt32(writer, "literalStart", interest.LiteralStart);
            WriteNullableInt32(writer, "literalLength", interest.LiteralLength);
            WriteNullableGuid(writer, "markId", interest.MarkId);
            writer.WriteBoolean("isSuppressed", interest.IsSuppressed);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KustoRecordedInterest ReadInterest(JsonElement element, Guid sessionId)
    {
        RequireKind(element, JsonValueKind.Object, "recorded interest");
        JsonElement identityElement = RequireKind(
            ReadRequiredProperty(element, "identity"),
            JsonValueKind.Object,
            "recorded value identity");
        KustoRecordedValueIdentity identity = new(
            ReadRequiredString(identityElement, "typeName", allowEmpty: false),
            ReadRequiredString(identityElement, "canonicalValue", allowEmpty: true),
            ReadRequiredBoolean(identityElement, "isNull"));
        return new KustoRecordedInterest(
            ReadRequiredGuid(element, "id"),
            sessionId,
            ReadRequiredGuid(element, "declaredExecutionId"),
            ReadRequiredEnum<KustoRecordedInterestSource>(element, "source"),
            ReadRequiredString(element, "columnName", allowEmpty: false),
            identity,
            ReadCoordinate(element, "coordinate"),
            ReadNullableInt32(element, "literalStart"),
            ReadNullableInt32(element, "literalLength"),
            ReadRequiredBoolean(element, "isSuppressed"),
            ReadNullableGuid(element, "markId"));
    }

    private static void WriteMarks(Utf8JsonWriter writer, IReadOnlyList<KustoRecordedMark> marks)
    {
        writer.WritePropertyName("marks");
        writer.WriteStartArray();
        foreach (KustoRecordedMark mark in marks)
        {
            writer.WriteStartObject();
            writer.WriteString("id", mark.Id);
            writer.WriteString("kind", mark.Kind.ToString());
            WriteCoordinate(writer, "coordinate", mark.Coordinate);
            writer.WriteString("createdAtUtc", mark.CreatedAtUtc);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KustoRecordedMark ReadMark(JsonElement element, Guid sessionId)
    {
        RequireKind(element, JsonValueKind.Object, "recorded mark");
        KustoRecordedValueCoordinate coordinate = ReadCoordinate(element, "coordinate")
            ?? throw new InvalidDataException("A recorded mark requires a result coordinate.");
        return new KustoRecordedMark(
            ReadRequiredGuid(element, "id"),
            sessionId,
            ReadRequiredEnum<KustoRecordedMarkKind>(element, "kind"),
            coordinate,
            ReadRequiredDateTimeOffset(element, "createdAtUtc"));
    }

    private static void WriteEndpoints(Utf8JsonWriter writer, IReadOnlyList<KustoChainEndpoint> endpoints)
    {
        writer.WritePropertyName("endpoints");
        writer.WriteStartArray();
        foreach (KustoChainEndpoint endpoint in endpoints)
        {
            writer.WriteStartObject();
            writer.WriteString("role", endpoint.Role.ToString());
            WriteCoordinate(writer, "coordinate", endpoint.Coordinate);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KustoChainEndpoint ReadEndpoint(JsonElement element, Guid sessionId)
    {
        RequireKind(element, JsonValueKind.Object, "chain endpoint");
        KustoRecordedValueCoordinate coordinate = ReadCoordinate(element, "coordinate")
            ?? throw new InvalidDataException("A chain endpoint requires a result coordinate.");
        return new KustoChainEndpoint(
            sessionId,
            ReadRequiredEnum<KustoChainEndpointRole>(element, "role"),
            coordinate);
    }

    private static void WriteCoordinate(
        Utf8JsonWriter writer,
        string propertyName,
        KustoRecordedValueCoordinate? coordinate)
    {
        writer.WritePropertyName(propertyName);
        if (coordinate is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("executionId", coordinate.ExecutionId);
        writer.WriteNumber("tableOrdinal", coordinate.TableOrdinal);
        writer.WriteNumber("rowOrdinal", coordinate.RowOrdinal);
        writer.WriteNumber("columnOrdinal", coordinate.ColumnOrdinal);
        writer.WriteEndObject();
    }

    private static KustoRecordedValueCoordinate? ReadCoordinate(JsonElement element, string propertyName)
    {
        JsonElement coordinate = ReadRequiredProperty(element, propertyName);
        if (coordinate.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireKind(coordinate, JsonValueKind.Object, "recorded value coordinate");
        return new KustoRecordedValueCoordinate(
            ReadRequiredGuid(coordinate, "executionId"),
            ReadRequiredInt32(coordinate, "tableOrdinal"),
            ReadRequiredInt32(coordinate, "rowOrdinal"),
            ReadRequiredInt32(coordinate, "columnOrdinal"));
    }

    private static JsonElement ReadRequiredProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new InvalidDataException($"The required property '{propertyName}' is missing.");
        }

        return property;
    }

    private static JsonElement ReadRequiredArray(JsonElement element, string propertyName)
    {
        return RequireKind(ReadRequiredProperty(element, propertyName), JsonValueKind.Array, propertyName);
    }

    private static JsonElement RequireKind(JsonElement element, JsonValueKind kind, string description)
    {
        if (element.ValueKind != kind)
        {
            throw new InvalidDataException($"The {description} must be a JSON {kind.ToString().ToLowerInvariant()}.");
        }

        return element;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName, bool allowEmpty)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.String || property.GetString() is not string value)
        {
            throw new InvalidDataException($"The property '{propertyName}' must be a string.");
        }

        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"The property '{propertyName}' cannot be empty.");
        }

        return value;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw new InvalidDataException($"The property '{propertyName}' must be a string or null.");
    }

    private static Guid ReadRequiredGuid(JsonElement element, string propertyName)
    {
        string value = ReadRequiredString(element, propertyName, allowEmpty: false);
        return Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException($"The property '{propertyName}' must be a non-empty GUID.");
    }

    private static Guid? ReadNullableGuid(JsonElement element, string propertyName)
    {
        string? value = ReadNullableString(element, propertyName);
        if (value is null)
        {
            return null;
        }

        return Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException($"The property '{propertyName}' must be a non-empty GUID or null.");
    }

    private static DateTimeOffset ReadRequiredDateTimeOffset(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.String
            && property.TryGetDateTimeOffset(out DateTimeOffset value)
                ? value.ToUniversalTime()
                : throw new InvalidDataException($"The property '{propertyName}' must be an ISO-8601 timestamp.");
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            && property.TryGetDateTimeOffset(out DateTimeOffset value)
                ? value.ToUniversalTime()
                : throw new InvalidDataException($"The property '{propertyName}' must be an ISO-8601 timestamp or null.");
    }

    private static int ReadRequiredInt32(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)
            ? value
            : throw new InvalidDataException($"The property '{propertyName}' must be a 32-bit integer.");
    }

    private static long ReadRequiredInt64(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long value)
            ? value
            : throw new InvalidDataException($"The property '{propertyName}' must be a 64-bit integer.");
    }

    private static int? ReadNullableInt32(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)
            ? value
            : throw new InvalidDataException($"The property '{propertyName}' must be a 32-bit integer or null.");
    }

    private static double ReadRequiredDouble(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double value)
            ? value
            : throw new InvalidDataException($"The property '{propertyName}' must be a number.");
    }

    private static bool ReadRequiredBoolean(JsonElement element, string propertyName)
    {
        JsonElement property = ReadRequiredProperty(element, propertyName);
        return property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : throw new InvalidDataException($"The property '{propertyName}' must be a Boolean.");
    }

    private static TEnum ReadRequiredEnum<TEnum>(JsonElement element, string propertyName)
        where TEnum : struct, Enum
    {
        string value = ReadRequiredString(element, propertyName, allowEmpty: false);
        return Enum.TryParse(value, ignoreCase: false, out TEnum parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"The property '{propertyName}' has unsupported value '{value}'.");
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableGuid(Utf8JsonWriter writer, string propertyName, Guid? value)
    {
        if (value is Guid guid)
        {
            writer.WriteString(propertyName, guid);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteNullableDateTimeOffset(
        Utf8JsonWriter writer,
        string propertyName,
        DateTimeOffset? value)
    {
        if (value is DateTimeOffset timestamp)
        {
            writer.WriteString(propertyName, timestamp);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteNullableInt32(Utf8JsonWriter writer, string propertyName, int? value)
    {
        if (value is int number)
        {
            writer.WriteNumber(propertyName, number);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }
}
