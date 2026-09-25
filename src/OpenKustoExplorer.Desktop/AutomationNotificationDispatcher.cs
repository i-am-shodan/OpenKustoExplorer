using System.Net;
using System.Net.Mail;
using OpenKustoExplorer.Application.Automations;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Delivers evaluated automation actions through application, desktop, webhook, and SMTP channels.
/// </summary>
internal sealed class AutomationNotificationDispatcher : IWorkbenchAutomationNotificationDispatcher, IDisposable
{
    private const string SmtpPasswordEnvironmentVariable = "OPENKUSTOEXPLORER_SMTP_PASSWORD";
    private const string SmtpUsernameEnvironmentVariable = "OPENKUSTOEXPLORER_SMTP_USERNAME";
    private readonly AutomationApplicationDispatcher applicationDispatcher;
    private readonly DesktopNotificationService notifications;
    private readonly AutomationWebhookDispatcher webhookDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationNotificationDispatcher"/> class.
    /// </summary>
    /// <param name="notifications">The window-owned notification presenter.</param>
    public AutomationNotificationDispatcher(DesktopNotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        applicationDispatcher = new AutomationApplicationDispatcher(new AutomationApplicationLauncher());
        webhookDispatcher = new AutomationWebhookDispatcher();
        this.notifications = notifications;
    }

    /// <summary>
    /// Delivers all enabled channels without propagating a channel failure into the scheduler.
    /// </summary>
    /// <param name="notification">The evaluated notification.</param>
    /// <param name="cancellationToken">Cancels network delivery during application shutdown.</param>
    /// <returns>A task that completes after enabled channels have been attempted.</returns>
    public async Task DispatchAsync(
        KustoAutomationNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Settings.DesktopEnabled)
        {
            notifications.ShowInformation(
                notification.Title,
                notification.Message);
        }

        string? applicationFailure = applicationDispatcher.Dispatch(notification);
        if (applicationFailure is not null)
        {
            notifications.ShowError(
                "Automation application failed",
                applicationFailure);
        }

        if (notification.Settings.Webhook is not null)
        {
            string? webhookFailure = await webhookDispatcher.DispatchAsync(
                notification,
                cancellationToken).ConfigureAwait(true);
            if (webhookFailure is not null)
            {
                notifications.ShowError(
                    "Automation webhook failed",
                    webhookFailure);
            }
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

    /// <inheritdoc />
    public void Dispose()
    {
        webhookDispatcher.Dispose();
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
        notifications.ShowError(
            "Automation email failed",
            errorMessage);
    }
}
