using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Infrastructure.Sessions;

/// <summary>
/// Converts strong pivot evidence into a minimized source-relation plan.
/// </summary>
public sealed class KustoRecordedRelationPlanner : IKustoRecordedRelationPlanner
{
    /// <inheritdoc />
    public KustoRelationalChainPlan? CreatePlan(KustoRecordedSession session, KustoQueryChain chain)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Pivots.Count == 0 || chain.Pivots.Any(pivot => pivot.Cost > 2))
        {
            return null;
        }

        Dictionary<Guid, KustoRecordedExecution> executions = session.Executions.ToDictionary(
            execution => execution.Id);
        List<KustoRelationalChainStep> steps = [];
        Uri? clusterUri = null;
        string? databaseName = null;

        foreach (KustoPivotEvidence pivot in chain.Pivots)
        {
            if (!executions.TryGetValue(pivot.ExecutionId, out KustoRecordedExecution? execution)
                || pivot.SourceTableName is null
                || pivot.InputSourceColumnName is null
                || pivot.OutputSourceColumnName is null)
            {
                return null;
            }

            clusterUri ??= execution.ClusterUri;
            databaseName ??= execution.DatabaseName;
            if (!TargetsMatch(clusterUri, databaseName, execution))
            {
                return null;
            }

            KustoRelationalChainStep step = new(
                execution.Id,
                execution.QueryText,
                pivot.InputCoordinate.ExecutionId != pivot.ExecutionId,
                pivot.SourceTableName,
                pivot.InputSourceColumnName,
                pivot.OutputSourceColumnName,
                pivot.Input,
                pivot.Output);
            if (steps.Count == 0 || !AreEquivalent(steps[^1], step))
            {
                steps.Add(step);
            }
        }

        if (steps.Count == 0 || !HasContinuousValues(steps))
        {
            return null;
        }

        return new KustoRelationalChainPlan(
            session.Summary.Id,
            clusterUri!,
            databaseName!,
            steps);
    }

    private static bool TargetsMatch(
        Uri clusterUri,
        string databaseName,
        KustoRecordedExecution execution)
    {
        return string.Equals(
                clusterUri.GetLeftPart(UriPartial.Authority),
                execution.ClusterUri.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(databaseName, execution.DatabaseName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEquivalent(KustoRelationalChainStep left, KustoRelationalChainStep right)
    {
        return string.Equals(left.SourceTableName, right.SourceTableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.InputColumnName, right.InputColumnName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.OutputColumnName, right.OutputColumnName, StringComparison.OrdinalIgnoreCase)
            && left.UsesQueryPipeline == right.UsesQueryPipeline;
    }

    private static bool HasContinuousValues(List<KustoRelationalChainStep> steps)
    {
        for (int index = 1; index < steps.Count; index++)
        {
            if (!steps[index - 1].Output.Equals(steps[index].Input))
            {
                return false;
            }
        }

        return true;
    }
}
