using System.Net.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Automations;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Edits persisted notification criteria, channels, and templates for one automation.
/// </summary>
public sealed class KustoAutomationNotificationEditorViewModel : ObservableObject
{
    private static readonly string[] SupportedTemplateHelpers =
    [
        "{row_count}",
        "{name}",
        "{query}",
        "{rows_changed}",
    ];

    private readonly Action<KustoAutomationViewModel> savedAction;
    private string automationName = string.Empty;
    private bool desktopEnabled;
    private string applicationArguments = string.Empty;
    private string applicationPath = string.Empty;
    private string emailRecipient = string.Empty;
    private string emailSender = string.Empty;
    private bool emailEnabled;
    private string errorText = string.Empty;
    private bool isOpen;
    private string messageTemplate = KustoAutomationNotificationSettings.DefaultMessageTemplate;
    private bool notifyWhenRowCountChanges;
    private KustoAutomationRowCountComparison rowCountComparison;
    private double rowCountValue;
    private bool runApplicationEnabled;
    private string smtpHost = string.Empty;
    private double smtpPort = 587;
    private bool smtpUseSsl = true;
    private string subjectTemplate = KustoAutomationNotificationSettings.DefaultSubjectTemplate;
    private KustoAutomationViewModel? target;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationNotificationEditorViewModel"/> class.
    /// </summary>
    /// <param name="savedAction">Persists a successfully updated automation.</param>
    public KustoAutomationNotificationEditorViewModel(Action<KustoAutomationViewModel> savedAction)
    {
        ArgumentNullException.ThrowIfNull(savedAction);
        this.savedAction = savedAction;
        RowCountComparisonOptions = Array.AsReadOnly(Enum.GetValues<KustoAutomationRowCountComparison>());
        CloseCommand = new RelayCommand(Close);
        SaveCommand = new RelayCommand(Save);
        AppendMessageHelperCommand = new RelayCommand<string>(AppendMessageHelper);
    }

    /// <summary>
    /// Gets available fixed row-count comparisons.
    /// </summary>
    public IReadOnlyList<KustoAutomationRowCountComparison> RowCountComparisonOptions { get; }

    /// <summary>
    /// Gets the command that closes the editor without applying changes.
    /// </summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>
    /// Gets the command that validates and saves notification settings.
    /// </summary>
    public IRelayCommand SaveCommand { get; }

    /// <summary>
    /// Gets the command that appends a supported helper to the message template.
    /// </summary>
    public IRelayCommand<string> AppendMessageHelperCommand { get; }

    /// <summary>
    /// Gets the automation name shown in the editor heading.
    /// </summary>
    public string AutomationName
    {
        get => automationName;
        private set => SetProperty(ref automationName, value);
    }

    /// <summary>
    /// Gets a value indicating whether the editor is open.
    /// </summary>
    public bool IsOpen
    {
        get => isOpen;
        private set => SetProperty(ref isOpen, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether a changed row count triggers a notification.
    /// </summary>
    public bool NotifyWhenRowCountChanges
    {
        get => notifyWhenRowCountChanges;
        set => SetProperty(ref notifyWhenRowCountChanges, value);
    }

    /// <summary>
    /// Gets or sets the optional fixed row-count comparison.
    /// </summary>
    public KustoAutomationRowCountComparison RowCountComparison
    {
        get => rowCountComparison;
        set
        {
            if (SetProperty(ref rowCountComparison, value))
            {
                OnPropertyChanged(nameof(HasRowCountComparison));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the fixed comparison value is used.
    /// </summary>
    public bool HasRowCountComparison => RowCountComparison != KustoAutomationRowCountComparison.None;

    /// <summary>
    /// Gets or sets the non-negative fixed row-count value.
    /// </summary>
    public double RowCountValue
    {
        get => rowCountValue;
        set => SetProperty(ref rowCountValue, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether desktop toast notifications are enabled.
    /// </summary>
    public bool DesktopEnabled
    {
        get => desktopEnabled;
        set => SetProperty(ref desktopEnabled, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether a configured application is launched.
    /// </summary>
    public bool RunApplicationEnabled
    {
        get => runApplicationEnabled;
        set
        {
            if (SetProperty(ref runApplicationEnabled, value))
            {
                OnPropertyChanged(nameof(ShowApplicationSettings));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether application settings are shown.
    /// </summary>
    public bool ShowApplicationSettings => RunApplicationEnabled;

    /// <summary>
    /// Gets or sets the application executable path.
    /// </summary>
    public string ApplicationPath
    {
        get => applicationPath;
        set => SetProperty(ref applicationPath, value);
    }

    /// <summary>
    /// Gets or sets the application command-line arguments.
    /// </summary>
    public string ApplicationArguments
    {
        get => applicationArguments;
        set => SetProperty(ref applicationArguments, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether email notifications are enabled.
    /// </summary>
    public bool EmailEnabled
    {
        get => emailEnabled;
        set
        {
            if (SetProperty(ref emailEnabled, value))
            {
                OnPropertyChanged(nameof(ShowEmailSettings));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether SMTP fields are shown.
    /// </summary>
    public bool ShowEmailSettings => EmailEnabled;

    /// <summary>
    /// Gets or sets the email recipient.
    /// </summary>
    public string EmailRecipient
    {
        get => emailRecipient;
        set => SetProperty(ref emailRecipient, value);
    }

    /// <summary>
    /// Gets or sets the SMTP sender address.
    /// </summary>
    public string EmailSender
    {
        get => emailSender;
        set => SetProperty(ref emailSender, value);
    }

    /// <summary>
    /// Gets or sets the SMTP server host.
    /// </summary>
    public string SmtpHost
    {
        get => smtpHost;
        set => SetProperty(ref smtpHost, value);
    }

    /// <summary>
    /// Gets or sets the SMTP server port.
    /// </summary>
    public double SmtpPort
    {
        get => smtpPort;
        set => SetProperty(ref smtpPort, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether SMTP transport uses TLS.
    /// </summary>
    public bool SmtpUseSsl
    {
        get => smtpUseSsl;
        set => SetProperty(ref smtpUseSsl, value);
    }

    /// <summary>
    /// Gets or sets the notification subject template.
    /// </summary>
    public string SubjectTemplate
    {
        get => subjectTemplate;
        set => SetProperty(ref subjectTemplate, value);
    }

    /// <summary>
    /// Gets or sets the notification body template.
    /// </summary>
    public string MessageTemplate
    {
        get => messageTemplate;
        set => SetProperty(ref messageTemplate, value);
    }

    /// <summary>
    /// Gets the latest settings validation error.
    /// </summary>
    public string ErrorText
    {
        get => errorText;
        private set
        {
            if (SetProperty(ref errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether validation failed.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>
    /// Opens the editor for an existing automation.
    /// </summary>
    /// <param name="automation">The automation being configured.</param>
    internal void Open(KustoAutomationViewModel automation)
    {
        ArgumentNullException.ThrowIfNull(automation);
        target = automation;
        KustoAutomationNotificationSettings settings = automation.NotificationSettings;
        AutomationName = automation.Name;
        NotifyWhenRowCountChanges = settings.NotifyWhenRowCountChanges;
        RowCountComparison = settings.RowCountComparison;
        RowCountValue = settings.RowCountValue;
        DesktopEnabled = settings.DesktopEnabled;
        RunApplicationEnabled = settings.RunApplicationEnabled;
        ApplicationPath = settings.ApplicationPath ?? string.Empty;
        ApplicationArguments = settings.ApplicationArguments ?? string.Empty;
        EmailEnabled = settings.EmailEnabled;
        EmailRecipient = settings.EmailRecipient ?? string.Empty;
        EmailSender = settings.EmailSender ?? string.Empty;
        SmtpHost = settings.SmtpHost ?? string.Empty;
        SmtpPort = settings.SmtpPort;
        SmtpUseSsl = settings.SmtpUseSsl;
        SubjectTemplate = settings.SubjectTemplate;
        MessageTemplate = settings.MessageTemplate;
        ErrorText = string.Empty;
        IsOpen = true;
    }

    private void AppendMessageHelper(string? helper)
    {
        if (helper is not null && SupportedTemplateHelpers.Contains(helper, StringComparer.Ordinal))
        {
            MessageTemplate += helper;
        }
    }

    private void Close()
    {
        target = null;
        ErrorText = string.Empty;
        IsOpen = false;
    }

    private void Save()
    {
        ErrorText = Validate();

        if (target is not null && string.IsNullOrEmpty(ErrorText))
        {
            KustoAutomationNotificationSettings settings = new(
                NotifyWhenRowCountChanges,
                RowCountComparison,
                (int)RowCountValue,
                DesktopEnabled,
                EmailEnabled,
                EmailRecipient,
                EmailSender,
                SmtpHost,
                (int)SmtpPort,
                SmtpUseSsl,
                SubjectTemplate,
                MessageTemplate,
                RunApplicationEnabled,
                ApplicationPath,
                ApplicationArguments);
            KustoAutomationViewModel automation = target;
            automation.SetNotificationSettings(settings);
            savedAction(automation);
            Close();
        }
    }

    private string Validate()
    {
        string error = string.Empty;
        bool hasChannel = DesktopEnabled || EmailEnabled || RunApplicationEnabled;
        bool hasCriterion = NotifyWhenRowCountChanges || HasRowCountComparison;

        if (hasChannel && !hasCriterion)
        {
            error = "Choose at least one row-count criterion.";
        }
        else if (RowCountValue < 0 || Math.Abs(RowCountValue - Math.Round(RowCountValue)) > 0.000001)
        {
            error = "The row-count comparison value must be a whole number.";
        }
        else if (SmtpPort is < 1 or > 65535 || Math.Abs(SmtpPort - Math.Round(SmtpPort)) > 0.000001)
        {
            error = "The SMTP port must be a whole number from 1 to 65535.";
        }
        else if (string.IsNullOrWhiteSpace(SubjectTemplate) || string.IsNullOrWhiteSpace(MessageTemplate))
        {
            error = "Enter both a subject and message template.";
        }
        else if (EmailEnabled && !IsValidEmailConfiguration())
        {
            error = "Email requires valid recipient and sender addresses plus an SMTP host.";
        }
        else if (RunApplicationEnabled && string.IsNullOrWhiteSpace(ApplicationPath))
        {
            error = "Run application requires an executable path.";
        }

        return error;
    }

    private bool IsValidEmailConfiguration()
    {
        return MailAddress.TryCreate(EmailRecipient, out _)
            && MailAddress.TryCreate(EmailSender, out _)
            && !string.IsNullOrWhiteSpace(SmtpHost);
    }
}
