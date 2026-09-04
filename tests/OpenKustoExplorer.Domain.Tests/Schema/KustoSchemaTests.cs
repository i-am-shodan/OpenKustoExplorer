using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Domain.Tests.Schema;

/// <summary>
/// Verifies validation and immutability guarantees for Kusto schema snapshots.
/// </summary>
public sealed class KustoSchemaTests
{
    /// <summary>
    /// Verifies that a column requires a nonempty name.
    /// </summary>
    [Fact]
    public void ColumnRejectsWhitespaceName()
    {
        Assert.Throws<ArgumentException>(() => new KustoColumnSchema(" ", KustoScalarType.Text));
    }

    /// <summary>
    /// Verifies that a table requires a column collection.
    /// </summary>
    [Fact]
    public void TableRejectsNullColumns()
    {
        Assert.Throws<ArgumentNullException>(() => new KustoTableSchema("StormEvents", null!));
    }

    /// <summary>
    /// Verifies that a table snapshots its input collection instead of retaining mutable storage.
    /// </summary>
    [Fact]
    public void TableCopiesColumnCollection()
    {
        KustoColumnSchema originalColumn = new("State", KustoScalarType.Text);
        KustoColumnSchema replacementColumn = new("EventType", KustoScalarType.Text);
        KustoColumnSchema[] mutableColumns = [originalColumn];

        KustoTableSchema tableSchema = new("StormEvents", mutableColumns);
        mutableColumns[0] = replacementColumn;

        KustoColumnSchema storedColumn = Assert.Single(tableSchema.Columns);
        Assert.Same(originalColumn, storedColumn);
    }

    /// <summary>
    /// Verifies that a database requires a table collection.
    /// </summary>
    [Fact]
    public void DatabaseRejectsNullTables()
    {
        Assert.Throws<ArgumentNullException>(
            () => new KustoDatabaseSchema("help.kusto.windows.net", "Samples", null!));
    }

    /// <summary>
    /// Verifies that a database snapshots its input collection instead of retaining mutable storage.
    /// </summary>
    [Fact]
    public void DatabaseCopiesTableCollection()
    {
        KustoTableSchema originalTable = new("StormEvents", []);
        KustoTableSchema replacementTable = new("PopulationData", []);
        KustoTableSchema[] mutableTables = [originalTable];

        KustoDatabaseSchema databaseSchema = new(
            "help.kusto.windows.net",
            "Samples",
            mutableTables);
        mutableTables[0] = replacementTable;

        KustoTableSchema storedTable = Assert.Single(databaseSchema.Tables);
        Assert.Same(originalTable, storedTable);
    }
}
