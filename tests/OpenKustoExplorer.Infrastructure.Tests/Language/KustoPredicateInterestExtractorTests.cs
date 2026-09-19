using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Language;

/// <summary>
/// Verifies schema-bound extraction of stable values from exact KQL predicates.
/// </summary>
public sealed class KustoPredicateInterestExtractorTests
{
    /// <summary>
    /// Verifies security investigation identifiers are extracted with source coordinates.
    /// </summary>
    [Fact]
    public void ExtractReturnsExactSecurityIdentifierPredicates()
    {
        const string Query = "OutboundBrowsing | where url == \"https://malware.example.test/payload\" and src_ip == \"192.0.2.56\"";
        KustoPredicateInterestExtractor extractor = new();

        IReadOnlyList<KustoPredicateInterest> interests = extractor.Extract(Query, CreateSchema());

        Assert.Equal(2, interests.Count);
        Assert.Equal("url", interests[0].ColumnName);
        Assert.Equal("https://malware.example.test/payload", interests[0].Value);
        Assert.Equal("string", interests[0].TypeName);
        Assert.Equal("src_ip", interests[1].ColumnName);
        Assert.Equal("192.0.2.56", interests[1].Value);
        Assert.Equal(
            "\"192.0.2.56\"",
            Query.Substring(interests[1].LiteralStart, interests[1].LiteralLength));
    }

    /// <summary>
    /// Verifies reversed equality and literal in-lists are extracted.
    /// </summary>
    [Fact]
    public void ExtractSupportsReversedEqualityAndLiteralInList()
    {
        const string Query = "Employees | where \"synthetic-user\" == username and employee_id in (42, 99)";
        KustoPredicateInterestExtractor extractor = new();

        IReadOnlyList<KustoPredicateInterest> interests = extractor.Extract(Query, CreateSchema());

        Assert.Equal(["synthetic-user", "42", "99"], interests.Select(interest => interest.Value));
        Assert.Equal(["username", "employee_id", "employee_id"], interests.Select(interest => interest.ColumnName));
    }

    /// <summary>
    /// Verifies broad, unstable, and unresolved values are not inferred as interests.
    /// </summary>
    [Fact]
    public void ExtractRejectsUnsafeAndUnresolvedPredicates()
    {
        const string Query = "Employees | where event_time == datetime(2026-09-06) and enabled == true and employee_id == 1 and username == \"x\" and missing == \"value\"";
        KustoPredicateInterestExtractor extractor = new();

        IReadOnlyList<KustoPredicateInterest> interests = extractor.Extract(Query, CreateSchema());

        Assert.Empty(interests);
    }

    /// <summary>
    /// Verifies predicates inside disjunctions are not treated as stable identity constraints.
    /// </summary>
    [Fact]
    public void ExtractRejectsDisjunctivePredicates()
    {
        const string Query = "Employees | where username == \"synthetic-user\" or username == \"other-user\"";
        KustoPredicateInterestExtractor extractor = new();

        IReadOnlyList<KustoPredicateInterest> interests = extractor.Extract(Query, CreateSchema());

        Assert.Empty(interests);
    }

    /// <summary>
    /// Verifies a direct table filter exposes conservative source-column lineage.
    /// </summary>
    [Fact]
    public void RelationExtractorAcceptsSimpleTableFilter()
    {
        const string Query = "OutboundBrowsing | where url == \"https://malware.example.test/payload\"";
        KustoRecordedRelationExtractor extractor = new();

        KustoRecordedRelationDescriptor? relation = extractor.Extract(Query, CreateSchema());

        Assert.NotNull(relation);
        Assert.Equal("OutboundBrowsing", relation.SourceTableName);
        Assert.True(relation.IsComposable);
        Assert.Contains(relation.Columns, column => column.ResultColumnName == "src_ip" && column.SourceColumnName == "src_ip");
    }

    /// <summary>
    /// Verifies aggregating transforms are not treated as safely composable relations.
    /// </summary>
    [Fact]
    public void RelationExtractorRejectsAggregatingPipeline()
    {
        const string Query = "OutboundBrowsing | where url == \"value.example\" | summarize Events=count() by src_ip";
        KustoRecordedRelationExtractor extractor = new();

        KustoRecordedRelationDescriptor? relation = extractor.Extract(Query, CreateSchema());

        Assert.Null(relation);
    }

    /// <summary>
    /// Verifies group-only summaries and expanded lookup joins expose conversion output lineage.
    /// </summary>
    [Fact]
    public void RelationExtractorAcceptsSyntheticConversionPipelines()
    {
        const string IpToFingerprintQuery = """
            SyntheticNetworkEvents
            | where ClientIp == "192.0.2.10"
            | where ProtocolFingerprint != ""
            | summarize by ProtocolFingerprint
            """;
        const string FingerprintToUsernameQuery = """
            SyntheticNetworkEvents
            | where ['ProtocolFingerprint'] == 'SYNTHETIC-FINGERPRINT-0001'
            | mv-expand PublicKeys
            | summarize by tostring(PublicKeys)
            | join SyntheticPublicKeyOwners on $left.PublicKeys == $right.PublicKey
            """;
        KustoRecordedRelationExtractor extractor = new();
        KustoDatabaseSchema schema = CreateSyntheticConversionSchema();

        KustoRecordedRelationDescriptor? ipToFingerprint = extractor.Extract(IpToFingerprintQuery, schema);
        KustoRecordedRelationDescriptor? fingerprintToUsername = extractor.Extract(
            FingerprintToUsernameQuery,
            schema);

        Assert.NotNull(ipToFingerprint);
        Assert.Equal("SyntheticNetworkEvents", ipToFingerprint.SourceTableName);
        Assert.Contains(
            ipToFingerprint.Columns,
            column => column.ResultColumnName == "ProtocolFingerprint");
        Assert.NotNull(fingerprintToUsername);
        Assert.Equal("SyntheticNetworkEvents", fingerprintToUsername.SourceTableName);
        Assert.Contains(
            fingerprintToUsername.Columns,
            column => column.ResultColumnName == "Username");
    }

    /// <summary>
    /// Verifies disjunctive filters cannot be simplified into generated source relations.
    /// </summary>
    [Fact]
    public void RelationExtractorRejectsDisjunctiveFilter()
    {
        const string Query = "Employees | where username == \"synthetic-user\" or username == \"other-user\"";
        KustoRecordedRelationExtractor extractor = new();

        KustoRecordedRelationDescriptor? relation = extractor.Extract(Query, CreateSchema());

        Assert.Null(relation);
    }

    /// <summary>
    /// Verifies non-equality and multi-statement queries cannot become conversion pipelines.
    /// </summary>
    [Fact]
    public void RelationExtractorRejectsUnsupportedConversionInputs()
    {
        const string InQuery = "OutboundBrowsing | where url in (\"value.example\") | summarize by src_ip";
        const string MultiStatementQuery = "let source = OutboundBrowsing; source | where url == \"value.example\"";
        KustoRecordedRelationExtractor extractor = new();

        Assert.Null(extractor.Extract(InQuery, CreateSchema()));
        Assert.Null(extractor.Extract(MultiStatementQuery, CreateSchema()));
    }

    private static KustoDatabaseSchema CreateSchema()
    {
        KustoTableSchema outboundBrowsing = new(
            "OutboundBrowsing",
            [
                new KustoColumnSchema("url", KustoScalarType.Text),
                new KustoColumnSchema("src_ip", KustoScalarType.Text),
            ]);
        KustoTableSchema employees = new(
            "Employees",
            [
                new KustoColumnSchema("username", KustoScalarType.Text),
                new KustoColumnSchema("employee_id", KustoScalarType.WideInteger),
                new KustoColumnSchema("event_time", KustoScalarType.DateTime),
                new KustoColumnSchema("enabled", KustoScalarType.Bool),
            ]);
        return new KustoDatabaseSchema(
            "mock.kusto.example",
            "SyntheticSecurity",
            [outboundBrowsing, employees]);
    }

    private static KustoDatabaseSchema CreateSyntheticConversionSchema()
    {
        return new KustoDatabaseSchema(
            "mock.kusto.example",
            "SyntheticSecurity",
            [
                new KustoTableSchema(
                    "SyntheticNetworkEvents",
                    [
                        new KustoColumnSchema("ClientIp", KustoScalarType.Text),
                        new KustoColumnSchema("ProtocolFingerprint", KustoScalarType.Text),
                        new KustoColumnSchema("PublicKeys", KustoScalarType.Dynamic),
                    ]),
                new KustoTableSchema(
                    "SyntheticPublicKeyOwners",
                    [
                        new KustoColumnSchema("PublicKey", KustoScalarType.Text),
                        new KustoColumnSchema("Username", KustoScalarType.Text),
                    ]),
            ]);
    }
}
