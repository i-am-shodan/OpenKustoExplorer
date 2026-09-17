using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Application.Updates;
using OpenKustoExplorer.Desktop.Appearance;
using OpenKustoExplorer.Desktop.Controls;
using OpenKustoExplorer.Desktop.Editor;
using OpenKustoExplorer.Desktop.Graphs;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Hosts the primary Open Kusto Explorer desktop workbench.
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private const double CompactLayoutBreakpoint = 1080;
    private const int DashboardGridColumnCount = 48;
    private const int DashboardGridMaximumRowCount = 80;
    private const int DashboardMinimumColumnSpan = 8;
    private const int DashboardMinimumRowSpan = 6;
    private const double DocumentTabDragThreshold = 6;
    private const double ResultWheelScrollDistance = 90;
    private static readonly TimeSpan AutomationTickInterval = TimeSpan.FromSeconds(5);

    private static readonly (string ResourceKey, string LightColor, string DarkColor)[] HighContrastBrushSpecifications =
    [
        ("CanvasBrush", "#FFFFFF", "#000000"),
        ("SurfaceBrush", "#FFFFFF", "#000000"),
        ("RaisedSurfaceBrush", "#F2F2F2", "#090909"),
        ("SubtleSurfaceBrush", "#E6E6E6", "#161616"),
        ("ChromeSurfaceBrush", "#FFFFFF", "#000000"),
        ("ToolbarSurfaceBrush", "#F2F2F2", "#090909"),
        ("TabBarSurfaceBrush", "#E6E6E6", "#161616"),
        ("DecorativeDividerBrush", "#000000", "#FFFFFF"),
        ("ControlBoundaryBrush", "#000000", "#FFFFFF"),
        ("TextPrimaryBrush", "#000000", "#FFFFFF"),
        ("TextSecondaryBrush", "#000000", "#FFFFFF"),
        ("TextTertiaryBrush", "#1A1A1A", "#F2F2F2"),
        ("AccentBrush", "#004FCC", "#00E5FF"),
        ("AccentHoverBrush", "#003A99", "#8CEEFF"),
        ("AccentSubtleBrush", "#DDE8FF", "#003A42"),
        ("SelectionBrush", "#B8D2FF", "#005A66"),
        ("FocusBrush", "#004FCC", "#FFFF00"),
        ("SuccessBrush", "#006B3C", "#54FF9F"),
        ("WarningBrush", "#6D4600", "#FFFF00"),
        ("ErrorBrush", "#A40000", "#FF8080"),
        ("QueryErrorHighlightBrush", "#33A40000", "#55FF8080"),
        ("InfoBrush", "#004FCC", "#00E5FF"),
        ("OnAccentBrush", "#FFFFFF", "#000000"),
        ("SyntaxCommentBrush", "#333333", "#E6E6E6"),
        ("SyntaxKeywordBrush", "#004FCC", "#8CEEFF"),
        ("SyntaxEntityBrush", "#6D4600", "#FFFF00"),
        ("SyntaxNameBrush", "#004FCC", "#00E5FF"),
        ("SyntaxFunctionBrush", "#A40000", "#FF8080"),
        ("SyntaxLiteralBrush", "#006B3C", "#54FF9F"),
        ("SyntaxVariableBrush", "#6D4600", "#FFFF00"),
    ];

    private readonly CancellationTokenSource automationCancellationSource = new();
    private readonly ObservableCollection<SignedInUserAvatarViewModel> signedInUsers = [];
    private readonly CancellationTokenSource updateCancellationSource = new();
    private Button? appearanceButton;
    private AppearanceSettings? appearanceSettings;
    private KustoApplicationUpdate? availableUpdate;
    private Button? automationButton;
    private AutomationNotificationDispatcher? automationNotificationDispatcher;
    private Grid? automationVisualizationSurface;
    private DispatcherTimer? automationTimer;
    private ColorPicker? conditionalColorPicker;
    private Button? connectionsButton;
    private Button? copilotExpandButton;
    private Border? copilotPanel;
    private TextBox? copilotPrompt;
    private TextBlock? densityDescription;
    private ToggleSwitch? densityToggle;
    private Slider? textZoomSlider;
    private TextBlock? textZoomValue;
    private Button? dashboardButton;
    private ItemsControl? dashboardCanvas;
    private Control? dashboardInteractionControl;
    private Point dashboardInteractionOrigin;
    private int dashboardInteractionStartColumn;
    private int dashboardInteractionStartColumnSpan;
    private int dashboardInteractionStartRow;
    private int dashboardInteractionStartRowSpan;
    private KustoDashboardWidgetViewModel? dashboardInteractionWidget;
    private bool isDashboardResize;
    private bool documentTabDropAfter;
    private TabStripItem? documentTabDropTarget;
    private Point documentTabDragOrigin;
    private IPointer? documentTabPointer;
    private TabStrip? documentTabs;
    private KustoDocumentViewModel? draggedDocument;
    private Control? draggedDocumentTab;
    private int focusRegionIndex = -1;
    private Button? graphButton;
    private KustoGraphControl? graphCanvas;
    private TextBox? graphCypherEditor;
    private TextBox? graphSearch;
    private Grid? graphViewport;
    private Border? graphViewportToolbar;
    private Border? highContrastNotice;
    private KustoEditorController? editorController;
    private RadioButton? darkThemeOption;
    private RadioButton? settingsDarkThemeOption;
    private ComboBox? settingsAIProvider;
    private ComboBox? settingsAzureOpenAIAuthentication;
    private TextBox? settingsAzureOpenAIApiKeyEnvironmentVariable;
    private Grid? settingsAzureOpenAIApiKeyRow;
    private TextBox? settingsAzureOpenAIDeployment;
    private TextBox? settingsAzureOpenAIEndpoint;
    private StackPanel? settingsAzureOpenAIOptions;
    private ToggleSwitch? settingsCopilotAzureMcpToggle;
    private Grid? settingsCopilotAzureMcpRow;
    private ComboBox? settingsCopilotDefaultModel;
    private Grid? settingsCopilotDefaultModelRow;
    private ToggleSwitch? settingsCopilotMicrosoftLearnMcpToggle;
    private Grid? settingsCopilotMicrosoftLearnMcpRow;
    private ToggleSwitch? settingsCopilotShareResultDataToggle;
    private ToggleSwitch? settingsCopilotShareSchemaToggle;
    private ToggleSwitch? settingsCopilotShareTabContentToggle;
    private ToggleSwitch? settingsDensityToggle;
    private Border? settingsDialog;
    private ToggleSwitch? settingsKqlHoverHelpToggle;
    private RadioButton? settingsLightThemeOption;
    private TextBox? settingsOpenAIApiKeyEnvironmentVariable;
    private TextBox? settingsOpenAIEndpoint;
    private TextBox? settingsOpenAIModel;
    private StackPanel? settingsOpenAIOptions;
    private Slider? settingsResultTextZoomSlider;
    private TextBlock? settingsResultTextZoomValue;
    private Button? settingsButton;
    private RadioButton? settingsSystemThemeOption;
    private Slider? settingsTextZoomSlider;
    private TextBlock? settingsTextZoomValue;
    private bool suppressSettingsAIProviderChange;
    private bool suppressSettingsAzureOpenAIAuthenticationChange;
    private bool suppressSettingsCopilotDefaultModelChange;
    private bool isDisposed;
    private bool isDocumentTabDragging;
    private CancellationTokenSource? identityRefreshCancellationSource;
    private IKustoIdentityService? identityService;
    private bool isNarrowLayout;
    private RadioButton? lightThemeOption;
    private SplitView? navigationSplitView;
    private Button? newQueryButton;
    private SplitView? pertinentValuesSplitView;
    private TextEditor? queryEditor;
    private Button? recordButton;
    private TextBox? recordingNameTextBox;
    private Border? resultHorizontalOverflowHint;
    private ScrollBar? resultHorizontalScrollBar;
    private ScrollViewer? resultRowsScrollViewer;
    private ScrollViewer? resultScrollViewer;
    private TabControl? resultTabs;
    private Slider? resultTextZoomSlider;
    private TextBlock? resultTextZoomValue;
    private ScrollBar? resultVerticalScrollBar;
    private Grid? resultView;
    private ListBox? resultsList;
    private TextBox? schemaSearch;
    private Button? sessionsButton;
    private ItemsControl? signedInUsersList;
    private RadioButton? systemThemeOption;
    private TextBox? tabSearchBox;
    private Button? updateButton;
    private int updateCheckStarted;
    private IKustoApplicationUpdateService? updateService;
    private Grid? visualizationSurface;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class for compiled XAML and design tools.
    /// </summary>
    public MainWindow()
    {
        using (KustoPerformanceTrace.Measure("startup.main_window.xaml.load"))
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="viewModel">The workbench presentation model.</param>
    /// <param name="appearanceSettings">The desktop appearance settings.</param>
    /// <param name="identityService">The process-lifetime signed-in account service.</param>
    /// <param name="updateService">The official application release checker.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="appearanceSettings"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="identityService"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updateService"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The compiled editor control cannot be located.</exception>
    internal MainWindow(
        MainWindowViewModel viewModel,
        AppearanceSettings appearanceSettings,
        IKustoIdentityService identityService,
        IKustoApplicationUpdateService updateService)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(appearanceSettings);
        ArgumentNullException.ThrowIfNull(identityService);
        ArgumentNullException.ThrowIfNull(updateService);

        DataContext = viewModel;
        InitializeInteractiveControls();
        automationNotificationDispatcher = new AutomationNotificationDispatcher(
            new DesktopNotificationService(this));
        viewModel.AutomationNotificationRequested += OnAutomationNotificationRequested;
        this.appearanceSettings = appearanceSettings;
        viewModel.ApplyCopilotDefaults(CreateCopilotDefaults(appearanceSettings));
        this.identityService = identityService;
        this.updateService = updateService;
        signedInUsersList!.ItemsSource = signedInUsers;
        identityService.SignedInUsersChanged += OnSignedInUsersChanged;
        QueueSignedInUsersRefresh();
        appearanceSettings.PropertyChanged += OnAppearancePropertyChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        UpdateAppearanceClasses();
        UpdateResponsiveLayout(Width);

        editorController = new KustoEditorController(queryEditor!, viewModel, appearanceSettings);
        automationTimer = new DispatcherTimer(
            AutomationTickInterval,
            DispatcherPriority.Background,
            OnAutomationTimerTick);
        automationTimer.Start();
    }

    /// <summary>
    /// Releases editor event subscriptions owned by this window.
    /// </summary>
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            automationTimer?.Stop();
            automationTimer = null;
            automationCancellationSource.Cancel();
            updateCancellationSource.Cancel();
            identityRefreshCancellationSource?.Cancel();
            identityRefreshCancellationSource?.Dispose();
            identityRefreshCancellationSource = null;
            editorController?.Dispose();
            ActualThemeVariantChanged -= OnActualThemeVariantChanged;

            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.AutomationNotificationRequested -= OnAutomationNotificationRequested;
            }

            automationNotificationDispatcher?.Dispose();
            automationNotificationDispatcher = null;

            if (appearanceSettings is not null)
            {
                appearanceSettings.PropertyChanged -= OnAppearancePropertyChanged;
            }

            if (identityService is not null)
            {
                identityService.SignedInUsersChanged -= OnSignedInUsersChanged;
                identityService = null;
            }

            updateService = null;

            foreach (SignedInUserAvatarViewModel user in signedInUsers)
            {
                user.Dispose();
            }

            signedInUsers.Clear();

            automationCancellationSource.Dispose();
            updateCancellationSource.Dispose();
        }
    }

    /// <summary>
    /// Applies one bounded keyboard move or resize step to a dashboard widget.
    /// </summary>
    /// <param name="widget">The widget to adjust.</param>
    /// <param name="key">The requested arrow direction.</param>
    /// <param name="isResize">Whether to resize instead of move.</param>
    /// <returns><see langword="true"/> when the key represents a layout action.</returns>
    internal static bool AdjustDashboardWidgetLayout(
        KustoDashboardWidgetViewModel widget,
        Key key,
        bool isResize)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return false;
        }

        int column = widget.Column;
        int row = widget.Row;
        int columnSpan = widget.ColumnSpan;
        int rowSpan = widget.RowSpan;
        if (isResize)
        {
            columnSpan = key switch
            {
                Key.Left => Math.Max(DashboardMinimumColumnSpan, columnSpan - 1),
                Key.Right => Math.Min(DashboardGridColumnCount - column, columnSpan + 1),
                _ => columnSpan,
            };
            rowSpan = key switch
            {
                Key.Up => Math.Max(DashboardMinimumRowSpan, rowSpan - 1),
                Key.Down => Math.Min(DashboardGridMaximumRowCount - row, rowSpan + 1),
                _ => rowSpan,
            };
        }
        else
        {
            column = key switch
            {
                Key.Left => Math.Max(0, column - 1),
                Key.Right => Math.Min(DashboardGridColumnCount - columnSpan, column + 1),
                _ => column,
            };
            row = key switch
            {
                Key.Up => Math.Max(0, row - 1),
                Key.Down => Math.Min(DashboardGridMaximumRowCount - rowSpan, row + 1),
                _ => row,
            };
        }

        widget.PreviewLayout(column, row, columnSpan, rowSpan);
        widget.CommitLayout();
        return true;
    }

    /// <summary>
    /// Calculates the bounded row offset produced by vertical wheel input.
    /// </summary>
    /// <param name="currentOffset">The current vertical row offset.</param>
    /// <param name="maximumOffset">The maximum vertical row offset.</param>
    /// <param name="wheelDelta">The vertical wheel delta, where positive values scroll up.</param>
    /// <returns>The next bounded vertical row offset.</returns>
    internal static double CalculateResultWheelOffset(
        double currentOffset,
        double maximumOffset,
        double wheelDelta)
    {
        return Math.Clamp(
            currentOffset - (wheelDelta * ResultWheelScrollDistance),
            0,
            Math.Max(0, maximumOffset));
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        KustoPerformanceTrace.RecordStartupMilestone("startup.main_window.opened");

        if (DataContext is MainWindowViewModel viewModel
            && !automationCancellationSource.IsCancellationRequested)
        {
            _ = RunAutomationTickAsync(viewModel);
        }

        _ = CheckForUpdateAsync();
    }

    private static void CancelActiveExecution(MainWindowViewModel viewModel)
    {
        if (viewModel.Graph.IsFindingRoutes)
        {
            viewModel.Graph.CancelRouteCommand.Execute(null);
        }
        else if (viewModel.Graph.IsCypherRunning)
        {
            viewModel.Graph.CancelCypherCommand.Execute(null);
        }
        else
        {
            viewModel.CancelQueryCommand.Execute(null);
        }
    }

    private static KustoCopilotDefaults CreateCopilotDefaults(AppearanceSettings settings)
    {
        return new KustoCopilotDefaults(
            settings.CopilotShareTabContentByDefault,
            settings.CopilotShareSchemaByDefault,
            settings.CopilotShareResultDataByDefault,
            settings.CopilotEnableMicrosoftLearnMcpByDefault,
            settings.CopilotEnableAzureMcpByDefault,
            settings.CopilotDefaultModel);
    }

    private static bool IsCopilotDefaultProperty(string? propertyName)
    {
        return propertyName is nameof(AppearanceSettings.CopilotShareTabContentByDefault)
            or nameof(AppearanceSettings.CopilotDefaultModel)
            or nameof(AppearanceSettings.CopilotShareSchemaByDefault)
            or nameof(AppearanceSettings.CopilotShareResultDataByDefault)
            or nameof(AppearanceSettings.CopilotEnableMicrosoftLearnMcpByDefault)
            or nameof(AppearanceSettings.CopilotEnableAzureMcpByDefault);
    }

    private static bool IsAIProviderProperty(string? propertyName)
    {
        return propertyName is nameof(AppearanceSettings.ProviderKind)
            or nameof(AppearanceSettings.AzureOpenAIEndpoint)
            or nameof(AppearanceSettings.AzureOpenAIDeployment)
            or nameof(AppearanceSettings.AzureOpenAIAuthenticationKind)
            or nameof(AppearanceSettings.AzureOpenAIApiKeyEnvironmentVariable)
            or nameof(AppearanceSettings.OpenAIEndpoint)
            or nameof(AppearanceSettings.OpenAIModel)
            or nameof(AppearanceSettings.OpenAIApiKeyEnvironmentVariable);
    }

    private static int GetDocumentShortcutIndex(Key key)
    {
        return key switch
        {
            Key.D1 => 0,
            Key.D2 => 1,
            Key.D3 => 2,
            Key.D4 => 3,
            Key.D5 => 4,
            Key.D6 => 5,
            Key.D7 => 6,
            _ => -1,
        };
    }

    private static void OnDashboardWidgetKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        bool isMove = eventArguments.KeyModifiers == KeyModifiers.Alt;
        bool isResize = eventArguments.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift);
        if ((isMove || isResize)
            && sender is Control { DataContext: KustoDashboardWidgetViewModel widget }
            && AdjustDashboardWidgetLayout(widget, eventArguments.Key, isResize))
        {
            eventArguments.Handled = true;
        }
    }

    private static void OnResultRowContainerPrepared(object? sender, ContainerPreparedEventArgs eventArguments)
    {
        _ = sender;
        if (eventArguments.Container is ListBoxItem item
            && item.DataContext is KustoResultRowViewModel row)
        {
            AutomationProperties.SetName(item, row.AutomationText);
            AutomationProperties.SetControlTypeOverride(item, AutomationControlType.DataItem);
        }
    }

    private async Task CheckForUpdateAsync()
    {
        IKustoApplicationUpdateService? service = updateService;
        if (service is null || Interlocked.Exchange(ref updateCheckStarted, 1) != 0)
        {
            return;
        }

        try
        {
            Version? currentVersion = typeof(MainWindow).Assembly.GetName().Version;
            if (currentVersion is null)
            {
                return;
            }

            KustoApplicationUpdate? update = await service.CheckForUpdateAsync(
                currentVersion,
                updateCancellationSource.Token).ConfigureAwait(true);
            if (update is not null && !updateCancellationSource.IsCancellationRequested)
            {
                availableUpdate = update;
                if (updateButton is not null)
                {
                    AutomationProperties.SetName(updateButton, $"Update to {update.TagName}");
                    ToolTip.SetTip(updateButton, $"{update.TagName} is available. Open the official GitHub release.");
                    updateButton.IsVisible = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Update discovery is optional and must never interfere with startup or shutdown.
        }
        catch (HttpRequestException)
        {
            // Offline and GitHub API failures leave the optional update action hidden.
        }
        catch (IOException)
        {
            // Response stream failures leave the optional update action hidden.
        }
        catch (InvalidDataException)
        {
            // Ignore an unexpected release response instead of interrupting the workbench.
        }
        catch (JsonException)
        {
            // Ignore malformed remote JSON instead of interrupting the workbench.
        }
    }

    private void OnOpenUpdateClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (availableUpdate is null)
        {
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(availableUpdate.ReleasePageUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ReportDesktopStatus($"Unable to open the update page: {exception.Message}");
        }

        eventArguments.Handled = true;
    }

    private void OnAppearancePropertyChanged(object? sender, PropertyChangedEventArgs eventArguments)
    {
        if (sender is AppearanceSettings settings
            && DataContext is MainWindowViewModel viewModel)
        {
            if (IsCopilotDefaultProperty(eventArguments.PropertyName))
            {
                viewModel.ApplyCopilotDefaults(CreateCopilotDefaults(settings));
            }
            else if (IsAIProviderProperty(eventArguments.PropertyName))
            {
                viewModel.RefreshCopilotProvider();
            }
        }

        UpdateAppearanceClasses();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs eventArguments)
    {
        UpdateHighContrastResources();
    }

    private void OnSignedInUsersChanged(object? sender, EventArgs eventArguments)
    {
        Dispatcher.UIThread.Post(QueueSignedInUsersRefresh);
    }

    private void QueueSignedInUsersRefresh()
    {
        if (!isDisposed && identityService is not null)
        {
            identityRefreshCancellationSource?.Cancel();
            identityRefreshCancellationSource?.Dispose();
            identityRefreshCancellationSource = new CancellationTokenSource();
            _ = RefreshSignedInUsersAsync(
                identityService,
                identityRefreshCancellationSource.Token);
        }
    }

    private async Task RefreshSignedInUsersAsync(
        IKustoIdentityService service,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<KustoSignedInUser> users = await service.GetSignedInUsersAsync(cancellationToken)
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(
                () => ApplySignedInUsers(users),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer authentication result superseded this account refresh.
        }
    }

    private void ApplySignedInUsers(IReadOnlyList<KustoSignedInUser> users)
    {
        foreach (SignedInUserAvatarViewModel user in signedInUsers)
        {
            user.Dispose();
        }

        signedInUsers.Clear();

        foreach (KustoSignedInUser user in users)
        {
            signedInUsers.Add(new SignedInUserAvatarViewModel(user));
        }
    }

    private async void OnSignOutClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Button { CommandParameter: string accountId }
            && identityService is not null)
        {
            await identityService.SignOutAsync(
                accountId,
                automationCancellationSource.Token).ConfigureAwait(true);
        }
    }

    private void OnConnectionsClick(object? sender, RoutedEventArgs eventArguments)
    {
        bool returningFromWorkspace = false;
        MainWindowViewModel? viewModel = DataContext as MainWindowViewModel;

        if (viewModel is not null)
        {
            returningFromWorkspace = !viewModel.IsQueryWorkbenchView;
        }

        if (navigationSplitView is not null)
        {
            navigationSplitView.IsPaneOpen = returningFromWorkspace || !navigationSplitView.IsPaneOpen;
            Classes.Set("explorerOpen", navigationSplitView.IsPaneOpen);
        }

        viewModel?.ShowQueryWorkbenchCommand.Execute(null);

        connectionsButton?.Classes.Add("selected");
        dashboardButton?.Classes.Remove("selected");
        automationButton?.Classes.Remove("selected");
        sessionsButton?.Classes.Remove("selected");
        graphButton?.Classes.Remove("selected");
    }

    private void OnDashboardsClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ShowDashboardsCommand.Execute(null);
            _ = RunAutomationTickAsync(viewModel);
        }

        connectionsButton?.Classes.Remove("selected");
        dashboardButton?.Classes.Add("selected");
        automationButton?.Classes.Remove("selected");
        sessionsButton?.Classes.Remove("selected");
        graphButton?.Classes.Remove("selected");

        if (navigationSplitView is not null)
        {
            navigationSplitView.IsPaneOpen = false;
            Classes.Set("explorerOpen", false);
        }
    }

    private void OnAutomationsClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ShowAutomationsCommand.Execute(null);
        }

        connectionsButton?.Classes.Remove("selected");
        dashboardButton?.Classes.Remove("selected");
        automationButton?.Classes.Add("selected");
        sessionsButton?.Classes.Remove("selected");
        graphButton?.Classes.Remove("selected");
    }

    private async void OnSessionsClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ShowSessionsCommand.ExecuteAsync(null);
        }

        connectionsButton?.Classes.Remove("selected");
        dashboardButton?.Classes.Remove("selected");
        automationButton?.Classes.Remove("selected");
        sessionsButton?.Classes.Add("selected");
        graphButton?.Classes.Remove("selected");

        if (navigationSplitView is not null)
        {
            navigationSplitView.IsPaneOpen = false;
            Classes.Set("explorerOpen", false);
        }
    }

    private void OnGraphClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ShowGraphCommand.Execute(null);
        }

        connectionsButton?.Classes.Remove("selected");
        dashboardButton?.Classes.Remove("selected");
        automationButton?.Classes.Remove("selected");
        sessionsButton?.Classes.Remove("selected");
        graphButton?.Classes.Add("selected");

        if (navigationSplitView is not null)
        {
            navigationSplitView.IsPaneOpen = false;
            Classes.Set("explorerOpen", false);
        }

        graphSearch?.Focus();
    }

    private void OnGraphFitClick(object? sender, RoutedEventArgs eventArguments)
    {
        graphCanvas?.FitToView();
    }

    private void OnGraphResetLayoutClick(object? sender, RoutedEventArgs eventArguments)
    {
        graphCanvas?.ResetNodePositions();
    }

    private void OnSaveGraphPngClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = SaveGraphPngAsync();
    }

    private void OnSaveGraphMlClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = SaveGraphMlAsync();
    }

    private void OnGraphZoomInClick(object? sender, RoutedEventArgs eventArguments)
    {
        graphCanvas?.ZoomIn();
    }

    private void OnGraphZoomOutClick(object? sender, RoutedEventArgs eventArguments)
    {
        graphCanvas?.ZoomOut();
    }

    private void OnAutomationTimerTick(object? sender, EventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel && !automationCancellationSource.IsCancellationRequested)
        {
            _ = RunAutomationTickAsync(viewModel);
        }
    }

    private void OnDashboardWidgetDeleteClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoDashboardWidgetViewModel widget }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dashboard.OpenDeleteWidgetCommand.Execute(widget);
        }
    }

    private void OnDashboardThemeClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoDashboardThemeViewModel theme }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dashboard.ApplyDashboardThemeCommand.Execute(theme);
        }
    }

    private void OnDashboardWidgetEditClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoDashboardWidgetViewModel widget }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dashboard.OpenEditWidgetCommand.Execute(widget);
        }
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDashboardWidgetRefreshClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoDashboardWidgetViewModel widget })
        {
            widget.RefreshCommand.Execute(null);
        }
    }

    private void OnWidgetThemeClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoDashboardThemeViewModel theme }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dashboard.ApplyWidgetThemeCommand.Execute(theme);
        }
    }

    private void OnDashboardWidgetPointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        BeginDashboardWidgetInteraction(sender, eventArguments, isResize: false);
    }

    private void OnDashboardWidgetResizePointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        BeginDashboardWidgetInteraction(sender, eventArguments, isResize: true);
    }

    private void OnDashboardWidgetPointerMoved(object? sender, PointerEventArgs eventArguments)
    {
        if (dashboardCanvas is null
            || dashboardInteractionControl is null
            || dashboardInteractionWidget is null
            || !ReferenceEquals(sender, dashboardInteractionControl))
        {
            return;
        }

        Point position = eventArguments.GetPosition(dashboardCanvas);
        double gridUnitSize = KustoDashboardWidgetViewModel.GridUnitSize;
        int columnDelta = (int)Math.Round(
            (position.X - dashboardInteractionOrigin.X) / gridUnitSize,
            MidpointRounding.AwayFromZero);
        int rowDelta = (int)Math.Round(
            (position.Y - dashboardInteractionOrigin.Y) / gridUnitSize,
            MidpointRounding.AwayFromZero);

        if (isDashboardResize)
        {
            int maximumColumnSpan = DashboardGridColumnCount - dashboardInteractionWidget.Column;
            int maximumRowSpan = DashboardGridMaximumRowCount - dashboardInteractionWidget.Row;
            int columnSpan = Math.Clamp(
                dashboardInteractionStartColumnSpan + columnDelta,
                DashboardMinimumColumnSpan,
                maximumColumnSpan);
            int rowSpan = Math.Clamp(
                dashboardInteractionStartRowSpan + rowDelta,
                DashboardMinimumRowSpan,
                maximumRowSpan);
            dashboardInteractionWidget.PreviewLayout(
                dashboardInteractionWidget.Column,
                dashboardInteractionWidget.Row,
                columnSpan,
                rowSpan);
        }
        else
        {
            int maximumColumn = DashboardGridColumnCount - dashboardInteractionStartColumnSpan;
            int maximumRow = DashboardGridMaximumRowCount - dashboardInteractionStartRowSpan;
            int column = Math.Clamp(dashboardInteractionStartColumn + columnDelta, 0, maximumColumn);
            int row = Math.Clamp(dashboardInteractionStartRow + rowDelta, 0, maximumRow);
            dashboardInteractionWidget.PreviewLayout(
                column,
                row,
                dashboardInteractionStartColumnSpan,
                dashboardInteractionStartRowSpan);
        }

        eventArguments.Handled = true;
    }

    private void OnDashboardWidgetPointerReleased(object? sender, PointerReleasedEventArgs eventArguments)
    {
        if (dashboardInteractionWidget is not null
            && dashboardInteractionControl is not null
            && ReferenceEquals(sender, dashboardInteractionControl))
        {
            dashboardInteractionWidget.CommitLayout();
            eventArguments.Pointer.Capture(null);
            dashboardInteractionWidget = null;
            dashboardInteractionControl = null;
            isDashboardResize = false;
            eventArguments.Handled = true;
        }
    }

    private void OnImportDashboardClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = ImportDashboardAsync();
    }

    private void OnExportDashboardClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = ExportDashboardAsync();
    }

    private async void OnAutomationNotificationRequested(
        object? sender,
        KustoAutomationNotificationEventArgs eventArguments)
    {
        if (automationNotificationDispatcher is not null)
        {
            try
            {
                await automationNotificationDispatcher.DispatchAsync(
                    eventArguments.Notification,
                    automationCancellationSource.Token);
            }
            catch (OperationCanceledException) when (automationCancellationSource.IsCancellationRequested)
            {
                // Application shutdown cancels pending email delivery.
            }
        }
    }

    private void OnResultScrollChanged(object? sender, ScrollChangedEventArgs eventArguments)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            double maximumOffset = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
            if (resultHorizontalOverflowHint is not null)
            {
                resultHorizontalOverflowHint.IsVisible = maximumOffset - scrollViewer.Offset.X > 1;
            }

            if (resultHorizontalScrollBar is not null)
            {
                resultHorizontalScrollBar.Maximum = maximumOffset;
                resultHorizontalScrollBar.ViewportSize = scrollViewer.Viewport.Width;
                resultHorizontalScrollBar.LargeChange = Math.Max(1, scrollViewer.Viewport.Width);
                resultHorizontalScrollBar.SmallChange = 32;
                resultHorizontalScrollBar.Value = Math.Clamp(scrollViewer.Offset.X, 0, maximumOffset);
                resultHorizontalScrollBar.IsEnabled = maximumOffset > 1;
            }
        }
    }

    private void OnResultHorizontalScroll(object? sender, ScrollEventArgs eventArguments)
    {
        if (sender is ScrollBar scrollBar && resultScrollViewer is not null)
        {
            resultScrollViewer.Offset = new Vector(
                Math.Clamp(eventArguments.NewValue, 0, scrollBar.Maximum),
                resultScrollViewer.Offset.Y);
        }
    }

    private void OnResultRowsScrollChanged(object? sender, ScrollChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (sender is ScrollViewer scrollViewer)
        {
            UpdateResultVerticalScrollBar(scrollViewer);
        }
    }

    private void OnResultPointerWheelChanged(object? sender, PointerWheelEventArgs eventArguments)
    {
        _ = sender;
        if (resultRowsScrollViewer is null || eventArguments.Delta.Y == 0)
        {
            return;
        }

        double maximumOffset = Math.Max(
            0,
            resultRowsScrollViewer.Extent.Height - resultRowsScrollViewer.Viewport.Height);
        double nextOffset = CalculateResultWheelOffset(
            resultRowsScrollViewer.Offset.Y,
            maximumOffset,
            eventArguments.Delta.Y);
        if (Math.Abs(nextOffset - resultRowsScrollViewer.Offset.Y) < 0.01)
        {
            return;
        }

        resultRowsScrollViewer.Offset = new Vector(resultRowsScrollViewer.Offset.X, nextOffset);
        eventArguments.Handled = true;
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnResultsListTemplateApplied(object? sender, TemplateAppliedEventArgs eventArguments)
    {
        _ = sender;
        ScrollViewer? scrollViewer = eventArguments.NameScope.Find<ScrollViewer>("PART_ScrollViewer")
            ?? resultsList?
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();

        if (!ReferenceEquals(resultRowsScrollViewer, scrollViewer))
        {
            if (resultRowsScrollViewer is not null)
            {
                resultRowsScrollViewer.ScrollChanged -= OnResultRowsScrollChanged;
            }

            resultRowsScrollViewer = scrollViewer;

            if (resultRowsScrollViewer is not null)
            {
                resultRowsScrollViewer.ScrollChanged += OnResultRowsScrollChanged;
                UpdateResultVerticalScrollBar(resultRowsScrollViewer);
            }
        }
    }

    private void OnResultVerticalScroll(object? sender, ScrollEventArgs eventArguments)
    {
        if (sender is ScrollBar scrollBar && resultRowsScrollViewer is not null)
        {
            resultRowsScrollViewer.Offset = new Vector(
                resultRowsScrollViewer.Offset.X,
                Math.Clamp(eventArguments.NewValue, 0, scrollBar.Maximum));
        }
    }

    private void UpdateResultVerticalScrollBar(ScrollViewer scrollViewer)
    {
        if (resultVerticalScrollBar is not null)
        {
            double maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            resultVerticalScrollBar.Maximum = maximumOffset;
            resultVerticalScrollBar.ViewportSize = scrollViewer.Viewport.Height;
            resultVerticalScrollBar.LargeChange = Math.Max(1, scrollViewer.Viewport.Height);
            resultVerticalScrollBar.SmallChange = 30;
            resultVerticalScrollBar.Value = Math.Clamp(scrollViewer.Offset.Y, 0, maximumOffset);
            resultVerticalScrollBar.IsEnabled = maximumOffset > 1;
        }
    }

    private async Task RunAutomationTickAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.RunDueAutomationsAsync(
                DateTimeOffset.UtcNow,
                automationCancellationSource.Token);

            if (viewModel.IsDashboardView)
            {
                await viewModel.Dashboard.RefreshDueWidgetsAsync(
                    DateTimeOffset.UtcNow,
                    automationCancellationSource.Token);
            }
        }
        catch (OperationCanceledException) when (automationCancellationSource.IsCancellationRequested)
        {
            // Window shutdown cancels any active scheduled execution.
        }
    }

    private void OnConnectionTreeContainerPrepared(object? sender, ContainerPreparedEventArgs eventArguments)
    {
        if (eventArguments.Container is TreeViewItem treeViewItem)
        {
            treeViewItem.ContainerPrepared -= OnConnectionTreeContainerPrepared;
            treeViewItem.ContainerPrepared += OnConnectionTreeContainerPrepared;

            string? automationName = treeViewItem.DataContext switch
            {
                KustoClusterViewModel cluster => cluster.DisplayName,
                KustoDatabaseViewModel database => database.DisplayName,
                SchemaFunctionsFolderViewModel folder => folder.Name,
                SchemaFunctionViewModel function => function.Signature,
                SchemaTableViewModel table => table.Name,
                SchemaColumnViewModel column => $"{column.Name}, {column.TypeName}",
                _ => null,
            };

            if (automationName is not null)
            {
                AutomationProperties.SetName(treeViewItem, automationName);
            }

            bool isSelectedDatabase = DataContext is MainWindowViewModel viewModel
                && ReferenceEquals(viewModel.SelectedExplorerItem, treeViewItem.DataContext);
            treeViewItem.IsExpanded = treeViewItem.DataContext is KustoClusterViewModel || isSelectedDatabase;
        }
    }

    private void OnDarkThemeChecked(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is RadioButton { IsChecked: true })
        {
            appearanceSettings.ThemePreference = ThemePreference.Dark;
        }
    }

    private void OnDensityToggleChanged(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.Density = toggle.IsChecked == true
                ? WorkbenchDensity.Comfortable
                : WorkbenchDensity.Compact;
        }
    }

    private void OnSettingsCopilotAzureMcpChanged(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.CopilotEnableAzureMcpByDefault = toggle.IsChecked == true;
        }
    }

    private void OnSettingsAIProviderChanged(object? sender, SelectionChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (!suppressSettingsAIProviderChange
            && appearanceSettings is not null
            && sender is ComboBox { SelectedIndex: >= 0 } providerSelector
            && Enum.IsDefined((KustoAIProviderKind)providerSelector.SelectedIndex))
        {
            appearanceSettings.ProviderKind = (KustoAIProviderKind)providerSelector.SelectedIndex;
        }
    }

    private void OnSettingsAzureOpenAIAuthenticationChanged(
        object? sender,
        SelectionChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (!suppressSettingsAzureOpenAIAuthenticationChange
            && appearanceSettings is not null
            && sender is ComboBox { SelectedIndex: >= 0 } authenticationSelector
            && Enum.IsDefined(
                (KustoAzureOpenAIAuthenticationKind)authenticationSelector.SelectedIndex))
        {
            appearanceSettings.AzureOpenAIAuthenticationKind =
                (KustoAzureOpenAIAuthenticationKind)authenticationSelector.SelectedIndex;
        }
    }

    private void OnSettingsAIProviderTextLostFocus(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        ApplyAIProviderSettingsFromControls();
    }

    private void OnSettingsCopilotDefaultModelChanged(object? sender, SelectionChangedEventArgs eventArguments)
    {
        _ = eventArguments;

        if (!suppressSettingsCopilotDefaultModelChange
            && appearanceSettings is not null
            && sender is ComboBox { SelectedItem: KustoCopilotModel model })
        {
            appearanceSettings.CopilotDefaultModel = model;
        }
    }

    private async void OnSettingsCopilotModelsRefreshClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        if (DataContext is MainWindowViewModel viewModel)
        {
            await RefreshSettingsCopilotModelsAsync(viewModel);
        }
    }

    private void OnSettingsCopilotMicrosoftLearnMcpChanged(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.CopilotEnableMicrosoftLearnMcpByDefault = toggle.IsChecked == true;
        }
    }

    private void OnSettingsCopilotShareResultDataChanged(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.CopilotShareResultDataByDefault = toggle.IsChecked == true;
        }
    }

    private void OnSettingsCopilotShareSchemaChanged(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.CopilotShareSchemaByDefault = toggle.IsChecked == true;
        }
    }

    private void OnSettingsCopilotShareTabContentChanged(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.CopilotShareTabContentByDefault = toggle.IsChecked == true;
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (settingsDialog is not null)
        {
            _ = sender;
            _ = eventArguments;
            settingsDialog.IsVisible = true;
            UpdateCopilotDefaultControls();
            settingsTextZoomSlider?.Focus();

            if (DataContext is MainWindowViewModel viewModel && !viewModel.Copilot.IsSignedIn)
            {
                await RefreshSettingsCopilotModelsAsync(viewModel);
            }
        }
    }

    private async void OnOpenRecordingClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Recording.OpenRecordingCommand.ExecuteAsync(null);
            Dispatcher.UIThread.Post(() => recordingNameTextBox?.Focus());
        }
    }

    private void OnCloseRecordingClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Recording.CloseRecordingCommand.Execute(null);
            recordButton?.Focus();
        }
    }

    private async void OnStartRecordingClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.Recording.StartRecordingCommand.CanExecute(null))
        {
            await viewModel.Recording.StartRecordingCommand.ExecuteAsync(null);
            if (!viewModel.Recording.IsRecordingDialogOpen)
            {
                viewModel.ShowQueryWorkbenchCommand.Execute(null);
                queryEditor?.Focus();
            }
        }
    }

    private void OnTogglePertinentValuesClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (pertinentValuesSplitView is not null)
        {
            pertinentValuesSplitView.IsPaneOpen = !pertinentValuesSplitView.IsPaneOpen;
        }
    }

    private void OnCancelRecordedSessionDeleteClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Recording.CancelDeleteSessionCommand.Execute(null);
            sessionsButton?.Focus();
        }
    }

    private async void OnDeleteRecordedSessionClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.Recording.DeleteSessionCommand.CanExecute(null))
        {
            await viewModel.Recording.DeleteSessionCommand.ExecuteAsync(null);
            sessionsButton?.Focus();
        }
    }

    private async void OnRecordedSessionSelectionChanged(object? sender, SelectionChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (sender is ListBox { SelectedItem: KustoRecordedSessionSummaryViewModel session }
            && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Recording.SelectSessionCommand.ExecuteAsync(session);
        }
    }

    private void OnRenameRecordedQueryClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoRecordedExecutionViewModel execution }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Recording.OpenRenameExecution(execution);
            eventArguments.Handled = true;
        }
    }

    private async Task RefreshSettingsCopilotModelsAsync(MainWindowViewModel viewModel)
    {
        if (!viewModel.Copilot.RefreshModelsCommand.CanExecute(null))
        {
            return;
        }

        suppressSettingsCopilotDefaultModelChange = true;

        try
        {
            await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);
        }
        finally
        {
            suppressSettingsCopilotDefaultModelChange = false;
            UpdateCopilotDefaultControls();
        }
    }

    private void OnSettingsCloseClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (settingsDialog is not null)
        {
            ApplyAIProviderSettingsFromControls();
            settingsDialog.IsVisible = false;
            settingsButton?.Focus();
        }
    }

    private void OnOpenLocalDataFolderClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DataContext is not MainWindowViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.Recording.DatabaseDirectoryPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(viewModel.Recording.DatabaseDirectoryPath);
            _ = Process.Start(new ProcessStartInfo(viewModel.Recording.DatabaseDirectoryPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            ReportDesktopStatus($"Unable to open the local data folder: {exception.Message}");
        }
    }

    private void OnTextZoomChanged(object? sender, RangeBaseValueChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (appearanceSettings is not null && sender is Slider slider)
        {
            appearanceSettings.TextZoomPercentage = (int)Math.Round(
                slider.Value,
                MidpointRounding.AwayFromZero);
        }
    }

    private void OnResultTextZoomChanged(object? sender, RangeBaseValueChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (appearanceSettings is not null && sender is Slider slider)
        {
            appearanceSettings.ResultTextZoomPercentage = (int)Math.Round(
                slider.Value,
                MidpointRounding.AwayFromZero);
        }
    }

    private void OnSettingsKqlHoverHelpChanged(object? sender, RoutedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (appearanceSettings is not null && sender is ToggleSwitch toggle)
        {
            appearanceSettings.ShowKqlHoverHelp = toggle.IsChecked == true;
        }
    }

    private void OnResultCellPointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoResultCellViewModel cell } control
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetResultContext(cell);
            PointerPointProperties properties = eventArguments.GetCurrentPoint(control).Properties;

            if (properties.IsLeftButtonPressed && eventArguments.ClickCount == 2)
            {
                viewModel.OpenResultValue(cell);
                eventArguments.Handled = true;
                return;
            }

            if (properties.IsRightButtonPressed && resultsList?.SelectedItems is { } selectedItems
                && !selectedItems.Contains(cell.Row))
            {
                selectedItems.Clear();
                selectedItems.Add(cell.Row);
            }
        }
    }

    private void OnRecordedResultCellPointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoResultCellViewModel cell }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Recording.SetResultContext(cell);
            eventArguments.Handled = eventArguments.GetCurrentPoint((Control)sender).Properties.IsRightButtonPressed;
        }
    }

    private void OnResultColumnHeaderClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoResultColumnViewModel column }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ToggleResultSort(column);
            eventArguments.Handled = true;
        }
    }

    private void OnClearResultColumnFilterClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoResultColumnViewModel column }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearResultColumnFilter(column);
            eventArguments.Handled = true;
        }
    }

    private void OnCopyResultValueClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (sender is MenuItem { DataContext: KustoResultCellViewModel cell })
            {
                viewModel.SetResultContext(cell);
            }

            CopySelectedResultValues(viewModel);
        }
    }

    private void OnResultsListKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        _ = sender;

        if (eventArguments.Key == Key.C
            && eventArguments.KeyModifiers == KeyModifiers.Control
            && DataContext is MainWindowViewModel viewModel)
        {
            ReadOnlyCollection<KustoResultRowViewModel> selectedRows = GetSelectedResultRows();

            if (!string.IsNullOrEmpty(viewModel.GetContextColumnName()))
            {
                CopySelectedResultValues(viewModel, selectedRows);
            }
            else if (selectedRows.Count > 0)
            {
                _ = CopyTextAsync(
                    viewModel.CreateClipboardText(selectedRows),
                    selectedRows.Count == 1 ? "Copied result row" : $"Copied {selectedRows.Count:N0} result rows");
            }

            eventArguments.Handled = true;
        }
    }

    private void OnCopyQueryErrorClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel { HasQueryError: true } viewModel)
        {
            _ = CopyTextAsync(viewModel.QueryErrorText, "Copied query error");
        }
    }

    private void OnCopyQueryEditorClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (editorController is not null)
        {
            _ = editorController.CopySelectionWithFormattingAsync();
        }
    }

    private void OnCopyCopilotMessageClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is MenuItem { DataContext: KustoCopilotMessageViewModel message })
        {
            _ = CopyTextAsync(message.Content, "Copied Copilot message");
        }
    }

    private void OnCopyGraphInspectorTextClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is MenuItem { CommandParameter: string text })
        {
            _ = CopyTextAsync(text, "Copied graph property value");
            eventArguments.Handled = true;
        }
    }

    private void OnCopilotPromptKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        bool requestsSend = eventArguments.Key == Key.Enter
            && eventArguments.KeyModifiers == KeyModifiers.Control;

        if (requestsSend
            && DataContext is MainWindowViewModel viewModel
            && viewModel.Copilot.SendCommand.CanExecute(null))
        {
            viewModel.Copilot.SendCommand.Execute(null);
            eventArguments.Handled = true;
        }
    }

    private void OnCopyResultRowClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = CopyTextAsync(viewModel.CreateClipboardText([]), "Copied result row");
        }
    }

    private void OnCopyDatatableClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = CopyTextAsync(
                viewModel.CreateKqlDatatable(GetSelectedResultRows()),
                "Copied KQL datatable");
        }
    }

    private void OnCopyAllDatatableClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = CopyTextAsync(
                viewModel.CreateKqlDatatable(),
                "Copied all rows as KQL datatable");
        }
    }

    private void OnCopyResultsClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = CopyTextAsync(
                viewModel.CreateClipboardText(viewModel.ResultRows),
                "Copied query results");
        }
    }

    private void OnAddCellFilterClick(object? sender, RoutedEventArgs eventArguments)
    {
        ExecuteResultContextCommand(
            sender,
            viewModel => viewModel.AddCellFilterFromResults(GetSelectedResultRows()));
        eventArguments.Handled = true;
    }

    private void OnAddRowFilterClick(object? sender, RoutedEventArgs eventArguments)
    {
        ExecuteResultContextCommand(sender, viewModel => viewModel.AddRowFilterCommand.Execute(null));
    }

    private async void OnMarkRecordedCellClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (sender is MenuItem { DataContext: KustoResultCellViewModel cell })
            {
                viewModel.SetResultContext(cell);
            }

            await viewModel.MarkRecordedCellCommand.ExecuteAsync(null);
            eventArguments.Handled = true;
        }
    }

    private async void OnMarkRecordedColumnClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (sender is MenuItem { DataContext: KustoResultCellViewModel cell })
            {
                viewModel.SetResultContext(cell);
            }

            await viewModel.MarkRecordedColumnCommand.ExecuteAsync(null);
            eventArguments.Handled = true;
        }
    }

    private async void OnUnmarkRecordedCellClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (sender is MenuItem { DataContext: KustoResultCellViewModel cell })
            {
                viewModel.SetResultContext(cell);
            }

            await viewModel.UnmarkRecordedCellCommand.ExecuteAsync(null);
            eventArguments.Handled = true;
        }
    }

    private async void OnMarkHistoricalCellClick(object? sender, RoutedEventArgs eventArguments)
    {
        await ExecuteHistoricalContextCommandAsync(
            sender,
            workspace => workspace.MarkSelectedCellCommand.ExecuteAsync(null));
        eventArguments.Handled = true;
    }

    private async void OnMarkHistoricalColumnClick(object? sender, RoutedEventArgs eventArguments)
    {
        await ExecuteHistoricalContextCommandAsync(
            sender,
            workspace => workspace.MarkSelectedColumnCommand.ExecuteAsync(null));
        eventArguments.Handled = true;
    }

    private async void OnUnmarkHistoricalCellClick(object? sender, RoutedEventArgs eventArguments)
    {
        await ExecuteHistoricalContextCommandAsync(
            sender,
            workspace => workspace.UnmarkSelectedCellCommand.ExecuteAsync(null));
        eventArguments.Handled = true;
    }

    private async void OnSetHistoricalChainStartClick(object? sender, RoutedEventArgs eventArguments)
    {
        await ExecuteHistoricalContextCommandAsync(
            sender,
            workspace => workspace.SetChainStartCommand.ExecuteAsync(null));
        eventArguments.Handled = true;
    }

    private async void OnSetHistoricalChainEndClick(object? sender, RoutedEventArgs eventArguments)
    {
        await ExecuteHistoricalContextCommandAsync(
            sender,
            workspace => workspace.SetChainEndCommand.ExecuteAsync(null));
        eventArguments.Handled = true;
    }

    private async void OnSetPertinentValueChainStartClick(object? sender, RoutedEventArgs eventArguments)
    {
        await SetPertinentValueEndpointAsync(sender, KustoChainEndpointRole.Start);
        eventArguments.Handled = true;
    }

    private async void OnSetPertinentValueChainEndClick(object? sender, RoutedEventArgs eventArguments)
    {
        await SetPertinentValueEndpointAsync(sender, KustoChainEndpointRole.End);
        eventArguments.Handled = true;
    }

    private void OnRecordedTimelineValuePointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoRecordedTimelineValueViewModel value } control
            && DataContext is MainWindowViewModel viewModel
            && eventArguments.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            viewModel.Recording.SelectTimelineValue(value);
            eventArguments.Handled = true;
        }
    }

    private async void OnSetTimelineValueChainStartClick(object? sender, RoutedEventArgs eventArguments)
    {
        await SetTimelineValueEndpointAsync(sender, KustoChainEndpointRole.Start);
        eventArguments.Handled = true;
    }

    private async void OnSetTimelineValueChainEndClick(object? sender, RoutedEventArgs eventArguments)
    {
        await SetTimelineValueEndpointAsync(sender, KustoChainEndpointRole.End);
        eventArguments.Handled = true;
    }

    private async Task SetPertinentValueEndpointAsync(
        object? sender,
        KustoChainEndpointRole role)
    {
        if (sender is MenuItem { DataContext: KustoRecordedPertinentValueViewModel value }
            && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Recording.SetEndpointFromPertinentValueAsync(
                value,
                role,
                automationCancellationSource.Token);
        }
    }

    private async Task SetTimelineValueEndpointAsync(
        object? sender,
        KustoChainEndpointRole role)
    {
        if (sender is MenuItem { DataContext: KustoRecordedTimelineValueViewModel value }
            && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Recording.SetEndpointFromTimelineValueAsync(
                value,
                role,
                automationCancellationSource.Token);
        }
    }

    private async Task ExecuteHistoricalContextCommandAsync(
        object? sender,
        Func<KustoRecordingWorkspaceViewModel, Task> action)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (sender is MenuItem { DataContext: KustoResultCellViewModel cell })
            {
                viewModel.Recording.SetResultContext(cell);
            }

            await action(viewModel.Recording);
        }
    }

    private void OnConditionalFormattingClick(object? sender, RoutedEventArgs eventArguments)
    {
        ExecuteResultContextCommand(sender, viewModel =>
        {
            viewModel.ConditionalRuleColumnName = viewModel.GetContextColumnName();
            viewModel.OpenConditionalFormattingCommand.Execute(null);
        });
    }

    private void OnExportCsvClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = ExportResultsAsync(KustoResultExportFormat.Csv);
    }

    private void OnExportPertinentValuesCsvClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        _ = ExportPertinentValuesCsvAsync();
    }

    private void OnExportExcelClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = ExportResultsAsync(KustoResultExportFormat.Excel);
    }

    private void OnExportJsonClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = ExportResultsAsync(KustoResultExportFormat.Json);
    }

    private void OnConditionalColorChanged(object? sender, ColorChangedEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ConditionalRuleColorHex = $"#{eventArguments.NewColor.R:X2}{eventArguments.NewColor.G:X2}{eventArguments.NewColor.B:X2}";
        }
    }

    private void OnConditionalPresetClick(object? sender, RoutedEventArgs eventArguments)
    {
        if (sender is Button { CommandParameter: string colorHex }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetConditionalColorCommand.Execute(colorHex);
            if (conditionalColorPicker is not null)
            {
                conditionalColorPicker.Color = Color.Parse(colorHex);
            }
        }
    }

    private void OnSaveChartClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = SaveChartAsync(visualizationSurface);
    }

    private void OnSaveAutomationChartClick(object? sender, RoutedEventArgs eventArguments)
    {
        _ = SaveChartAsync(automationVisualizationSurface);
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDocumentTabContainerPrepared(object? sender, ContainerPreparedEventArgs eventArguments)
    {
        if (eventArguments.Container is TabStripItem tabStripItem
            && tabStripItem.DataContext is KustoDocumentViewModel document)
        {
            AutomationProperties.SetName(tabStripItem, $"{document.TabAutomationText}, {document.TargetDisplayText}");
        }
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDocumentTabPointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        if (sender is Control { DataContext: KustoDocumentViewModel document } control
            && documentTabs is not null
            && eventArguments.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            bool startedOnButton = eventArguments.Source is Visual sourceVisual
                && sourceVisual.GetSelfAndVisualAncestors()
                    .TakeWhile(visual => !ReferenceEquals(visual, control))
                    .OfType<Button>()
                    .Any();

            if (!startedOnButton)
            {
                ResetDocumentTabDrag(releaseCapture: true);
                draggedDocument = document;
                draggedDocumentTab = control;
                documentTabDragOrigin = eventArguments.GetPosition(documentTabs);
            }
        }
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDocumentTabsPointerMoved(object? sender, PointerEventArgs eventArguments)
    {
        if (TryGetDocumentTabDragPosition(eventArguments, out Point position))
        {
            if (!isDocumentTabDragging && !HasPassedDocumentTabDragThreshold(position))
            {
                return;
            }

            BeginDocumentTabDrag(eventArguments.Pointer);
            UpdateDocumentTabDropTarget(position);
            eventArguments.Handled = true;
        }
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDocumentTabsPointerReleased(object? sender, PointerReleasedEventArgs eventArguments)
    {
        bool isLeftRelease = documentTabs is not null
            && eventArguments.GetCurrentPoint(documentTabs).Properties.PointerUpdateKind
                == PointerUpdateKind.LeftButtonReleased;

        if (isLeftRelease)
        {
            bool handled = isDocumentTabDragging;
            if (handled
                && draggedDocument is not null
                && documentTabDropTarget?.DataContext is KustoDocumentViewModel target
                && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.MoveDocumentBlock(draggedDocument, target, documentTabDropAfter);
            }

            ResetDocumentTabDrag(releaseCapture: true);
            eventArguments.Handled = handled;
        }
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDocumentTabsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArguments)
    {
        ResetDocumentTabDrag(releaseCapture: false);
    }

    [SuppressMessage("Major Code Smell", "S2325", Justification = "Avalonia compiled XAML resolves this instance event handler.")]
    private void OnDocumentTabPointerReleased(object? sender, PointerReleasedEventArgs eventArguments)
    {
        if (sender is Control { ContextMenu: ContextMenu contextMenu, DataContext: KustoDocumentViewModel document } control
            && eventArguments.GetCurrentPoint(control).Properties.PointerUpdateKind
                == PointerUpdateKind.RightButtonReleased)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SelectedDocument = document;
            }

            contextMenu.Open(control);
            eventArguments.Handled = true;
        }
    }

    private bool TryGetDocumentTabDragPosition(PointerEventArgs eventArguments, out Point position)
    {
        position = default;
        if (documentTabs is null || draggedDocument is null || draggedDocumentTab is null)
        {
            return false;
        }

        PointerPoint currentPoint = eventArguments.GetCurrentPoint(documentTabs);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            ResetDocumentTabDrag(releaseCapture: true);
            return false;
        }

        position = currentPoint.Position;
        return true;
    }

    private bool HasPassedDocumentTabDragThreshold(Point position)
    {
        return Math.Abs(position.X - documentTabDragOrigin.X) >= DocumentTabDragThreshold
            || Math.Abs(position.Y - documentTabDragOrigin.Y) >= DocumentTabDragThreshold;
    }

    private void BeginDocumentTabDrag(IPointer pointer)
    {
        if (!isDocumentTabDragging)
        {
            isDocumentTabDragging = true;
            documentTabPointer = pointer;
            pointer.Capture(documentTabs);
            draggedDocumentTab?.Classes.Add("dragging");
        }
    }

    private void UpdateDocumentTabDropTarget(Point position)
    {
        KustoDocumentViewModel? source = draggedDocument;
        if (documentTabs is null || source is null)
        {
            return;
        }

        TabStripItem? targetItem = documentTabs
            .GetVisualsAt(position)
            .SelectMany(visual => visual.GetSelfAndVisualAncestors())
            .OfType<TabStripItem>()
            .FirstOrDefault();
        KustoDocumentViewModel? target = targetItem?.DataContext as KustoDocumentViewModel;
        bool isSameBlock = target is null
            || ReferenceEquals(target, source)
            || (target.GroupName is not null
                && string.Equals(target.GroupName, source.GroupName, StringComparison.OrdinalIgnoreCase));

        if (isSameBlock)
        {
            SetDocumentTabDropTarget(null, placeAfter: false);
        }
        else
        {
            TabStripItem dropTarget = targetItem!;
            Point? targetCenter = dropTarget.TranslatePoint(
                new Point(dropTarget.Bounds.Width / 2, dropTarget.Bounds.Height / 2),
                documentTabs);
            bool placeAfter = targetCenter is { } center && position.X >= center.X;
            SetDocumentTabDropTarget(dropTarget, placeAfter);
        }
    }

    private void SetDocumentTabDropTarget(TabStripItem? target, bool placeAfter)
    {
        documentTabDropTarget?.Classes.Remove("dropBefore");
        documentTabDropTarget?.Classes.Remove("dropAfter");
        documentTabDropTarget = target;
        documentTabDropAfter = placeAfter;
        target?.Classes.Add(placeAfter ? "dropAfter" : "dropBefore");
    }

    private void ResetDocumentTabDrag(bool releaseCapture)
    {
        draggedDocumentTab?.Classes.Remove("dragging");
        SetDocumentTabDropTarget(null, placeAfter: false);
        IPointer? pointer = documentTabPointer;
        documentTabPointer = null;
        draggedDocument = null;
        draggedDocumentTab = null;
        isDocumentTabDragging = false;

        if (releaseCapture)
        {
            pointer?.Capture(null);
        }
    }

    private void OnTabSearchSelectionChanged(object? sender, SelectionChangedEventArgs eventArguments)
    {
        if (sender is ListBox { SelectedItem: KustoTabSearchResultViewModel result }
            && DataContext is MainWindowViewModel viewModel)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(DataContext, viewModel))
                {
                    viewModel.SelectTabSearchResultCommand.Execute(result);
                    queryEditor?.Focus();
                }
            });
        }
    }

    private void OnTabSearchKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        if (DataContext is MainWindowViewModel viewModel && eventArguments.Key == Key.Escape)
        {
            viewModel.CloseTabSearchCommand.Execute(null);
            queryEditor?.Focus();
            eventArguments.Handled = true;
        }
        else if (DataContext is MainWindowViewModel searchViewModel
            && eventArguments.Key == Key.Enter
            && searchViewModel.TabSearchResults.FirstOrDefault() is { } firstResult)
        {
            searchViewModel.SelectTabSearchResultCommand.Execute(firstResult);
            queryEditor?.Focus();
            eventArguments.Handled = true;
        }
    }

    private void OnLightThemeChecked(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is RadioButton { IsChecked: true })
        {
            appearanceSettings.ThemePreference = ThemePreference.Light;
        }
    }

    private void OnSystemThemeChecked(object? sender, RoutedEventArgs eventArguments)
    {
        if (appearanceSettings is not null && sender is RadioButton { IsChecked: true })
        {
            appearanceSettings.ThemePreference = ThemePreference.System;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        bool handled = TryHandleGlobalShortcut(eventArguments)
            || TryHandleDocumentShortcut(eventArguments)
            || TryHandlePanelShortcut(eventArguments)
            || TryHandleVisualizationShortcut(eventArguments);

        if (handled)
        {
            eventArguments.Handled = true;
        }
    }

    private bool TryHandleGlobalShortcut(KeyEventArgs eventArguments)
    {
        bool handled = TryCloseRecordingDialog(eventArguments)
            || TryCloseSettings(eventArguments);

        if (!handled && DataContext is MainWindowViewModel viewModel)
        {
            handled = TryHandleExecutionShortcut(eventArguments, viewModel);
        }

        KeyModifiers focusModifiers = eventArguments.KeyModifiers & ~KeyModifiers.Shift;
        if (!handled
            && eventArguments.Key == Key.F6
            && focusModifiers == KeyModifiers.None)
        {
            MoveFocusRegion(eventArguments.KeyModifiers.HasFlag(KeyModifiers.Shift));
            handled = true;
        }
        else if (!handled
            && eventArguments.Key == Key.F
            && eventArguments.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            tabSearchBox?.Focus();
            tabSearchBox?.SelectAll();
            handled = true;
        }
        else if (!handled
            && eventArguments.Key == Key.F12
            && eventArguments.KeyModifiers == KeyModifiers.Shift)
        {
            ToggleTheme();
            handled = true;
        }

        return handled;
    }

    private bool TryHandleExecutionShortcut(
        KeyEventArgs eventArguments,
        MainWindowViewModel viewModel)
    {
        bool requestsRun = eventArguments.Key == Key.F5
            && eventArguments.KeyModifiers == KeyModifiers.None;
        requestsRun |= eventArguments.Key == Key.Enter
            && eventArguments.KeyModifiers == KeyModifiers.Shift;
        bool requestsCancellation = eventArguments.Key == Key.F5
            && eventArguments.KeyModifiers == KeyModifiers.Shift;
        requestsCancellation |= eventArguments.Key == Key.Escape
            && eventArguments.KeyModifiers == KeyModifiers.None
            && (viewModel.IsRunningQuery
                || viewModel.Graph.IsCypherRunning
                || viewModel.Graph.IsFindingRoutes);
        bool targetsGraphQuery = viewModel.IsGraphView
            && graphCypherEditor?.IsKeyboardFocusWithin == true;

        if (requestsRun)
        {
            if (targetsGraphQuery)
            {
                viewModel.Graph.RunCypherCommand.Execute(null);
            }
            else
            {
                viewModel.RunQueryCommand.Execute(null);
            }
        }
        else if (requestsCancellation)
        {
            CancelActiveExecution(viewModel);
        }

        return requestsRun || requestsCancellation;
    }

    private bool TryCloseRecordingDialog(KeyEventArgs eventArguments)
    {
        bool requestsClose = eventArguments.Key == Key.Escape
            && eventArguments.KeyModifiers == KeyModifiers.None;
        if (!requestsClose || DataContext is not MainWindowViewModel viewModel)
        {
            return false;
        }

        if (viewModel.Recording.IsDatabaseRecoveryOpen)
        {
            viewModel.Recording.CancelDatabaseRecoveryCommand.Execute(null);
            sessionsButton?.Focus();
            return true;
        }

        if (viewModel.Recording.IsRecordingDialogOpen)
        {
            viewModel.Recording.CloseRecordingCommand.Execute(null);
            recordButton?.Focus();
            return true;
        }

        if (viewModel.Recording.IsDeleteConfirmationOpen)
        {
            viewModel.Recording.CancelDeleteSessionCommand.Execute(null);
            sessionsButton?.Focus();
            return true;
        }

        if (viewModel.Recording.IsDeleteExecutionConfirmationOpen)
        {
            viewModel.Recording.CancelDeleteExecutionCommand.Execute(null);
            sessionsButton?.Focus();
            return true;
        }

        if (viewModel.Recording.IsChainGenerationDialogOpen)
        {
            viewModel.Recording.DismissChainGenerationDialogCommand.Execute(null);
            sessionsButton?.Focus();
            return true;
        }

        return false;
    }

    private bool TryCloseSettings(KeyEventArgs eventArguments)
    {
        bool handled = settingsDialog?.IsVisible == true
            && eventArguments.Key == Key.Escape
            && eventArguments.KeyModifiers == KeyModifiers.None;

        if (handled)
        {
            settingsDialog!.IsVisible = false;
            settingsButton?.Focus();
        }

        return handled;
    }

    private bool TryHandleDocumentShortcut(KeyEventArgs eventArguments)
    {
        if (TryHandleDocumentReorderShortcut(eventArguments))
        {
            return true;
        }

        bool handled = false;
        if (eventArguments.KeyModifiers == KeyModifiers.Control
            && DataContext is MainWindowViewModel viewModel)
        {
            int documentIndex = GetDocumentShortcutIndex(eventArguments.Key);

            if (eventArguments.Key == Key.N)
            {
                viewModel.NewQueryCommand.Execute(null);
                handled = true;
            }
            else if (eventArguments.Key == Key.W && viewModel.SelectedDocument is not null)
            {
                viewModel.SelectedDocument.CloseCommand.Execute(null);
                handled = true;
            }
            else if (eventArguments.Key == Key.F2 && viewModel.SelectedDocument is not null)
            {
                viewModel.SelectedDocument.RenameCommand.Execute(null);
                handled = true;
            }
            else if (documentIndex >= 0 && documentIndex < viewModel.Documents.Count)
            {
                viewModel.SelectedDocument = viewModel.Documents[documentIndex];
                queryEditor?.Focus();
                handled = true;
            }
        }

        return handled;
    }

    private bool TryHandleDocumentReorderShortcut(KeyEventArgs eventArguments)
    {
        if (eventArguments.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && DataContext is MainWindowViewModel viewModel
            && viewModel.SelectedDocument is KustoDocumentViewModel selectedDocument
            && eventArguments.Key is Key.PageUp or Key.PageDown)
        {
            int offset = eventArguments.Key == Key.PageUp ? -1 : 1;
            viewModel.MoveDocumentBlock(selectedDocument, offset);
            documentTabs?.Focus();
            return true;
        }

        return false;
    }

    private bool TryHandlePanelShortcut(KeyEventArgs eventArguments)
    {
        bool handled = false;
        KeyModifiers panelModifiers = KeyModifiers.Control | KeyModifiers.Shift;

        if (eventArguments.KeyModifiers == panelModifiers
            && DataContext is MainWindowViewModel viewModel)
        {
            switch (eventArguments.Key)
            {
                case Key.R:
                    FocusOutputPanel(viewModel, 0);
                    handled = true;
                    break;
                case Key.T:
                    FocusConnectionsPanel(viewModel);
                    handled = true;
                    break;
                case Key.Q:
                    viewModel.ShowQueryWorkbenchCommand.Execute(null);
                    documentTabs?.Focus();
                    handled = true;
                    break;
                case Key.Y:
                    viewModel.ShowQueryWorkbenchCommand.Execute(null);
                    queryEditor?.Focus();
                    handled = true;
                    break;
                case Key.P:
                    FocusOutputPanel(viewModel, 1);
                    handled = true;
                    break;
                case Key.I:
                    FocusOutputPanel(viewModel, 2);
                    handled = true;
                    break;
                case Key.G:
                    viewModel.ShowGraphCommand.Execute(null);
                    connectionsButton?.Classes.Remove("selected");
                    automationButton?.Classes.Remove("selected");
                    graphButton?.Classes.Add("selected");
                    graphSearch?.Focus();
                    handled = true;
                    break;
            }
        }

        return handled;
    }

    private bool TryHandleVisualizationShortcut(KeyEventArgs eventArguments)
    {
        string? visualizationName = null;
        KeyModifiers pivotModifiers = KeyModifiers.Control | KeyModifiers.Shift;
        bool editorReservesShortcut = eventArguments.Key == Key.P
            && eventArguments.KeyModifiers == KeyModifiers.Alt
            && queryEditor?.IsKeyboardFocusWithin == true;

        if (!editorReservesShortcut && eventArguments.KeyModifiers == KeyModifiers.Alt)
        {
            visualizationName = eventArguments.Key switch
            {
                Key.C => "ColumnChart",
                Key.T => "TimeChart",
                Key.A => "AnomalyChart",
                Key.P => "PieChart",
                Key.L => "LadderChart",
                Key.V => "PivotChart",
                _ => null,
            };
        }
        else if (eventArguments.Key == Key.V && eventArguments.KeyModifiers == pivotModifiers)
        {
            visualizationName = "TimePivot";
        }

        MainWindowViewModel? viewModel = DataContext as MainWindowViewModel;
        bool handled = visualizationName is not null
            && viewModel?.RenderVisualizationCommand.CanExecute(visualizationName) == true;

        if (handled)
        {
            viewModel!.RenderVisualizationCommand.Execute(visualizationName);
        }

        return handled;
    }

    private void FocusConnectionsPanel(MainWindowViewModel viewModel)
    {
        if (navigationSplitView is not null)
        {
            navigationSplitView.IsPaneOpen = true;
            Classes.Set("explorerOpen", true);
        }

        viewModel.ShowQueryWorkbenchCommand.Execute(null);
        connectionsButton?.Classes.Add("selected");
        dashboardButton?.Classes.Remove("selected");
        automationButton?.Classes.Remove("selected");
        sessionsButton?.Classes.Remove("selected");
        graphButton?.Classes.Remove("selected");

        schemaSearch?.Focus();
    }

    private void FocusOutputPanel(MainWindowViewModel viewModel, int outputIndex)
    {
        viewModel.ShowQueryWorkbenchCommand.Execute(null);
        viewModel.SelectedOutputTabIndex = outputIndex;
        resultTabs?.Focus();
    }

    private void ToggleTheme()
    {
        if (appearanceSettings is not null)
        {
            appearanceSettings.ThemePreference = ActualThemeVariant == ThemeVariant.Dark
                ? ThemePreference.Light
                : ThemePreference.Dark;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs eventArguments)
    {
        UpdateResponsiveLayout(Bounds.Width);
    }

    private T FindRequiredControl<T>(string controlName)
        where T : Control
    {
        T control = this.FindControl<T>(controlName)
            ?? throw new InvalidOperationException($"The {controlName} control was not created by compiled XAML.");

        return control;
    }

    private List<Control> GetFocusRegions()
    {
        List<Control> focusRegions = [.. new Control?[]
        {
            newQueryButton,
            connectionsButton,
            dashboardButton,
            automationButton,
            graphButton,
            copilotExpandButton,
        }.OfType<Control>()];

        if (DataContext is MainWindowViewModel { IsGraphView: true })
        {
            focusRegions.AddRange(new Control?[]
            {
                graphSearch,
                graphCypherEditor,
            }.OfType<Control>());
        }

        if (navigationSplitView?.IsPaneOpen == true && schemaSearch is not null)
        {
            focusRegions.Add(schemaSearch);
        }

        if (DataContext is MainWindowViewModel { IsQueryWorkbenchView: true }
            && queryEditor is not null)
        {
            focusRegions.Add(queryEditor);
        }

        if (DataContext is MainWindowViewModel { Copilot.IsOpen: true }
            && copilotPrompt is not null)
        {
            focusRegions.Add(copilotPrompt);
        }

        focusRegions.AddRange(new Control?[]
        {
            documentTabs,
            resultTabs,
            appearanceButton,
            settingsButton,
        }.OfType<Control>());

        return focusRegions;
    }

    private void InitializeInteractiveControls()
    {
        appearanceButton = FindRequiredControl<Button>("AppearanceButton");
        automationButton = FindRequiredControl<Button>("AutomationButton");
        automationVisualizationSurface = FindRequiredControl<Grid>("AutomationVisualizationSurface");
        connectionsButton = FindRequiredControl<Button>("ConnectionsButton");
        conditionalColorPicker = FindRequiredControl<ColorPicker>("ConditionalColorPicker");
        copilotExpandButton = FindRequiredControl<Button>("CopilotExpandButton");
        copilotPanel = FindRequiredControl<Border>("CopilotPanel");
        copilotPrompt = FindRequiredControl<TextBox>("CopilotPrompt");
        darkThemeOption = FindRequiredControl<RadioButton>("DarkThemeOption");
        densityDescription = FindRequiredControl<TextBlock>("DensityDescription");
        densityToggle = FindRequiredControl<ToggleSwitch>("DensityToggle");
        dashboardButton = FindRequiredControl<Button>("DashboardButton");
        dashboardCanvas = FindRequiredControl<ItemsControl>("DashboardCanvas");
        documentTabs = FindRequiredControl<TabStrip>("DocumentTabs");
        graphButton = FindRequiredControl<Button>("GraphButton");
        graphCanvas = FindRequiredControl<KustoGraphControl>("GraphCanvas");
        graphCypherEditor = FindRequiredControl<TextBox>("GraphCypherEditor");
        graphSearch = FindRequiredControl<TextBox>("GraphSearch");
        graphViewport = FindRequiredControl<Grid>("GraphViewport");
        graphViewportToolbar = FindRequiredControl<Border>("GraphViewportToolbar");
        highContrastNotice = FindRequiredControl<Border>("HighContrastNotice");
        lightThemeOption = FindRequiredControl<RadioButton>("LightThemeOption");
        navigationSplitView = FindRequiredControl<SplitView>("NavigationSplitView");
        newQueryButton = FindRequiredControl<Button>("NewQueryButton");
        pertinentValuesSplitView = FindRequiredControl<SplitView>("PertinentValuesSplitView");
        queryEditor = FindRequiredControl<TextEditor>("QueryEditor");
        recordButton = FindRequiredControl<Button>("RecordButton");
        recordingNameTextBox = FindRequiredControl<TextBox>("RecordingNameTextBox");
        resultHorizontalOverflowHint = FindRequiredControl<Border>("ResultHorizontalOverflowHint");
        resultHorizontalScrollBar = FindRequiredControl<ScrollBar>("ResultHorizontalScrollBar");
        resultScrollViewer = FindRequiredControl<ScrollViewer>("ResultScrollViewer");
        resultTabs = FindRequiredControl<TabControl>("ResultTabs");
        resultTextZoomSlider = FindRequiredControl<Slider>("ResultTextZoomSlider");
        resultTextZoomValue = FindRequiredControl<TextBlock>("ResultTextZoomValue");
        resultVerticalScrollBar = FindRequiredControl<ScrollBar>("ResultVerticalScrollBar");
        resultView = FindRequiredControl<Grid>("ResultView");
        resultView.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnResultPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        resultsList = FindRequiredControl<ListBox>("ResultsList");
        schemaSearch = FindRequiredControl<TextBox>("SchemaSearch");
        sessionsButton = FindRequiredControl<Button>("SessionsButton");
        settingsButton = FindRequiredControl<Button>("SettingsButton");
        settingsAIProvider = FindRequiredControl<ComboBox>("SettingsAIProvider");
        settingsAzureOpenAIAuthentication = FindRequiredControl<ComboBox>(
            "SettingsAzureOpenAIAuthentication");
        settingsAzureOpenAIApiKeyEnvironmentVariable = FindRequiredControl<TextBox>(
            "SettingsAzureOpenAIApiKeyEnvironmentVariable");
        settingsAzureOpenAIApiKeyRow = FindRequiredControl<Grid>("SettingsAzureOpenAIApiKeyRow");
        settingsAzureOpenAIDeployment = FindRequiredControl<TextBox>("SettingsAzureOpenAIDeployment");
        settingsAzureOpenAIEndpoint = FindRequiredControl<TextBox>("SettingsAzureOpenAIEndpoint");
        settingsAzureOpenAIOptions = FindRequiredControl<StackPanel>("SettingsAzureOpenAIOptions");
        settingsCopilotAzureMcpToggle = FindRequiredControl<ToggleSwitch>("SettingsCopilotAzureMcpToggle");
        settingsCopilotAzureMcpRow = FindRequiredControl<Grid>("SettingsCopilotAzureMcpRow");
        settingsCopilotDefaultModel = FindRequiredControl<ComboBox>("SettingsCopilotDefaultModel");
        settingsCopilotDefaultModelRow = FindRequiredControl<Grid>("SettingsCopilotDefaultModelRow");
        settingsCopilotMicrosoftLearnMcpToggle = FindRequiredControl<ToggleSwitch>("SettingsCopilotMicrosoftLearnMcpToggle");
        settingsCopilotMicrosoftLearnMcpRow = FindRequiredControl<Grid>("SettingsCopilotMicrosoftLearnMcpRow");
        settingsCopilotShareResultDataToggle = FindRequiredControl<ToggleSwitch>("SettingsCopilotShareResultDataToggle");
        settingsCopilotShareSchemaToggle = FindRequiredControl<ToggleSwitch>("SettingsCopilotShareSchemaToggle");
        settingsCopilotShareTabContentToggle = FindRequiredControl<ToggleSwitch>("SettingsCopilotShareTabContentToggle");
        settingsDarkThemeOption = FindRequiredControl<RadioButton>("SettingsDarkThemeOption");
        settingsDensityToggle = FindRequiredControl<ToggleSwitch>("SettingsDensityToggle");
        settingsDialog = FindRequiredControl<Border>("SettingsDialog");
        settingsKqlHoverHelpToggle = FindRequiredControl<ToggleSwitch>("SettingsKqlHoverHelpToggle");
        settingsLightThemeOption = FindRequiredControl<RadioButton>("SettingsLightThemeOption");
        settingsOpenAIApiKeyEnvironmentVariable = FindRequiredControl<TextBox>(
            "SettingsOpenAIApiKeyEnvironmentVariable");
        settingsOpenAIEndpoint = FindRequiredControl<TextBox>("SettingsOpenAIEndpoint");
        settingsOpenAIModel = FindRequiredControl<TextBox>("SettingsOpenAIModel");
        settingsOpenAIOptions = FindRequiredControl<StackPanel>("SettingsOpenAIOptions");
        settingsResultTextZoomSlider = FindRequiredControl<Slider>("SettingsResultTextZoomSlider");
        settingsResultTextZoomValue = FindRequiredControl<TextBlock>("SettingsResultTextZoomValue");
        settingsSystemThemeOption = FindRequiredControl<RadioButton>("SettingsSystemThemeOption");
        settingsTextZoomSlider = FindRequiredControl<Slider>("SettingsTextZoomSlider");
        settingsTextZoomValue = FindRequiredControl<TextBlock>("SettingsTextZoomValue");
        signedInUsersList = FindRequiredControl<ItemsControl>("SignedInUsersList");
        systemThemeOption = FindRequiredControl<RadioButton>("SystemThemeOption");
        tabSearchBox = FindRequiredControl<TextBox>("TabSearchBox");
        textZoomSlider = FindRequiredControl<Slider>("TextZoomSlider");
        textZoomValue = FindRequiredControl<TextBlock>("TextZoomValue");
        updateButton = FindRequiredControl<Button>("UpdateButton");
        visualizationSurface = FindRequiredControl<Grid>("VisualizationSurface");
    }

    private void CopySelectedResultValues(MainWindowViewModel viewModel)
    {
        CopySelectedResultValues(viewModel, GetSelectedResultRows());
    }

    private void CopySelectedResultValues(
        MainWindowViewModel viewModel,
        ReadOnlyCollection<KustoResultRowViewModel> selectedRows)
    {
        int valueCount = Math.Max(1, selectedRows.Count);
        _ = CopyTextAsync(
            viewModel.CreateSelectedResultValues(selectedRows),
            valueCount == 1 ? "Copied result value" : $"Copied {valueCount:N0} result values");
    }

    private ReadOnlyCollection<KustoResultRowViewModel> GetSelectedResultRows()
    {
        HashSet<KustoResultRowViewModel> selectedRows = resultsList?.SelectedItems?
            .OfType<KustoResultRowViewModel>()
            .ToHashSet() ?? [];
        KustoResultRowViewModel[] rows = DataContext is MainWindowViewModel viewModel
            ? viewModel.ResultRows.Where(selectedRows.Contains).ToArray()
            : selectedRows.OrderBy(row => row.RowIndex).ToArray();
        return Array.AsReadOnly(rows);
    }

    private void ExecuteResultContextCommand(
        object? sender,
        Action<MainWindowViewModel> executeAction)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (sender is MenuItem { DataContext: KustoResultCellViewModel cell })
            {
                viewModel.SetResultContext(cell);
            }

            executeAction(viewModel);
        }
    }

    private async Task CopyTextAsync(string text, string successMessage)
    {
        try
        {
            DataTransfer transfer = new();
            transfer.Add(DataTransferItem.CreateText(text));
            IClipboard clipboard = Clipboard
                ?? throw new InvalidOperationException("The system clipboard is unavailable.");
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
            ReportDesktopStatus(successMessage);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            ReportDesktopStatus($"Copy failed: {exception.Message}");
        }
    }

    private async Task ExportResultsAsync(KustoResultExportFormat format)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                KustoResultExportFile export = viewModel.CreateResultExport(format);
                FilePickerFileType fileType = new(format.ToString())
                {
                    Patterns = [$"*{Path.GetExtension(export.SuggestedFileName)}"],
                    MimeTypes = [export.ContentType],
                };
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Export {format}",
                    SuggestedFileName = export.SuggestedFileName,
                    DefaultExtension = Path.GetExtension(export.SuggestedFileName).TrimStart('.'),
                    SuggestedFileType = fileType,
                    FileTypeChoices = [fileType],
                    ShowOverwritePrompt = true,
                });

                if (file is not null)
                {
                    await using Stream stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    await stream.WriteAsync(export.Content, automationCancellationSource.Token);
                    viewModel.ReportActionStatus($"Exported {format}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                viewModel.ReportActionStatus($"Export failed: {exception.Message}");
            }
        }
    }

    private async Task ExportPertinentValuesCsvAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                KustoResultExportFile export = viewModel.Recording.CreatePertinentValuesCsvExport();
                FilePickerFileType fileType = new("CSV")
                {
                    Patterns = ["*.csv"],
                    MimeTypes = [export.ContentType],
                };
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export pertinent values",
                    SuggestedFileName = export.SuggestedFileName,
                    DefaultExtension = "csv",
                    SuggestedFileType = fileType,
                    FileTypeChoices = [fileType],
                    ShowOverwritePrompt = true,
                });

                if (file is not null)
                {
                    await using Stream stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    await stream.WriteAsync(export.Content, automationCancellationSource.Token);
                    viewModel.ReportActionStatus("Exported pertinent values");
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
            {
                viewModel.ReportActionStatus($"Export failed: {exception.Message}");
            }
        }
    }

    private void BeginDashboardWidgetInteraction(
        object? sender,
        PointerPressedEventArgs eventArguments,
        bool isResize)
    {
        if (sender is Control { DataContext: KustoDashboardWidgetViewModel widget } control
            && dashboardCanvas is not null
            && eventArguments.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            dashboardInteractionControl = control;
            dashboardInteractionWidget = widget;
            dashboardInteractionOrigin = eventArguments.GetPosition(dashboardCanvas);
            dashboardInteractionStartColumn = widget.Column;
            dashboardInteractionStartRow = widget.Row;
            dashboardInteractionStartColumnSpan = widget.ColumnSpan;
            dashboardInteractionStartRowSpan = widget.RowSpan;
            isDashboardResize = isResize;
            eventArguments.Pointer.Capture(control);
            eventArguments.Handled = true;
        }
    }

    private async Task ExportDashboardAsync()
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.Dashboard.SelectedDashboard is KustoDashboardViewModel dashboard)
        {
            try
            {
                FilePickerFileType fileType = new("Dashboard JSON")
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"],
                };
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export dashboard",
                    SuggestedFileName = $"{dashboard.Title}.json",
                    DefaultExtension = "json",
                    SuggestedFileType = fileType,
                    FileTypeChoices = [fileType],
                    ShowOverwritePrompt = true,
                });

                if (file is not null)
                {
                    await using Stream stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    viewModel.Dashboard.ExportSelected(stream);
                    viewModel.ReportActionStatus("Exported dashboard JSON");
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
            {
                viewModel.ReportActionStatus($"Dashboard export failed: {exception.Message}");
            }
        }
    }

    private async Task ImportDashboardAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                FilePickerFileType fileType = new("Dashboard JSON")
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"],
                };
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Import dashboard",
                        AllowMultiple = false,
                        FileTypeFilter = [fileType],
                    });

                if (files.Count == 1)
                {
                    await using Stream stream = await files[0].OpenReadAsync();
                    KustoDashboardViewModel dashboard = viewModel.Dashboard.Import(stream);
                    viewModel.ShowDashboardsCommand.Execute(null);
                    viewModel.ReportActionStatus($"Imported {dashboard.Title}");
                    await viewModel.Dashboard.RefreshSelectedAsync(
                        DateTimeOffset.UtcNow,
                        automationCancellationSource.Token);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or ArgumentException)
            {
                viewModel.ReportActionStatus($"Dashboard import failed: {exception.Message}");
            }
        }
    }

    private async Task SaveChartAsync(Control? chartSurface)
    {
        if (chartSurface is not null && DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                FilePickerFileType fileType = new("PNG image")
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"],
                };
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save chart",
                    SuggestedFileName = "kusto-chart.png",
                    DefaultExtension = "png",
                    SuggestedFileType = fileType,
                    FileTypeChoices = [fileType],
                    ShowOverwritePrompt = true,
                });

                if (file is not null)
                {
                    PixelSize pixelSize = new(
                        Math.Max(1, (int)Math.Ceiling(chartSurface.Bounds.Width)),
                        Math.Max(1, (int)Math.Ceiling(chartSurface.Bounds.Height)));
                    using RenderTargetBitmap bitmap = new(pixelSize, new Vector(96, 96));
                    bitmap.Render(chartSurface);
                    await using Stream stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                    viewModel.ReportActionStatus("Saved chart image");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                viewModel.ReportActionStatus($"Chart save failed: {exception.Message}");
            }
        }
    }

    private async Task SaveGraphPngAsync()
    {
        if (graphViewport is null || graphCanvas?.CreateExportLayout()?.IsEmpty != false)
        {
            return;
        }

        try
        {
            FilePickerFileType fileType = new("PNG image")
            {
                Patterns = ["*.png"],
                MimeTypes = ["image/png"],
            };
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save graph as PNG",
                SuggestedFileName = "kusto-graph.png",
                DefaultExtension = "png",
                SuggestedFileType = fileType,
                FileTypeChoices = [fileType],
                ShowOverwritePrompt = true,
            });

            if (file is not null)
            {
                PixelSize pixelSize = new(
                    Math.Max(1, (int)Math.Ceiling(graphViewport.Bounds.Width)),
                    Math.Max(1, (int)Math.Ceiling(graphViewport.Bounds.Height)));
                using RenderTargetBitmap bitmap = new(pixelSize, new Vector(96, 96));
                RenderGraphViewport(bitmap);
                await using Stream stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                ReportDesktopStatus("Saved graph image");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ReportDesktopStatus($"Graph image save failed: {exception.Message}");
        }
    }

    private void RenderGraphViewport(RenderTargetBitmap bitmap)
    {
        if (graphViewport is null)
        {
            return;
        }

        bool toolbarWasVisible = graphViewportToolbar?.IsVisible == true;
        if (graphViewportToolbar is not null)
        {
            graphViewportToolbar.IsVisible = false;
        }

        try
        {
            bitmap.Render(graphViewport);
        }
        finally
        {
            if (graphViewportToolbar is not null)
            {
                graphViewportToolbar.IsVisible = toolbarWasVisible;
            }
        }
    }

    private async Task SaveGraphMlAsync()
    {
        GraphLayout? graphLayout = graphCanvas?.CreateExportLayout();
        if (graphLayout is null || graphLayout.IsEmpty)
        {
            return;
        }

        try
        {
            FilePickerFileType fileType = new("GraphML graph")
            {
                Patterns = ["*.graphml"],
                MimeTypes = ["application/graphml+xml"],
            };
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save graph as GraphML",
                SuggestedFileName = "kusto-graph.graphml",
                DefaultExtension = "graphml",
                SuggestedFileType = fileType,
                FileTypeChoices = [fileType],
                ShowOverwritePrompt = true,
            });

            if (file is not null)
            {
                byte[] content = KustoGraphMlSerializer.Serialize(graphLayout);
                await using Stream stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(content, automationCancellationSource.Token);
                ReportDesktopStatus("Saved graph as GraphML");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ReportDesktopStatus($"GraphML save failed: {exception.Message}");
        }
    }

    private void ReportDesktopStatus(string message)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ReportActionStatus(message);
        }
    }

    private void MoveFocusRegion(bool reverse)
    {
        List<Control> focusRegions = GetFocusRegions();
        if (focusRegions.Count > 0)
        {
            if (focusRegionIndex < 0 || focusRegionIndex >= focusRegions.Count)
            {
                focusRegionIndex = reverse ? 0 : -1;
            }

            int direction = reverse ? -1 : 1;
            focusRegionIndex = (focusRegionIndex + direction + focusRegions.Count) % focusRegions.Count;
            FocusRegion(focusRegions[focusRegionIndex]);
        }
    }

    private void FocusRegion(Control focusRegion)
    {
        if (ReferenceEquals(focusRegion, queryEditor) && queryEditor is not null)
        {
            queryEditor.TextArea.Focus();
        }
        else if (ReferenceEquals(focusRegion, resultTabs) && resultTabs?.SelectedItem is Control selectedTab)
        {
            selectedTab.Focus();
        }
        else
        {
            focusRegion.Focus();
        }
    }

    private void UpdateAppearanceClasses()
    {
        if (appearanceSettings is not null)
        {
            Classes.Set("comfortable", appearanceSettings.Density == WorkbenchDensity.Comfortable);
            Classes.Set("compact", appearanceSettings.Density == WorkbenchDensity.Compact);
            Classes.Set("highContrast", appearanceSettings.IsHighContrast);
            UpdateAppearanceControls();
            UpdateHighContrastResources();
        }
    }

    private void UpdateAppearanceControls()
    {
        if (appearanceSettings is not null)
        {
            UpdateDensityControls(appearanceSettings.Density);
            UpdateCopilotDefaultControls();
            UpdateHighContrastNotice(appearanceSettings.IsHighContrast);
            UpdateResultTextZoomControls(appearanceSettings.ResultTextZoomPercentage);
            UpdateTextZoomControls(appearanceSettings.TextZoomPercentage);
            UpdateThemeControls(appearanceSettings.ThemePreference);

            if (settingsKqlHoverHelpToggle is not null)
            {
                settingsKqlHoverHelpToggle.IsChecked = appearanceSettings.ShowKqlHoverHelp;
            }
        }
    }

    private void UpdateDensityControls(WorkbenchDensity density)
    {
        if (densityToggle is not null)
        {
            densityToggle.IsChecked = density == WorkbenchDensity.Comfortable;
        }

        if (settingsDensityToggle is not null)
        {
            settingsDensityToggle.IsChecked = density == WorkbenchDensity.Comfortable;
        }

        if (densityDescription is not null)
        {
            densityDescription.Text = density == WorkbenchDensity.Comfortable
                ? "Larger controls and rows"
                : "Compact controls and rows";
        }
    }

    private void UpdateTextZoomControls(int textZoomPercentage)
    {
        if (textZoomSlider is not null)
        {
            textZoomSlider.Value = textZoomPercentage;
        }

        if (settingsTextZoomSlider is not null)
        {
            settingsTextZoomSlider.Value = textZoomPercentage;
        }

        string valueText = $"{textZoomPercentage}%";
        if (textZoomValue is not null)
        {
            textZoomValue.Text = valueText;
        }

        if (settingsTextZoomValue is not null)
        {
            settingsTextZoomValue.Text = valueText;
        }
    }

    private void UpdateResultTextZoomControls(int textZoomPercentage)
    {
        if (resultTextZoomSlider is not null)
        {
            resultTextZoomSlider.Value = textZoomPercentage;
        }

        if (settingsResultTextZoomSlider is not null)
        {
            settingsResultTextZoomSlider.Value = textZoomPercentage;
        }

        string valueText = $"{textZoomPercentage}%";
        if (resultTextZoomValue is not null)
        {
            resultTextZoomValue.Text = valueText;
        }

        if (settingsResultTextZoomValue is not null)
        {
            settingsResultTextZoomValue.Text = valueText;
        }
    }

    private void UpdateCopilotDefaultControls()
    {
        if (appearanceSettings is not null)
        {
            suppressSettingsAIProviderChange = true;
            try
            {
                settingsAIProvider!.SelectedIndex = (int)appearanceSettings.ProviderKind;
            }
            finally
            {
                suppressSettingsAIProviderChange = false;
            }

            bool usesAzureOpenAI = appearanceSettings.ProviderKind == KustoAIProviderKind.AzureOpenAI;
            bool usesOpenAI = appearanceSettings.ProviderKind == KustoAIProviderKind.OpenAI;
            bool usesGitHubCopilot = appearanceSettings.ProviderKind == KustoAIProviderKind.GitHubCopilot;
            settingsAzureOpenAIOptions!.IsVisible = usesAzureOpenAI;
            settingsOpenAIOptions!.IsVisible = usesOpenAI;
            settingsCopilotDefaultModelRow!.IsVisible = usesGitHubCopilot;
            settingsCopilotMicrosoftLearnMcpRow!.IsVisible = usesGitHubCopilot;
            settingsCopilotAzureMcpRow!.IsVisible = usesGitHubCopilot;
            settingsAzureOpenAIEndpoint!.Text = appearanceSettings.AzureOpenAIEndpoint;
            settingsAzureOpenAIDeployment!.Text = appearanceSettings.AzureOpenAIDeployment;
            UpdateAzureOpenAIAuthenticationControls();
            settingsAzureOpenAIApiKeyEnvironmentVariable!.Text =
                appearanceSettings.AzureOpenAIApiKeyEnvironmentVariable;
            settingsOpenAIEndpoint!.Text = appearanceSettings.OpenAIEndpoint;
            settingsOpenAIModel!.Text = appearanceSettings.OpenAIModel;
            settingsOpenAIApiKeyEnvironmentVariable!.Text = appearanceSettings.OpenAIApiKeyEnvironmentVariable;

            if (settingsCopilotDefaultModel is not null && DataContext is MainWindowViewModel viewModel)
            {
                KustoCopilotModel? selectedModel = viewModel.Copilot.Models.FirstOrDefault(model => string.Equals(
                    model.Id,
                    appearanceSettings.CopilotDefaultModel.Id,
                    StringComparison.Ordinal));
                suppressSettingsCopilotDefaultModelChange = true;

                try
                {
                    settingsCopilotDefaultModel.SelectedItem = selectedModel;
                }
                finally
                {
                    suppressSettingsCopilotDefaultModelChange = false;
                }
            }

            settingsCopilotShareTabContentToggle!.IsChecked = appearanceSettings.CopilotShareTabContentByDefault;
            settingsCopilotShareSchemaToggle!.IsChecked = appearanceSettings.CopilotShareSchemaByDefault;
            settingsCopilotShareResultDataToggle!.IsChecked = appearanceSettings.CopilotShareResultDataByDefault;
            settingsCopilotMicrosoftLearnMcpToggle!.IsChecked = appearanceSettings.CopilotEnableMicrosoftLearnMcpByDefault;
            settingsCopilotAzureMcpToggle!.IsChecked = appearanceSettings.CopilotEnableAzureMcpByDefault;
            settingsCopilotAzureMcpToggle.IsEnabled = appearanceSettings.CanEnableAzureMcpByDefault;
        }
    }

    private void ApplyAIProviderSettingsFromControls()
    {
        if (appearanceSettings is not null)
        {
            appearanceSettings.AzureOpenAIEndpoint = settingsAzureOpenAIEndpoint?.Text ?? string.Empty;
            appearanceSettings.AzureOpenAIDeployment = settingsAzureOpenAIDeployment?.Text ?? string.Empty;
            appearanceSettings.AzureOpenAIApiKeyEnvironmentVariable =
                settingsAzureOpenAIApiKeyEnvironmentVariable?.Text ?? string.Empty;
            appearanceSettings.OpenAIEndpoint = settingsOpenAIEndpoint?.Text ?? string.Empty;
            appearanceSettings.OpenAIModel = settingsOpenAIModel?.Text ?? string.Empty;
            appearanceSettings.OpenAIApiKeyEnvironmentVariable =
                settingsOpenAIApiKeyEnvironmentVariable?.Text ?? string.Empty;
        }
    }

    private void UpdateAzureOpenAIAuthenticationControls()
    {
        if (appearanceSettings is not null)
        {
            suppressSettingsAzureOpenAIAuthenticationChange = true;
            try
            {
                settingsAzureOpenAIAuthentication!.SelectedIndex =
                    (int)appearanceSettings.AzureOpenAIAuthenticationKind;
            }
            finally
            {
                suppressSettingsAzureOpenAIAuthenticationChange = false;
            }

            settingsAzureOpenAIApiKeyRow!.IsVisible =
                appearanceSettings.AzureOpenAIAuthenticationKind
                == KustoAzureOpenAIAuthenticationKind.ApiKey;
        }
    }

    private void UpdateHighContrastNotice(bool isHighContrast)
    {
        if (highContrastNotice is not null)
        {
            highContrastNotice.IsVisible = isHighContrast;
        }
    }

    private void UpdateThemeControls(ThemePreference themePreference)
    {
        if (systemThemeOption is not null)
        {
            systemThemeOption.IsChecked = themePreference == ThemePreference.System;
        }

        if (lightThemeOption is not null)
        {
            lightThemeOption.IsChecked = themePreference == ThemePreference.Light;
        }

        if (darkThemeOption is not null)
        {
            darkThemeOption.IsChecked = themePreference == ThemePreference.Dark;
        }

        if (settingsSystemThemeOption is not null)
        {
            settingsSystemThemeOption.IsChecked = themePreference == ThemePreference.System;
        }

        if (settingsLightThemeOption is not null)
        {
            settingsLightThemeOption.IsChecked = themePreference == ThemePreference.Light;
        }

        if (settingsDarkThemeOption is not null)
        {
            settingsDarkThemeOption.IsChecked = themePreference == ThemePreference.Dark;
        }
    }

    private void UpdateHighContrastResources()
    {
        bool useHighContrast = appearanceSettings?.IsHighContrast == true;
        bool useDarkColors = ActualThemeVariant == ThemeVariant.Dark;

        foreach ((string resourceKey, string lightColor, string darkColor) in HighContrastBrushSpecifications)
        {
            if (useHighContrast)
            {
                string colorText = useDarkColors ? darkColor : lightColor;
                Resources[resourceKey] = new SolidColorBrush(Color.Parse(colorText));
            }
            else
            {
                Resources.Remove(resourceKey);
            }
        }

        queryEditor?.TextArea.TextView.Redraw();
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (navigationSplitView is not null)
        {
            bool useNarrowLayout = width < CompactLayoutBreakpoint;
            if (isNarrowLayout != useNarrowLayout)
            {
                isNarrowLayout = useNarrowLayout;
                navigationSplitView.DisplayMode = useNarrowLayout
                    ? SplitViewDisplayMode.Overlay
                    : SplitViewDisplayMode.Inline;
                navigationSplitView.IsPaneOpen = !useNarrowLayout;
                Classes.Set("explorerOpen", navigationSplitView.IsPaneOpen);
            }
        }

        if (copilotPanel is not null)
        {
            bool useOverlay = width < CompactLayoutBreakpoint;
            Grid.SetColumn(copilotPanel, useOverlay ? 1 : 2);
            copilotPanel.HorizontalAlignment = useOverlay
                ? Avalonia.Layout.HorizontalAlignment.Right
                : Avalonia.Layout.HorizontalAlignment.Stretch;
            copilotPanel.ZIndex = useOverlay ? 20 : 0;
        }

        UpdatePertinentValuesLayout(width);
    }

    private void UpdatePertinentValuesLayout(double width)
    {
        if (pertinentValuesSplitView is not null)
        {
            SplitViewDisplayMode displayMode = width < CompactLayoutBreakpoint
                ? SplitViewDisplayMode.Overlay
                : SplitViewDisplayMode.Inline;
            if (pertinentValuesSplitView.DisplayMode != displayMode)
            {
                pertinentValuesSplitView.DisplayMode = displayMode;
                pertinentValuesSplitView.IsPaneOpen = displayMode == SplitViewDisplayMode.Inline;
            }
        }
    }
}
