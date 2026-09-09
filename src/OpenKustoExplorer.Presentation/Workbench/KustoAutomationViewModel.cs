using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one scheduled query, mutable scheduler state, and bounded run history.
/// </summary>
public sealed class KustoAutomationViewModel : ObservableObject
{
    private const int MaximumRunHistory = 100;

    private readonly DateTimeOffset createdAtUtc;
    private readonly Action<KustoAutomationViewModel> stateChangedAction;
    private bool isEnabled;
    private bool isRunning;
    private string name;
    private KustoAutomationNotificationSettings notificationSettings;
    private DateTimeOffset schedulerUtcNow = DateTimeOffset.UtcNow;
    private DateTimeOffset nextRunAtUtc;
    private KustoAutomationRunViewModel? selectedRun;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationViewModel"/> class.
    /// </summary>
    /// <param name="automation">The immutable persisted automation.</param>
    /// <param name="fallbackVisualizationFactory">Creates render instructions recovered from scheduled KQL.</param>
    /// <param name="deleteAction">Deletes this automation.</param>
    /// <param name="stateChangedAction">Persists a scheduler state change.</param>
    /// <param name="runNowAction">Executes this automation immediately.</param>
    /// <param name="renameAction">Opens automation renaming when supplied.</param>
    /// <param name="configureNotificationsAction">Opens notification settings when supplied.</param>
    internal KustoAutomationViewModel(
        KustoAutomation automation,
        Func<KustoVisualization?> fallbackVisualizationFactory,
        Action<KustoAutomationViewModel> deleteAction,
        Action<KustoAutomationViewModel> stateChangedAction,
        Func<KustoAutomationViewModel, Task> runNowAction,
        Action<KustoAutomationViewModel>? renameAction = null,
        Action<KustoAutomationViewModel>? configureNotificationsAction = null)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(fallbackVisualizationFactory);
        ArgumentNullException.ThrowIfNull(deleteAction);
        ArgumentNullException.ThrowIfNull(stateChangedAction);
        ArgumentNullException.ThrowIfNull(runNowAction);

        this.stateChangedAction = stateChangedAction;
        Id = automation.Id;
        name = automation.Name;
        notificationSettings = automation.NotificationSettings;
        ClusterUri = automation.ClusterUri;
        DatabaseName = automation.DatabaseName;
        QueryText = automation.QueryText;
        Interval = automation.Interval;
        createdAtUtc = automation.CreatedAtUtc;
        nextRunAtUtc = automation.NextRunAtUtc;
        StopAtUtc = automation.StopAtUtc;
        isEnabled = automation.IsEnabled;
        Lazy<KustoVisualization?> fallbackVisualization = new(fallbackVisualizationFactory);
        Runs = new ObservableCollection<KustoAutomationRunViewModel>(
            automation.Runs
                .Reverse()
                .Select(run => new KustoAutomationRunViewModel(run, fallbackVisualization)));
        UpdateRunDeltas();
        SelectedRun = Runs.FirstOrDefault();
        DeleteCommand = new RelayCommand(() => deleteAction(this));
        RunNowCommand = new AsyncRelayCommand(() => runNowAction(this), () => !IsRunning);
        RenameCommand = new RelayCommand(() => renameAction?.Invoke(this));
        ConfigureNotificationsCommand = new RelayCommand(() => configureNotificationsAction?.Invoke(this));
    }

    /// <summary>
    /// Gets the stable automation identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the user-defined automation name.
    /// </summary>
    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    /// <summary>
    /// Gets the persisted notification settings.
    /// </summary>
    public KustoAutomationNotificationSettings NotificationSettings => notificationSettings;

    /// <summary>
    /// Gets a concise notification-channel summary.
    /// </summary>
    public string NotificationSummary
    {
        get
        {
            List<string> channels = [];
            if (NotificationSettings.DesktopEnabled)
            {
                channels.Add("Desktop");
            }

            if (NotificationSettings.EmailEnabled)
            {
                channels.Add("Email");
            }

            if (NotificationSettings.RunApplicationEnabled)
            {
                channels.Add("Application");
            }

            return channels.Count == 0
                ? "Actions off"
                : $"{string.Join(" + ", channels)} actions";
        }
    }

    /// <summary>
    /// Gets the target cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the target database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the independent scheduled KQL block.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the recurrence interval.
    /// </summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Gets the optional UTC schedule end.
    /// </summary>
    public DateTimeOffset? StopAtUtc { get; }

    /// <summary>
    /// Gets completed runs ordered newest first.
    /// </summary>
    public ObservableCollection<KustoAutomationRunViewModel> Runs { get; }

    /// <summary>
    /// Gets or sets the selected historical run.
    /// </summary>
    public KustoAutomationRunViewModel? SelectedRun
    {
        get => selectedRun;
        set => SetProperty(ref selectedRun, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether future runs are enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(NextRunText));
                stateChangedAction(this);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this automation is currently running.
    /// </summary>
    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (SetProperty(ref isRunning, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(NextRunText));
                RunNowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the next planned UTC run time.
    /// </summary>
    public DateTimeOffset NextRunAtUtc
    {
        get => nextRunAtUtc;
        private set
        {
            if (SetProperty(ref nextRunAtUtc, value))
            {
                OnPropertyChanged(nameof(NextRunText));
            }
        }
    }

    /// <summary>
    /// Gets a concise target display string.
    /// </summary>
    public string TargetText => $"{ClusterUri.Host} / {DatabaseName}";

    /// <summary>
    /// Gets a concise recurrence description.
    /// </summary>
    public string ScheduleText => $"Every {FormatInterval(Interval)}";

    /// <summary>
    /// Gets a concise schedule-end description.
    /// </summary>
    public string StopText => StopAtUtc is null
        ? "No automatic stop"
        : $"Stops {StopAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Gets the next local run time or disabled state.
    /// </summary>
    public string NextRunText
    {
        get
        {
            string text = "Paused";

            if (IsRunning)
            {
                text = "Running now";
            }
            else if (IsEnabled)
            {
                text = FormatNextRun(NextRunAtUtc - schedulerUtcNow);
            }

            return text;
        }
    }

    /// <summary>
    /// Gets the current scheduler state.
    /// </summary>
    public string StatusText
    {
        get
        {
            string status = "Paused";

            if (IsRunning)
            {
                status = "Running";
            }
            else if (IsEnabled)
            {
                status = "Scheduled";
            }

            return status;
        }
    }

    /// <summary>
    /// Gets the command that deletes this automation and its history.
    /// </summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>
    /// Gets the command that executes this automation immediately.
    /// </summary>
    public IAsyncRelayCommand RunNowCommand { get; }

    /// <summary>
    /// Gets the command that opens automation renaming.
    /// </summary>
    public IRelayCommand RenameCommand { get; }

    /// <summary>
    /// Gets the command that opens notification configuration.
    /// </summary>
    public IRelayCommand ConfigureNotificationsCommand { get; }

    /// <summary>
    /// Determines whether this automation is due at the supplied UTC time.
    /// </summary>
    /// <param name="utcNow">The scheduler's UTC clock.</param>
    /// <returns><see langword="true"/> when execution should begin.</returns>
    internal bool IsDue(DateTimeOffset utcNow)
    {
        bool scheduledBeforeStop = StopAtUtc is null || NextRunAtUtc < StopAtUtc;
        return IsEnabled && !IsRunning && scheduledBeforeStop && NextRunAtUtc <= utcNow;
    }

    /// <summary>
    /// Disables an expired schedule.
    /// </summary>
    /// <param name="utcNow">The scheduler's UTC clock.</param>
    /// <returns><see langword="true"/> when the enabled state changed.</returns>
    internal bool DisableIfExpired(DateTimeOffset utcNow)
    {
        bool changed = IsEnabled && StopAtUtc is not null && utcNow >= StopAtUtc;

        if (changed)
        {
            isEnabled = false;
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(NextRunText));
        }

        return changed;
    }

    /// <summary>
    /// Marks execution as active.
    /// </summary>
    internal void BeginRun()
    {
        IsRunning = true;
    }

    /// <summary>
    /// Refreshes the relative next-run display against the scheduler clock.
    /// </summary>
    /// <param name="utcNow">The scheduler's UTC clock.</param>
    internal void RefreshNextRunText(DateTimeOffset utcNow)
    {
        schedulerUtcNow = utcNow.ToUniversalTime();
        OnPropertyChanged(nameof(NextRunText));
    }

    /// <summary>
    /// Adds a completed run and advances the recurrence schedule.
    /// </summary>
    /// <param name="run">The completed run presentation.</param>
    /// <param name="completedAtUtc">The UTC completion time.</param>
    internal void CompleteRun(KustoAutomationRunViewModel run, DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.SetPreviousRun(Runs.FirstOrDefault(candidate =>
            candidate.Status == KustoAutomationRunStatus.Succeeded));
        Runs.Insert(0, run);
        while (Runs.Count > MaximumRunHistory)
        {
            Runs.RemoveAt(Runs.Count - 1);
        }

        SelectedRun = run;
        DateTimeOffset nextRun = NextRunAtUtc;
        do
        {
            nextRun = nextRun.Add(Interval);
        }
        while (nextRun <= completedAtUtc);

        NextRunAtUtc = nextRun;
        IsRunning = false;
        RefreshNextRunText(completedAtUtc);
        DisableIfExpired(completedAtUtc);
    }

    /// <summary>
    /// Replaces the user-defined automation name.
    /// </summary>
    /// <param name="newName">The validated new name.</param>
    internal void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
    }

    /// <summary>
    /// Replaces persisted notification criteria and channels.
    /// </summary>
    /// <param name="settings">The validated notification settings.</param>
    internal void SetNotificationSettings(KustoAutomationNotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        notificationSettings = settings;
        OnPropertyChanged(nameof(NotificationSettings));
        OnPropertyChanged(nameof(NotificationSummary));
    }

    /// <summary>
    /// Creates the immutable persistence snapshot.
    /// </summary>
    /// <returns>The current automation and bounded run history.</returns>
    internal KustoAutomation CreateAutomation()
    {
        IEnumerable<KustoAutomationRun> runs = Runs
            .Reverse()
            .Select(run => run.CreateRun());
        return new KustoAutomation(
            Id,
            Name,
            ClusterUri,
            DatabaseName,
            QueryText,
            Interval,
            createdAtUtc,
            NextRunAtUtc,
            StopAtUtc,
            IsEnabled,
            runs,
            NotificationSettings);
    }

    private static string FormatInterval(TimeSpan interval)
    {
        string text;

        if (interval >= TimeSpan.FromDays(1) && interval.Ticks % TimeSpan.TicksPerDay == 0)
        {
            text = $"{interval.Days:N0} days";
        }
        else if (interval >= TimeSpan.FromHours(1) && interval.Ticks % TimeSpan.TicksPerHour == 0)
        {
            text = $"{interval.TotalHours:N0} hours";
        }
        else
        {
            text = $"{interval.TotalMinutes:N0} minutes";
        }

        return text;
    }

    private static string FormatNextRun(TimeSpan remaining)
    {
        string text;

        if (remaining <= TimeSpan.Zero)
        {
            text = "Due now";
        }
        else if (remaining < TimeSpan.FromMinutes(1))
        {
            int seconds = Math.Max(1, (int)Math.Floor(remaining.TotalSeconds));
            text = $"Next in {seconds:N0}s";
        }
        else if (remaining < TimeSpan.FromHours(1))
        {
            int minutes = Math.Max(1, (int)Math.Floor(remaining.TotalMinutes));
            text = $"Next in {minutes:N0}m";
        }
        else if (remaining < TimeSpan.FromDays(1))
        {
            int hours = (int)Math.Floor(remaining.TotalHours);
            int minutes = remaining.Minutes;
            text = minutes == 0
                ? $"Next in {hours:N0}h"
                : $"Next in {hours:N0}h {minutes:N0}m";
        }
        else
        {
            int days = (int)Math.Floor(remaining.TotalDays);
            int hours = remaining.Hours;
            text = hours == 0
                ? $"Next in {days:N0}d"
                : $"Next in {days:N0}d {hours:N0}h";
        }

        return text;
    }

    private void UpdateRunDeltas()
    {
        for (int index = 0; index < Runs.Count; index++)
        {
            KustoAutomationRunViewModel? previousRun = Runs
                .Skip(index + 1)
                .FirstOrDefault(candidate => candidate.Status == KustoAutomationRunStatus.Succeeded);
            Runs[index].SetPreviousRun(previousRun);
        }
    }
}
