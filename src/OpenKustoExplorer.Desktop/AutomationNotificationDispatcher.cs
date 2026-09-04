using System.Net;
using System.Net.Mail;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using OpenKustoExplorer.Application.Automations;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Delivers evaluated automation actions through application, desktop toast, and SMTP channels.
/// </summary>
internal sealed class AutomationNotificationDispatcher
{
    private const string SmtpPasswordEnvironmentVariable = "OPENKUSTOEXPLORER_SMTP_PASSWORD";
    private const string SmtpUsernameEnvironmentVariable = "OPENKUSTOEXPLORER_SMTP_USERNAME";
    private readonly AutomationApplicationDispatcher applicationDispatcher;
    private readonly WindowNotificationManager notificationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationNotificationDispatcher"/> class.
    /// </summary>
    /// <param name="host">The window that owns in-app desktop toasts.</param>
    public AutomationNotificationDispatcher(TopLevel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        applicationDispatcher = new AutomationApplicationDispatcher(new AutomationApplicationLauncher());
        notificationManager = new WindowNotificationManager(host)
        {
            MaxItems = 4,
            Position = NotificationPosition.BottomRight,
        };
    }

    /// <summary>
    /// Delivers all enabled channels without propagating a channel failure into the scheduler.
    /// </summary>
    /// <param name="notification">The evaluated notification.</param>
    /// <param name="cancellationToken">Cancels email delivery during application shutdown.</param>
    /// <returns>A task that completes after enabled channels have been attempted.</returns>
    public async Task DispatchAsync(
        KustoAutomationNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Settings.DesktopEnabled)
        {
            notificationManager.Show(new Notification(
                notification.Title,
                notification.Message,
                NotificationType.Information,
                TimeSpan.FromSeconds(10)));
        }

        string? applicationFailure = applicationDispatcher.Dispatch(notification);
        if (applicationFailure is not null)
        {
            notificationManager.Show(new Notification(
                "Automation application failed",
                applicationFailure,
                NotificationType.Error,
                TimeSpan.FromSeconds(12)));
        }

        if (notification.Settings.EmailEnabled)
        {
            try
            {
                await SendEmailAsync(notification, cancellationToken).ConfigureAwait(true);
            }
            catch (SmtpException exception)
            {
                ShowEmailFailure(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                ShowEmailFailure(exception.Message);
            }
            catch (FormatException exception)
            {
                ShowEmailFailure(exception.Message);
            }
        }
    }

    private static async Task SendEmailAsync(
        KustoAutomationNotification notification,
        CancellationToken cancellationToken)
    {
        KustoAutomationNotificationSettings settings = notification.Settings;
        using MailMessage message = new(
            settings.EmailSender!,
            settings.EmailRecipient!,
            notification.Title,
            notification.Message);
        using SmtpClient client = new(settings.SmtpHost!, settings.SmtpPort)
        {
            EnableSsl = true,
        };
        string? username = Environment.GetEnvironmentVariable(SmtpUsernameEnvironmentVariable);
        string? password = Environment.GetEnvironmentVariable(SmtpPasswordEnvironmentVariable);
        bool hasUsername = !string.IsNullOrWhiteSpace(username);
        bool hasPassword = !string.IsNullOrWhiteSpace(password);

        if (hasUsername != hasPassword)
        {
            throw new InvalidOperationException(
                "SMTP username and password environment variables must be configured together.");
        }

        if (hasUsername)
        {
            client.Credentials = new NetworkCredential(username, password);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private void ShowEmailFailure(string errorMessage)
    {
        notificationManager.Show(new Notification(
            "Automation email failed",
            errorMessage,
            NotificationType.Error,
            TimeSpan.FromSeconds(12)));
    }
}
