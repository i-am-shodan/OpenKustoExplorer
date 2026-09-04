using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents a user-defined Explorer folder containing one or more clusters.
/// </summary>
public sealed class KustoFolderViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoFolderViewModel"/> class.
    /// </summary>
    /// <param name="name">The user-defined folder name.</param>
    /// <param name="clusters">The clusters assigned to the folder.</param>
    internal KustoFolderViewModel(string name, IEnumerable<KustoClusterViewModel> clusters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clusters);

        Name = name;
        Clusters = Array.AsReadOnly(clusters.ToArray());
        VisibleClusters = new ObservableCollection<KustoClusterViewModel>(Clusters);
    }

    /// <summary>
    /// Gets the user-defined folder name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the clusters assigned to the folder.
    /// </summary>
    public IReadOnlyList<KustoClusterViewModel> Clusters { get; }

    /// <summary>
    /// Gets the clusters matching the active Explorer filter.
    /// </summary>
    public ObservableCollection<KustoClusterViewModel> VisibleClusters { get; }

    /// <summary>
    /// Gets the concise folder content summary.
    /// </summary>
    public string StatusText => Clusters.Count == 1 ? "1 cluster" : $"{Clusters.Count:N0} clusters";

    /// <summary>
    /// Applies an Explorer filter to folder descendants.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    internal void ApplyFilter(string filterText)
    {
        VisibleClusters.Clear();
        bool folderMatches = string.IsNullOrWhiteSpace(filterText)
            || Name.Contains(filterText, StringComparison.OrdinalIgnoreCase);

        foreach (KustoClusterViewModel cluster in Clusters)
        {
            cluster.ApplyFilter(folderMatches ? string.Empty : filterText);

            if (folderMatches || cluster.MatchesFilter(filterText))
            {
                VisibleClusters.Add(cluster);
            }
        }
    }
}
