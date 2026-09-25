using OpenKustoExplorer.Graph;

#pragma warning disable SA1600

namespace OpenKustoExplorer.Portable.Graphs;

internal static class GraphSnapshotMapper
{
    internal static GraphIngestionDocument ToDocument(IGraphImportSource source)
    {
        GraphIngestion ingestion = source.Ingestion;
        return new GraphIngestionDocument
        {
            Id = ingestion.Id,
            SourceKind = ingestion.SourceKind,
            SourceId = ingestion.SourceId,
            SourceName = ingestion.SourceName,
            ClusterUri = ingestion.ClusterUri.AbsoluteUri,
            DatabaseName = ingestion.DatabaseName,
            QueryText = ingestion.QueryText,
            StartedAtUtc = ingestion.StartedAtUtc,
            CompletedAtUtc = ingestion.CompletedAtUtc,
            Evidence = source.GetEvidence().Select(ToDocument).ToList(),
            EntityObservations = source.GetEntityObservations().Select(ToDocument).ToList(),
            RelationshipObservations = source.GetRelationshipObservations().Select(ToDocument).ToList(),
        };
    }

    internal static GraphIngestion ToIngestion(GraphIngestionDocument document)
    {
        return new GraphIngestion(
            document.Id,
            document.SourceKind,
            document.SourceId,
            document.SourceName,
            new Uri(document.ClusterUri),
            document.DatabaseName,
            document.QueryText,
            document.StartedAtUtc,
            document.CompletedAtUtc);
    }

    internal static GraphEvidence ToEvidence(GraphEvidenceDocument document)
    {
        return new GraphEvidence(
            document.OccurrenceId,
            document.ContentHash,
            document.TableName,
            document.RowOrdinal,
            document.SchemaJson,
            document.RowJson);
    }

    internal static GraphEntityObservation ToObservation(GraphEntityObservationDocument document)
    {
        return new GraphEntityObservation(
            document.Id,
            ToEntityKey(document.Entity),
            document.DisplayLabel,
            document.SourceLabels,
            document.Properties.ToDictionary(
                property => property.Name,
                property => property.Value,
                StringComparer.Ordinal),
            ToTemporalInterval(document.TemporalInterval),
            document.EvidenceIds);
    }

    internal static GraphRelationshipObservation ToObservation(GraphRelationshipObservationDocument document)
    {
        return new GraphRelationshipObservation(
            document.Id,
            ToRelationshipKey(document.Relationship),
            document.SourceLabels,
            document.Properties.ToDictionary(
                property => property.Name,
                property => property.Value,
                StringComparer.Ordinal),
            ToTemporalInterval(document.TemporalInterval),
            document.EvidenceIds);
    }

    internal static GraphEntityKey ToEntityKey(GraphEntityKeyDocument document)
    {
        return new GraphEntityKey(
            document.Kind,
            document.TypeName,
            document.CanonicalId,
            document.SourceNamespace);
    }

    internal static GraphRelationshipKey ToRelationshipKey(GraphRelationshipKeyDocument document)
    {
        return new GraphRelationshipKey(
            ToEntityKey(document.Source),
            ToEntityKey(document.Target),
            document.TypeName,
            document.Discriminator);
    }

    private static GraphEvidenceDocument ToDocument(GraphEvidence evidence)
    {
        return new GraphEvidenceDocument
        {
            OccurrenceId = evidence.OccurrenceId,
            ContentHash = evidence.ContentHash,
            TableName = evidence.TableName,
            RowOrdinal = evidence.RowOrdinal,
            SchemaJson = evidence.SchemaJson,
            RowJson = evidence.RowJson,
        };
    }

    private static GraphEntityObservationDocument ToDocument(GraphEntityObservation observation)
    {
        return new GraphEntityObservationDocument
        {
            Id = observation.Id,
            Entity = ToDocument(observation.Entity),
            DisplayLabel = observation.DisplayLabel,
            SourceLabels = observation.SourceLabels.ToList(),
            Properties = observation.Properties
                .Select(property => new GraphPropertyDocument
                {
                    Name = property.Key,
                    Value = property.Value,
                })
                .ToList(),
            TemporalInterval = ToDocument(observation.TemporalInterval),
            EvidenceIds = observation.EvidenceIds.ToList(),
        };
    }

    private static GraphRelationshipObservationDocument ToDocument(GraphRelationshipObservation observation)
    {
        return new GraphRelationshipObservationDocument
        {
            Id = observation.Id,
            Relationship = ToDocument(observation.Relationship),
            SourceLabels = observation.SourceLabels.ToList(),
            Properties = observation.Properties
                .Select(property => new GraphPropertyDocument
                {
                    Name = property.Key,
                    Value = property.Value,
                })
                .ToList(),
            TemporalInterval = ToDocument(observation.TemporalInterval),
            EvidenceIds = observation.EvidenceIds.ToList(),
        };
    }

    private static GraphEntityKeyDocument ToDocument(GraphEntityKey entity)
    {
        return new GraphEntityKeyDocument
        {
            Kind = entity.Kind,
            TypeName = entity.TypeName,
            CanonicalId = entity.CanonicalId,
            SourceNamespace = entity.SourceNamespace,
        };
    }

    private static GraphRelationshipKeyDocument ToDocument(GraphRelationshipKey relationship)
    {
        return new GraphRelationshipKeyDocument
        {
            Source = ToDocument(relationship.Source),
            Target = ToDocument(relationship.Target),
            TypeName = relationship.TypeName,
            Discriminator = relationship.Discriminator,
        };
    }

    private static GraphTemporalIntervalDocument ToDocument(GraphTemporalInterval interval)
    {
        return new GraphTemporalIntervalDocument
        {
            DiscoveredAtUtc = interval.DiscoveredAtUtc,
            ValidFromUtc = interval.ValidFromUtc,
            ValidToUtc = interval.ValidToUtc,
            SupersededAtUtc = interval.SupersededAtUtc,
        };
    }

    private static GraphTemporalInterval ToTemporalInterval(GraphTemporalIntervalDocument document)
    {
        return new GraphTemporalInterval(
            document.DiscoveredAtUtc,
            document.ValidFromUtc,
            document.ValidToUtc,
            document.SupersededAtUtc);
    }
}

#pragma warning restore SA1600
