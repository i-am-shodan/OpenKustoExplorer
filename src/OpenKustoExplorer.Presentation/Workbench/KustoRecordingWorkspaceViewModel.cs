using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Coordinates named query recording sessions and their investigation workflow.
/// </summary>
public sealed class KustoRecordingWorkspaceViewModel : ObservableObject
{
    private readonly HashSet<KustoRecordedValueIdentity> activeInterests = [];
    private readonly HashSet<string> activeManualInterestValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly IKustoRecordedChainQueryGenerator? chainGenerator;
    private readonly IKustoRecordedChainSearcher? chainSearcher;
    private readonly IKustoPredicateInterestExtractor? interestExtractor;
    private readonly IKustoRecordedRelationExtractor? relationExtractor;
    private readonly IKustoRecordedRelationPlanner? relationPlanner;
    private readonly IKustoRecordedSessionStore? store;
    private readonly TimeProvider timeProvider;
    private Guid? activeSessionId;
    private string activeSessionName = string.Empty;
    private KustoRecordingPeriod? activePeriod;
    private KustoRecordedExecutionViewModel? executionBeingRenamed;
    private bool isAppendMode;
    private bool isChainGenerationDialogOpen;
    private bool isDatabaseRecoveryOpen;
    private bool isDeleteConfirmationOpen;
    private bool isDeleteExecutionConfirmationOpen;
    private bool isLoading;
    private bool isRenameExecutionOpen;
    private bool isRecordingDialogOpen;
    private string newSessionName = string.Empty;
    private KustoRecordedExecutionViewModel? observedExecution;
    private string recordingErrorText = string.Empty;
    private string renameExecutionErrorText = string.Empty;
    private string renameExecutionName = string.Empty;
    private KustoRecordedSessionViewModel? selectedSession;
    private KustoRecordedSessionSummaryViewModel? selectedSessionSummary;
    private KustoRecordedSessionSummaryViewModel? selectedAppendSession;
    private KustoResultCellViewModel? contextCell;
    private string chainGenerationGuidanceText = string.Empty;
    private string chainGenerationDialogMessage = string.Empty;
    private string databaseRecoveryErrorText = string.Empty;
    private string databaseRecoveryMessage = string.Empty;
    private string generatedQueryText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordingWorkspaceViewModel"/> class without recording services.
    /// </summary>
    public KustoRecordingWorkspaceViewModel()
        : this(null, null, null, null, null, null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordingWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="store">The recorded-session store.</param>
    /// <param name="interestExtractor">The exact predicate-interest extractor.</param>
    /// <param name="relationExtractor">The conservative source-relation extractor.</param>
    /// <param name="chainSearcher">The weighted pivot-chain searcher.</param>
    /// <param name="relationPlanner">The relational plan minimizer.</param>
    /// <param name="chainGenerator">The validated KQL generator.</param>
    /// <param name="timeProvider">The application clock.</param>
    public KustoRecordingWorkspaceViewModel(
        IKustoRecordedSessionStore? store,
        IKustoPredicateInterestExtractor? interestExtractor,
        IKustoRecordedRelationExtractor? relationExtractor,
        IKustoRecordedChainSearcher? chainSearcher,
        IKustoRecordedRelationPlanner? relationPlanner,
        IKustoRecordedChainQueryGenerator? chainGenerator,
        TimeProvider? timeProvider = null)
    {
        this.store = store;
        this.interestExtractor = interestExtractor;
        this.relationExtractor = relationExtractor;
        this.chainSearcher = chainSearcher;
        this.relationPlanner = relationPlanner;
        this.chainGenerator = chainGenerator;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        Sessions = new ObservableCollection<KustoRecordedSessionSummaryViewModel>();
        OpenRecordingCommand = new AsyncRelayCommand(OpenRecordingAsync, () => IsAvailable && !IsRecording);
        CloseRecordingCommand = new RelayCommand(CloseRecordingDialog);
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => CanStartRecording);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsAvailable && !IsLoading);
        CancelDatabaseRecoveryCommand = new RelayCommand(CancelDatabaseRecovery);
        ResetDatabaseCommand = new AsyncRelayCommand(ResetDatabaseAsync, () => CanResetDatabase);
        SelectSessionCommand = new AsyncRelayCommand<KustoRecordedSessionSummaryViewModel>(SelectSessionAsync);
        OpenDeleteSessionCommand = new RelayCommand(OpenDeleteSession, () => CanDeleteSelectedSession);
        CancelDeleteSessionCommand = new RelayCommand(() => IsDeleteConfirmationOpen = false);
        DeleteSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync, () => CanDeleteSelectedSession);
        OpenDeleteExecutionCommand = new RelayCommand(OpenDeleteExecution, () => CanDeleteSelectedExecution);
        CancelDeleteExecutionCommand = new RelayCommand(() => IsDeleteExecutionConfirmationOpen = false);
        DeleteExecutionCommand = new AsyncRelayCommand(
            DeleteSelectedExecutionAsync,
            () => CanDeleteSelectedExecution);
        CloseRenameExecutionCommand = new RelayCommand(CloseRenameExecution);
        SaveRenameExecutionCommand = new AsyncRelayCommand(
            SaveRenameExecutionAsync,
            () => CanSaveRenameExecution);
        DismissChainGenerationDialogCommand = new RelayCommand(
            () => IsChainGenerationDialogOpen = false);
        AddChainEvidenceCommand = new RelayCommand(OpenChainEvidenceRecording, CanAddChainEvidence);
        MarkSelectedCellCommand = new AsyncRelayCommand(MarkSelectedCellAsync, CanAnnotateSelectedCell);
        MarkSelectedColumnCommand = new AsyncRelayCommand(MarkSelectedColumnAsync, CanAnnotateSelectedCell);
        UnmarkSelectedCellCommand = new AsyncRelayCommand(UnmarkSelectedCellAsync, CanAnnotateSelectedCell);
        SetChainStartCommand = new AsyncRelayCommand(
            cancellationToken => SetEndpointAsync(KustoChainEndpointRole.Start, cancellationToken),
            CanAnnotateSelectedCell);
        SetChainEndCommand = new AsyncRelayCommand(
            cancellationToken => SetEndpointAsync(KustoChainEndpointRole.End, cancellationToken),
            CanAnnotateSelectedCell);
        ClearChainEndpointsCommand = new AsyncRelayCommand(
            ClearEndpointsAsync,
            () => HasChainStart || HasChainEnd);
    }

    /// <summary>
    /// Occurs when active recording interests change and live result annotations should be refreshed.
    /// </summary>
    public event EventHandler? ActiveInterestsChanged;

    /// <summary>Gets recorded-session catalog entries.</summary>
    public ObservableCollection<KustoRecordedSessionSummaryViewModel> Sessions { get; }

    /// <summary>Gets a value indicating whether recording services are configured.</summary>
    public bool IsAvailable => store is not null
        && interestExtractor is not null
        && relationExtractor is not null
        && chainSearcher is not null
        && relationPlanner is not null
        && chainGenerator is not null;

    /// <summary>Gets a value indicating whether a session is actively recording.</summary>
    public bool IsRecording => activePeriod is not null;

    /// <summary>Gets the active session name.</summary>
    public string ActiveSessionName => activeSessionName;

    /// <summary>Gets an accessible active-recording description.</summary>
    public string RecordingAutomationText => IsRecording
        ? $"Recording session {ActiveSessionName}"
        : "Query recording stopped";

    /// <summary>Gets a value indicating whether the start-recording dialog is visible.</summary>
    public bool IsRecordingDialogOpen
    {
        get => isRecordingDialogOpen;
        private set
        {
            if (SetProperty(ref isRecordingDialogOpen, value))
            {
                NotifyStartStateChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether a recording appends to an existing session.</summary>
    public bool IsAppendMode
    {
        get => isAppendMode;
        set
        {
            if (SetProperty(ref isAppendMode, value))
            {
                NotifyStartStateChanged();
            }
        }
    }

    /// <summary>Gets or sets the proposed new session name.</summary>
    public string NewSessionName
    {
        get => newSessionName;
        set
        {
            if (SetProperty(ref newSessionName, value))
            {
                NotifyStartStateChanged();
            }
        }
    }

    /// <summary>Gets or sets the existing session selected for append.</summary>
    public KustoRecordedSessionSummaryViewModel? SelectedAppendSession
    {
        get => selectedAppendSession;
        set
        {
            if (SetProperty(ref selectedAppendSession, value))
            {
                NotifyStartStateChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether recording can start.</summary>
    public bool CanStartRecording => IsAvailable
        && IsRecordingDialogOpen
        && !IsRecording
        && (IsAppendMode ? SelectedAppendSession is not null : !string.IsNullOrWhiteSpace(NewSessionName));

    /// <summary>Gets the latest recording validation or persistence error.</summary>
    public string RecordingErrorText
    {
        get => recordingErrorText;
        private set
        {
            if (SetProperty(ref recordingErrorText, value))
            {
                OnPropertyChanged(nameof(HasRecordingError));
            }
        }
    }

    /// <summary>Gets a value indicating whether a recording error is visible.</summary>
    public bool HasRecordingError => RecordingErrorText.Length > 0;

    /// <summary>Gets the directory containing recorded-session storage.</summary>
    public string DatabaseDirectoryPath =>
        (store as IKustoRecordedSessionStoreMaintenance)?.DatabaseDirectoryPath ?? string.Empty;

    /// <summary>Gets the incompatible-database recovery guidance.</summary>
    public string DatabaseRecoveryMessage
    {
        get => databaseRecoveryMessage;
        private set => SetProperty(ref databaseRecoveryMessage, value);
    }

    /// <summary>Gets the latest database reset failure.</summary>
    public string DatabaseRecoveryErrorText
    {
        get => databaseRecoveryErrorText;
        private set
        {
            if (SetProperty(ref databaseRecoveryErrorText, value))
            {
                OnPropertyChanged(nameof(HasDatabaseRecoveryError));
            }
        }
    }

    /// <summary>Gets a value indicating whether database reset failed.</summary>
    public bool HasDatabaseRecoveryError => DatabaseRecoveryErrorText.Length > 0;

    /// <summary>Gets a value indicating whether incompatible storage recovery is visible.</summary>
    public bool IsDatabaseRecoveryOpen
    {
        get => isDatabaseRecoveryOpen;
        private set
        {
            if (SetProperty(ref isDatabaseRecoveryOpen, value))
            {
                OnPropertyChanged(nameof(CanResetDatabase));
                ResetDatabaseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether incompatible storage can be reset.</summary>
    public bool CanResetDatabase => IsDatabaseRecoveryOpen
        && !IsLoading
        && store is IKustoRecordedSessionStoreMaintenance;

    /// <summary>Gets a value indicating whether session data is loading.</summary>
    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            if (SetProperty(ref isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanResetDatabase));
                ResetDatabaseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether any recorded sessions exist.</summary>
    public bool HasSessions => Sessions.Count > 0;

    /// <summary>Gets or sets the selected session catalog entry.</summary>
    public KustoRecordedSessionSummaryViewModel? SelectedSessionSummary
    {
        get => selectedSessionSummary;
        set
        {
            if (SetProperty(ref selectedSessionSummary, value))
            {
                OnPropertyChanged(nameof(CanDeleteSelectedSession));
                OpenDeleteSessionCommand.NotifyCanExecuteChanged();
                DeleteSessionCommand.NotifyCanExecuteChanged();
                AddChainEvidenceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the loaded selected session.</summary>
    public KustoRecordedSessionViewModel? SelectedSession
    {
        get => selectedSession;
        private set
        {
            if (SetProperty(ref selectedSession, value))
            {
                ObserveSelectedExecution();
                OnPropertyChanged(nameof(HasSelectedSession));
                OnPropertyChanged(nameof(SelectedExecution));
                OnPropertyChanged(nameof(HasSelectedExecution));
                OnPropertyChanged(nameof(SelectedTable));
                OnPropertyChanged(nameof(HasSelectedTable));
                NotifyChainStateChanged();
                NotifyExecutionDeleteStateChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether a complete session is selected.</summary>
    public bool HasSelectedSession => SelectedSession is not null;

    /// <summary>Gets or sets the query whose retained result is displayed.</summary>
    public KustoRecordedExecutionViewModel? SelectedExecution
    {
        get => SelectedSession?.SelectedExecution;
        set
        {
            if (SelectedSession is not null
                && !ReferenceEquals(SelectedSession.SelectedExecution, value))
            {
                ClearResultContext();
                SelectedSession.SelectedExecution = value;
                ObserveSelectedExecution();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedExecution));
                OnPropertyChanged(nameof(SelectedTable));
                OnPropertyChanged(nameof(HasSelectedTable));
                NotifyExecutionDeleteStateChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether a recorded query is selected.</summary>
    public bool HasSelectedExecution => SelectedExecution is not null;

    /// <summary>Gets or sets the retained result table displayed for the selected query.</summary>
    public KustoRecordedResultTableViewModel? SelectedTable
    {
        get => SelectedExecution?.SelectedTable;
        set
        {
            if (SelectedExecution is not null
                && !ReferenceEquals(SelectedExecution.SelectedTable, value))
            {
                ClearResultContext();
                SelectedExecution.SelectedTable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedTable));
            }
        }
    }

    /// <summary>Gets a value indicating whether a retained result table is selected.</summary>
    public bool HasSelectedTable => SelectedTable is not null;

    /// <summary>Gets a value indicating whether the selected session can be deleted.</summary>
    public bool CanDeleteSelectedSession => SelectedSessionSummary is not null
        && SelectedSessionSummary.Id != activeSessionId;

    /// <summary>Gets a value indicating whether the selected recorded query can be deleted.</summary>
    public bool CanDeleteSelectedExecution => SelectedExecution is not null
        && SelectedExecution.Execution.Status != KustoRecordedExecutionStatus.Running;

    /// <summary>Gets a value indicating whether session deletion confirmation is visible.</summary>
    public bool IsDeleteConfirmationOpen
    {
        get => isDeleteConfirmationOpen;
        private set => SetProperty(ref isDeleteConfirmationOpen, value);
    }

    /// <summary>Gets a value indicating whether query deletion confirmation is visible.</summary>
    public bool IsDeleteExecutionConfirmationOpen
    {
        get => isDeleteExecutionConfirmationOpen;
        private set => SetProperty(ref isDeleteExecutionConfirmationOpen, value);
    }

    /// <summary>Gets a value indicating whether the recorded-query rename dialog is visible.</summary>
    public bool IsRenameExecutionOpen
    {
        get => isRenameExecutionOpen;
        private set => SetProperty(ref isRenameExecutionOpen, value);
    }

    /// <summary>Gets or sets the proposed recorded-query name.</summary>
    public string RenameExecutionName
    {
        get => renameExecutionName;
        set
        {
            if (SetProperty(ref renameExecutionName, value))
            {
                OnPropertyChanged(nameof(CanSaveRenameExecution));
                SaveRenameExecutionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the latest recorded-query rename error.</summary>
    public string RenameExecutionErrorText
    {
        get => renameExecutionErrorText;
        private set
        {
            if (SetProperty(ref renameExecutionErrorText, value))
            {
                OnPropertyChanged(nameof(HasRenameExecutionError));
            }
        }
    }

    /// <summary>Gets a value indicating whether a recorded-query rename error is visible.</summary>
    public bool HasRenameExecutionError => RenameExecutionErrorText.Length > 0;

    /// <summary>Gets a value indicating whether the proposed recorded-query name can be saved.</summary>
    public bool CanSaveRenameExecution => executionBeingRenamed is not null
        && !string.IsNullOrWhiteSpace(RenameExecutionName)
        && RenameExecutionName.Trim().Length <= 80;

    /// <summary>Gets a value indicating whether chain-generation guidance is visible.</summary>
    public bool IsChainGenerationDialogOpen
    {
        get => isChainGenerationDialogOpen;
        private set => SetProperty(ref isChainGenerationDialogOpen, value);
    }

    /// <summary>Gets the reason the selected chain could not be generated.</summary>
    public string ChainGenerationDialogMessage
    {
        get => chainGenerationDialogMessage;
        private set => SetProperty(ref chainGenerationDialogMessage, value);
    }

    /// <summary>Gets guidance for collecting evidence that can produce a safe query.</summary>
    public string ChainGenerationGuidanceText
    {
        get => chainGenerationGuidanceText;
        private set => SetProperty(ref chainGenerationGuidanceText, value);
    }

    /// <summary>Gets a value indicating whether a chain start value has been selected.</summary>
    public bool HasChainStart => GetEndpoint(KustoChainEndpointRole.Start) is not null;

    /// <summary>Gets a value indicating whether a chain end value has been selected.</summary>
    public bool HasChainEnd => GetEndpoint(KustoChainEndpointRole.End) is not null;

    /// <summary>Gets a value indicating whether both endpoints are ready for query generation.</summary>
    public bool CanGenerateChain => IsAvailable && HasChainStart && HasChainEnd;

    /// <summary>Gets the selected chain start value.</summary>
    public string ChainStartValueText => GetEndpointDisplay(KustoChainEndpointRole.Start).ValueText;

    /// <summary>Gets the selected chain start location.</summary>
    public string ChainStartLocationText => GetEndpointDisplay(KustoChainEndpointRole.Start).LocationText;

    /// <summary>Gets the selected chain end value.</summary>
    public string ChainEndValueText => GetEndpointDisplay(KustoChainEndpointRole.End).ValueText;

    /// <summary>Gets the selected chain end location.</summary>
    public string ChainEndLocationText => GetEndpointDisplay(KustoChainEndpointRole.End).LocationText;

    /// <summary>Gets a value indicating whether a historical result value is selected for an action.</summary>
    public bool HasSelectedResultValue => contextCell is not null;

    /// <summary>Gets the selected historical result value.</summary>
    public string SelectedResultValueText => contextCell is null
        ? "No value selected"
        : FormatValue(contextCell.Value);

    /// <summary>Gets the selected historical result value location.</summary>
    public string SelectedResultValueLocationText => contextCell is null
        ? "Select a value in the results"
        : $"{SelectedTable?.DisplayName} · {contextCell.ColumnName} · row {contextCell.Row.RowIndex + 1:N0}";

    /// <summary>Gets a value indicating whether the selected value was manually marked interesting.</summary>
    public bool SelectedResultValueIsMarked => contextCell?.IsRecordedPertinent == true;

    /// <summary>Gets the latest generated KQL text.</summary>
    public string GeneratedQueryText
    {
        get => generatedQueryText;
        private set
        {
            if (SetProperty(ref generatedQueryText, value))
            {
                OnPropertyChanged(nameof(HasGeneratedQuery));
            }
        }
    }

    /// <summary>Gets a value indicating whether valid generated KQL is available.</summary>
    public bool HasGeneratedQuery => GeneratedQueryText.Length > 0;

    /// <summary>Gets the command that opens the new-or-append recording dialog.</summary>
    public IAsyncRelayCommand OpenRecordingCommand { get; }

    /// <summary>Gets the command that closes the recording dialog.</summary>
    public IRelayCommand CloseRecordingCommand { get; }

    /// <summary>Gets the command that creates or appends and begins recording.</summary>
    public IAsyncRelayCommand StartRecordingCommand { get; }

    /// <summary>Gets the command that stops active recording.</summary>
    public IAsyncRelayCommand StopRecordingCommand { get; }

    /// <summary>Gets the command that refreshes the session catalog.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that dismisses incompatible-database recovery.</summary>
    public IRelayCommand CancelDatabaseRecoveryCommand { get; }

    /// <summary>Gets the command that permanently replaces incompatible recorded-session storage.</summary>
    public IAsyncRelayCommand ResetDatabaseCommand { get; }

    /// <summary>Gets the command that loads one selected session.</summary>
    public IAsyncRelayCommand<KustoRecordedSessionSummaryViewModel> SelectSessionCommand { get; }

    /// <summary>Gets the command that opens session deletion confirmation.</summary>
    public IRelayCommand OpenDeleteSessionCommand { get; }

    /// <summary>Gets the command that cancels session deletion.</summary>
    public IRelayCommand CancelDeleteSessionCommand { get; }

    /// <summary>Gets the command that permanently deletes the selected session.</summary>
    public IAsyncRelayCommand DeleteSessionCommand { get; }

    /// <summary>Gets the command that opens query deletion confirmation.</summary>
    public IRelayCommand OpenDeleteExecutionCommand { get; }

    /// <summary>Gets the command that cancels query deletion.</summary>
    public IRelayCommand CancelDeleteExecutionCommand { get; }

    /// <summary>Gets the command that permanently deletes the selected recorded query.</summary>
    public IAsyncRelayCommand DeleteExecutionCommand { get; }

    /// <summary>Gets the command that closes the recorded-query rename dialog.</summary>
    public IRelayCommand CloseRenameExecutionCommand { get; }

    /// <summary>Gets the command that saves the recorded-query display name.</summary>
    public IAsyncRelayCommand SaveRenameExecutionCommand { get; }

    /// <summary>Gets the command that dismisses chain-generation guidance.</summary>
    public IRelayCommand DismissChainGenerationDialogCommand { get; }

    /// <summary>Gets the command that prepares to append stronger evidence to the selected session.</summary>
    public IRelayCommand AddChainEvidenceCommand { get; }

    /// <summary>Gets the command that marks the selected historical result cell.</summary>
    public IAsyncRelayCommand MarkSelectedCellCommand { get; }

    /// <summary>Gets the command that marks every retained value in the selected column.</summary>
    public IAsyncRelayCommand MarkSelectedColumnCommand { get; }

    /// <summary>Gets the command that removes the selected historical cell mark.</summary>
    public IAsyncRelayCommand UnmarkSelectedCellCommand { get; }

    /// <summary>Gets the command that selects the historical context cell as chain start.</summary>
    public IAsyncRelayCommand SetChainStartCommand { get; }

    /// <summary>Gets the command that selects the historical context cell as chain end.</summary>
    public IAsyncRelayCommand SetChainEndCommand { get; }

    /// <summary>Gets the command that clears both selected chain endpoints.</summary>
    public IAsyncRelayCommand ClearChainEndpointsCommand { get; }

    /// <summary>
    /// Begins recording one manual query when a recording period is active.
    /// </summary>
    /// <param name="documentId">The source document identifier.</param>
    /// <param name="documentTitle">The source document title.</param>
    /// <param name="request">The query request.</param>
    /// <param name="databaseSchema">The active database schema.</param>
    /// <param name="startedAtUtc">The UTC execution start.</param>
    /// <param name="cancellationToken">Cancels persistence.</param>
    /// <returns>The recorded execution identifier, or <see langword="null"/> when not recording.</returns>
    public async Task<Guid?> BeginExecutionAsync(
        Guid documentId,
        string documentTitle,
        KustoQueryRequest request,
        KustoDatabaseSchema databaseSchema,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (activePeriod is null || store is null || interestExtractor is null || relationExtractor is null)
        {
            return null;
        }

        IReadOnlyList<KustoPredicateInterest> interests = interestExtractor.Extract(
            request.QueryText,
            databaseSchema,
            cancellationToken);
        KustoRecordedRelationDescriptor? relation = relationExtractor.Extract(
            request.QueryText,
            databaseSchema,
            cancellationToken);
        Guid executionId = await store.BeginExecutionAsync(
            new KustoRecordedExecutionStart(
                activePeriod.Id,
                documentId,
                documentTitle,
                request,
                startedAtUtc,
                interests,
                relation),
            cancellationToken);
        foreach (KustoPredicateInterest interest in interests)
        {
            activeInterests.Add(new KustoRecordedValueIdentity(interest.TypeName, interest.Value, false));
        }

        return executionId;
    }

    /// <summary>
    /// Finalizes one recorded execution and refreshes selected session state.
    /// </summary>
    /// <param name="executionId">The optional recorded execution identifier.</param>
    /// <param name="completion">The final execution state.</param>
    /// <returns>A task that completes after persistence.</returns>
    public async Task CompleteExecutionAsync(Guid? executionId, KustoRecordedExecutionCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (executionId is Guid recordedExecutionId && store is not null)
        {
            await store.CompleteExecutionAsync(recordedExecutionId, completion);
            await ReloadActiveValueInterestsAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Marks a live result cell pertinent in the active session.
    /// </summary>
    /// <param name="executionId">The recorded execution identifier.</param>
    /// <param name="cell">The selected live result cell.</param>
    /// <returns>A task that completes after persistence.</returns>
    public async Task MarkLiveCellAsync(Guid executionId, KustoResultCellViewModel cell)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(cell);
        if (activeSessionId is Guid sessionId && store is not null)
        {
            await store.AddMarkAsync(
                sessionId,
                KustoRecordedMarkKind.Cell,
                new KustoRecordedValueCoordinate(executionId, 0, cell.Row.RowIndex, cell.ColumnIndex),
                timeProvider.GetUtcNow());
            KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(cell.TypeName, cell.Value);
            activeInterests.Add(identity);
            AddManualInterestValue(identity);
            KustoRecordedValueColor color = KustoRecordedValueColorPalette.GetColor(identity);
            cell.SetRecordingAnnotation(
                true,
                true,
                false,
                false,
                true,
                color.AccentHex,
                color.HighlightHex);
            ActiveInterestsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Marks every live result value in one column pertinent in the active session.
    /// </summary>
    /// <param name="executionId">The recorded execution identifier.</param>
    /// <param name="rows">All source rows in the live result.</param>
    /// <param name="columnIndex">The result column to mark.</param>
    /// <returns>A task that completes after the marks are persisted atomically.</returns>
    public async Task MarkLiveColumnAsync(
        Guid executionId,
        IReadOnlyList<KustoResultRowViewModel> rows,
        int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        if (rows.Count == 0 || activeSessionId is not Guid sessionId || store is null)
        {
            return;
        }

        if (rows.Any(row => columnIndex >= row.Cells.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        KustoResultCellViewModel[] cells = rows
            .Select(row => row.Cells[columnIndex])
            .ToArray();
        KustoRecordedValueCoordinate[] coordinates = cells
            .Select(cell => new KustoRecordedValueCoordinate(
                executionId,
                0,
                cell.Row.RowIndex,
                columnIndex))
            .ToArray();
        await store.AddMarksAsync(
            sessionId,
            KustoRecordedMarkKind.Cell,
            coordinates,
            timeProvider.GetUtcNow());
        foreach (KustoResultCellViewModel cell in cells)
        {
            KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(
                cell.TypeName,
                cell.Value);
            activeInterests.Add(identity);
            AddManualInterestValue(identity);
            KustoRecordedValueColor color = KustoRecordedValueColorPalette.GetColor(identity);
            cell.SetRecordingAnnotation(
                true,
                true,
                false,
                false,
                true,
                color.AccentHex,
                color.HighlightHex);
        }

        ActiveInterestsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a pertinent mark from a live result cell.
    /// </summary>
    /// <param name="executionId">The recorded execution identifier.</param>
    /// <param name="cell">The selected live result cell.</param>
    /// <returns>A task that completes after persistence.</returns>
    public async Task UnmarkLiveCellAsync(Guid executionId, KustoResultCellViewModel cell)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(cell);
        KustoRecordedValueCoordinate coordinate = new(executionId, 0, cell.Row.RowIndex, cell.ColumnIndex);
        await RemoveLiveMarkAsync(KustoRecordedMarkKind.Cell, coordinate);
        KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(cell.TypeName, cell.Value);
        KustoRecordedValueColor color = KustoRecordedValueColorPalette.GetColor(identity);
        cell.SetRecordingAnnotation(
            IsInteresting(cell.TypeName, cell.Value),
            false,
            cell.IsChainStart,
            cell.IsChainEnd,
            IsManualInterestMatch(cell.Value),
            color.AccentHex,
            color.HighlightHex);
    }

    /// <summary>
    /// Gets whether a live value matches an active recording interest.
    /// </summary>
    /// <param name="typeName">The server-reported value type.</param>
    /// <param name="value">The typed result value.</param>
    /// <returns><see langword="true"/> when the value is interesting.</returns>
    public bool IsInteresting(string typeName, KustoResultValue value)
    {
        return IsRecording && activeInterests.Contains(KustoRecordedValueCanonicalizer.Create(typeName, value));
    }

    /// <summary>
    /// Gets whether a live value contains a manually marked recording interest.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <returns><see langword="true"/> when the value contains a manual interest.</returns>
    public bool IsManualInterestMatch(KustoResultValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsRecording
            && !value.IsNull
            && activeManualInterestValues.Any(markedValue => value.DisplayText.Contains(
                markedValue,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sets the historical result cell targeted by annotation commands.
    /// </summary>
    /// <param name="cell">The selected historical result cell.</param>
    public void SetResultContext(KustoResultCellViewModel cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        contextCell?.SetActionTarget(false);
        KustoRecordedResultTableViewModel? owningTable = SelectedExecution?.Tables.FirstOrDefault(
            table => table.Rows.Contains(cell.Row));
        if (SelectedExecution is not null && owningTable is not null)
        {
            SelectedExecution.SelectedTable = owningTable;
            OnPropertyChanged(nameof(SelectedTable));
            OnPropertyChanged(nameof(HasSelectedTable));
        }

        contextCell = cell;
        contextCell.SetActionTarget(true);
        NotifyContextCommandsChanged();
    }

    /// <summary>
    /// Sets a pertinent value occurrence as a chain endpoint.
    /// </summary>
    /// <param name="value">The pertinent value selected in the session rail.</param>
    /// <param name="role">The endpoint role.</param>
    /// <param name="cancellationToken">Cancels persistence.</param>
    /// <returns>A task that completes after the endpoint is stored.</returns>
    public async Task SetEndpointFromPertinentValueAsync(
        KustoRecordedPertinentValueViewModel value,
        KustoChainEndpointRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(role) || value.Coordinates.Count == 0)
        {
            return;
        }

        KustoRecordedValueCoordinate coordinate = role == KustoChainEndpointRole.Start
            ? value.Coordinates[0]
            : value.Coordinates[^1];
        await SetEndpointAsync(role, coordinate, cancellationToken);
    }

    /// <summary>
    /// Selects the query and available result cell represented by a timeline value.
    /// </summary>
    /// <param name="value">The timeline value to reveal.</param>
    public void SelectTimelineValue(KustoRecordedTimelineValueViewModel value)
    {
        ArgumentNullException.ThrowIfNull(value);
        KustoRecordedExecutionViewModel? execution = SelectedSession?.Executions.FirstOrDefault(
            candidate => candidate.Id == value.ExecutionId);
        if (execution is null)
        {
            return;
        }

        SelectedExecution = execution;
        if (value.QueryCoordinate is { } coordinate
            && execution.FindCell(
                coordinate.TableOrdinal,
                coordinate.RowOrdinal,
                coordinate.ColumnOrdinal) is { } cell)
        {
            SetResultContext(cell);
        }
    }

    /// <summary>
    /// Sets a timeline value as a chain endpoint.
    /// </summary>
    /// <param name="value">The timeline value.</param>
    /// <param name="role">The endpoint role.</param>
    /// <param name="cancellationToken">Cancels persistence.</param>
    /// <returns>A task that completes after the endpoint is stored.</returns>
    public Task SetEndpointFromTimelineValueAsync(
        KustoRecordedTimelineValueViewModel value,
        KustoChainEndpointRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SetEndpointFromPertinentValueAsync(value.PertinentValue, role, cancellationToken);
    }

    /// <summary>
    /// Creates a CSV export containing every unique pertinent value in the selected session.
    /// </summary>
    /// <returns>The generated CSV file.</returns>
    public KustoResultExportFile CreatePertinentValuesCsvExport()
    {
        if (SelectedSession is null)
        {
            throw new InvalidOperationException("Select a recorded session before exporting pertinent values.");
        }

        return KustoResultDataExporter.CreateValueCsvFile(
            $"{SelectedSession.Name}-pertinent-values",
            SelectedSession.PertinentValues.Select(value => value.ValueText));
    }

    /// <summary>
    /// Opens the rename dialog for one recorded query.
    /// </summary>
    /// <param name="execution">The recorded query to rename.</param>
    public void OpenRenameExecution(KustoRecordedExecutionViewModel execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        executionBeingRenamed = execution;
        RenameExecutionName = execution.QueryTitle;
        RenameExecutionErrorText = string.Empty;
        IsRenameExecutionOpen = true;
        OnPropertyChanged(nameof(CanSaveRenameExecution));
        SaveRenameExecutionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reports why a ready chain cannot be generated in the current application state.
    /// </summary>
    /// <param name="message">The actionable failure message.</param>
    public void ReportChainGenerationUnavailable(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        GeneratedQueryText = string.Empty;
        ShowChainGenerationDialog(message);
    }

    /// <summary>
    /// Finds and generates KQL for the selected session endpoints.
    /// </summary>
    /// <param name="databaseSchema">The current target database schema.</param>
    /// <param name="cancellationToken">Cancels graph traversal.</param>
    /// <returns>The validated generated query result.</returns>
    public async Task<KustoGeneratedChainQuery?> GenerateChainAsync(
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseSchema);
        if (SelectedSession is null || chainSearcher is null || relationPlanner is null || chainGenerator is null)
        {
            return null;
        }

        KustoChainEndpoint? start = SelectedSession.Session.Endpoints.FirstOrDefault(
            endpoint => endpoint.Role == KustoChainEndpointRole.Start);
        KustoChainEndpoint? destination = SelectedSession.Session.Endpoints.FirstOrDefault(
            endpoint => endpoint.Role == KustoChainEndpointRole.End);
        if (start is null || destination is null)
        {
            GeneratedQueryText = string.Empty;
            ShowChainGenerationDialog("Select both a chain start and chain end");
            return null;
        }

        KustoQueryChain? chain = await chainSearcher.FindAsync(
            SelectedSession.Id,
            start.Coordinate,
            destination.Coordinate,
            databaseSchema,
            cancellationToken);
        if (chain is null)
        {
            GeneratedQueryText = string.Empty;
            ShowChainGenerationDialog("No inferred pivot chain connects the selected values");
            return null;
        }

        KustoRelationalChainPlan? plan = relationPlanner.CreatePlan(SelectedSession.Session, chain);
        if (plan is null)
        {
            GeneratedQueryText = string.Empty;
            ShowChainGenerationDialog(
                "The inferred chain contains weak or unsupported pivots and cannot be generated safely");
            return null;
        }

        KustoGeneratedChainQuery generated = chainGenerator.Generate(plan, databaseSchema);
        GeneratedQueryText = generated.QueryText;
        if (generated.Succeeded)
        {
            IsChainGenerationDialogOpen = false;
        }
        else
        {
            ShowChainGenerationDialog(string.Join(Environment.NewLine, generated.Diagnostics));
        }

        return generated;
    }

    private static string FormatValue(KustoResultValue value)
    {
        if (value.IsNull)
        {
            return "(null)";
        }

        return value.DisplayText.Length == 0 ? "(empty)" : value.DisplayText;
    }

    private KustoChainEndpoint? GetEndpoint(KustoChainEndpointRole role)
    {
        return SelectedSession?.Session.Endpoints.FirstOrDefault(endpoint => endpoint.Role == role);
    }

    private EndpointDisplay GetEndpointDisplay(KustoChainEndpointRole role)
    {
        KustoChainEndpoint? endpoint = GetEndpoint(role);
        KustoRecordedExecutionViewModel? executionViewModel = endpoint is null
            ? null
            : SelectedSession?.Executions.FirstOrDefault(
                candidate => candidate.Id == endpoint.Coordinate.ExecutionId);
        KustoRecordedExecution? execution = executionViewModel?.Execution;
        KustoResultTable? table = execution?.Result?.Tables.ElementAtOrDefault(
            endpoint?.Coordinate.TableOrdinal ?? -1);
        KustoResultRow? row = table?.Rows.ElementAtOrDefault(endpoint?.Coordinate.RowOrdinal ?? -1);
        KustoResultColumn? column = table?.Columns.ElementAtOrDefault(
            endpoint?.Coordinate.ColumnOrdinal ?? -1);
        KustoResultValue? value = row?.ResultValues.ElementAtOrDefault(
            endpoint?.Coordinate.ColumnOrdinal ?? -1);
        return execution is null || column is null || value is null
            ? new EndpointDisplay("Not selected", "Choose a value from any recorded result")
            : new EndpointDisplay(
                FormatValue(value),
                $"{executionViewModel!.QueryTitle} · {column.Name} · row {endpoint!.Coordinate.RowOrdinal + 1:N0}");
    }

    private KustoRecordedValueCoordinate? TryGetContextCoordinate()
    {
        return SelectedExecution is null || SelectedTable is null || contextCell is null
            ? null
            : new KustoRecordedValueCoordinate(
                SelectedExecution.Id,
                SelectedTable.TableOrdinal,
                contextCell.Row.RowIndex,
                contextCell.ColumnIndex);
    }

    private KustoResultCellViewModel? FindResultCell(KustoRecordedValueCoordinate coordinate)
    {
        return SelectedSession?.Executions.FirstOrDefault(execution => execution.Id == coordinate.ExecutionId)?
            .FindCell(
                coordinate.TableOrdinal,
                coordinate.RowOrdinal,
                coordinate.ColumnOrdinal);
    }

    private async Task OpenRecordingAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        NewSessionName = $"Investigation {timeProvider.GetLocalNow():yyyy-MM-dd HHmm}";
        RecordingErrorText = string.Empty;
        IsAppendMode = false;
        IsRecordingDialogOpen = true;
    }

    private void CloseRecordingDialog()
    {
        IsRecordingDialogOpen = false;
        RecordingErrorText = string.Empty;
    }

    private bool CanAddChainEvidence()
    {
        return IsAvailable && !IsRecording && SelectedSessionSummary is not null;
    }

    private void OpenChainEvidenceRecording()
    {
        SelectedAppendSession = Sessions.FirstOrDefault(
            session => session.Id == SelectedSessionSummary?.Id);
        if (SelectedAppendSession is null)
        {
            return;
        }

        RecordingErrorText = string.Empty;
        IsAppendMode = true;
        IsChainGenerationDialogOpen = false;
        IsRecordingDialogOpen = true;
    }

    private void ShowChainGenerationDialog(string message)
    {
        ChainGenerationDialogMessage = message;
        ChainGenerationGuidanceText =
            "Append direct queries from one cluster and database that expose the shared values between each step. "
            + "Mark those shared result values as interesting, then select the start and end again.";
        IsChainGenerationDialogOpen = true;
    }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return;
        }

        try
        {
            activePeriod = IsAppendMode
                ? await store.AppendSessionAsync(
                    SelectedAppendSession?.Id
                        ?? throw new InvalidOperationException("Select a session to append."),
                    timeProvider.GetUtcNow(),
                    cancellationToken)
                : await store.CreateSessionAsync(
                    NewSessionName,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            activeSessionId = activePeriod.SessionId;
            activeSessionName = IsAppendMode
                ? SelectedAppendSession?.Name ?? string.Empty
                : NewSessionName.Trim();
            activeInterests.Clear();
            activeManualInterestValues.Clear();
            if (IsAppendMode)
            {
                await ReloadActiveInterestsAsync(cancellationToken);
            }

            IsRecordingDialogOpen = false;
            RecordingErrorText = string.Empty;
            ActiveInterestsChanged?.Invoke(this, EventArgs.Empty);
            NotifyRecordingStateChanged();
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordingErrorText = exception.Message;
        }
    }

    private async Task StopRecordingAsync(CancellationToken cancellationToken)
    {
        if (activePeriod is not null && store is not null)
        {
            await store.StopRecordingAsync(
                activePeriod.Id,
                timeProvider.GetUtcNow(),
                cancellationToken);
            activePeriod = null;
            activeSessionId = null;
            activeSessionName = string.Empty;
            activeInterests.Clear();
            activeManualInterestValues.Clear();
            ActiveInterestsChanged?.Invoke(this, EventArgs.Empty);
            NotifyRecordingStateChanged();
            await RefreshAsync(cancellationToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            Guid? selectedId = activeSessionId ?? SelectedSessionSummary?.Id;
            IReadOnlyList<KustoRecordedSessionSummary> summaries = await store
                .GetSessionsAsync(cancellationToken);
            IsDatabaseRecoveryOpen = false;
            DatabaseRecoveryMessage = string.Empty;
            DatabaseRecoveryErrorText = string.Empty;
            Sessions.Clear();
            foreach (KustoRecordedSessionSummary summary in summaries)
            {
                Sessions.Add(new KustoRecordedSessionSummaryViewModel(summary));
            }

            OnPropertyChanged(nameof(HasSessions));
            SelectedAppendSession = Sessions.FirstOrDefault(session => session.Id == activeSessionId)
                ?? Sessions.FirstOrDefault();
            SelectedSessionSummary = Sessions.FirstOrDefault(session => session.Id == selectedId)
                ?? Sessions.FirstOrDefault();
            if (SelectedSessionSummary is not null)
            {
                await SelectSessionAsync(SelectedSessionSummary, cancellationToken);
            }
            else
            {
                SelectedSession = null;
            }
        }
        catch (KustoRecordedSessionDatabaseVersionException exception)
        {
            Sessions.Clear();
            SelectedAppendSession = null;
            SelectedSessionSummary = null;
            SelectedSession = null;
            OnPropertyChanged(nameof(HasSessions));
            DatabaseRecoveryMessage =
                $"This database uses pre-release schema version {exception.DatabaseVersion}; "
                + $"this build supports version {exception.SupportedVersion}. Remove it to create empty storage.";
            DatabaseRecoveryErrorText = string.Empty;
            IsDatabaseRecoveryOpen = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CancelDatabaseRecovery()
    {
        IsDatabaseRecoveryOpen = false;
        DatabaseRecoveryErrorText = string.Empty;
    }

    private async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (store is not IKustoRecordedSessionStoreMaintenance maintenance)
        {
            return;
        }

        IsLoading = true;
        DatabaseRecoveryErrorText = string.Empty;
        try
        {
            await maintenance.ResetDatabaseAsync(cancellationToken);
            IsDatabaseRecoveryOpen = false;
            DatabaseRecoveryMessage = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DatabaseRecoveryErrorText = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }

        if (!IsDatabaseRecoveryOpen)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    private async Task SelectSessionAsync(
        KustoRecordedSessionSummaryViewModel? summary,
        CancellationToken cancellationToken)
    {
        if (summary is null || store is null)
        {
            return;
        }

        Guid? selectedExecutionId = SelectedSession?.Id == summary.Id
            ? SelectedSession.SelectedExecution?.Id
            : null;
        int? selectedTableOrdinal = SelectedSession?.Id == summary.Id
            ? SelectedSession.SelectedExecution?.SelectedTable?.TableOrdinal
            : null;
        KustoRecordedValueCoordinate? selectedCoordinate = SelectedSession?.Id == summary.Id
            ? TryGetContextCoordinate()
            : null;
        bool changedSession = SelectedSession?.Id != summary.Id;
        contextCell?.SetActionTarget(false);
        SelectedSessionSummary = summary;
        KustoRecordedSession? session = await store.GetSessionAsync(summary.Id, cancellationToken);
        SelectedSession = session is null ? null : new KustoRecordedSessionViewModel(session);
        if (SelectedSession?.Executions.FirstOrDefault(execution => execution.Id == selectedExecutionId)
            is { } selectedExecution)
        {
            SelectedSession.SelectedExecution = selectedExecution;
            ObserveSelectedExecution();
            selectedExecution.SelectedTable = selectedExecution.Tables.FirstOrDefault(
                table => table.TableOrdinal == selectedTableOrdinal)
                ?? selectedExecution.Tables.FirstOrDefault();
        }

        contextCell = selectedCoordinate is null ? null : FindResultCell(selectedCoordinate);
        contextCell?.SetActionTarget(true);
        OnPropertyChanged(nameof(SelectedExecution));
        OnPropertyChanged(nameof(HasSelectedExecution));
        OnPropertyChanged(nameof(SelectedTable));
        OnPropertyChanged(nameof(HasSelectedTable));
        NotifyContextCommandsChanged();
        if (changedSession)
        {
            GeneratedQueryText = string.Empty;
        }
    }

    private async Task DeleteSelectedSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedSessionSummary is not null && store is not null)
        {
            await store.DeleteSessionAsync(SelectedSessionSummary.Id, cancellationToken);
            IsDeleteConfirmationOpen = false;
            SelectedSession = null;
            SelectedSessionSummary = null;
            await RefreshAsync(cancellationToken);
        }
    }

    private void OpenDeleteSession()
    {
        IsDeleteExecutionConfirmationOpen = false;
        IsDeleteConfirmationOpen = SelectedSessionSummary is not null;
    }

    private void OpenDeleteExecution()
    {
        IsDeleteConfirmationOpen = false;
        IsDeleteExecutionConfirmationOpen = SelectedExecution is not null;
    }

    private async Task DeleteSelectedExecutionAsync(CancellationToken cancellationToken)
    {
        if (SelectedExecution is not { } execution || store is null)
        {
            return;
        }

        await store.DeleteExecutionAsync(execution.Id, cancellationToken);
        IsDeleteExecutionConfirmationOpen = false;
        GeneratedQueryText = string.Empty;
        await RefreshAsync(cancellationToken);
        await ReloadActiveInterestsIfSelectedAsync(cancellationToken);
    }

    private void CloseRenameExecution()
    {
        IsRenameExecutionOpen = false;
        executionBeingRenamed = null;
        RenameExecutionName = string.Empty;
        RenameExecutionErrorText = string.Empty;
        OnPropertyChanged(nameof(CanSaveRenameExecution));
        SaveRenameExecutionCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveRenameExecutionAsync(CancellationToken cancellationToken)
    {
        if (executionBeingRenamed is not { } execution
            || store is null
            || !CanSaveRenameExecution)
        {
            return;
        }

        try
        {
            await store.RenameExecutionAsync(
                execution.Id,
                RenameExecutionName,
                cancellationToken);
            CloseRenameExecution();
            await ReloadSelectedSessionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RenameExecutionErrorText = exception.Message;
        }
    }

    private async Task MarkSelectedCellAsync(CancellationToken cancellationToken)
    {
        KustoRecordedValueCoordinate coordinate = GetContextCoordinate();
        await AddHistoricalMarkAsync(KustoRecordedMarkKind.Cell, coordinate, cancellationToken);
    }

    private async Task MarkSelectedColumnAsync(CancellationToken cancellationToken)
    {
        KustoRecordedValueCoordinate context = GetContextCoordinate();
        KustoRecordedExecution? execution = SelectedSession?.Session.Executions.FirstOrDefault(
            candidate => candidate.Id == context.ExecutionId);
        KustoResultTable? table = execution?.Result?.Tables.ElementAtOrDefault(context.TableOrdinal);
        if (table is null || context.ColumnOrdinal >= table.Columns.Count)
        {
            return;
        }

        KustoRecordedValueCoordinate[] coordinates = Enumerable.Range(0, table.Rows.Count)
            .Select(rowOrdinal => new KustoRecordedValueCoordinate(
                context.ExecutionId,
                context.TableOrdinal,
                rowOrdinal,
                context.ColumnOrdinal))
            .ToArray();
        await AddHistoricalMarksAsync(KustoRecordedMarkKind.Cell, coordinates, cancellationToken);
    }

    private async Task AddHistoricalMarkAsync(
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        await AddHistoricalMarksAsync(kind, [coordinate], cancellationToken);
    }

    private async Task AddHistoricalMarksAsync(
        KustoRecordedMarkKind kind,
        IReadOnlyList<KustoRecordedValueCoordinate> coordinates,
        CancellationToken cancellationToken)
    {
        if (SelectedSession is not null && store is not null)
        {
            await store.AddMarksAsync(
                SelectedSession.Id,
                kind,
                coordinates,
                timeProvider.GetUtcNow(),
                cancellationToken);
            await ReloadSelectedSessionAsync(cancellationToken);
            await ReloadActiveInterestsIfSelectedAsync(cancellationToken);
        }
    }

    private async Task UnmarkSelectedCellAsync(CancellationToken cancellationToken)
    {
        if (SelectedSession is null || store is null)
        {
            return;
        }

        KustoRecordedValueCoordinate coordinate = GetContextCoordinate();
        KustoRecordedMark? mark = SelectedSession.Session.Marks.FirstOrDefault(candidate =>
            candidate.Kind == KustoRecordedMarkKind.Cell
            && candidate.Coordinate.ExecutionId == coordinate.ExecutionId
            && candidate.Coordinate.TableOrdinal == coordinate.TableOrdinal
            && candidate.Coordinate.RowOrdinal == coordinate.RowOrdinal
            && candidate.Coordinate.ColumnOrdinal == coordinate.ColumnOrdinal);
        if (mark is not null)
        {
            await store.RemoveMarkAsync(mark.Id, cancellationToken);
            await ReloadSelectedSessionAsync(cancellationToken);
            await ReloadActiveInterestsIfSelectedAsync(cancellationToken);
        }
    }

    private async Task SetEndpointAsync(
        KustoChainEndpointRole role,
        CancellationToken cancellationToken)
    {
        await SetEndpointAsync(role, GetContextCoordinate(), cancellationToken);
    }

    private async Task SetEndpointAsync(
        KustoChainEndpointRole role,
        KustoRecordedValueCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        if (SelectedSession is null || store is null)
        {
            return;
        }

        await store.SetEndpointAsync(SelectedSession.Id, role, coordinate, cancellationToken);
        GeneratedQueryText = string.Empty;
        await ReloadSelectedSessionAsync(cancellationToken);
    }

    private async Task ClearEndpointsAsync(CancellationToken cancellationToken)
    {
        if (SelectedSession is not null && store is not null)
        {
            await store.ClearEndpointAsync(
                SelectedSession.Id,
                KustoChainEndpointRole.Start,
                cancellationToken);
            await store.ClearEndpointAsync(
                SelectedSession.Id,
                KustoChainEndpointRole.End,
                cancellationToken);
            GeneratedQueryText = string.Empty;
            await ReloadSelectedSessionAsync(cancellationToken);
        }
    }

    private KustoRecordedValueCoordinate GetContextCoordinate()
    {
        return TryGetContextCoordinate()
            ?? throw new InvalidOperationException("Select a recorded result value.");
    }

    private async Task ReloadSelectedSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedSessionSummary is not null)
        {
            await SelectSessionAsync(SelectedSessionSummary, cancellationToken);
        }
    }

    private async Task ReloadActiveInterestsIfSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedSession?.Id == activeSessionId)
        {
            await ReloadActiveInterestsAsync(cancellationToken);
        }
    }

    private async Task RemoveLiveMarkAsync(
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate)
    {
        if (activeSessionId is not Guid sessionId || store is null)
        {
            return;
        }

        KustoRecordedSession? session = await store.GetSessionAsync(sessionId);
        KustoRecordedMark? mark = session?.Marks.FirstOrDefault(candidate =>
            candidate.Kind == kind
            && candidate.Coordinate.ExecutionId == coordinate.ExecutionId
            && candidate.Coordinate.TableOrdinal == coordinate.TableOrdinal
            && candidate.Coordinate.RowOrdinal == coordinate.RowOrdinal
            && candidate.Coordinate.ColumnOrdinal == coordinate.ColumnOrdinal);
        if (mark is not null)
        {
            await store.RemoveMarkAsync(mark.Id);
            await ReloadActiveInterestsAsync(CancellationToken.None);
        }
    }

    private async Task ReloadActiveInterestsAsync(CancellationToken cancellationToken)
    {
        if (activeSessionId is Guid sessionId && store is not null)
        {
            activeInterests.Clear();
            activeManualInterestValues.Clear();
            KustoRecordedSession? session = await store.GetSessionAsync(sessionId, cancellationToken);
            foreach (KustoRecordedInterest interest in session?.Interests.Where(interest => !interest.IsSuppressed)
                ?? [])
            {
                activeInterests.Add(interest.Identity);
                if (interest.Source is KustoRecordedInterestSource.ManualCell
                    or KustoRecordedInterestSource.ConfirmedPredicate)
                {
                    AddManualInterestValue(interest.Identity);
                }
            }

            ActiveInterestsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ReloadActiveValueInterestsAsync(CancellationToken cancellationToken)
    {
        if (activeSessionId is not Guid sessionId || store is null)
        {
            return;
        }

        activeInterests.Clear();
        activeManualInterestValues.Clear();
        IReadOnlyList<KustoRecordedInterest> interests = await store.GetInterestsAsync(
            sessionId,
            cancellationToken);
        foreach (KustoRecordedInterest interest in interests.Where(interest => !interest.IsSuppressed))
        {
            activeInterests.Add(interest.Identity);
            if (interest.Source is KustoRecordedInterestSource.ManualCell
                or KustoRecordedInterestSource.ConfirmedPredicate)
            {
                AddManualInterestValue(interest.Identity);
            }
        }

        ActiveInterestsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddManualInterestValue(KustoRecordedValueIdentity identity)
    {
        if (!identity.IsNull && identity.CanonicalValue.Length > 0)
        {
            activeManualInterestValues.Add(identity.CanonicalValue);
        }
    }

    private bool CanAnnotateSelectedCell()
    {
        return store is not null
            && SelectedTable is not null
            && contextCell is not null;
    }

    private void ClearResultContext()
    {
        contextCell?.SetActionTarget(false);
        contextCell = null;
        NotifyContextCommandsChanged();
    }

    private void ObserveSelectedExecution()
    {
        KustoRecordedExecutionViewModel? selectedExecution = SelectedExecution;
        if (ReferenceEquals(observedExecution, selectedExecution))
        {
            return;
        }

        if (observedExecution is not null)
        {
            observedExecution.PageChanged -= OnSelectedExecutionPageChanged;
        }

        observedExecution = selectedExecution;
        if (observedExecution is not null)
        {
            observedExecution.PageChanged += OnSelectedExecutionPageChanged;
        }
    }

    private void OnSelectedExecutionPageChanged(object? sender, EventArgs eventArguments)
    {
        ClearResultContext();
        OnPropertyChanged(nameof(SelectedTable));
        OnPropertyChanged(nameof(HasSelectedTable));
    }

    private void NotifyStartStateChanged()
    {
        OnPropertyChanged(nameof(CanStartRecording));
        StartRecordingCommand.NotifyCanExecuteChanged();
    }

    private void NotifyRecordingStateChanged()
    {
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(ActiveSessionName));
        OnPropertyChanged(nameof(RecordingAutomationText));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OpenRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        OpenDeleteSessionCommand.NotifyCanExecuteChanged();
        DeleteSessionCommand.NotifyCanExecuteChanged();
        AddChainEvidenceCommand.NotifyCanExecuteChanged();
        NotifyStartStateChanged();
    }

    private void NotifyContextCommandsChanged()
    {
        OnPropertyChanged(nameof(HasSelectedResultValue));
        OnPropertyChanged(nameof(SelectedResultValueText));
        OnPropertyChanged(nameof(SelectedResultValueLocationText));
        OnPropertyChanged(nameof(SelectedResultValueIsMarked));
        MarkSelectedCellCommand.NotifyCanExecuteChanged();
        MarkSelectedColumnCommand.NotifyCanExecuteChanged();
        UnmarkSelectedCellCommand.NotifyCanExecuteChanged();
        SetChainStartCommand.NotifyCanExecuteChanged();
        SetChainEndCommand.NotifyCanExecuteChanged();
    }

    private void NotifyExecutionDeleteStateChanged()
    {
        OnPropertyChanged(nameof(CanDeleteSelectedExecution));
        OpenDeleteExecutionCommand.NotifyCanExecuteChanged();
        DeleteExecutionCommand.NotifyCanExecuteChanged();
    }

    private void NotifyChainStateChanged()
    {
        OnPropertyChanged(nameof(HasChainStart));
        OnPropertyChanged(nameof(HasChainEnd));
        OnPropertyChanged(nameof(CanGenerateChain));
        OnPropertyChanged(nameof(ChainStartValueText));
        OnPropertyChanged(nameof(ChainStartLocationText));
        OnPropertyChanged(nameof(ChainEndValueText));
        OnPropertyChanged(nameof(ChainEndLocationText));
        ClearChainEndpointsCommand.NotifyCanExecuteChanged();
    }

    private readonly record struct EndpointDisplay(string ValueText, string LocationText);
}
