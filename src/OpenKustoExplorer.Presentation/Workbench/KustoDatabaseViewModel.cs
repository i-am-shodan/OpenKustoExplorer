using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one database and its lazily loaded table schema in the Explorer tree.
/// </summary>
public sealed class KustoDatabaseViewModel : ObservableObject
{
    private bool isLoading;
    private KustoDatabaseSchema? schema;
    private string statusText;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDatabaseViewModel"/> class.
    /// </summary>
    /// <param name="clusterUri">The owning cluster URI.</param>
    /// <param name="connection">The persisted database connection.</param>
    internal KustoDatabaseViewModel(Uri clusterUri, KustoDatabaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentNullException.ThrowIfNull(connection);

        ClusterUri = clusterUri;
        Name = connection.Name;
        DisplayName = connection.DisplayName;
        Tables = new ObservableCollection<SchemaTableViewModel>();
        VisibleTables = new ObservableCollection<SchemaTableViewModel>();
        Functions = new ObservableCollection<SchemaFunctionViewModel>();
        VisibleSchemaItems = new ObservableCollection<object>();
        statusText = connection.Schema is null ? "Select to load schema" : "Schema cached";

        if (connection.Schema is not null)
        {
            ApplySchema(connection.Schema);
        }
    }

    /// <summary>
    /// Gets the absolute cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the case-sensitive database name used by Kusto requests.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the preferred database display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the loaded tables.
    /// </summary>
    public ObservableCollection<SchemaTableViewModel> Tables { get; }

    /// <summary>
    /// Gets the loaded tables matching the active Explorer filter.
    /// </summary>
    public ObservableCollection<SchemaTableViewModel> VisibleTables { get; }

    /// <summary>
    /// Gets the loaded stored functions.
    /// </summary>
    public ObservableCollection<SchemaFunctionViewModel> Functions { get; }

    /// <summary>
    /// Gets filtered function and table nodes in Explorer display order.
    /// </summary>
    public ObservableCollection<object> VisibleSchemaItems { get; }

    /// <summary>
    /// Gets the stored-functions folder, or <see langword="null"/> when no functions exist.
    /// </summary>
    public SchemaFunctionsFolderViewModel? FunctionsFolder { get; private set; }

    /// <summary>
    /// Gets the immutable loaded schema, or <see langword="null"/> before discovery completes.
    /// </summary>
    public KustoDatabaseSchema? Schema
    {
        get => schema;
        private set
        {
            if (SetProperty(ref schema, value))
            {
                OnPropertyChanged(nameof(IsSchemaLoaded));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether schema discovery is active.
    /// </summary>
    public bool IsLoading
    {
        get => isLoading;
        private set => SetProperty(ref isLoading, value);
    }

    /// <summary>
    /// Gets a value indicating whether a table schema has been loaded.
    /// </summary>
    public bool IsSchemaLoaded => Schema is not null;

    /// <summary>
    /// Gets the concise schema state shown under the database name.
    /// </summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>
    /// Applies an Explorer filter to loaded tables and columns.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    internal void ApplyFilter(string filterText)
    {
        VisibleTables.Clear();
        VisibleSchemaItems.Clear();

        if (FunctionsFolder is not null)
        {
            FunctionsFolder.ApplyFilter(filterText);
            if (FunctionsFolder.VisibleFunctions.Count > 0)
            {
                VisibleSchemaItems.Add(FunctionsFolder);
            }
        }

        foreach (SchemaTableViewModel table in Tables.Where(table => TableMatchesFilter(table, filterText)))
        {
            VisibleTables.Add(table);
            VisibleSchemaItems.Add(table);
        }
    }

    /// <summary>
    /// Replaces the loaded immutable schema and visible table nodes.
    /// </summary>
    /// <param name="databaseSchema">The discovered database schema.</param>
    internal void ApplySchema(KustoDatabaseSchema databaseSchema)
    {
        ArgumentNullException.ThrowIfNull(databaseSchema);

        Schema = databaseSchema;
        Tables.Clear();
        Functions.Clear();

        foreach (KustoTableSchema table in databaseSchema.Tables)
        {
            Tables.Add(new SchemaTableViewModel(table));
        }

        foreach (KustoFunctionSchema function in databaseSchema.Functions)
        {
            Functions.Add(new SchemaFunctionViewModel(function));
        }

        FunctionsFolder = Functions.Count == 0 ? null : new SchemaFunctionsFolderViewModel(Functions);

        ApplyFilter(string.Empty);
        StatusText = $"{Functions.Count:N0} functions · {Tables.Count:N0} tables";
    }

    /// <summary>
    /// Marks schema discovery as active.
    /// </summary>
    internal void BeginLoading()
    {
        IsLoading = true;
        StatusText = "Loading schema";
    }

    /// <summary>
    /// Creates an immutable persistence snapshot.
    /// </summary>
    /// <returns>The database connection snapshot.</returns>
    internal KustoDatabaseConnection CreateConnection()
    {
        return new KustoDatabaseConnection(Name, DisplayName, Schema);
    }

    /// <summary>
    /// Marks schema discovery as inactive.
    /// </summary>
    internal void EndLoading()
    {
        IsLoading = false;
    }

    /// <summary>
    /// Shows a schema-discovery error on the database node.
    /// </summary>
    /// <param name="message">The concise error message.</param>
    internal void ReportError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        StatusText = message;
    }

    /// <summary>
    /// Determines whether this database or a descendant matches a filter.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    /// <returns><see langword="true"/> when the database should remain visible.</returns>
    internal bool MatchesFilter(string filterText)
    {
        bool matches = string.IsNullOrWhiteSpace(filterText)
            || Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || (FunctionsFolder?.VisibleFunctions.Count > 0)
            || VisibleTables.Count > 0;

        return matches;
    }

    private static bool TableMatchesFilter(SchemaTableViewModel table, string filterText)
    {
        bool matches = string.IsNullOrWhiteSpace(filterText)
            || table.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || table.Columns.Any(column =>
                column.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || column.TypeName.Contains(filterText, StringComparison.OrdinalIgnoreCase));

        return matches;
    }
}
