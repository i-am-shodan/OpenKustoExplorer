using System.Text;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Portable.Assistance;

/// <summary>
/// Creates bounded text snapshots from browser-local investigation data after explicit consent.
/// </summary>
public sealed class KustoCopilotSharedDataBuilder
{
    /// <summary>Gets the maximum UTF-8 byte count sent as shared Copilot data.</summary>
    public const int MaximumSnapshotBytes = 64 * 1024;

    private const int MaximumCellLength = 500;
    private const int MaximumExecutionCount = 20;
    private const int MaximumGraphEntityCount = 200;
    private const int MaximumGraphPropertyCount = 25;
    private const int MaximumGraphRelationshipCount = 400;
    private const int MaximumResultColumnCount = 25;
    private const int MaximumResultRowCount = 10;
    private const int MaximumResultTableCount = 3;
    private readonly IGraphQueryService graphQueryService;
    private readonly IGraphStore graphStore;
    private readonly IKustoRecordedSessionStore recordedSessionStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotSharedDataBuilder"/> class.
    /// </summary>
    /// <param name="graphStore">The browser-local graph store.</param>
    /// <param name="graphQueryService">The browser-local graph query service.</param>
    /// <param name="recordedSessionStore">The browser-local recorded-session store.</param>
    public KustoCopilotSharedDataBuilder(
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        IKustoRecordedSessionStore recordedSessionStore)
    {
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(graphQueryService);
        ArgumentNullException.ThrowIfNull(recordedSessionStore);
        this.graphStore = graphStore;
        this.graphQueryService = graphQueryService;
        this.recordedSessionStore = recordedSessionStore;
    }

    /// <summary>
    /// Creates the complete bounded data payload for one Copilot turn.
    /// </summary>
    /// <param name="context">The current workbench context.</param>
    /// <param name="options">The explicit data-sharing choices.</param>
    /// <param name="cancellationToken">Cancels local snapshot creation.</param>
    /// <returns>The consented data text, bounded by <see cref="MaximumSnapshotBytes"/>.</returns>
    public async Task<string> CreateAsync(
        KustoCopilotContext context,
        KustoCopilotOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        BoundedTextBuilder output = new(MaximumSnapshotBytes);

        if (context.SharedDataText.Length > 0)
        {
            output.AppendLine("Consented result data:");
            output.AppendLine(context.SharedDataText);
        }

        if (!output.IsFull
            && context.ScopeKind == KustoCopilotScopeKind.Graph
            && options.ShareGraphData
            && context.GraphSnapshot is GraphSnapshot graphSnapshot)
        {
            await AppendGraphAsync(output, graphSnapshot, cancellationToken).ConfigureAwait(false);
        }

        if (!output.IsFull
            && context.ScopeKind == KustoCopilotScopeKind.RecordedSession
            && options.ShareRecordedSessionData
            && context.RecordedSessionScope is KustoCopilotRecordedSessionScope sessionScope)
        {
            await AppendRecordedSessionAsync(
                output,
                sessionScope.SessionId,
                cancellationToken).ConfigureAwait(false);
        }

        return output.ToString();
    }

    private static string Normalize(string value)
    {
        string normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return normalized.Length <= MaximumCellLength
            ? normalized
            : normalized[..MaximumCellLength];
    }

    private async Task AppendGraphAsync(
        BoundedTextBuilder output,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        GraphQuerySchema schema = await graphQueryService.GetSchemaAsync(
            snapshot,
            cancellationToken).ConfigureAwait(false);
        GraphViewport viewport = await graphStore.GetViewportAsync(
            snapshot,
            null,
            MaximumGraphEntityCount,
            MaximumGraphRelationshipCount,
            cancellationToken).ConfigureAwait(false);
        output.AppendLine("Consented graph snapshot:");
        output.AppendLine($"Nodes: {viewport.Entities.Count}; relationships: {viewport.Relationships.Count}; truncated: {viewport.IsTruncated}");
        output.AppendLine("Node labels:");

        foreach (GraphQuerySchemaEntry entry in schema.NodeLabels.Take(100))
        {
            output.AppendLine($"- {Normalize(entry.Name)} ({entry.Count}): {string.Join(", ", entry.PropertyNames.Take(100).Select(Normalize))}");
        }

        output.AppendLine("Relationship types:");
        foreach (GraphQuerySchemaEntry entry in schema.RelationshipTypes.Take(100))
        {
            output.AppendLine($"- {Normalize(entry.Name)} ({entry.Count}): {string.Join(", ", entry.PropertyNames.Take(100).Select(Normalize))}");
        }

        output.AppendLine("Nodes:");
        foreach (GraphEntitySummary entity in viewport.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.AppendLine($"- {entity.Entity.Kind}|{Normalize(entity.Entity.TypeName)}|{Normalize(entity.Entity.CanonicalId)}|{Normalize(entity.Entity.SourceNamespace)}|{Normalize(entity.DisplayLabel)}|degree={entity.Degree}");
            GraphEntityDetails? details = await graphStore.GetEntityDetailsAsync(
                snapshot,
                entity.Entity,
                cancellationToken).ConfigureAwait(false);
            if (details is not null)
            {
                foreach (GraphEntityProperty property in details.Properties.Take(MaximumGraphPropertyCount))
                {
                    output.AppendLine($"  {Normalize(property.Name)}={Normalize(property.Value)}");
                }
            }

            if (output.IsFull)
            {
                return;
            }
        }

        output.AppendLine("Relationships:");
        foreach (GraphRelationshipKey relationship in viewport.Relationships)
        {
            output.AppendLine($"- {Normalize(relationship.Source.ToString())} -> {Normalize(relationship.Target.ToString())} | {Normalize(relationship.TypeName)} | {Normalize(relationship.Discriminator)}");
            if (output.IsFull)
            {
                return;
            }
        }
    }

    private async Task AppendRecordedSessionAsync(
        BoundedTextBuilder output,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        KustoRecordedSession? session = await recordedSessionStore.GetSessionAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            output.AppendLine("Consented recorded-session data: unavailable");
            return;
        }

        output.AppendLine("Consented recorded-session result snapshot:");
        output.AppendLine($"Session: {Normalize(session.Summary.Name)}; executions: {session.Executions.Count}");
        foreach (KustoRecordedExecution execution in session.Executions.Take(MaximumExecutionCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.AppendLine($"Execution: {Normalize(execution.DisplayName ?? execution.DocumentTitle)}; status: {execution.Status}");
            IReadOnlyList<KustoResultTable> tables = execution.Result?.Tables ?? [];
            foreach (KustoResultTable table in tables.Take(MaximumResultTableCount))
            {
                KustoResultColumn[] columns = table.Columns.Take(MaximumResultColumnCount).ToArray();
                output.AppendLine($"Table: {Normalize(table.Name)}");
                output.AppendLine(string.Join('\t', columns.Select(column => Normalize(column.Name))));
                foreach (KustoResultRow row in table.Rows.Take(MaximumResultRowCount))
                {
                    output.AppendLine(string.Join(
                        '\t',
                        row.Values.Take(columns.Length).Select(Normalize)));
                }

                if (output.IsFull)
                {
                    return;
                }
            }
        }
    }

    private sealed class BoundedTextBuilder
    {
        private const string TruncationMarker = "\n(shared data truncated)";
        private static readonly int TruncationMarkerBytes = Encoding.UTF8.GetByteCount(TruncationMarker);
        private readonly StringBuilder builder = new();
        private readonly int maximumBytes;
        private int byteCount;

        public BoundedTextBuilder(int maximumBytes)
        {
            maximumBytes = Math.Max(maximumBytes, TruncationMarkerBytes);
            this.maximumBytes = maximumBytes;
        }

        public bool IsFull { get; private set; }

        public override string ToString() => builder.ToString();

        public void AppendLine(string value)
        {
            if (IsFull)
            {
                return;
            }

            Append(value);
            Append(Environment.NewLine);
        }

        private void Append(string value)
        {
            if (IsFull)
            {
                return;
            }

            foreach (Rune rune in value.EnumerateRunes())
            {
                if (byteCount + rune.Utf8SequenceLength > maximumBytes - TruncationMarkerBytes)
                {
                    builder.Append(TruncationMarker);
                    byteCount += TruncationMarkerBytes;
                    IsFull = true;
                    return;
                }

                builder.Append(rune);
                byteCount += rune.Utf8SequenceLength;
            }
        }
    }
}
