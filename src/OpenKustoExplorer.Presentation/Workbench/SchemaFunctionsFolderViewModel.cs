using System.Collections.ObjectModel;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents the stored-functions folder beneath one database.
/// </summary>
public sealed class SchemaFunctionsFolderViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaFunctionsFolderViewModel"/> class.
    /// </summary>
    /// <param name="functions">The database's stored functions.</param>
    public SchemaFunctionsFolderViewModel(IEnumerable<SchemaFunctionViewModel> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);

        Name = "Functions";
        Functions = Array.AsReadOnly(functions.ToArray());
        VisibleFunctions = new ObservableCollection<SchemaFunctionViewModel>(Functions);
    }

    /// <summary>
    /// Gets the folder display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets every stored function in the database.
    /// </summary>
    public IReadOnlyList<SchemaFunctionViewModel> Functions { get; }

    /// <summary>
    /// Gets stored functions matching the active Explorer filter.
    /// </summary>
    public ObservableCollection<SchemaFunctionViewModel> VisibleFunctions { get; }

    /// <summary>
    /// Gets the concise function count.
    /// </summary>
    public string StatusText => Functions.Count == 1 ? "1 function" : $"{Functions.Count:N0} functions";

    /// <summary>
    /// Applies an Explorer filter to stored functions.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    internal void ApplyFilter(string filterText)
    {
        VisibleFunctions.Clear();
        bool folderMatches = string.IsNullOrWhiteSpace(filterText)
            || Name.Contains(filterText, StringComparison.OrdinalIgnoreCase);

        foreach (SchemaFunctionViewModel function in Functions
            .Where(function => folderMatches || function.MatchesFilter(filterText)))
        {
            VisibleFunctions.Add(function);
        }
    }
}
