using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents count-only durable graph state and bounded entity search without materializing the graph.
/// </summary>
public sealed class GraphModeViewModel : ObservableObject
{
    private const int ExpandedViewportEntityCount = 500;
    private const int ExpandedViewportRelationshipCount = 2_000;
    private const int MaximumNeighborhoodDepth = 5;
    private const int MaximumViewportEntityCount = 200;
    private const int MaximumViewportRelationshipCount = 500;
    private const int MaximumSearchResults = 100;
    private const int MaximumTimelinePoints = 200;
    private readonly IGraphLayoutService layoutService;
    private readonly IGraphQueryService graphQueryService;
    private readonly IGraphStore graphStore;
    private GraphEntityKey? contextEntity;
    private string graphEditorDescription = string.Empty;
    private string graphEditorError = string.Empty;
    private Guid? graphEditorGraphId;
    private string graphEditorName = string.Empty;
    private string cypherResultSummary = "Run openCypher to inspect the selected graph";
    private string cypherText = "MATCH (n) RETURN n LIMIT 100";
    private bool hasEmptyCypherResult;
    private bool hasViewFilters;
    private bool isClearGraphOpen;
    private bool isDeleteAllGraphsOpen;
    private bool isDeleteGraphOpen;
    private bool isCypherRunning;
    private bool isFindingRoutes;
    private bool isGraphEditorOpen;
    private bool isFocusedViewport;
    private bool isBusy;
    private bool isNodeLabelEditorOpen;
    private GraphLayout? layout;
    private int neighborhoodDepth = 1;
    private GraphEntityKey? nodeLabelEntity;
    private string nodeLabelError = string.Empty;
    private string nodeLabelText = string.Empty;
    private string searchText = string.Empty;
    private GraphCatalogEntry? selectedGraph;
    private GraphEntityKey? selectedEntity;
    private GraphEntityDetails? selectedEntityDetails;
    private string selectedEntityDetailsError = string.Empty;
    private GraphRelationshipKey? selectedRelationship;
    private GraphRelationshipDetails? selectedRelationshipDetails;
    private string selectedRelationshipDetailsError = string.Empty;
    private GraphEntityKey? routeStart;
    private string routeStartLabel = string.Empty;
    private bool isSelectedEntityDetailsLoading;
    private bool isSelectedRelationshipDetailsLoading;
    private int selectedEntityDetailsRequestVersion;
    private int selectedRelationshipDetailsRequestVersion;
    private GraphEntitySummary? selectedSearchResult;
    private GraphTimelinePointViewModel? selectedTimelinePoint;
    private bool suppressSelectionLoad;
    private bool suppressGraphActivation;
    private bool suppressTimelineSelection;
    private GraphStateSummary? state;
    private string statusText = "Open Graph to load investigation state";
    private bool suppressSelectionSynchronization;
    private GraphViewport? sourceViewport;
    private GraphViewport? visibleViewport;
    private int viewportEntityLimit = MaximumViewportEntityCount;
    private int viewportRelationshipLimit = MaximumViewportRelationshipCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphModeViewModel"/> class.
    /// </summary>
    /// <param name="graphStore">The app-wide durable graph store.</param>
    /// <param name="graphQueryService">The read-only openCypher query service.</param>
    /// <param name="layoutService">The bounded graph layout service.</param>
    public GraphModeViewModel(
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        IGraphLayoutService layoutService)
    {
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(graphQueryService);
        ArgumentNullException.ThrowIfNull(layoutService);
        this.graphStore = graphStore;
        this.graphQueryService = graphQueryService;
        this.layoutService = layoutService;
        CypherColumns = new ObservableCollection<GraphQueryColumnViewModel>();
        CypherDiagnostics = new ObservableCollection<GraphQueryDiagnosticViewModel>();
        CypherRows = new ObservableCollection<GraphQueryRowViewModel>();
        Graphs = new ObservableCollection<GraphCatalogEntry>();
        LegendItems = new ObservableCollection<GraphTypeLegendItem>();
        SearchResults = new ObservableCollection<GraphEntitySummary>();
        SelectedEntities = new ObservableCollection<GraphEntityKey>();
        TimelinePoints = new ObservableCollection<GraphTimelinePointViewModel>();
        SelectedEntities.CollectionChanged += OnSelectedEntitiesChanged;
        ActivateGraphCommand = new AsyncRelayCommand<GraphCatalogEntry?>(
            ActivateGraphAsync,
            graph => graph is not null && !IsBusy);
        ClearGraphCommand = new AsyncRelayCommand(ClearGraphAsync, () => CanClearGraph);
        CloseClearGraphCommand = new RelayCommand(CloseClearGraph);
        CloseDeleteAllGraphsCommand = new RelayCommand(CloseDeleteAllGraphs);
        CloseDeleteGraphCommand = new RelayCommand(CloseDeleteGraph);
        CloseGraphEditorCommand = new RelayCommand(CloseGraphEditor);
        CancelCypherCommand = new RelayCommand(CancelCypher, () => IsCypherRunning);
        CancelRouteCommand = new RelayCommand(CancelRoute, () => IsFindingRoutes);
        ClearRouteStartCommand = new RelayCommand(ClearRouteStart, () => HasRouteStart && !IsFindingRoutes);
        DeleteAllGraphsCommand = new AsyncRelayCommand(DeleteAllGraphsAsync, () => CanDeleteAllGraphs);
        DeleteGraphCommand = new AsyncRelayCommand(DeleteGraphAsync, () => CanDeleteGraph);
        ExpandNodeCommand = new AsyncRelayCommand<GraphEntityKey?>(ExpandNodeAsync);
        HideNodeCommand = new AsyncRelayCommand<GraphEntityKey?>(HideNodeAsync);
        HideTypeCommand = new AsyncRelayCommand<GraphEntityKey?>(HideTypeAsync);
        KeepConnectedCommand = new AsyncRelayCommand(KeepConnectedAsync, () => CanFilterToSelection);
        LoadViewportCommand = new AsyncRelayCommand<GraphEntitySummary?>(LoadViewportAsync);
        OpenCreateGraphCommand = new RelayCommand(OpenCreateGraph, () => !IsBusy);
        OpenClearGraphCommand = new RelayCommand(OpenClearGraph, () => CanClearGraph);
        OpenDeleteAllGraphsCommand = new RelayCommand(OpenDeleteAllGraphs, () => CanDeleteAllGraphs);
        OpenDeleteGraphCommand = new RelayCommand(OpenDeleteGraph, () => CanDeleteGraph);
        OpenEditGraphCommand = new RelayCommand(OpenEditGraph, () => SelectedGraph is not null && !IsBusy);
        OpenNodeLabelEditorCommand = new RelayCommand<GraphEntityKey?>(
            OpenNodeLabelEditor,
            entity => entity is not null && !IsBusy && !IsViewingHistory);
        PruneUpstreamCommand = new AsyncRelayCommand<GraphEntityKey?>(PruneUpstreamAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ReloadNeighborhoodCommand = new AsyncRelayCommand(
            ReloadSelectedNeighborhoodAsync,
            () => CanReloadNeighborhood);
        ResetFiltersCommand = new AsyncRelayCommand(ResetFiltersAsync, () => HasViewFilters && !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => CanSearch);
        SaveGraphCommand = new AsyncRelayCommand(SaveGraphAsync, () => CanSaveGraph);
        SaveNodeLabelCommand = new AsyncRelayCommand(SaveNodeLabelAsync, () => CanSaveNodeLabel);
        CloseNodeLabelEditorCommand = new RelayCommand(CloseNodeLabelEditor);
        RunCypherCommand = new AsyncRelayCommand(RunCypherAsync, () => CanRunCypher);
        SelectOnlyNodeCommand = new RelayCommand<GraphEntityKey?>(SelectOnlyNode);
        SetRouteStartCommand = new RelayCommand<GraphEntityKey?>(
            SetRouteStart,
            entity => CanSetRouteEndpoint(entity));
        ShowMoreCommand = new AsyncRelayCommand(ShowMoreAsync, () => CanShowMore);
        ShowOverviewCommand = new AsyncRelayCommand(ShowOverviewAsync, () => CanShowOverview);
        ShowRoutesToNodeCommand = new AsyncRelayCommand<GraphEntityKey?>(
            ShowRoutesToNodeAsync,
            CanShowRoutesToNode);
        ViewTimelinePointCommand = new AsyncRelayCommand<GraphTimelinePointViewModel?>(
            ViewTimelinePointAsync,
            point => point is not null && !IsBusy);
    }

    /// <summary>
    /// Gets bounded entity search results.
    /// </summary>
    public ObservableCollection<GraphEntitySummary> SearchResults { get; }

    /// <summary>
    /// Gets all saved local graphs in catalog order.
    /// </summary>
    public ObservableCollection<GraphCatalogEntry> Graphs { get; }

    /// <summary>
    /// Gets projected openCypher result columns.
    /// </summary>
    public ObservableCollection<GraphQueryColumnViewModel> CypherColumns { get; }

    /// <summary>
    /// Gets openCypher diagnostics from the most recent run.
    /// </summary>
    public ObservableCollection<GraphQueryDiagnosticViewModel> CypherDiagnostics { get; }

    /// <summary>
    /// Gets projected openCypher result rows.
    /// </summary>
    public ObservableCollection<GraphQueryRowViewModel> CypherRows { get; }

    /// <summary>
    /// Gets the semantic node types currently visible in the graph layout.
    /// </summary>
    public ObservableCollection<GraphTypeLegendItem> LegendItems { get; }

    /// <summary>
    /// Gets the live graph followed by retained investigation points.
    /// </summary>
    public ObservableCollection<GraphTimelinePointViewModel> TimelinePoints { get; }

    /// <summary>
    /// Gets the graph entities highlighted on the canvas.
    /// </summary>
    public ObservableCollection<GraphEntityKey> SelectedEntities { get; }

    /// <summary>
    /// Gets the command that activates and loads a saved graph.
    /// </summary>
    public IAsyncRelayCommand<GraphCatalogEntry?> ActivateGraphCommand { get; }

    /// <summary>
    /// Gets the command that closes graph deletion confirmation.
    /// </summary>
    public IRelayCommand CloseDeleteGraphCommand { get; }

    /// <summary>
    /// Gets the command that closes deletion confirmation for all saved graphs.
    /// </summary>
    public IRelayCommand CloseDeleteAllGraphsCommand { get; }

    /// <summary>
    /// Gets the command that closes graph metadata editing.
    /// </summary>
    public IRelayCommand CloseGraphEditorCommand { get; }

    /// <summary>
    /// Gets the command that cancels the active openCypher query.
    /// </summary>
    public IRelayCommand CancelCypherCommand { get; }

    /// <summary>
    /// Gets the command that cancels the active route search.
    /// </summary>
    public IRelayCommand CancelRouteCommand { get; }

    /// <summary>
    /// Gets the command that clears the retained route start node.
    /// </summary>
    public IRelayCommand ClearRouteStartCommand { get; }

    /// <summary>
    /// Gets the command that permanently deletes the selected graph after confirmation.
    /// </summary>
    public IAsyncRelayCommand DeleteGraphCommand { get; }

    /// <summary>
    /// Gets the command that permanently deletes all saved graphs after confirmation.
    /// </summary>
    public IAsyncRelayCommand DeleteAllGraphsCommand { get; }

    /// <summary>
    /// Gets the command that refreshes durable graph counts.
    /// </summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Gets the command that searches graph entity labels, identifiers, and types.
    /// </summary>
    public IAsyncRelayCommand SearchCommand { get; }

    /// <summary>
    /// Gets the command that centers and lays out a selected search result.
    /// </summary>
    public IAsyncRelayCommand<GraphEntitySummary?> LoadViewportCommand { get; }

    /// <summary>
    /// Gets the command that opens creation of an empty named graph.
    /// </summary>
    public IRelayCommand OpenCreateGraphCommand { get; }

    /// <summary>
    /// Gets the command that opens confirmation before clearing the selected graph generation.
    /// </summary>
    public IRelayCommand OpenClearGraphCommand { get; }

    /// <summary>
    /// Gets the command that opens deletion confirmation for the selected graph.
    /// </summary>
    public IRelayCommand OpenDeleteGraphCommand { get; }

    /// <summary>
    /// Gets the command that opens confirmation before deleting all saved graphs.
    /// </summary>
    public IRelayCommand OpenDeleteAllGraphsCommand { get; }

    /// <summary>
    /// Gets the command that closes graph clearing confirmation.
    /// </summary>
    public IRelayCommand CloseClearGraphCommand { get; }

    /// <summary>
    /// Gets the command that replaces the selected graph with an empty generation.
    /// </summary>
    public IAsyncRelayCommand ClearGraphCommand { get; }

    /// <summary>
    /// Gets the command that edits the selected graph's name and description.
    /// </summary>
    public IRelayCommand OpenEditGraphCommand { get; }

    /// <summary>
    /// Gets the command that opens visible-label editing for a context node.
    /// </summary>
    public IRelayCommand<GraphEntityKey?> OpenNodeLabelEditorCommand { get; }

    /// <summary>
    /// Gets the command that expands one more level around a context node and the current selection.
    /// </summary>
    public IAsyncRelayCommand<GraphEntityKey?> ExpandNodeCommand { get; }

    /// <summary>
    /// Gets the command that hides one node from the current view.
    /// </summary>
    public IAsyncRelayCommand<GraphEntityKey?> HideNodeCommand { get; }

    /// <summary>
    /// Gets the command that hides the context node's semantic type from the current view.
    /// </summary>
    public IAsyncRelayCommand<GraphEntityKey?> HideTypeCommand { get; }

    /// <summary>
    /// Gets the command that removes components unrelated to the current selection.
    /// </summary>
    public IAsyncRelayCommand KeepConnectedCommand { get; }

    /// <summary>
    /// Gets the command that prunes a node and its recursive incoming chain from the current view.
    /// </summary>
    public IAsyncRelayCommand<GraphEntityKey?> PruneUpstreamCommand { get; }

    /// <summary>
    /// Gets the command that reloads the selected nodes at the configured depth.
    /// </summary>
    public IAsyncRelayCommand ReloadNeighborhoodCommand { get; }

    /// <summary>
    /// Gets the command that restores the unfiltered loaded viewport.
    /// </summary>
    public IAsyncRelayCommand ResetFiltersCommand { get; }

    /// <summary>
    /// Gets the command that creates or updates graph catalog metadata.
    /// </summary>
    public IAsyncRelayCommand SaveGraphCommand { get; }

    /// <summary>
    /// Gets the command that saves the context node's graph-scoped visible label.
    /// </summary>
    public IAsyncRelayCommand SaveNodeLabelCommand { get; }

    /// <summary>
    /// Gets the command that closes node label editing without saving.
    /// </summary>
    public IRelayCommand CloseNodeLabelEditorCommand { get; }

    /// <summary>
    /// Gets the command that executes the current openCypher text.
    /// </summary>
    public IAsyncRelayCommand RunCypherCommand { get; }

    /// <summary>
    /// Gets the command that replaces the selection with one context node.
    /// </summary>
    public IRelayCommand<GraphEntityKey?> SelectOnlyNodeCommand { get; }

    /// <summary>
    /// Gets the command that retains a node as the route start.
    /// </summary>
    public IRelayCommand<GraphEntityKey?> SetRouteStartCommand { get; }

    /// <summary>
    /// Gets the command that expands the bounded graph overview.
    /// </summary>
    public IAsyncRelayCommand ShowMoreCommand { get; }

    /// <summary>
    /// Gets the command that leaves a focused neighborhood and restores the graph overview.
    /// </summary>
    public IAsyncRelayCommand ShowOverviewCommand { get; }

    /// <summary>
    /// Gets the command that displays shortest routes from the retained start to a destination node.
    /// </summary>
    public IAsyncRelayCommand<GraphEntityKey?> ShowRoutesToNodeCommand { get; }

    /// <summary>
    /// Gets the command that displays the live graph or one retained investigation point.
    /// </summary>
    public IAsyncRelayCommand<GraphTimelinePointViewModel?> ViewTimelinePointCommand { get; }

    /// <summary>
    /// Gets or sets the node under the graph context menu.
    /// </summary>
    public GraphEntityKey? ContextEntity
    {
        get => contextEntity;
        set
        {
            if (SetProperty(ref contextEntity, value))
            {
                SetRouteStartCommand.NotifyCanExecuteChanged();
                ShowRoutesToNodeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the relationship depth loaded around selected nodes.
    /// </summary>
    public int NeighborhoodDepth
    {
        get => neighborhoodDepth;
        set
        {
            int boundedValue = Math.Clamp(value, 1, MaximumNeighborhoodDepth);

            if (SetProperty(ref neighborhoodDepth, boundedValue))
            {
                OnPropertyChanged(nameof(NeighborhoodDepthText));
            }
        }
    }

    /// <summary>
    /// Gets concise display text for the configured relationship depth.
    /// </summary>
    public string NeighborhoodDepthText => NeighborhoodDepth == 1
        ? "1 hop"
        : $"{NeighborhoodDepth} hops";

    /// <summary>
    /// Gets or sets the graph selected in the saved graph catalog.
    /// </summary>
    public GraphCatalogEntry? SelectedGraph
    {
        get => selectedGraph;
        set
        {
            if (SetProperty(ref selectedGraph, value))
            {
                OnPropertyChanged(nameof(GraphDescriptionText));
                OnPropertyChanged(nameof(CanDeleteGraph));
                OnPropertyChanged(nameof(DeleteGraphName));
                DeleteGraphCommand.NotifyCanExecuteChanged();
                OpenDeleteGraphCommand.NotifyCanExecuteChanged();
                OpenEditGraphCommand.NotifyCanExecuteChanged();

                if (!suppressGraphActivation && value is not null)
                {
                    ActivateGraphCommand.Execute(value);
                }
            }
        }
    }

    /// <summary>
    /// Gets the selected graph description or concise empty-description text.
    /// </summary>
    public string GraphDescriptionText => string.IsNullOrWhiteSpace(SelectedGraph?.Description)
        ? "No description"
        : SelectedGraph.Description;

    /// <summary>
    /// Gets or sets the graph name entered in the metadata editor.
    /// </summary>
    public string GraphEditorName
    {
        get => graphEditorName;
        set
        {
            if (SetProperty(ref graphEditorName, value))
            {
                GraphEditorError = string.Empty;
                OnPropertyChanged(nameof(CanSaveGraph));
                SaveGraphCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the optional graph description entered in the metadata editor.
    /// </summary>
    public string GraphEditorDescription
    {
        get => graphEditorDescription;
        set
        {
            if (SetProperty(ref graphEditorDescription, value))
            {
                GraphEditorError = string.Empty;
                OnPropertyChanged(nameof(CanSaveGraph));
                SaveGraphCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a metadata validation or persistence error.
    /// </summary>
    public string GraphEditorError
    {
        get => graphEditorError;
        private set
        {
            if (SetProperty(ref graphEditorError, value))
            {
                OnPropertyChanged(nameof(HasGraphEditorError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether graph metadata contains an error.
    /// </summary>
    public bool HasGraphEditorError => !string.IsNullOrWhiteSpace(GraphEditorError);

    /// <summary>
    /// Gets a value indicating whether graph metadata editing is open.
    /// </summary>
    public bool IsGraphEditorOpen
    {
        get => isGraphEditorOpen;
        private set
        {
            if (SetProperty(ref isGraphEditorOpen, value))
            {
                OnPropertyChanged(nameof(GraphEditorTitle));
                OnPropertyChanged(nameof(GraphEditorSaveText));
                OnPropertyChanged(nameof(CanSaveGraph));
                SaveGraphCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether graph deletion confirmation is open.
    /// </summary>
    public bool IsDeleteGraphOpen
    {
        get => isDeleteGraphOpen;
        private set => SetProperty(ref isDeleteGraphOpen, value);
    }

    /// <summary>
    /// Gets a value indicating whether deletion confirmation for all saved graphs is open.
    /// </summary>
    public bool IsDeleteAllGraphsOpen
    {
        get => isDeleteAllGraphsOpen;
        private set => SetProperty(ref isDeleteAllGraphsOpen, value);
    }

    /// <summary>
    /// Gets a value indicating whether graph clearing confirmation is open.
    /// </summary>
    public bool IsClearGraphOpen
    {
        get => isClearGraphOpen;
        private set => SetProperty(ref isClearGraphOpen, value);
    }

    /// <summary>
    /// Gets the graph metadata editor heading.
    /// </summary>
    public string GraphEditorTitle => graphEditorGraphId is null ? "New graph" : "Edit graph details";

    /// <summary>
    /// Gets the graph metadata editor primary action text.
    /// </summary>
    public string GraphEditorSaveText => graphEditorGraphId is null ? "Create graph" : "Save details";

    /// <summary>
    /// Gets a value indicating whether graph metadata can be saved.
    /// </summary>
    public bool CanSaveGraph => IsGraphEditorOpen
        && !IsBusy
        && !string.IsNullOrWhiteSpace(GraphEditorName)
        && GraphEditorName.Trim().Length <= GraphCatalogMetadata.MaximumNameLength
        && GraphEditorDescription.Trim().Length <= GraphCatalogMetadata.MaximumDescriptionLength;

    /// <summary>
    /// Gets or sets the analyst-entered visible label for the context node.
    /// </summary>
    public string NodeLabelText
    {
        get => nodeLabelText;
        set
        {
            if (SetProperty(ref nodeLabelText, value ?? string.Empty))
            {
                NodeLabelError = string.Empty;
                OnPropertyChanged(nameof(CanSaveNodeLabel));
                SaveNodeLabelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a node label validation or persistence error.
    /// </summary>
    public string NodeLabelError
    {
        get => nodeLabelError;
        private set
        {
            if (SetProperty(ref nodeLabelError, value))
            {
                OnPropertyChanged(nameof(HasNodeLabelError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the node label contains an error.
    /// </summary>
    public bool HasNodeLabelError => NodeLabelError.Length > 0;

    /// <summary>
    /// Gets a value indicating whether node label editing is open.
    /// </summary>
    public bool IsNodeLabelEditorOpen
    {
        get => isNodeLabelEditorOpen;
        private set
        {
            if (SetProperty(ref isNodeLabelEditorOpen, value))
            {
                OnPropertyChanged(nameof(CanSaveNodeLabel));
                SaveNodeLabelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the visible node label can be saved.
    /// </summary>
    public bool CanSaveNodeLabel => IsNodeLabelEditorOpen
        && nodeLabelEntity is not null
        && !IsBusy
        && !string.IsNullOrWhiteSpace(NodeLabelText)
        && NodeLabelText.Trim().Length <= GraphEntityDisplayLabel.MaximumLength;

    /// <summary>
    /// Gets a value indicating whether the selected graph can be deleted.
    /// </summary>
    public bool CanDeleteGraph => !IsBusy && SelectedGraph is not null && Graphs.Count > 1;

    /// <summary>
    /// Gets a value indicating whether the complete saved graph catalog can be deleted.
    /// </summary>
    public bool CanDeleteAllGraphs => !IsBusy && Graphs.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the current graph generation can be cleared.
    /// </summary>
    public bool CanClearGraph => !IsBusy && !IsViewingHistory && State?.IsEmpty == false;

    /// <summary>
    /// Gets the selected graph name used by deletion confirmation.
    /// </summary>
    public string DeleteGraphName => SelectedGraph?.Name ?? string.Empty;

    /// <summary>
    /// Gets the saved graph count shown by complete deletion confirmation.
    /// </summary>
    public string DeleteAllGraphsSummary => FormatCount(Graphs.Count, "saved graph");

    /// <summary>
    /// Gets or sets the current read-only openCypher query text.
    /// </summary>
    public string CypherText
    {
        get => cypherText;
        set
        {
            if (SetProperty(ref cypherText, value))
            {
                OnPropertyChanged(nameof(CanRunCypher));
                RunCypherCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a concise summary of the most recent openCypher result.
    /// </summary>
    public string CypherResultSummary
    {
        get => cypherResultSummary;
        private set => SetProperty(ref cypherResultSummary, value);
    }

    /// <summary>
    /// Gets a value indicating whether an openCypher query is active.
    /// </summary>
    public bool IsCypherRunning
    {
        get => isCypherRunning;
        private set
        {
            if (SetProperty(ref isCypherRunning, value))
            {
                OnPropertyChanged(nameof(CanRunCypher));
                CancelCypherCommand.NotifyCanExecuteChanged();
                RunCypherCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the openCypher query can execute.
    /// </summary>
    public bool CanRunCypher => !IsBusy
        && !IsCypherRunning
        && !IsViewingHistory
        && State is not null
        && !string.IsNullOrWhiteSpace(CypherText);

    /// <summary>
    /// Gets a value indicating whether projected openCypher rows are available.
    /// </summary>
    public bool HasCypherRows => CypherRows.Count > 0;

    /// <summary>
    /// Gets a value indicating whether openCypher diagnostics are available.
    /// </summary>
    public bool HasCypherDiagnostics => CypherDiagnostics.Count > 0;

    /// <summary>
    /// Gets a value indicating whether a successful openCypher query matched no graph elements.
    /// </summary>
    public bool HasEmptyCypherResult
    {
        get => hasEmptyCypherResult;
        private set
        {
            if (SetProperty(ref hasEmptyCypherResult, value))
            {
                OnPropertyChanged(nameof(ShowEmptyGraphState));
                OnPropertyChanged(nameof(ShowLayoutPlaceholder));
            }
        }
    }

    /// <summary>
    /// Gets the minimum width needed to keep openCypher result columns readable.
    /// </summary>
    public double CypherResultMinimumWidth => CypherColumns.Sum(column => column.DisplayWidth);

    /// <summary>
    /// Gets a value indicating whether visible graph filters can be reset.
    /// </summary>
    public bool HasViewFilters
    {
        get => hasViewFilters;
        private set
        {
            if (SetProperty(ref hasViewFilters, value))
            {
                ResetFiltersCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether at least one type is available for the graph legend.
    /// </summary>
    public bool HasLegendItems => LegendItems.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected nodes can load a merged neighborhood.
    /// </summary>
    public bool CanReloadNeighborhood => !IsBusy && !IsViewingHistory && SelectedEntities.Count > 0;

    /// <summary>
    /// Gets a value indicating whether unrelated components can be removed from the current view.
    /// </summary>
    public bool CanFilterToSelection => !IsBusy
        && visibleViewport is not null
        && SelectedEntities.Any(entity => visibleViewport.Entities.Any(summary => summary.Entity == entity));

    /// <summary>
    /// Gets concise selected-node count text.
    /// </summary>
    public string SelectionCountText => FormatCount(SelectedEntities.Count, "selected node");

    /// <summary>
    /// Gets a value indicating whether graph state or search is loading.
    /// </summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanSearch));
                OnPropertyChanged(nameof(CanRunCypher));
                OnPropertyChanged(nameof(CanDeleteGraph));
                OnPropertyChanged(nameof(CanDeleteAllGraphs));
                OnPropertyChanged(nameof(CanClearGraph));
                OnPropertyChanged(nameof(CanSaveGraph));
                OnPropertyChanged(nameof(CanSaveNodeLabel));
                ActivateGraphCommand.NotifyCanExecuteChanged();
                ClearGraphCommand.NotifyCanExecuteChanged();
                DeleteAllGraphsCommand.NotifyCanExecuteChanged();
                DeleteGraphCommand.NotifyCanExecuteChanged();
                OpenCreateGraphCommand.NotifyCanExecuteChanged();
                OpenClearGraphCommand.NotifyCanExecuteChanged();
                OpenDeleteAllGraphsCommand.NotifyCanExecuteChanged();
                OpenDeleteGraphCommand.NotifyCanExecuteChanged();
                OpenEditGraphCommand.NotifyCanExecuteChanged();
                OpenNodeLabelEditorCommand.NotifyCanExecuteChanged();
                SaveGraphCommand.NotifyCanExecuteChanged();
                SaveNodeLabelCommand.NotifyCanExecuteChanged();
                RunCypherCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                SearchCommand.NotifyCanExecuteChanged();
                ReloadNeighborhoodCommand.NotifyCanExecuteChanged();
                KeepConnectedCommand.NotifyCanExecuteChanged();
                ResetFiltersCommand.NotifyCanExecuteChanged();
                ViewTimelinePointCommand.NotifyCanExecuteChanged();
                SetRouteStartCommand.NotifyCanExecuteChanged();
                ShowRoutesToNodeCommand.NotifyCanExecuteChanged();
                NotifyViewportVisibilityChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a route search is active.
    /// </summary>
    public bool IsFindingRoutes
    {
        get => isFindingRoutes;
        private set
        {
            if (SetProperty(ref isFindingRoutes, value))
            {
                CancelRouteCommand.NotifyCanExecuteChanged();
                ClearRouteStartCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a route start node is retained.
    /// </summary>
    public bool HasRouteStart => RouteStart is not null;

    /// <summary>
    /// Gets the retained route start node.
    /// </summary>
    public GraphEntityKey? RouteStart
    {
        get => routeStart;
        private set
        {
            if (SetProperty(ref routeStart, value))
            {
                OnPropertyChanged(nameof(HasRouteStart));
                OnPropertyChanged(nameof(RouteStartText));
                ClearRouteStartCommand.NotifyCanExecuteChanged();
                ShowRoutesToNodeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets concise display text for the retained route start node.
    /// </summary>
    public string RouteStartText => HasRouteStart ? $"Start · {routeStartLabel}" : string.Empty;

    /// <summary>
    /// Gets a value indicating whether the active graph generation is empty.
    /// </summary>
    public bool IsEmpty => State?.IsEmpty != false;

    /// <summary>
    /// Gets a value indicating whether graph storage is empty without a query-specific empty state.
    /// </summary>
    public bool ShowEmptyGraphState => IsEmpty && !HasEmptyCypherResult;

    /// <summary>
    /// Gets a value indicating whether render geometry is ready for the graph canvas.
    /// </summary>
    public bool HasLayout => Layout?.IsEmpty == false;

    /// <summary>
    /// Gets a value indicating whether a nonempty graph is waiting for render geometry.
    /// </summary>
    public bool ShowLayoutPlaceholder => !IsEmpty && !HasLayout && !HasEmptyCypherResult;

    /// <summary>
    /// Gets the number of active graph nodes omitted from the current layout.
    /// </summary>
    public long HiddenEntityCount => IsViewingHistory || visibleViewport is null || State is null
        ? 0
        : Math.Max(0, State.EntityCount - visibleViewport.Entities.Count);

    /// <summary>
    /// Gets the number of active graph edges omitted from the current layout.
    /// </summary>
    public long HiddenRelationshipCount => IsViewingHistory || visibleViewport is null || State is null
        ? 0
        : Math.Max(0, State.RelationshipCount - visibleViewport.Relationships.Count);

    /// <summary>
    /// Gets a value indicating whether the current layout omits active graph data.
    /// </summary>
    public bool HasHiddenItems => HiddenEntityCount > 0 || HiddenRelationshipCount > 0;

    /// <summary>
    /// Gets a value indicating whether the current layout can expand within renderer limits.
    /// </summary>
    public bool CanShowMore => !IsBusy
        && !IsViewingHistory
        && HasHiddenItems
        && (viewportEntityLimit < ExpandedViewportEntityCount
            || viewportRelationshipLimit < ExpandedViewportRelationshipCount);

    /// <summary>
    /// Gets a value indicating whether a focused neighborhood can return to the overview.
    /// </summary>
    public bool CanShowOverview => !IsBusy && isFocusedViewport;

    /// <summary>
    /// Gets exact hidden graph counts for the current layout.
    /// </summary>
    public string HiddenItemsText
    {
        get
        {
            List<string> counts = [];

            if (HiddenEntityCount > 0)
            {
                counts.Add(FormatCount(HiddenEntityCount, "node"));
            }

            if (HiddenRelationshipCount > 0)
            {
                counts.Add(FormatCount(HiddenRelationshipCount, "edge"));
            }

            return $"{string.Join(" · ", counts)} hidden from this view";
        }
    }

    /// <summary>
    /// Gets contextual guidance for displaying graph data outside the current layout.
    /// </summary>
    public string HiddenItemsHelpText => CanShowMore
        ? "Expand the overview or search for a node to focus its neighborhood."
        : "Renderer limit reached. Search for a node to display its neighborhood.";

    /// <summary>
    /// Gets the bounded overview expansion command label.
    /// </summary>
    public string ShowMoreButtonText => State is not null
        && State.EntityCount <= ExpandedViewportEntityCount
        && State.RelationshipCount <= ExpandedViewportRelationshipCount
            ? "Show all"
            : "Show more";

    /// <summary>
    /// Gets the active graph entity and relationship counts.
    /// </summary>
    public string CountText
    {
        get
        {
            string countText;

            if (IsViewingHistory)
            {
                countText = $"Historical view · {FormatCount(Layout?.Nodes.Count ?? 0, "visible node")} · {FormatCount(Layout?.Edges.Count ?? 0, "visible edge")}";
            }
            else
            {
                countText = State is null
                    ? "Graph state not loaded"
                    : $"{FormatCount(State.EntityCount, "node")} · {FormatCount(State.RelationshipCount, "edge")} · {FormatCount(State.EvidenceCount, "evidence row")}";
            }

            return countText;
        }
    }

    /// <summary>
    /// Gets or sets the analyst-entered entity search text.
    /// </summary>
    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                OnPropertyChanged(nameof(CanSearch));
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected graph entity search result.
    /// </summary>
    public GraphEntitySummary? SelectedSearchResult
    {
        get => selectedSearchResult;
        set
        {
            if (SetProperty(ref selectedSearchResult, value)
                && value is not null
                && !suppressSelectionLoad)
            {
                LoadViewportCommand.Execute(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the live graph or retained investigation point displayed on the canvas.
    /// </summary>
    public GraphTimelinePointViewModel? SelectedTimelinePoint
    {
        get => selectedTimelinePoint;
        set
        {
            if (SetProperty(ref selectedTimelinePoint, value))
            {
                OnPropertyChanged(nameof(IsViewingHistory));
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(CanSearch));
                OnPropertyChanged(nameof(CanRunCypher));
                OnPropertyChanged(nameof(CanClearGraph));
                OnPropertyChanged(nameof(CanReloadNeighborhood));
                SearchCommand.NotifyCanExecuteChanged();
                RunCypherCommand.NotifyCanExecuteChanged();
                ClearGraphCommand.NotifyCanExecuteChanged();
                OpenClearGraphCommand.NotifyCanExecuteChanged();
                ReloadNeighborhoodCommand.NotifyCanExecuteChanged();
                SetRouteStartCommand.NotifyCanExecuteChanged();
                ShowRoutesToNodeCommand.NotifyCanExecuteChanged();
                NotifyViewportVisibilityChanged();

                if (!suppressTimelineSelection && value is not null)
                {
                    ViewTimelinePointCommand.Execute(value);
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the canvas displays retained historical state.
    /// </summary>
    public bool IsViewingHistory => SelectedTimelinePoint?.Point is not null;

    /// <summary>
    /// Gets or sets the graph entity highlighted on the native canvas.
    /// </summary>
    public GraphEntityKey? SelectedEntity
    {
        get => selectedEntity;
        set
        {
            if (SetProperty(ref selectedEntity, value))
            {
                if (value is not null && SelectedRelationship is not null)
                {
                    SelectedRelationship = null;
                }

                SynchronizeSelectedEntities(value);
                int requestVersion = ++selectedEntityDetailsRequestVersion;
                SelectedEntityDetails = null;
                SelectedEntityDetailsError = string.Empty;
                OnPropertyChanged(nameof(HasSelectedEntity));
                OnPropertyChanged(nameof(HasInspectorSelection));
                OnPropertyChanged(nameof(InspectorTitle));
                SetRouteStartCommand.NotifyCanExecuteChanged();
                ShowRoutesToNodeCommand.NotifyCanExecuteChanged();

                if (value is GraphEntityKey entity)
                {
                    _ = LoadSelectedEntityDetailsAsync(entity, requestVersion);
                }
                else
                {
                    IsSelectedEntityDetailsLoading = false;
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a graph entity is selected.
    /// </summary>
    public bool HasSelectedEntity => SelectedEntity is not null;

    /// <summary>
    /// Gets or sets the graph relationship highlighted on the native canvas.
    /// </summary>
    public GraphRelationshipKey? SelectedRelationship
    {
        get => selectedRelationship;
        set
        {
            if (SetProperty(ref selectedRelationship, value))
            {
                if (value is not null && SelectedEntity is not null)
                {
                    SelectedEntity = null;
                }

                int requestVersion = ++selectedRelationshipDetailsRequestVersion;
                SelectedRelationshipDetails = null;
                SelectedRelationshipDetailsError = string.Empty;
                OnPropertyChanged(nameof(HasSelectedRelationship));
                OnPropertyChanged(nameof(HasInspectorSelection));
                OnPropertyChanged(nameof(InspectorTitle));
                OnPropertyChanged(nameof(SelectedRelationshipSourceText));
                OnPropertyChanged(nameof(SelectedRelationshipTargetText));
                OnPropertyChanged(nameof(SelectedRelationshipTypeText));

                if (value is GraphRelationshipKey relationship)
                {
                    _ = LoadSelectedRelationshipDetailsAsync(relationship, requestVersion);
                }
                else
                {
                    IsSelectedRelationshipDetailsLoading = false;
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a graph relationship is selected.
    /// </summary>
    public bool HasSelectedRelationship => SelectedRelationship is not null;

    /// <summary>
    /// Gets a value indicating whether the inspector has a selected node or relationship.
    /// </summary>
    public bool HasInspectorSelection => HasSelectedEntity || HasSelectedRelationship;

    /// <summary>
    /// Gets the heading for the active graph inspector subject.
    /// </summary>
    public string InspectorTitle => HasSelectedRelationship ? "RELATIONSHIP EVIDENCE" : "NODE PROPERTIES";

    /// <summary>
    /// Gets the selected entity's latest retained source properties and cumulative counts.
    /// </summary>
    public GraphEntityDetails? SelectedEntityDetails
    {
        get => selectedEntityDetails;
        private set
        {
            if (SetProperty(ref selectedEntityDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedEntityDetails));
                OnPropertyChanged(nameof(HasSelectedEntityProperties));
                OnPropertyChanged(nameof(HasNoSelectedEntityProperties));
                OnPropertyChanged(nameof(HasSelectedEntitySourceLabels));
                OnPropertyChanged(nameof(SelectedEntitySourceLabelsText));
                OnPropertyChanged(nameof(SelectedEntityNamespaceText));
                OnPropertyChanged(nameof(SelectedEntityFirstSeenText));
                OnPropertyChanged(nameof(SelectedEntityLastUpdatedText));
                OnPropertyChanged(nameof(HasSelectedEntityEvidence));
                OnPropertyChanged(nameof(SelectedEntityEvidenceSummary));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether selected entity details are available.
    /// </summary>
    public bool HasSelectedEntityDetails => SelectedEntityDetails is not null;

    /// <summary>
    /// Gets a value indicating whether the selected entity has retained source properties.
    /// </summary>
    public bool HasSelectedEntityProperties => SelectedEntityDetails?.Properties.Count > 0;

    /// <summary>
    /// Gets a value indicating whether selected entity details contain no source properties.
    /// </summary>
    public bool HasNoSelectedEntityProperties => SelectedEntityDetails is not null
        && SelectedEntityDetails.Properties.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the selected entity has source labels.
    /// </summary>
    public bool HasSelectedEntitySourceLabels => SelectedEntityDetails?.SourceLabels.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected entity has bounded supporting evidence.
    /// </summary>
    public bool HasSelectedEntityEvidence => SelectedEntityDetails?.Evidence.Count > 0;

    /// <summary>
    /// Gets concise bounded evidence coverage for the selected entity.
    /// </summary>
    public string SelectedEntityEvidenceSummary => CreateEvidenceSummary(
        SelectedEntityDetails?.Evidence.Count ?? 0,
        SelectedEntityDetails?.EvidenceCount ?? 0);

    /// <summary>
    /// Gets a value indicating whether selected entity details are loading.
    /// </summary>
    public bool IsSelectedEntityDetailsLoading
    {
        get => isSelectedEntityDetailsLoading;
        private set
        {
            if (SetProperty(ref isSelectedEntityDetailsLoading, value))
            {
                OnPropertyChanged(nameof(IsInspectorDetailsLoading));
            }
        }
    }

    /// <summary>
    /// Gets an error raised while loading selected entity details.
    /// </summary>
    public string SelectedEntityDetailsError
    {
        get => selectedEntityDetailsError;
        private set
        {
            if (SetProperty(ref selectedEntityDetailsError, value))
            {
                OnPropertyChanged(nameof(HasSelectedEntityDetailsError));
                OnPropertyChanged(nameof(HasInspectorDetailsError));
                OnPropertyChanged(nameof(InspectorDetailsError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether selected entity details failed to load.
    /// </summary>
    public bool HasSelectedEntityDetailsError => !string.IsNullOrWhiteSpace(SelectedEntityDetailsError);

    /// <summary>
    /// Gets retained properties and bounded evidence for the selected relationship.
    /// </summary>
    public GraphRelationshipDetails? SelectedRelationshipDetails
    {
        get => selectedRelationshipDetails;
        private set
        {
            if (SetProperty(ref selectedRelationshipDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedRelationshipDetails));
                OnPropertyChanged(nameof(HasSelectedRelationshipProperties));
                OnPropertyChanged(nameof(HasNoSelectedRelationshipProperties));
                OnPropertyChanged(nameof(HasSelectedRelationshipSourceLabels));
                OnPropertyChanged(nameof(HasSelectedRelationshipEvidence));
                OnPropertyChanged(nameof(SelectedRelationshipSourceLabelsText));
                OnPropertyChanged(nameof(SelectedRelationshipFirstSeenText));
                OnPropertyChanged(nameof(SelectedRelationshipLastUpdatedText));
                OnPropertyChanged(nameof(SelectedRelationshipEvidenceSummary));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether selected relationship details are available.
    /// </summary>
    public bool HasSelectedRelationshipDetails => SelectedRelationshipDetails is not null;

    /// <summary>
    /// Gets a value indicating whether the selected relationship has source properties.
    /// </summary>
    public bool HasSelectedRelationshipProperties => SelectedRelationshipDetails?.Properties.Count > 0;

    /// <summary>
    /// Gets a value indicating whether selected relationship details contain no source properties.
    /// </summary>
    public bool HasNoSelectedRelationshipProperties => SelectedRelationshipDetails is not null
        && SelectedRelationshipDetails.Properties.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the selected relationship has source labels.
    /// </summary>
    public bool HasSelectedRelationshipSourceLabels => SelectedRelationshipDetails?.SourceLabels.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected relationship has bounded supporting evidence.
    /// </summary>
    public bool HasSelectedRelationshipEvidence => SelectedRelationshipDetails?.Evidence.Count > 0;

    /// <summary>
    /// Gets a value indicating whether selected relationship details are loading.
    /// </summary>
    public bool IsSelectedRelationshipDetailsLoading
    {
        get => isSelectedRelationshipDetailsLoading;
        private set
        {
            if (SetProperty(ref isSelectedRelationshipDetailsLoading, value))
            {
                OnPropertyChanged(nameof(IsInspectorDetailsLoading));
            }
        }
    }

    /// <summary>
    /// Gets an error raised while loading selected relationship details.
    /// </summary>
    public string SelectedRelationshipDetailsError
    {
        get => selectedRelationshipDetailsError;
        private set
        {
            if (SetProperty(ref selectedRelationshipDetailsError, value))
            {
                OnPropertyChanged(nameof(HasSelectedRelationshipDetailsError));
                OnPropertyChanged(nameof(HasInspectorDetailsError));
                OnPropertyChanged(nameof(InspectorDetailsError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether selected relationship details failed to load.
    /// </summary>
    public bool HasSelectedRelationshipDetailsError => !string.IsNullOrWhiteSpace(
        SelectedRelationshipDetailsError);

    /// <summary>
    /// Gets a value indicating whether either inspector subject is loading.
    /// </summary>
    public bool IsInspectorDetailsLoading => IsSelectedEntityDetailsLoading
        || IsSelectedRelationshipDetailsLoading;

    /// <summary>
    /// Gets a value indicating whether either inspector subject failed to load.
    /// </summary>
    public bool HasInspectorDetailsError => HasSelectedEntityDetailsError
        || HasSelectedRelationshipDetailsError;

    /// <summary>
    /// Gets the active inspector error.
    /// </summary>
    public string InspectorDetailsError => HasSelectedRelationshipDetailsError
        ? SelectedRelationshipDetailsError
        : SelectedEntityDetailsError;

    /// <summary>
    /// Gets the selected relationship source identity.
    /// </summary>
    public string SelectedRelationshipSourceText => SelectedRelationship?.Source.ToString() ?? string.Empty;

    /// <summary>
    /// Gets the selected relationship target identity.
    /// </summary>
    public string SelectedRelationshipTargetText => SelectedRelationship?.Target.ToString() ?? string.Empty;

    /// <summary>
    /// Gets the selected relationship type.
    /// </summary>
    public string SelectedRelationshipTypeText => SelectedRelationship?.TypeName ?? string.Empty;

    /// <summary>
    /// Gets the selected relationship's source labels as concise display text.
    /// </summary>
    public string SelectedRelationshipSourceLabelsText => SelectedRelationshipDetails is null
        ? string.Empty
        : string.Join(" · ", SelectedRelationshipDetails.SourceLabels);

    /// <summary>
    /// Gets the selected relationship's first discovery time for display.
    /// </summary>
    public string SelectedRelationshipFirstSeenText => SelectedRelationshipDetails?.FirstDiscoveredAtUtc.ToString(
        "yyyy-MM-dd HH:mm:ss 'UTC'",
        CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Gets the selected relationship's latest update time for display.
    /// </summary>
    public string SelectedRelationshipLastUpdatedText => SelectedRelationshipDetails?.LastUpdatedAtUtc.ToString(
        "yyyy-MM-dd HH:mm:ss 'UTC'",
        CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Gets concise bounded evidence coverage for the selected relationship.
    /// </summary>
    public string SelectedRelationshipEvidenceSummary => CreateEvidenceSummary(
        SelectedRelationshipDetails?.Evidence.Count ?? 0,
        SelectedRelationshipDetails?.EvidenceCount ?? 0);

    /// <summary>
    /// Gets the selected entity's source labels as concise display text.
    /// </summary>
    public string SelectedEntitySourceLabelsText => SelectedEntityDetails is null
        ? string.Empty
        : string.Join(" · ", SelectedEntityDetails.SourceLabels);

    /// <summary>
    /// Gets the selected entity's source boundary for display.
    /// </summary>
    public string SelectedEntityNamespaceText => SelectedEntityDetails?.Summary.Entity.IsSourceScoped == true
        ? SelectedEntityDetails.Summary.Entity.SourceNamespace
        : "Global";

    /// <summary>
    /// Gets the selected entity's first discovery time for display.
    /// </summary>
    public string SelectedEntityFirstSeenText => SelectedEntityDetails?.Summary.FirstDiscoveredAtUtc.ToString(
        "yyyy-MM-dd HH:mm:ss 'UTC'",
        CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Gets the selected entity's latest update time for display.
    /// </summary>
    public string SelectedEntityLastUpdatedText => SelectedEntityDetails?.Summary.LastUpdatedAtUtc.ToString(
        "yyyy-MM-dd HH:mm:ss 'UTC'",
        CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Gets the normalized bounded graph geometry ready for native rendering.
    /// </summary>
    public GraphLayout? Layout
    {
        get => layout;
        private set
        {
            if (SetProperty(ref layout, value))
            {
                UpdateLegendItems();
                OnPropertyChanged(nameof(HasLayout));
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(ShowLayoutPlaceholder));
                NotifyViewportVisibilityChanged();
            }
        }
    }

    /// <summary>
    /// Gets a concise graph operation status.
    /// </summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>
    /// Gets a value indicating whether the current entity search can run.
    /// </summary>
    public bool CanSearch => !IsBusy
        && !IsViewingHistory
        && !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// Gets the active graph generation counts, or <see langword="null"/> before first refresh.
    /// </summary>
    public GraphStateSummary? State
    {
        get => state;
        private set
        {
            GraphSnapshot? previousSnapshot = state?.Snapshot;

            if (SetProperty(ref state, value))
            {
                if (previousSnapshot is GraphSnapshot snapshot && snapshot != value?.Snapshot)
                {
                    ResetRouteStart();
                }

                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(CanClearGraph));
                OnPropertyChanged(nameof(ShowEmptyGraphState));
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(ShowLayoutPlaceholder));
                ClearGraphCommand.NotifyCanExecuteChanged();
                OpenClearGraphCommand.NotifyCanExecuteChanged();
                NotifyViewportVisibilityChanged();
            }
        }
    }

    /// <summary>
    /// Refreshes durable graph counts without loading graph elements.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels state loading.</param>
    /// <returns>A task that completes when state has refreshed.</returns>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusText = "Loading graph state";

        try
        {
            GraphCatalog catalog = await graphStore.GetCatalogAsync(cancellationToken);
            SynchronizeCatalog(catalog);
            State = await graphStore.GetStateAsync(catalog.ActiveGraph.Snapshot, cancellationToken);
            await LoadTimelineAsync(cancellationToken);
            await LoadCurrentGraphAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not load graph: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatCount(long count, string singularNoun)
    {
        string noun = count == 1 ? singularNoun : $"{singularNoun}s";
        return $"{count:N0} {noun}";
    }

    private static int GetFullViewportLimit(long count)
    {
        return (int)Math.Clamp(count, 1, int.MaxValue);
    }

    private static GraphViewport? ReplaceEntitySummary(GraphViewport? viewport, GraphEntitySummary updated)
    {
        if (viewport is null || !viewport.Entities.Any(entity => entity.Entity == updated.Entity))
        {
            return viewport;
        }

        return new GraphViewport(
            viewport.Center,
            viewport.Entities.Select(entity => entity.Entity == updated.Entity ? updated : entity),
            viewport.Relationships,
            viewport.IsTruncated);
    }

    private static double[] CreateCypherColumnWidths(GraphQueryResult result)
    {
        double[] widths = new double[result.Columns.Count];

        for (int columnIndex = 0; columnIndex < result.Columns.Count; columnIndex++)
        {
            int maximumLength = result.Rows
                .Select(row => row.Values[columnIndex].DisplayText.Length)
                .Append(result.Columns[columnIndex].Name.Length)
                .Max();
            widths[columnIndex] = Math.Clamp((maximumLength * 7) + 24, 84, 360);
        }

        return widths;
    }

    private static HashSet<GraphEntityKey> GetConnectedEntities(
        GraphViewport viewport,
        IEnumerable<GraphEntityKey> selectedEntities)
    {
        HashSet<GraphEntityKey> visibleEntities = viewport.Entities
            .Select(entity => entity.Entity)
            .ToHashSet();
        HashSet<GraphEntityKey> connectedEntities = selectedEntities
            .Where(visibleEntities.Contains)
            .ToHashSet();
        ILookup<GraphEntityKey, GraphEntityKey> neighbors = viewport.Relationships
            .SelectMany(relationship => new[]
            {
                new KeyValuePair<GraphEntityKey, GraphEntityKey>(
                    relationship.Source,
                    relationship.Target),
                new KeyValuePair<GraphEntityKey, GraphEntityKey>(
                    relationship.Target,
                    relationship.Source),
            })
            .ToLookup(pair => pair.Key, pair => pair.Value);
        Queue<GraphEntityKey> pendingEntities = new(connectedEntities);

        while (pendingEntities.TryDequeue(out GraphEntityKey currentEntity))
        {
            foreach (GraphEntityKey neighbor in neighbors[currentEntity].Where(connectedEntities.Add))
            {
                pendingEntities.Enqueue(neighbor);
            }
        }

        return connectedEntities;
    }

    private static HashSet<GraphEntityKey> GetIncomingClosure(
        GraphViewport viewport,
        GraphEntityKey contextNode)
    {
        ILookup<GraphEntityKey, GraphEntityKey> incomingSources = viewport.Relationships
            .ToLookup(relationship => relationship.Target, relationship => relationship.Source);
        HashSet<GraphEntityKey> prunedEntities = [contextNode];
        Queue<GraphEntityKey> pendingEntities = new([contextNode]);

        while (pendingEntities.TryDequeue(out GraphEntityKey target))
        {
            foreach (GraphEntityKey source in incomingSources[target].Where(prunedEntities.Add))
            {
                pendingEntities.Enqueue(source);
            }
        }

        return prunedEntities;
    }

    private static string CreateEvidenceSummary(int visibleCount, long totalCount)
    {
        return visibleCount < totalCount
            ? $"Showing {visibleCount:N0} of {totalCount:N0} evidence rows"
            : FormatCount(totalCount, "evidence row");
    }

    private async Task ActivateGraphAsync(
        GraphCatalogEntry? graph,
        CancellationToken cancellationToken)
    {
        if (graph is not null && graph.GraphId != State?.GraphId)
        {
            IsBusy = true;
            StatusText = $"Loading {graph.Name}";
            ClearGraphView();

            try
            {
                State = await graphStore.ActivateGraphAsync(graph.GraphId, cancellationToken);
                GraphCatalog catalog = await graphStore.GetCatalogAsync(cancellationToken);
                SynchronizeCatalog(catalog);
                await LoadTimelineAsync(cancellationToken);
                await LoadCurrentGraphAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                StatusText = $"Could not switch graph: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private void ClearGraphView()
    {
        selectedEntityDetailsRequestVersion++;
        selectedRelationshipDetailsRequestVersion++;
        SearchResults.Clear();
        SelectedSearchResult = null;
        sourceViewport = null;
        visibleViewport = null;
        Layout = null;
        HasViewFilters = false;
        isFocusedViewport = false;
        ResetRouteStart();
        SetSelection([], null);
        ClearCypherOutput();
        NotifyViewportVisibilityChanged();
    }

    private void ClearCypherOutput()
    {
        CypherColumns.Clear();
        CypherRows.Clear();
        CypherDiagnostics.Clear();
        HasEmptyCypherResult = false;
        OnPropertyChanged(nameof(HasCypherRows));
        OnPropertyChanged(nameof(HasCypherDiagnostics));
        OnPropertyChanged(nameof(CypherResultMinimumWidth));
    }

    private void CancelCypher()
    {
        RunCypherCommand.Cancel();
    }

    private void CancelRoute()
    {
        ShowRoutesToNodeCommand.Cancel();
    }

    private bool CanSetRouteEndpoint(GraphEntityKey? entity)
    {
        return entity is not null && !IsBusy && !IsViewingHistory;
    }

    private bool CanShowRoutesToNode(GraphEntityKey? entity)
    {
        return RouteStart is GraphEntityKey start
            && entity is GraphEntityKey destination
            && destination != start
            && CanSetRouteEndpoint(entity);
    }

    private void ClearRouteStart()
    {
        ResetRouteStart();
        StatusText = "Route start cleared";
    }

    private void CloseDeleteGraph()
    {
        IsDeleteGraphOpen = false;
    }

    private void CloseDeleteAllGraphs()
    {
        IsDeleteAllGraphsOpen = false;
    }

    private void CloseClearGraph()
    {
        IsClearGraphOpen = false;
    }

    private void CloseGraphEditor()
    {
        IsGraphEditorOpen = false;
        GraphEditorError = string.Empty;
        graphEditorGraphId = null;
    }

    private void CloseNodeLabelEditor()
    {
        IsNodeLabelEditorOpen = false;
        NodeLabelError = string.Empty;
        nodeLabelEntity = null;
    }

    private async Task DeleteGraphAsync(CancellationToken cancellationToken)
    {
        GraphCatalogEntry? graph = SelectedGraph;

        if (graph is not null && Graphs.Count > 1)
        {
            IsBusy = true;
            StatusText = $"Deleting {graph.Name}";

            try
            {
                GraphCatalog catalog = await graphStore.DeleteGraphAsync(graph.GraphId, cancellationToken);
                SynchronizeCatalog(catalog);
                State = await graphStore.GetStateAsync(catalog.ActiveGraph.Snapshot, cancellationToken);
                CloseDeleteGraph();
                ClearGraphView();
                await LoadTimelineAsync(cancellationToken);
                await LoadCurrentGraphAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                StatusText = $"Could not delete graph: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task DeleteAllGraphsAsync(CancellationToken cancellationToken)
    {
        if (CanDeleteAllGraphs)
        {
            IsBusy = true;
            StatusText = "Deleting all saved graphs";

            try
            {
                GraphCatalog catalog = await graphStore.DeleteAllGraphsAsync(cancellationToken);
                SynchronizeCatalog(catalog);
                State = await graphStore.GetStateAsync(catalog.ActiveGraph.Snapshot, cancellationToken);
                CloseDeleteAllGraphs();
                ClearGraphView();
                await LoadTimelineAsync(cancellationToken);
                await LoadCurrentGraphAsync(cancellationToken);
                StatusText = "All saved graphs deleted";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                StatusText = $"Could not delete all graphs: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task ClearGraphAsync(CancellationToken cancellationToken)
    {
        if (CanClearGraph)
        {
            IsBusy = true;
            StatusText = $"Clearing {State!.GraphName}";

            try
            {
                State = await graphStore.ClearAsync(new GraphWriteTarget(State.Snapshot), cancellationToken);
                CloseClearGraph();
                ClearGraphView();
                GraphCatalog catalog = await graphStore.GetCatalogAsync(cancellationToken);
                SynchronizeCatalog(catalog);
                await LoadTimelineAsync(cancellationToken);
                StatusText = "Graph cleared";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                StatusText = $"Could not clear graph: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private GraphSnapshot GetCurrentSnapshot()
    {
        return State?.Snapshot
            ?? throw new InvalidOperationException("Select a saved graph before loading graph data.");
    }

    private GraphSnapshot GetDisplayedSnapshot()
    {
        return SelectedTimelinePoint?.Point?.Snapshot ?? GetCurrentSnapshot();
    }

    private async Task LoadTimelineAsync(CancellationToken cancellationToken)
    {
        if (State is not null)
        {
            IReadOnlyList<GraphTimelinePoint> points = await graphStore.GetTimelineAsync(
                State.GraphId,
                MaximumTimelinePoints,
                cancellationToken);
            suppressTimelineSelection = true;

            try
            {
                TimelinePoints.Clear();
                TimelinePoints.Add(new GraphTimelinePointViewModel(null));

                foreach (GraphTimelinePoint point in points)
                {
                    TimelinePoints.Add(new GraphTimelinePointViewModel(point));
                }

                SelectedTimelinePoint = TimelinePoints[0];
            }
            finally
            {
                suppressTimelineSelection = false;
            }
        }
    }

    private async Task ViewTimelinePointAsync(
        GraphTimelinePointViewModel? selection,
        CancellationToken cancellationToken)
    {
        if (selection is not null)
        {
            IsBusy = true;
            ClearGraphView();

            try
            {
                if (selection.Point is GraphTimelinePoint point)
                {
                    StatusText = $"Loading graph history at {selection.Title}";
                    GraphViewport viewport = await graphStore.GetTimelineViewportAsync(
                        point,
                        MaximumViewportEntityCount,
                        MaximumViewportRelationshipCount,
                        cancellationToken);
                    await ApplySourceViewportAsync(viewport, false, false, cancellationToken);
                    StatusText = viewport.IsEmpty
                        ? $"No graph data known by {selection.Title}"
                        : $"Historical graph as known by {selection.Title}";
                }
                else
                {
                    await LoadCurrentGraphAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                StatusText = $"Could not load graph history: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task LoadCurrentGraphAsync(CancellationToken cancellationToken)
    {
        GraphStateSummary? currentState = State;

        if (currentState?.IsEmpty != false)
        {
            ClearGraphView();
            StatusText = "No graph data yet";
        }
        else
        {
            StatusText = "Preparing graph layout";
            await UpdateViewportAsync(
                null,
                GetFullViewportLimit(currentState.EntityCount),
                GetFullViewportLimit(currentState.RelationshipCount),
                cancellationToken);
            StatusText = CreateViewportStatus();
        }
    }

    private void OpenCreateGraph()
    {
        graphEditorGraphId = null;
        GraphEditorName = string.Empty;
        GraphEditorDescription = string.Empty;
        GraphEditorError = string.Empty;
        IsGraphEditorOpen = true;
    }

    private void OpenClearGraph()
    {
        if (CanClearGraph)
        {
            IsClearGraphOpen = true;
        }
    }

    private void OpenDeleteGraph()
    {
        if (CanDeleteGraph)
        {
            IsDeleteGraphOpen = true;
        }
    }

    private void OpenDeleteAllGraphs()
    {
        if (CanDeleteAllGraphs)
        {
            IsDeleteAllGraphsOpen = true;
        }
    }

    private void OpenEditGraph()
    {
        if (SelectedGraph is GraphCatalogEntry graph)
        {
            graphEditorGraphId = graph.GraphId;
            GraphEditorName = graph.Name;
            GraphEditorDescription = graph.Description;
            GraphEditorError = string.Empty;
            IsGraphEditorOpen = true;
        }
    }

    private void OpenNodeLabelEditor(GraphEntityKey? entity)
    {
        if (entity is GraphEntityKey contextNode && !IsBusy && !IsViewingHistory)
        {
            nodeLabelEntity = contextNode;
            NodeLabelText = FindEntityDisplayLabel(contextNode);
            NodeLabelError = string.Empty;
            IsNodeLabelEditorOpen = true;
        }
    }

    private async Task SaveGraphAsync(CancellationToken cancellationToken)
    {
        GraphEditorError = ValidateGraphMetadata();

        if (GraphEditorError.Length == 0)
        {
            IsBusy = true;

            try
            {
                if (graphEditorGraphId is Guid graphId)
                {
                    await graphStore.UpdateGraphAsync(
                        graphId,
                        GraphEditorName,
                        GraphEditorDescription,
                        cancellationToken);
                }
                else
                {
                    State = await graphStore.CreateGraphAsync(
                        GraphEditorName,
                        GraphEditorDescription,
                        cancellationToken);
                    ClearGraphView();
                }

                GraphCatalog catalog = await graphStore.GetCatalogAsync(cancellationToken);
                SynchronizeCatalog(catalog);
                State = await graphStore.GetStateAsync(catalog.ActiveGraph.Snapshot, cancellationToken);
                CloseGraphEditor();
                await LoadTimelineAsync(cancellationToken);
                await LoadCurrentGraphAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                GraphEditorError = exception is InvalidOperationException
                    ? exception.Message
                    : $"Could not save graph details: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task SaveNodeLabelAsync(CancellationToken cancellationToken)
    {
        NodeLabelError = ValidateNodeLabel();
        GraphEntityKey? entity = nodeLabelEntity;

        if (NodeLabelError.Length == 0 && entity is GraphEntityKey node)
        {
            IsBusy = true;

            try
            {
                GraphSnapshot snapshot = GetCurrentSnapshot();
                GraphEntitySummary updated = await graphStore.SetEntityDisplayLabelAsync(
                    snapshot,
                    node,
                    NodeLabelText,
                    cancellationToken);
                await ApplyUpdatedEntitySummaryAsync(updated, cancellationToken);
                State = await graphStore.GetStateAsync(snapshot, cancellationToken);
                CloseNodeLabelEditor();
                StatusText = $"Node label changed to {updated.DisplayLabel}";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                NodeLabelError = exception.Message;
                StatusText = $"Could not change node label: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task ApplyUpdatedEntitySummaryAsync(
        GraphEntitySummary updated,
        CancellationToken cancellationToken)
    {
        sourceViewport = ReplaceEntitySummary(sourceViewport, updated);
        visibleViewport = ReplaceEntitySummary(visibleViewport, updated);

        if (visibleViewport is GraphViewport viewport)
        {
            Layout = await layoutService.LayoutAsync(viewport, cancellationToken);
        }

        for (int index = 0; index < SearchResults.Count; index++)
        {
            if (SearchResults[index].Entity == updated.Entity)
            {
                SearchResults[index] = updated;
            }
        }

        if (selectedSearchResult?.Entity == updated.Entity)
        {
            selectedSearchResult = updated;
            OnPropertyChanged(nameof(SelectedSearchResult));
        }

        if (RouteStart == updated.Entity)
        {
            routeStartLabel = updated.DisplayLabel;
            OnPropertyChanged(nameof(RouteStartText));
        }

        if (SelectedEntity == updated.Entity)
        {
            await LoadSelectedEntityDetailsAsync(updated.Entity, ++selectedEntityDetailsRequestVersion);
        }
    }

    private string ValidateNodeLabel()
    {
        if (string.IsNullOrWhiteSpace(NodeLabelText))
        {
            return "Enter a visible node label.";
        }

        if (NodeLabelText.Trim().Length > GraphEntityDisplayLabel.MaximumLength)
        {
            return $"Node labels cannot exceed {GraphEntityDisplayLabel.MaximumLength:N0} characters.";
        }

        return string.Empty;
    }

    private async Task RunCypherAsync(CancellationToken cancellationToken)
    {
        GraphSnapshot snapshot = GetCurrentSnapshot();
        IsCypherRunning = true;
        IsBusy = true;
        CypherResultSummary = "Running openCypher";

        try
        {
            GraphQueryResult result = await graphQueryService.ExecuteOpenCypherAsync(
                new GraphQueryRequest(snapshot, CypherText),
                cancellationToken);

            if (State?.Snapshot != result.Snapshot)
            {
                CypherResultSummary = "Query completed for a graph that is no longer selected";
            }
            else
            {
                ApplyCypherOutput(result);

                if (result.Succeeded)
                {
                    await ApplySourceViewportAsync(result.Viewport, true, false, cancellationToken);
                    HasEmptyCypherResult = result.Viewport.IsEmpty;
                    StatusText = result.Viewport.IsEmpty
                        ? "OpenCypher matched no graph elements"
                        : CreateViewportStatus();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CypherResultSummary = "OpenCypher canceled";
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            CypherResultSummary = $"OpenCypher failed: {exception.Message}";
            StatusText = CypherResultSummary;
        }
        finally
        {
            IsBusy = false;
            IsCypherRunning = false;
        }
    }

    private void ApplyCypherOutput(GraphQueryResult result)
    {
        ClearCypherOutput();
        double[] widths = CreateCypherColumnWidths(result);

        for (int index = 0; index < result.Columns.Count; index++)
        {
            CypherColumns.Add(new GraphQueryColumnViewModel(result.Columns[index], widths[index]));
        }

        foreach (GraphQueryRow row in result.Rows)
        {
            CypherRows.Add(new GraphQueryRowViewModel(row, widths, SelectCypherCell));
        }

        foreach (GraphQueryDiagnostic diagnostic in result.Diagnostics)
        {
            CypherDiagnostics.Add(new GraphQueryDiagnosticViewModel(diagnostic));
        }

        string truncationText = result.AreRowsTruncated ? " · truncated" : string.Empty;
        CypherResultSummary = result.Succeeded
            ? $"{result.Rows.Count:N0} rows · {result.Viewport.Entities.Count:N0} nodes · {result.Viewport.Relationships.Count:N0} edges · {result.Duration.TotalMilliseconds:N0} ms{truncationText}"
            : "OpenCypher has errors";
        OnPropertyChanged(nameof(HasCypherRows));
        OnPropertyChanged(nameof(HasCypherDiagnostics));
        OnPropertyChanged(nameof(CypherResultMinimumWidth));
    }

    private void SelectCypherCell(GraphQueryCellViewModel cell)
    {
        if (cell?.Entity is GraphEntityKey entity)
        {
            SetSelection([entity], entity);
        }
        else if (cell?.Relationship is GraphRelationshipKey relationship)
        {
            SelectedRelationship = relationship;
        }
    }

    private void SynchronizeCatalog(GraphCatalog catalog)
    {
        Graphs.Clear();

        foreach (GraphCatalogEntry graph in catalog.Graphs)
        {
            Graphs.Add(graph);
        }

        suppressGraphActivation = true;

        try
        {
            SelectedGraph = Graphs.Single(graph => graph.GraphId == catalog.ActiveGraphId);
        }
        finally
        {
            suppressGraphActivation = false;
        }

        OnPropertyChanged(nameof(CanDeleteGraph));
        OnPropertyChanged(nameof(CanDeleteAllGraphs));
        OnPropertyChanged(nameof(DeleteAllGraphsSummary));
        DeleteAllGraphsCommand.NotifyCanExecuteChanged();
        DeleteGraphCommand.NotifyCanExecuteChanged();
        OpenDeleteAllGraphsCommand.NotifyCanExecuteChanged();
        OpenDeleteGraphCommand.NotifyCanExecuteChanged();
    }

    private string ValidateGraphMetadata()
    {
        string error = string.Empty;

        if (string.IsNullOrWhiteSpace(GraphEditorName))
        {
            error = "Enter a graph name.";
        }
        else if (GraphEditorName.Trim().Length > GraphCatalogMetadata.MaximumNameLength)
        {
            error = $"Graph names cannot exceed {GraphCatalogMetadata.MaximumNameLength:N0} characters.";
        }

        return error;
    }

    private void OnSelectedEntitiesChanged(object? sender, NotifyCollectionChangedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        if (!suppressSelectionSynchronization
            && (SelectedEntity is null || !SelectedEntities.Contains(SelectedEntity.Value)))
        {
            suppressSelectionSynchronization = true;

            try
            {
                SelectedEntity = SelectedEntities.Count > 0 ? SelectedEntities[^1] : null;
            }
            finally
            {
                suppressSelectionSynchronization = false;
            }
        }

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionCountText));
        OnPropertyChanged(nameof(CanReloadNeighborhood));
        OnPropertyChanged(nameof(CanFilterToSelection));
        ReloadNeighborhoodCommand.NotifyCanExecuteChanged();
        KeepConnectedCommand.NotifyCanExecuteChanged();
    }

    private void SetSelection(IEnumerable<GraphEntityKey> entities, GraphEntityKey? primaryEntity)
    {
        SelectedRelationship = null;
        GraphEntityKey[] snapshot = entities.Distinct().ToArray();
        suppressSelectionSynchronization = true;

        try
        {
            SelectedEntities.Clear();

            foreach (GraphEntityKey entity in snapshot)
            {
                SelectedEntities.Add(entity);
            }
        }
        finally
        {
            suppressSelectionSynchronization = false;
        }

        GraphEntityKey? effectivePrimary = null;

        if (primaryEntity is GraphEntityKey primary && snapshot.Contains(primary))
        {
            effectivePrimary = primary;
        }
        else if (snapshot.Length > 0)
        {
            effectivePrimary = snapshot[0];
        }

        SelectedEntity = effectivePrimary;
        NotifySelectionChanged();
    }

    private void SynchronizeSelectedEntities(GraphEntityKey? primaryEntity)
    {
        if (suppressSelectionSynchronization)
        {
            return;
        }

        suppressSelectionSynchronization = true;

        try
        {
            if (primaryEntity is null)
            {
                SelectedEntities.Clear();
            }
            else if (!SelectedEntities.Contains(primaryEntity.Value))
            {
                SelectedEntities.Clear();
                SelectedEntities.Add(primaryEntity.Value);
            }
        }
        finally
        {
            suppressSelectionSynchronization = false;
        }

        NotifySelectionChanged();
    }

    private void UpdateLegendItems()
    {
        LegendItems.Clear();
        IEnumerable<GraphTypeLegendItem> legendItems = Layout is null
            ? []
            : Layout.Nodes
                .GroupBy(node => node.Entity.Entity.TypeName, StringComparer.Ordinal)
                .Select(group => new GraphTypeLegendItem(
                    group.Key,
                    GraphTypeColor.GetLightAccentHex(GraphTypeColor.GetIndex(group.Key)),
                    group.Count()))
                .OrderByDescending(item => item.NodeCount)
                .ThenBy(item => item.TypeName, StringComparer.OrdinalIgnoreCase);

        foreach (GraphTypeLegendItem item in legendItems)
        {
            LegendItems.Add(item);
        }

        OnPropertyChanged(nameof(HasLegendItems));
    }

    private async Task LoadSelectedEntityDetailsAsync(GraphEntityKey entity, int requestVersion)
    {
        IsSelectedEntityDetailsLoading = true;

        try
        {
            GraphSnapshot snapshot = GetDisplayedSnapshot();
            GraphEntityDetails? details = await graphStore.GetEntityDetailsAsync(snapshot, entity);

            if (requestVersion == selectedEntityDetailsRequestVersion && SelectedEntity == entity)
            {
                SelectedEntityDetails = details;
                SelectedEntityDetailsError = details is null
                    ? "Node properties are no longer available in the selected graph state."
                    : string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (requestVersion == selectedEntityDetailsRequestVersion)
            {
                SelectedEntityDetailsError = $"Could not load node properties: {exception.Message}";
            }
        }
        finally
        {
            if (requestVersion == selectedEntityDetailsRequestVersion)
            {
                IsSelectedEntityDetailsLoading = false;
            }
        }
    }

    private async Task LoadSelectedRelationshipDetailsAsync(
        GraphRelationshipKey relationship,
        int requestVersion)
    {
        IsSelectedRelationshipDetailsLoading = true;

        try
        {
            GraphSnapshot snapshot = GetDisplayedSnapshot();
            GraphRelationshipDetails? details = await graphStore.GetRelationshipDetailsAsync(
                snapshot,
                relationship);

            if (requestVersion == selectedRelationshipDetailsRequestVersion
                && SelectedRelationship == relationship)
            {
                SelectedRelationshipDetails = details;
                SelectedRelationshipDetailsError = details is null
                    ? "Relationship evidence is no longer available in the selected graph."
                    : string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (requestVersion == selectedRelationshipDetailsRequestVersion)
            {
                SelectedRelationshipDetailsError = $"Could not load relationship evidence: {exception.Message}";
            }
        }
        finally
        {
            if (requestVersion == selectedRelationshipDetailsRequestVersion)
            {
                IsSelectedRelationshipDetailsLoading = false;
            }
        }
    }

    private string CreateViewportStatus()
    {
        GraphLayout? currentLayout = Layout;

        if (currentLayout is null || currentLayout.IsEmpty)
        {
            return "No graph data in this viewport";
        }

        string status = $"Showing {FormatCount(currentLayout.Nodes.Count, "node")} · {FormatCount(currentLayout.Edges.Count, "edge")}";
        return HasHiddenItems ? $"{status} · {HiddenItemsText}" : status;
    }

    private async Task LoadViewportAsync(
        GraphEntitySummary? center,
        CancellationToken cancellationToken)
    {
        if (center is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"Loading neighborhood for {center.DisplayLabel}";

        try
        {
            SetSelection([center.Entity], center.Entity);
            await UpdateNeighborhoodAsync(
                SelectedEntities,
                NeighborhoodDepth,
                cancellationToken);
            StatusText = CreateViewportStatus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not load neighborhood: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusText = "Searching graph";

        try
        {
            GraphSnapshot snapshot = GetCurrentSnapshot();
            IReadOnlyList<GraphEntitySummary> results = await graphStore.SearchEntitiesAsync(
                snapshot,
                SearchText,
                MaximumSearchResults,
                cancellationToken);
            SearchResults.Clear();

            foreach (GraphEntitySummary result in results)
            {
                SearchResults.Add(result);
            }

            suppressSelectionLoad = true;
            try
            {
                SelectedSearchResult = SearchResults.FirstOrDefault();
            }
            finally
            {
                suppressSelectionLoad = false;
            }

            if (SelectedSearchResult is not null)
            {
                GraphEntityKey[] matchingEntities = SearchResults
                    .Select(result => result.Entity)
                    .ToArray();
                SetSelection(matchingEntities, SelectedSearchResult.Entity);
                await UpdateNeighborhoodAsync(
                    matchingEntities,
                    NeighborhoodDepth,
                    cancellationToken);
                StatusText = CreateViewportStatus();
            }
            else
            {
                StatusText = "No matching nodes";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not search graph: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdateViewportAsync(
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        GraphSnapshot snapshot = GetCurrentSnapshot();
        GraphViewport viewport = await graphStore.GetViewportAsync(
            snapshot,
            center,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
        await ApplySourceViewportAsync(viewport, center is not null, false, cancellationToken);
        viewportEntityLimit = maximumEntityCount;
        viewportRelationshipLimit = maximumRelationshipCount;
        NotifyViewportVisibilityChanged();
    }

    private async Task UpdateNeighborhoodAsync(
        IReadOnlyCollection<GraphEntityKey> centers,
        int depth,
        CancellationToken cancellationToken)
    {
        GraphSnapshot snapshot = GetCurrentSnapshot();
        GraphViewport viewport = await graphStore.GetNeighborhoodAsync(
            snapshot,
            centers,
            depth,
            ExpandedViewportEntityCount,
            ExpandedViewportRelationshipCount,
            cancellationToken);
        await ApplySourceViewportAsync(viewport, true, true, cancellationToken);
        viewportEntityLimit = ExpandedViewportEntityCount;
        viewportRelationshipLimit = ExpandedViewportRelationshipCount;
        NotifyViewportVisibilityChanged();
    }

    private async Task ApplySourceViewportAsync(
        GraphViewport viewport,
        bool isFocused,
        bool preserveSelection,
        CancellationToken cancellationToken)
    {
        GraphLayout nextLayout = await layoutService.LayoutAsync(viewport, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        sourceViewport = viewport;
        visibleViewport = viewport;
        HasViewFilters = false;
        isFocusedViewport = isFocused;
        Layout = nextLayout;

        if (preserveSelection)
        {
            GraphEntityKey[] retainedSelection = SelectedEntities
                .Where(entity => viewport.Entities.Any(summary => summary.Entity == entity))
                .ToArray();
            GraphEntityKey? primary = SelectedEntity is GraphEntityKey selected
                && retainedSelection.Contains(selected)
                    ? selected
                    : retainedSelection.Cast<GraphEntityKey?>().FirstOrDefault();
            SetSelection(retainedSelection, primary);
        }
        else
        {
            SetSelection(
                viewport.Center is GraphEntityKey center ? [center] : [],
                viewport.Center);
        }
    }

    private async Task ReloadSelectedNeighborhoodAsync(CancellationToken cancellationToken)
    {
        if (SelectedEntities.Count > 0)
        {
            IsBusy = true;
            StatusText = $"Loading {NeighborhoodDepthText} around {SelectionCountText}";

            try
            {
                await UpdateNeighborhoodAsync(
                    SelectedEntities.ToArray(),
                    NeighborhoodDepth,
                    cancellationToken);
                StatusText = CreateViewportStatus();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Canceled";
            }
            catch (Exception exception)
            {
                StatusText = $"Could not load graph neighborhood: {exception.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task ExpandNodeAsync(
        GraphEntityKey? entity,
        CancellationToken cancellationToken)
    {
        if (entity is GraphEntityKey contextNode)
        {
            GraphEntityKey[] expandedSelection = SelectedEntities
                .Append(contextNode)
                .Distinct()
                .ToArray();
            SetSelection(expandedSelection, contextNode);
            NeighborhoodDepth = Math.Min(MaximumNeighborhoodDepth, NeighborhoodDepth + 1);
            await ReloadSelectedNeighborhoodAsync(cancellationToken);
        }
    }

    private void SelectOnlyNode(GraphEntityKey? entity)
    {
        if (entity is GraphEntityKey contextNode)
        {
            SetSelection([contextNode], contextNode);
            StatusText = $"Selected {contextNode.CanonicalId}";
        }
    }

    private void SetRouteStart(GraphEntityKey? entity)
    {
        if (entity is GraphEntityKey start && CanSetRouteEndpoint(entity))
        {
            routeStartLabel = FindEntityDisplayLabel(start);
            RouteStart = start;
            SetSelection([start], start);
            StatusText = $"Route start set to {routeStartLabel}";
        }
    }

    private async Task ShowRoutesToNodeAsync(
        GraphEntityKey? entity,
        CancellationToken cancellationToken)
    {
        if (RouteStart is not GraphEntityKey start
            || entity is not GraphEntityKey destination
            || !CanShowRoutesToNode(entity))
        {
            return;
        }

        string destinationLabel = FindEntityDisplayLabel(destination);
        IsFindingRoutes = true;
        IsBusy = true;
        StatusText = $"Finding routes from {routeStartLabel} to {destinationLabel}";

        try
        {
            GraphRouteResult result = await graphStore.FindRoutesAsync(
                GetCurrentSnapshot(),
                start,
                destination,
                ExpandedViewportEntityCount,
                ExpandedViewportRelationshipCount,
                cancellationToken);
            await ApplySourceViewportAsync(result.Viewport, true, false, cancellationToken);
            SetSelection([start, destination], destination);
            viewportEntityLimit = ExpandedViewportEntityCount;
            viewportRelationshipLimit = ExpandedViewportRelationshipCount;
            NotifyViewportVisibilityChanged();
            StatusText = CreateRouteStatus(result, destinationLabel);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Route search canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not find routes: {exception.Message}";
        }
        finally
        {
            IsFindingRoutes = false;
            IsBusy = false;
        }
    }

    private string CreateRouteStatus(GraphRouteResult result, string destinationLabel)
    {
        if (!result.IsConnected)
        {
            return $"No route connects {routeStartLabel} to {destinationLabel}";
        }

        string truncationText = result.Viewport.IsTruncated ? " · showing one shortest route" : string.Empty;
        return $"{FormatCount(result.ShortestHopCount.GetValueOrDefault(), "hop")} · "
            + $"{FormatCount(result.Viewport.Entities.Count, "route node")} · "
            + $"{FormatCount(result.Viewport.Relationships.Count, "route edge")}{truncationText}";
    }

    private string FindEntityDisplayLabel(GraphEntityKey entity)
    {
        return visibleViewport?.Entities.FirstOrDefault(summary => summary.Entity == entity)?.DisplayLabel
            ?? SearchResults.FirstOrDefault(summary => summary.Entity == entity)?.DisplayLabel
            ?? entity.CanonicalId;
    }

    private void ResetRouteStart()
    {
        routeStartLabel = string.Empty;
        RouteStart = null;
    }

    private async Task KeepConnectedAsync(CancellationToken cancellationToken)
    {
        GraphViewport? currentViewport = visibleViewport;

        if (currentViewport is not null)
        {
            HashSet<GraphEntityKey> connectedEntities = GetConnectedEntities(
                currentViewport,
                SelectedEntities);
            await ApplyEntityFilterAsync(
                connectedEntities,
                "Removed nodes unrelated to the selection",
                cancellationToken);
        }
    }

    private async Task HideNodeAsync(
        GraphEntityKey? entity,
        CancellationToken cancellationToken)
    {
        if (entity is GraphEntityKey contextNode && visibleViewport is GraphViewport currentViewport)
        {
            HashSet<GraphEntityKey> retainedEntities = currentViewport.Entities
                .Select(summary => summary.Entity)
                .Where(candidate => candidate != contextNode)
                .ToHashSet();
            await ApplyEntityFilterAsync(
                retainedEntities,
                $"Hid {contextNode.CanonicalId}",
                cancellationToken);
        }
    }

    private async Task HideTypeAsync(
        GraphEntityKey? entity,
        CancellationToken cancellationToken)
    {
        if (entity is GraphEntityKey contextNode && visibleViewport is GraphViewport currentViewport)
        {
            HashSet<GraphEntityKey> retainedEntities = currentViewport.Entities
                .Select(summary => summary.Entity)
                .Where(candidate => !string.Equals(
                    candidate.TypeName,
                    contextNode.TypeName,
                    StringComparison.Ordinal))
                .ToHashSet();
            await ApplyEntityFilterAsync(
                retainedEntities,
                $"Hid type {contextNode.TypeName}",
                cancellationToken);
        }
    }

    private async Task PruneUpstreamAsync(
        GraphEntityKey? entity,
        CancellationToken cancellationToken)
    {
        if (entity is GraphEntityKey contextNode && visibleViewport is GraphViewport currentViewport)
        {
            HashSet<GraphEntityKey> prunedEntities = GetIncomingClosure(currentViewport, contextNode);
            HashSet<GraphEntityKey> retainedEntities = currentViewport.Entities
                .Select(summary => summary.Entity)
                .Where(candidate => !prunedEntities.Contains(candidate))
                .ToHashSet();
            await ApplyEntityFilterAsync(
                retainedEntities,
                $"Pruned {FormatCount(prunedEntities.Count, "upstream node")} from this view",
                cancellationToken);
        }
    }

    private async Task ApplyEntityFilterAsync(
        HashSet<GraphEntityKey> retainedEntities,
        string completedStatus,
        CancellationToken cancellationToken)
    {
        GraphViewport? currentViewport = visibleViewport;

        if (currentViewport is not null)
        {
            GraphEntitySummary[] entities = currentViewport.Entities
                .Where(entity => retainedEntities.Contains(entity.Entity))
                .ToArray();
            GraphRelationshipKey[] relationships = currentViewport.Relationships
                .Where(relationship => retainedEntities.Contains(relationship.Source)
                    && retainedEntities.Contains(relationship.Target))
                .ToArray();
            GraphEntityKey? center = currentViewport.Center is GraphEntityKey currentCenter
                && retainedEntities.Contains(currentCenter)
                    ? currentCenter
                    : SelectedEntities
                        .Where(retainedEntities.Contains)
                        .Cast<GraphEntityKey?>()
                        .FirstOrDefault();
            bool isTruncated = currentViewport.IsTruncated
                || entities.Length < currentViewport.Entities.Count
                || relationships.Length < currentViewport.Relationships.Count;
            GraphViewport filteredViewport = new(
                center,
                entities,
                relationships,
                isTruncated);
            visibleViewport = filteredViewport;
            HasViewFilters = true;
            Layout = await layoutService.LayoutAsync(filteredViewport, cancellationToken);
            GraphEntityKey[] retainedSelection = SelectedEntities
                .Where(retainedEntities.Contains)
                .ToArray();
            SetSelection(retainedSelection, center);
            StatusText = completedStatus;
            NotifyViewportVisibilityChanged();
        }
    }

    private async Task ResetFiltersAsync(CancellationToken cancellationToken)
    {
        if (sourceViewport is GraphViewport unfilteredViewport)
        {
            visibleViewport = unfilteredViewport;
            Layout = await layoutService.LayoutAsync(unfilteredViewport, cancellationToken);
            HasViewFilters = false;
            StatusText = CreateViewportStatus();
            NotifyViewportVisibilityChanged();
        }
    }

    private async Task ShowMoreAsync(CancellationToken cancellationToken)
    {
        await LoadOverviewAsync(
            ExpandedViewportEntityCount,
            ExpandedViewportRelationshipCount,
            "Expanding graph overview",
            cancellationToken);
    }

    private async Task ShowOverviewAsync(CancellationToken cancellationToken)
    {
        if (State is GraphStateSummary currentState)
        {
            await LoadOverviewAsync(
                GetFullViewportLimit(currentState.EntityCount),
                GetFullViewportLimit(currentState.RelationshipCount),
                "Loading graph overview",
                cancellationToken);
        }
    }

    private async Task LoadOverviewAsync(
        int maximumEntityCount,
        int maximumRelationshipCount,
        string loadingStatus,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusText = loadingStatus;

        try
        {
            await UpdateViewportAsync(
                null,
                maximumEntityCount,
                maximumRelationshipCount,
                cancellationToken);
            StatusText = CreateViewportStatus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not load graph overview: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyViewportVisibilityChanged()
    {
        OnPropertyChanged(nameof(HiddenEntityCount));
        OnPropertyChanged(nameof(HiddenRelationshipCount));
        OnPropertyChanged(nameof(HasHiddenItems));
        OnPropertyChanged(nameof(CanShowMore));
        OnPropertyChanged(nameof(CanShowOverview));
        OnPropertyChanged(nameof(HiddenItemsText));
        OnPropertyChanged(nameof(HiddenItemsHelpText));
        OnPropertyChanged(nameof(ShowMoreButtonText));
        ShowMoreCommand.NotifyCanExecuteChanged();
        ShowOverviewCommand.NotifyCanExecuteChanged();
    }
}
