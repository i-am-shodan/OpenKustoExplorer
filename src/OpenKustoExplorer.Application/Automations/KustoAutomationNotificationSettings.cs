namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Describes persisted row-count criteria, channels, and message templates for one automation.
/// </summary>
public sealed class KustoAutomationNotificationSettings
{
    /// <summary>
    /// Gets the default notification subject template.
    /// </summary>
    public const string DefaultSubjectTemplate = "{name}: {row_count} rows";

    /// <summary>
    /// Gets the default notification message template.
    /// </summary>
    public const string DefaultMessageTemplate = "{name} returned {row_count} rows ({rows_changed} since the previous run).\n\n{query}";

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationNotificationSettings"/> class.
    /// </summary>
    /// <param name="notifyWhenRowCountChanges">Whether a changed row count triggers a notification.</param>
    /// <param name="rowCountComparison">The optional fixed-value comparison.</param>
    /// <param name="rowCountValue">The non-negative comparison value.</param>
    /// <param name="desktopEnabled">Whether an in-app desktop toast is shown.</param>
    /// <param name="emailEnabled">Whether an email is sent.</param>
    /// <param name="emailRecipient">The optional email recipient.</param>
    /// <param name="emailSender">The optional SMTP sender address.</param>
    /// <param name="smtpHost">The optional SMTP server host.</param>
    /// <param name="smtpPort">The SMTP server port.</param>
    /// <param name="smtpUseSsl">Whether SMTP transport uses TLS.</param>
    /// <param name="subjectTemplate">The subject template.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="runApplicationEnabled">Whether a configured application is launched.</param>
    /// <param name="applicationPath">The optional application executable path.</param>
    /// <param name="applicationArguments">The optional application arguments.</param>
    /// <param name="webhook">The optional webhook channel settings.</param>
    public KustoAutomationNotificationSettings(
        bool notifyWhenRowCountChanges,
        KustoAutomationRowCountComparison rowCountComparison,
        int rowCountValue,
        bool desktopEnabled,
        bool emailEnabled,
        string? emailRecipient,
        string? emailSender,
        string? smtpHost,
        int smtpPort,
        bool smtpUseSsl,
        string subjectTemplate,
        string messageTemplate,
        bool runApplicationEnabled = false,
        string? applicationPath = null,
        string? applicationArguments = null,
        KustoAutomationWebhookSettings? webhook = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCountValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(smtpPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(smtpPort, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTemplate);

        if (!Enum.IsDefined(rowCountComparison))
        {
            throw new ArgumentOutOfRangeException(nameof(rowCountComparison));
        }

        if (emailEnabled && string.IsNullOrWhiteSpace(emailRecipient))
        {
            throw new ArgumentException(
                "Email notifications require recipient, sender, and SMTP host values.",
                nameof(emailRecipient));
        }

        if (emailEnabled && string.IsNullOrWhiteSpace(emailSender))
        {
            throw new ArgumentException(
                "Email notifications require recipient, sender, and SMTP host values.",
                nameof(emailSender));
        }

        if (emailEnabled && string.IsNullOrWhiteSpace(smtpHost))
        {
            throw new ArgumentException(
                "Email notifications require recipient, sender, and SMTP host values.",
                nameof(smtpHost));
        }

        if (runApplicationEnabled && string.IsNullOrWhiteSpace(applicationPath))
        {
            throw new ArgumentException(
                "Application actions require an executable path.",
                nameof(applicationPath));
        }

        NotifyWhenRowCountChanges = notifyWhenRowCountChanges;
        RowCountComparison = rowCountComparison;
        RowCountValue = rowCountValue;
        DesktopEnabled = desktopEnabled;
        EmailEnabled = emailEnabled;
        EmailRecipient = NormalizeOptionalText(emailRecipient);
        EmailSender = NormalizeOptionalText(emailSender);
        SmtpHost = NormalizeOptionalText(smtpHost);
        SmtpPort = smtpPort;
        SmtpUseSsl = smtpUseSsl;
        SubjectTemplate = subjectTemplate.Trim();
        MessageTemplate = messageTemplate.Trim();
        RunApplicationEnabled = runApplicationEnabled;
        ApplicationPath = NormalizeOptionalText(applicationPath);
        ApplicationArguments = NormalizeOptionalText(applicationArguments);
        Webhook = webhook;
    }

    /// <summary>
    /// Gets disabled default notification settings.
    /// </summary>
    public static KustoAutomationNotificationSettings Disabled { get; } = new(
        false,
        KustoAutomationRowCountComparison.None,
        0,
        false,
        false,
        null,
        null,
        null,
        587,
        true,
        DefaultSubjectTemplate,
        DefaultMessageTemplate);

    /// <summary>
    /// Gets a value indicating whether a changed row count triggers a notification.
    /// </summary>
    public bool NotifyWhenRowCountChanges { get; }

    /// <summary>
    /// Gets the optional row-count comparison.
    /// </summary>
    public KustoAutomationRowCountComparison RowCountComparison { get; }

    /// <summary>
    /// Gets the fixed row-count comparison value.
    /// </summary>
    public int RowCountValue { get; }

    /// <summary>
    /// Gets a value indicating whether a desktop toast is enabled.
    /// </summary>
    public bool DesktopEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether email is enabled.
    /// </summary>
    public bool EmailEnabled { get; }

    /// <summary>
    /// Gets the optional email recipient.
    /// </summary>
    public string? EmailRecipient { get; }

    /// <summary>
    /// Gets the optional SMTP sender address.
    /// </summary>
    public string? EmailSender { get; }

    /// <summary>
    /// Gets the optional SMTP server host.
    /// </summary>
    public string? SmtpHost { get; }

    /// <summary>
    /// Gets the SMTP server port.
    /// </summary>
    public int SmtpPort { get; }

    /// <summary>
    /// Gets a value indicating whether SMTP transport uses TLS.
    /// </summary>
    public bool SmtpUseSsl { get; }

    /// <summary>
    /// Gets the email and toast title template.
    /// </summary>
    public string SubjectTemplate { get; }

    /// <summary>
    /// Gets the email and toast body template.
    /// </summary>
    public string MessageTemplate { get; }

    /// <summary>
    /// Gets a value indicating whether the configured application is launched when criteria match.
    /// </summary>
    public bool RunApplicationEnabled { get; }

    /// <summary>
    /// Gets the optional application executable path.
    /// </summary>
    public string? ApplicationPath { get; }

    /// <summary>
    /// Gets the optional application command-line arguments.
    /// </summary>
    public string? ApplicationArguments { get; }

    /// <summary>
    /// Gets the optional webhook channel settings.
    /// </summary>
    public KustoAutomationWebhookSettings? Webhook { get; }

    /// <summary>
    /// Gets a value indicating whether at least one notification or application action is enabled.
    /// </summary>
    public bool HasEnabledChannel => DesktopEnabled
        || EmailEnabled
        || RunApplicationEnabled
        || Webhook is not null;

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
