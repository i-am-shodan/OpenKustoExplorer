using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Language;

namespace OpenKustoExplorer.Infrastructure.Sessions;

/// <summary>
/// Finds weighted paths through typed recorded result occurrences and exact query conversions.
/// </summary>
public sealed class KustoRecordedChainSearcher : IKustoRecordedChainSearcher
{
    private const int MaximumTransformations = 32;
    private const int MaximumVisitedOccurrences = 50_000;
    private readonly IKustoRecordedSessionStore store;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedChainSearcher"/> class.
    /// </summary>
    /// <param name="store">The recorded-session store.</param>
    public KustoRecordedChainSearcher(IKustoRecordedSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <inheritdoc />
    public async Task<KustoQueryChain?> FindAsync(
        Guid sessionId,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        return await FindCoreAsync(
            sessionId,
            start,
            destination,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<KustoQueryChain?> FindAsync(
        Guid sessionId,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate destination,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseSchema);
        return await FindCoreAsync(
            sessionId,
            start,
            destination,
            databaseSchema,
            cancellationToken).ConfigureAwait(false);
    }

    private static SearchGraph BuildGraph(
        KustoRecordedSession session,
        KustoDatabaseSchema? databaseSchema,
        CancellationToken cancellationToken)
    {
        Dictionary<CoordinateKey, Occurrence> occurrences = [];
        Dictionary<RowKey, List<Occurrence>> rows = [];
        Dictionary<KustoRecordedValueIdentity, List<Occurrence>> identities = [];
        Dictionary<Guid, long> executionSequences = session.Executions.ToDictionary(
            execution => execution.Id,
            execution => execution.Sequence);
        IReadOnlyDictionary<Guid, KustoRecordedRelationDescriptor?> relations = CreateRelations(
            session,
            databaseSchema,
            cancellationToken);

        foreach (KustoRecordedExecution execution in session.Executions.Where(
            execution => execution.Status == KustoRecordedExecutionStatus.Succeeded && execution.Result is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddExecutionOccurrences(
                execution,
                relations.GetValueOrDefault(execution.Id),
                occurrences,
                rows,
                identities);
        }

        foreach (List<Occurrence> matchingOccurrences in identities.Values)
        {
            matchingOccurrences.Sort(OccurrenceComparer.Instance);
        }

        HashSet<CoordinateKey> markedCells = session.Marks
            .Where(mark => mark.Kind == KustoRecordedMarkKind.Cell)
            .Select(mark => CoordinateKey.Create(mark.Coordinate))
            .ToHashSet();
        markedCells.UnionWith(session.Endpoints.Select(endpoint => CoordinateKey.Create(endpoint.Coordinate)));
        Dictionary<KustoRecordedValueIdentity, List<InterestDeclaration>> declarations = CreateDeclarations(
            session.Interests,
            executionSequences);
        Dictionary<KustoRecordedValueIdentity, List<PredicateTransformation>> predicateTransformations =
            CreatePredicateTransformations(session, occurrences, relations);

        Dictionary<KustoRecordedValueIdentity, int> executionFrequency = identities.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(value => value.Coordinate.ExecutionId).Distinct().Count());
        int commonalityLimit = Math.Max(5, Math.Min(50, (int)Math.Ceiling(session.Executions.Count * 0.2)));
        return new SearchGraph(
            occurrences,
            rows,
            identities,
            markedCells,
            declarations,
            predicateTransformations,
            executionFrequency,
            commonalityLimit);
    }

    private static void AddExecutionOccurrences(
        KustoRecordedExecution execution,
        KustoRecordedRelationDescriptor? relation,
        IDictionary<CoordinateKey, Occurrence> occurrences,
        IDictionary<RowKey, List<Occurrence>> rows,
        IDictionary<KustoRecordedValueIdentity, List<Occurrence>> identities)
    {
        for (int tableOrdinal = 0; tableOrdinal < execution.Result!.Tables.Count; tableOrdinal++)
        {
            KustoResultTable table = execution.Result.Tables[tableOrdinal];
            AddTableOccurrences(execution, relation, table, tableOrdinal, occurrences, rows, identities);
        }
    }

    private static void AddTableOccurrences(
        KustoRecordedExecution execution,
        KustoRecordedRelationDescriptor? relation,
        KustoResultTable table,
        int tableOrdinal,
        IDictionary<CoordinateKey, Occurrence> occurrences,
        IDictionary<RowKey, List<Occurrence>> rows,
        IDictionary<KustoRecordedValueIdentity, List<Occurrence>> identities)
    {
        for (int rowOrdinal = 0; rowOrdinal < table.Rows.Count; rowOrdinal++)
        {
            List<Occurrence> rowOccurrences = CreateRowOccurrences(
                execution,
                relation,
                table,
                tableOrdinal,
                rowOrdinal,
                occurrences,
                identities);
            rows.Add(new RowKey(execution.Id, tableOrdinal, rowOrdinal), rowOccurrences);
        }
    }

    private static List<Occurrence> CreateRowOccurrences(
        KustoRecordedExecution execution,
        KustoRecordedRelationDescriptor? relation,
        KustoResultTable table,
        int tableOrdinal,
        int rowOrdinal,
        IDictionary<CoordinateKey, Occurrence> occurrences,
        IDictionary<KustoRecordedValueIdentity, List<Occurrence>> identities)
    {
        KustoResultRow row = table.Rows[rowOrdinal];
        List<Occurrence> rowOccurrences = [];
        for (int columnOrdinal = 0; columnOrdinal < table.Columns.Count; columnOrdinal++)
        {
            KustoResultColumn column = table.Columns[columnOrdinal];
            KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(
                column.TypeName,
                row.ResultValues[columnOrdinal]);
            CoordinateKey coordinate = new(execution.Id, tableOrdinal, rowOrdinal, columnOrdinal);
            Occurrence occurrence = new(
                coordinate,
                execution.Sequence,
                relation?.SourceTableName,
                column.Name,
                FindSourceColumn(relation, column.Name),
                identity);
            occurrences.Add(coordinate, occurrence);
            rowOccurrences.Add(occurrence);
            if (!identities.TryGetValue(identity, out List<Occurrence>? matchingOccurrences))
            {
                matchingOccurrences = [];
                identities.Add(identity, matchingOccurrences);
            }

            matchingOccurrences.Add(occurrence);
        }

        return rowOccurrences;
    }

    private static Dictionary<Guid, KustoRecordedRelationDescriptor?> CreateRelations(
        KustoRecordedSession session,
        KustoDatabaseSchema? databaseSchema,
        CancellationToken cancellationToken)
    {
        KustoRecordedRelationExtractor? extractor = databaseSchema is null
            ? null
            : new KustoRecordedRelationExtractor();
        return session.Executions.ToDictionary(
            execution => execution.Id,
            execution => execution.Relation
                ?? (execution.Status == KustoRecordedExecutionStatus.Succeeded && execution.Result is not null
                    ? extractor?.Extract(
                        execution.QueryText,
                        databaseSchema!,
                        cancellationToken)
                    : null));
    }

    private static Dictionary<KustoRecordedValueIdentity, List<InterestDeclaration>> CreateDeclarations(
        IReadOnlyList<KustoRecordedInterest> interests,
        IReadOnlyDictionary<Guid, long> executionSequences)
    {
        Dictionary<KustoRecordedValueIdentity, List<InterestDeclaration>> declarations = [];
        foreach (KustoRecordedInterest interest in interests.Where(interest => !interest.IsSuppressed))
        {
            if (!declarations.TryGetValue(interest.Identity, out List<InterestDeclaration>? values))
            {
                values = [];
                declarations.Add(interest.Identity, values);
            }

            values.Add(new InterestDeclaration(
                executionSequences.GetValueOrDefault(interest.DeclaredExecutionId, long.MaxValue),
                interest.DeclaredExecutionId,
                interest.Source));
        }

        return declarations;
    }

    private static Dictionary<KustoRecordedValueIdentity, List<PredicateTransformation>>
        CreatePredicateTransformations(
            KustoRecordedSession session,
            IReadOnlyDictionary<CoordinateKey, Occurrence> occurrences,
            IReadOnlyDictionary<Guid, KustoRecordedRelationDescriptor?> relations)
    {
        Dictionary<KustoRecordedValueIdentity, List<PredicateTransformation>> transformations = [];
        Dictionary<Guid, KustoRecordedExecution> executions = session.Executions.ToDictionary(
            execution => execution.Id);
        foreach (IGrouping<Guid, KustoRecordedInterest> group in session.Interests
            .Where(interest => !interest.IsSuppressed
                && interest.Source == KustoRecordedInterestSource.QueryPredicate)
            .GroupBy(interest => interest.DeclaredExecutionId))
        {
            KustoRecordedInterest[] predicates = group.ToArray();
            KustoRecordedRelationDescriptor? relation = relations.GetValueOrDefault(group.Key);
            if (predicates.Length != 1
                || !executions.TryGetValue(group.Key, out KustoRecordedExecution? execution)
                || execution.Status != KustoRecordedExecutionStatus.Succeeded
                || relation?.IsComposable != true)
            {
                continue;
            }

            KustoRecordedInterest predicate = predicates[0];
            Occurrence[] outputs = occurrences.Values
                .Where(occurrence => occurrence.Coordinate.ExecutionId == execution.Id
                    && !occurrence.Identity.Equals(predicate.Identity)
                    && occurrence.SourceColumnName is not null)
                .ToArray();
            if (outputs.Length == 0)
            {
                continue;
            }

            if (!transformations.TryGetValue(
                predicate.Identity,
                out List<PredicateTransformation>? matchingTransformations))
            {
                matchingTransformations = [];
                transformations.Add(predicate.Identity, matchingTransformations);
            }

            matchingTransformations.Add(new PredicateTransformation(
                execution.Id,
                execution.Sequence,
                relation,
                predicate.ColumnName,
                outputs));
        }

        return transformations;
    }

    private static KustoQueryChain? Search(
        Guid sessionId,
        SearchGraph graph,
        CoordinateKey start,
        CoordinateKey end,
        CancellationToken cancellationToken)
    {
        IComparer<SearchPriority> comparer = Comparer<SearchPriority>.Create((left, right) => left.CompareTo(right));
        PriorityQueue<CoordinateKey, SearchPriority> pending = new(comparer);
        Dictionary<CoordinateKey, SearchScore> scores = new() { [start] = SearchScore.Zero };
        Dictionary<CoordinateKey, Predecessor> predecessors = [];
        pending.Enqueue(start, SearchPriority.Create(SearchScore.Zero, graph.Occurrences[start]));
        int visited = 0;

        while (pending.TryDequeue(out CoordinateKey current, out SearchPriority priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!scores.TryGetValue(current, out SearchScore currentScore)
                || priority.Score.CompareTo(currentScore) != 0)
            {
                continue;
            }

            if (++visited > MaximumVisitedOccurrences)
            {
                return null;
            }

            if (current == end)
            {
                return BuildChain(sessionId, start, end, currentScore, predecessors);
            }

            Occurrence currentOccurrence = graph.Occurrences[current];
            AddIdentityCarryNeighbor(graph, currentOccurrence, currentScore, scores, predecessors, pending);
            if (currentScore.Transformations < MaximumTransformations)
            {
                AddTransformNeighbors(graph, currentOccurrence, currentScore, scores, predecessors, pending);
                AddPredicateTransformNeighbors(
                    graph,
                    currentOccurrence,
                    currentScore,
                    scores,
                    predecessors,
                    pending);
            }
        }

        return null;
    }

    private static void AddIdentityCarryNeighbor(
        SearchGraph graph,
        Occurrence current,
        SearchScore score,
        IDictionary<CoordinateKey, SearchScore> scores,
        IDictionary<CoordinateKey, Predecessor> predecessors,
        PriorityQueue<CoordinateKey, SearchPriority> pending)
    {
        List<Occurrence> matching = graph.Identities[current.Identity];
        int index = matching.FindIndex(candidate => candidate.Coordinate == current.Coordinate);
        if (index >= 0 && index + 1 < matching.Count)
        {
            Occurrence next = matching[index + 1];
            TryUpdate(next, score, current.Coordinate, null, scores, predecessors, pending);
        }
    }

    private static void AddTransformNeighbors(
        SearchGraph graph,
        Occurrence current,
        SearchScore score,
        IDictionary<CoordinateKey, SearchScore> scores,
        IDictionary<CoordinateKey, Predecessor> predecessors,
        PriorityQueue<CoordinateKey, SearchPriority> pending)
    {
        RowKey rowKey = new(
            current.Coordinate.ExecutionId,
            current.Coordinate.TableOrdinal,
            current.Coordinate.RowOrdinal);
        foreach (Occurrence target in graph.Rows[rowKey])
        {
            if (target.Coordinate == current.Coordinate || target.Identity.Equals(current.Identity))
            {
                continue;
            }

            (KustoPivotEvidenceKind Kind, int Cost) classification = Classify(graph, current, target);
            if (classification.Kind == KustoPivotEvidenceKind.GenericCorrelation
                && (!IsEligibleGeneric(current.Identity, graph) || !IsEligibleGeneric(target.Identity, graph)))
            {
                continue;
            }

            KustoPivotEvidence evidence = new(
                current.Coordinate.ExecutionId,
                current.Sequence,
                current.SourceTableName,
                current.Coordinate.ToModel(),
                target.Coordinate.ToModel(),
                current.ColumnName,
                target.ColumnName,
                current.SourceColumnName,
                target.SourceColumnName,
                current.Identity,
                target.Identity,
                classification.Kind,
                classification.Cost);
            int frequency = graph.ExecutionFrequency.GetValueOrDefault(target.Identity, int.MaxValue / 4);
            SearchScore candidate = new(
                score.Cost + classification.Cost,
                score.Transformations + 1,
                score.Frequency + frequency);
            TryUpdate(
                target,
                candidate,
                current.Coordinate,
                evidence,
                scores,
                predecessors,
                pending);
        }
    }

    private static void AddPredicateTransformNeighbors(
        SearchGraph graph,
        Occurrence current,
        SearchScore score,
        IDictionary<CoordinateKey, SearchScore> scores,
        IDictionary<CoordinateKey, Predecessor> predecessors,
        PriorityQueue<CoordinateKey, SearchPriority> pending)
    {
        if (!graph.PredicateTransformations.TryGetValue(
            current.Identity,
            out List<PredicateTransformation>? transformations))
        {
            return;
        }

        foreach (PredicateTransformation transformation in transformations)
        {
            foreach (Occurrence target in transformation.Outputs)
            {
                (KustoPivotEvidenceKind Kind, int Cost)? classification =
                    ClassifyPredicateTransformationOutput(graph, transformation, target);
                if (classification is null)
                {
                    continue;
                }

                KustoPivotEvidence evidence = new(
                    transformation.ExecutionId,
                    transformation.Sequence,
                    transformation.Relation.SourceTableName,
                    current.Coordinate.ToModel(),
                    target.Coordinate.ToModel(),
                    transformation.InputColumnName,
                    target.ColumnName,
                    transformation.InputColumnName,
                    target.SourceColumnName,
                    current.Identity,
                    target.Identity,
                    classification.Value.Kind,
                    classification.Value.Cost);
                int frequency = graph.ExecutionFrequency.GetValueOrDefault(target.Identity, int.MaxValue / 4);
                SearchScore candidate = new(
                    score.Cost + classification.Value.Cost,
                    score.Transformations + 1,
                    score.Frequency + frequency);
                TryUpdate(
                    target,
                    candidate,
                    current.Coordinate,
                    evidence,
                    scores,
                    predecessors,
                    pending);
            }
        }
    }

    private static (KustoPivotEvidenceKind Kind, int Cost)? ClassifyPredicateTransformationOutput(
        SearchGraph graph,
        PredicateTransformation transformation,
        Occurrence output)
    {
        if (graph.MarkedCells.Contains(output.Coordinate)
            || HasMarkedDeclarationAfter(graph, output.Identity, transformation.Sequence))
        {
            return (KustoPivotEvidenceKind.PredicateToManual, 1);
        }

        if (HasPredicateDeclarationInOtherExecution(
            graph,
            output.Identity,
            transformation.ExecutionId))
        {
            return (KustoPivotEvidenceKind.PredicateToPredicate, 1);
        }

        return null;
    }

    private static (KustoPivotEvidenceKind Kind, int Cost) Classify(
        SearchGraph graph,
        Occurrence input,
        Occurrence output)
    {
        bool outputMarked = graph.MarkedCells.Contains(output.Coordinate);
        bool outputMarkedLater = HasMarkedDeclarationAfter(graph, output.Identity, output.Sequence);
        bool outputSupported = outputMarked || outputMarkedLater;
        bool sameExecutionPredicate = HasPredicateDeclaration(
            graph,
            input.Identity,
            input.Coordinate.ExecutionId);
        bool priorInterest = HasDeclarationAtOrBefore(graph, input.Identity, input.Sequence);
        bool laterInterest = HasDeclarationAfter(graph, input.Identity, input.Sequence);

        if (outputSupported && sameExecutionPredicate)
        {
            return (KustoPivotEvidenceKind.PredicateToManual, 1);
        }

        if (outputSupported && priorInterest)
        {
            return (KustoPivotEvidenceKind.PriorInterestToManual, 2);
        }

        if (outputSupported && laterInterest)
        {
            return (KustoPivotEvidenceKind.RetrospectiveToManual, 4);
        }

        return (KustoPivotEvidenceKind.GenericCorrelation, 8);
    }

    private static bool HasPredicateDeclaration(
        SearchGraph graph,
        KustoRecordedValueIdentity identity,
        Guid executionId)
    {
        return graph.Declarations.TryGetValue(identity, out List<InterestDeclaration>? declarations)
            && declarations.Exists(declaration =>
                declaration.ExecutionId == executionId
                && declaration.Source == KustoRecordedInterestSource.QueryPredicate);
    }

    private static bool HasPredicateDeclarationInOtherExecution(
        SearchGraph graph,
        KustoRecordedValueIdentity identity,
        Guid executionId)
    {
        return graph.Declarations.TryGetValue(identity, out List<InterestDeclaration>? declarations)
            && declarations.Exists(declaration =>
                declaration.ExecutionId != executionId
                && declaration.Source == KustoRecordedInterestSource.QueryPredicate);
    }

    private static bool HasDeclarationAtOrBefore(
        SearchGraph graph,
        KustoRecordedValueIdentity identity,
        long sequence)
    {
        return graph.Declarations.TryGetValue(identity, out List<InterestDeclaration>? declarations)
            && declarations.Exists(declaration => declaration.Sequence <= sequence);
    }

    private static bool HasDeclarationAfter(
        SearchGraph graph,
        KustoRecordedValueIdentity identity,
        long sequence)
    {
        return graph.Declarations.TryGetValue(identity, out List<InterestDeclaration>? declarations)
            && declarations.Exists(declaration => declaration.Sequence > sequence);
    }

    private static bool HasMarkedDeclarationAfter(
        SearchGraph graph,
        KustoRecordedValueIdentity identity,
        long sequence)
    {
        return graph.Declarations.TryGetValue(identity, out List<InterestDeclaration>? declarations)
            && declarations.Exists(declaration =>
                declaration.Sequence > sequence
                && declaration.Source is KustoRecordedInterestSource.ManualCell
                    or KustoRecordedInterestSource.ConfirmedPredicate);
    }

    private static bool IsEligibleGeneric(KustoRecordedValueIdentity identity, SearchGraph graph)
    {
        bool isEligible = !identity.IsNull
            && identity.TypeName is "string" or "guid" or "int" or "long"
            && !string.IsNullOrWhiteSpace(identity.CanonicalValue)
            && identity.CanonicalValue.Length <= 4 * 1024
            && graph.ExecutionFrequency.GetValueOrDefault(identity, int.MaxValue) <= graph.CommonalityLimit;
        if (isEligible && identity.TypeName == "string")
        {
            isEligible = identity.CanonicalValue.Length >= 3;
        }

        if (isEligible
            && identity.TypeName is "int" or "long"
            && long.TryParse(
                identity.CanonicalValue,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long integer))
        {
            isEligible = integer is not -1 and not 0 and not 1;
        }

        return isEligible;
    }

    private static void TryUpdate(
        Occurrence target,
        SearchScore candidate,
        CoordinateKey previous,
        KustoPivotEvidence? evidence,
        IDictionary<CoordinateKey, SearchScore> scores,
        IDictionary<CoordinateKey, Predecessor> predecessors,
        PriorityQueue<CoordinateKey, SearchPriority> pending)
    {
        if (!scores.TryGetValue(target.Coordinate, out SearchScore existing)
            || candidate.CompareTo(existing) < 0)
        {
            scores[target.Coordinate] = candidate;
            predecessors[target.Coordinate] = new Predecessor(previous, evidence);
            pending.Enqueue(target.Coordinate, SearchPriority.Create(candidate, target));
        }
    }

    private static KustoQueryChain BuildChain(
        Guid sessionId,
        CoordinateKey start,
        CoordinateKey end,
        SearchScore score,
        IReadOnlyDictionary<CoordinateKey, Predecessor> predecessors)
    {
        List<KustoPivotEvidence> pivots = [];
        CoordinateKey current = end;
        while (current != start)
        {
            Predecessor predecessor = predecessors[current];
            if (predecessor.Evidence is not null)
            {
                pivots.Add(predecessor.Evidence);
            }

            current = predecessor.Previous;
        }

        pivots.Reverse();
        return new KustoQueryChain(sessionId, start.ToModel(), end.ToModel(), pivots, score.Cost);
    }

    private static string? FindSourceColumn(KustoRecordedRelationDescriptor? relation, string resultColumnName)
    {
        return relation?.Columns.FirstOrDefault(column => string.Equals(
            column.ResultColumnName,
            resultColumnName,
            StringComparison.OrdinalIgnoreCase))?.SourceColumnName;
    }

    private async Task<KustoQueryChain?> FindCoreAsync(
        Guid sessionId,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate destination,
        KustoDatabaseSchema? databaseSchema,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(destination);
        KustoRecordedSession? session = await store.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        SearchGraph graph = BuildGraph(session, databaseSchema, cancellationToken);
        CoordinateKey startKey = CoordinateKey.Create(start);
        CoordinateKey endKey = CoordinateKey.Create(destination);
        if (!graph.Occurrences.TryGetValue(startKey, out Occurrence? startOccurrence)
            || !graph.Occurrences.TryGetValue(endKey, out Occurrence? endOccurrence)
            || endOccurrence.Sequence < startOccurrence.Sequence)
        {
            return null;
        }

        return Search(sessionId, graph, startKey, endKey, cancellationToken);
    }

    private readonly record struct InterestDeclaration(
        long Sequence,
        Guid ExecutionId,
        KustoRecordedInterestSource Source);

    private readonly record struct RowKey(Guid ExecutionId, int TableOrdinal, int RowOrdinal);

    private readonly record struct CoordinateKey(
        Guid ExecutionId,
        int TableOrdinal,
        int RowOrdinal,
        int ColumnOrdinal) : IComparable<CoordinateKey>
    {
        public int CompareTo(CoordinateKey other)
        {
            int comparison = ExecutionId.CompareTo(other.ExecutionId);
            if (comparison == 0)
            {
                comparison = TableOrdinal.CompareTo(other.TableOrdinal);
            }

            if (comparison == 0)
            {
                comparison = RowOrdinal.CompareTo(other.RowOrdinal);
            }

            return comparison != 0 ? comparison : ColumnOrdinal.CompareTo(other.ColumnOrdinal);
        }

        internal static CoordinateKey Create(KustoRecordedValueCoordinate coordinate)
        {
            return new CoordinateKey(
                coordinate.ExecutionId,
                coordinate.TableOrdinal,
                coordinate.RowOrdinal,
                coordinate.ColumnOrdinal);
        }

        internal KustoRecordedValueCoordinate ToModel()
        {
            return new KustoRecordedValueCoordinate(ExecutionId, TableOrdinal, RowOrdinal, ColumnOrdinal);
        }
    }

    private readonly record struct SearchScore(int Cost, int Transformations, int Frequency) : IComparable<SearchScore>
    {
        public static SearchScore Zero => new(0, 0, 0);

        public int CompareTo(SearchScore other)
        {
            int comparison = Cost.CompareTo(other.Cost);
            if (comparison == 0)
            {
                comparison = Transformations.CompareTo(other.Transformations);
            }

            return comparison != 0 ? comparison : Frequency.CompareTo(other.Frequency);
        }
    }

    private readonly record struct SearchPriority(
        SearchScore Score,
        long Sequence,
        CoordinateKey Coordinate) : IComparable<SearchPriority>
    {
        public int CompareTo(SearchPriority other)
        {
            int comparison = Score.CompareTo(other.Score);
            if (comparison == 0)
            {
                comparison = Sequence.CompareTo(other.Sequence);
            }

            return comparison != 0 ? comparison : Coordinate.CompareTo(other.Coordinate);
        }

        internal static SearchPriority Create(SearchScore score, Occurrence occurrence)
        {
            return new SearchPriority(score, occurrence.Sequence, occurrence.Coordinate);
        }
    }

    private sealed record Occurrence(
        CoordinateKey Coordinate,
        long Sequence,
        string? SourceTableName,
        string ColumnName,
        string? SourceColumnName,
        KustoRecordedValueIdentity Identity);

    private sealed record SearchGraph(
        Dictionary<CoordinateKey, Occurrence> Occurrences,
        Dictionary<RowKey, List<Occurrence>> Rows,
        Dictionary<KustoRecordedValueIdentity, List<Occurrence>> Identities,
        HashSet<CoordinateKey> MarkedCells,
        Dictionary<KustoRecordedValueIdentity, List<InterestDeclaration>> Declarations,
        Dictionary<KustoRecordedValueIdentity, List<PredicateTransformation>> PredicateTransformations,
        Dictionary<KustoRecordedValueIdentity, int> ExecutionFrequency,
        int CommonalityLimit);

    private sealed record PredicateTransformation(
        Guid ExecutionId,
        long Sequence,
        KustoRecordedRelationDescriptor Relation,
        string InputColumnName,
        IReadOnlyList<Occurrence> Outputs);

    private sealed record Predecessor(CoordinateKey Previous, KustoPivotEvidence? Evidence);

    private sealed class OccurrenceComparer : IComparer<Occurrence>
    {
        internal static readonly OccurrenceComparer Instance = new();

        public int Compare(Occurrence? left, Occurrence? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            int comparison = left.Sequence.CompareTo(right.Sequence);
            return comparison != 0 ? comparison : left.Coordinate.CompareTo(right.Coordinate);
        }
    }
}
