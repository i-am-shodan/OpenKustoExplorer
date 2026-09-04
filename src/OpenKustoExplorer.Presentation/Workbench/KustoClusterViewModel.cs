using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one Azure Data Explorer cluster and its accessible databases in the Explorer tree.
/// </summary>
public sealed class KustoClusterViewModel : ObservableObject
{
    private string? folderName;
    private bool isRefreshing;
    private string statusText;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoClusterViewModel"/> class.
    /// </summary>
    /// <param name="connection">The persisted cluster connection.</param>
    /// <param name="refreshAction">Refreshes accessible databases.</param>
    /// <param name="removeAction">Removes the cluster from the catalog.</param>
    /// <param name="organizeAction">Opens the folder organizer for this cluster.</param>
    internal KustoClusterViewModel(
        KustoClusterConnection connection,
        Func<KustoClusterViewModel, CancellationToken, Task> refreshAction,
        Action<KustoClusterViewModel> removeAction,
        Action<KustoClusterViewModel> organizeAction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(refreshAction);
        ArgumentNullException.ThrowIfNull(removeAction);
        ArgumentNullException.ThrowIfNull(organizeAction);

        ClusterUri = connection.ClusterUri;
        DisplayName = connection.DisplayName;
        folderName = connection.FolderName;
        Databases = new ObservableCollection<KustoDatabaseViewModel>(
            connection.Databases.Select(database => new KustoDatabaseViewModel(connection.ClusterUri, database)));
        VisibleDatabases = new ObservableCollection<KustoDatabaseViewModel>(Databases);
        statusText = GetDatabaseSummary(Databases.Count);
        RefreshCommand = new AsyncRelayCommand(
            cancellationToken => refreshAction(this, cancellationToken),
            () => !IsRefreshing);
        RemoveCommand = new RelayCommand(() => removeAction(this));
        OrganizeCommand = new RelayCommand(() => organizeAction(this));
    }

    /// <summary>
    /// Gets the absolute HTTPS cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the cluster display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the optional user-defined Explorer folder name.
    /// </summary>
    public string? FolderName
    {
        get => folderName;
        private set => SetProperty(ref folderName, value);
    }

    /// <summary>
    /// Gets the accessible databases.
    /// </summary>
    public ObservableCollection<KustoDatabaseViewModel> Databases { get; }

    /// <summary>
    /// Gets the databases matching the active Explorer filter.
    /// </summary>
    public ObservableCollection<KustoDatabaseViewModel> VisibleDatabases { get; }

    /// <summary>
    /// Gets the command that refreshes the cluster database list.
    /// </summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Gets the command that removes the cluster from the persisted catalog.
    /// </summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>
    /// Gets the command that moves this cluster into or out of a user folder.
    /// </summary>
    public IRelayCommand OrganizeCommand { get; }

    /// <summary>
    /// Gets a value indicating whether database discovery is active.
    /// </summary>
    public bool IsRefreshing
    {
        get => isRefreshing;
        private set
        {
            if (SetProperty(ref isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the concise cluster state shown under the display name.
    /// </summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>
    /// Applies an Explorer filter to database descendants.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    internal void ApplyFilter(string filterText)
    {
        VisibleDatabases.Clear();

        foreach (KustoDatabaseViewModel database in Databases)
        {
            database.ApplyFilter(filterText);

            if (database.MatchesFilter(filterText))
            {
                VisibleDatabases.Add(database);
            }
        }
    }

    /// <summary>
    /// Marks database discovery as active.
    /// </summary>
    internal void BeginRefreshing()
    {
        IsRefreshing = true;
        StatusText = "Refreshing databases";
    }

    /// <summary>
    /// Creates an immutable persistence snapshot.
    /// </summary>
    /// <returns>The cluster connection snapshot.</returns>
    internal KustoClusterConnection CreateConnection()
    {
        IEnumerable<KustoDatabaseConnection> databases = Databases.Select(database => database.CreateConnection());
        return new KustoClusterConnection(ClusterUri, DisplayName, databases, FolderName);
    }

    /// <summary>
    /// Marks database discovery as inactive.
    /// </summary>
    internal void EndRefreshing()
    {
        IsRefreshing = false;
    }

    /// <summary>
    /// Replaces the accessible database list while retaining matching cached schemas.
    /// </summary>
    /// <param name="databaseInfos">The newly discovered database descriptors.</param>
    internal void ReplaceDatabases(IEnumerable<KustoDatabaseInfo> databaseInfos)
    {
        ArgumentNullException.ThrowIfNull(databaseInfos);

        Dictionary<string, KustoDatabaseSchema?> cachedSchemas = Databases.ToDictionary(
            database => database.Name,
            database => database.Schema,
            StringComparer.OrdinalIgnoreCase);
        Databases.Clear();

        foreach (KustoDatabaseInfo databaseInfo in databaseInfos)
        {
            cachedSchemas.TryGetValue(databaseInfo.Name, out KustoDatabaseSchema? cachedSchema);
            KustoDatabaseConnection connection = new(databaseInfo.Name, databaseInfo.DisplayName, cachedSchema);
            Databases.Add(new KustoDatabaseViewModel(ClusterUri, connection));
        }

        ApplyFilter(string.Empty);
        StatusText = GetDatabaseSummary(Databases.Count);
    }

    /// <summary>
    /// Shows a database-discovery error on the cluster node.
    /// </summary>
    /// <param name="message">The concise error message.</param>
    internal void ReportError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        StatusText = message;
    }

    /// <summary>
    /// Moves the cluster into a named folder or to Explorer root.
    /// </summary>
    /// <param name="newFolderName">The normalized folder name, or <see langword="null"/> for root.</param>
    internal void SetFolder(string? newFolderName)
    {
        FolderName = string.IsNullOrWhiteSpace(newFolderName) ? null : newFolderName.Trim();
    }

    /// <summary>
    /// Determines whether this cluster or a descendant matches a filter.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    /// <returns><see langword="true"/> when the cluster should remain visible.</returns>
    internal bool MatchesFilter(string filterText)
    {
        bool matches = string.IsNullOrWhiteSpace(filterText)
            || DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || ClusterUri.Host.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || VisibleDatabases.Count > 0;

        return matches;
    }

    private static string GetDatabaseSummary(int databaseCount)
    {
        return databaseCount == 1 ? "1 database" : $"{databaseCount:N0} databases";
    }
}
