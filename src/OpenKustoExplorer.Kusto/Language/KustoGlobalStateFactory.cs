using Kusto.Language;
using Kusto.Language.Symbols;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Kusto.Language;

/// <summary>
/// Creates Kusto binding state from the application-owned schema model.
/// </summary>
internal static class KustoGlobalStateFactory
{
    /// <summary>
    /// Creates one immutable global state for a database schema.
    /// </summary>
    /// <param name="databaseSchema">The database schema to bind.</param>
    /// <returns>The configured Kusto global state.</returns>
    internal static GlobalState Create(KustoDatabaseSchema databaseSchema)
    {
        ArgumentNullException.ThrowIfNull(databaseSchema);
        IEnumerable<Symbol> tableSymbols = databaseSchema.Tables
            .Select(CreateTableSymbol)
            .Cast<Symbol>();
        IEnumerable<Symbol> functionSymbols = databaseSchema.Functions
            .Select(CreateFunctionSymbol)
            .Cast<Symbol>();
        DatabaseSymbol databaseSymbol = new(
            databaseSchema.DatabaseName,
            tableSymbols.Concat(functionSymbols).ToArray());
        ClusterSymbol clusterSymbol = new(databaseSchema.ClusterName, databaseSymbol);
        return GlobalState.Default
            .WithCluster(clusterSymbol)
            .WithDatabase(databaseSymbol);
    }

    private static FunctionSymbol CreateFunctionSymbol(KustoFunctionSchema functionSchema)
    {
        return new FunctionSymbol(
            functionSchema.Name,
            functionSchema.Parameters,
            functionSchema.Body,
            Tabularity.Tabular,
            functionSchema.Documentation);
    }

    private static TableSymbol CreateTableSymbol(KustoTableSchema tableSchema)
    {
        ColumnSymbol[] columnSymbols = tableSchema.Columns
            .Select(column => new ColumnSymbol(column.Name, GetScalarType(column.Type)))
            .ToArray();
        return new TableSymbol(tableSchema.Name, columnSymbols);
    }

    private static ScalarSymbol GetScalarType(KustoScalarType scalarType)
    {
        return scalarType switch
        {
            KustoScalarType.Bool => ScalarTypes.Bool,
            KustoScalarType.DateTime => ScalarTypes.DateTime,
            KustoScalarType.FixedPoint => ScalarTypes.Decimal,
            KustoScalarType.Dynamic => ScalarTypes.Dynamic,
            KustoScalarType.Identifier => ScalarTypes.Guid,
            KustoScalarType.WholeNumber => ScalarTypes.Int,
            KustoScalarType.WideInteger => ScalarTypes.Long,
            KustoScalarType.Real => ScalarTypes.Real,
            KustoScalarType.Text => ScalarTypes.String,
            KustoScalarType.TimeSpan => ScalarTypes.TimeSpan,
            _ => throw new ArgumentOutOfRangeException(nameof(scalarType), scalarType, "Unsupported Kusto scalar type."),
        };
    }
}
