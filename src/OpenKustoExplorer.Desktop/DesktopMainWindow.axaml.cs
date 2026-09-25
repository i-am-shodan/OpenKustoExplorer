using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Updates;
using OpenKustoExplorer.Desktop.Appearance;
using OpenKustoExplorer.Presentation.Workbench;

#pragma warning disable MA0048 // This native shell pairs with DesktopMainWindow.axaml.

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Hosts the shared workbench in a native desktop window.
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly CancellationTokenSource updateCancellationSource = new();
    private KustoApplicationUpdate? availableUpdate;
    private WorkbenchView? workbench;
    private bool isDisposed;
    private int updateCheckStarted;
    private IKustoApplicationUpdateService? updateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class for compiled XAML and design tools.
    /// </summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="viewModel">The workbench presentation model.</param>
    /// <param name="appearanceSettings">The desktop appearance settings.</param>
    /// <param name="identityService">The process-lifetime signed-in account service.</param>
    /// <param name="updateService">The official application release checker.</param>
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

        AutomationNotificationDispatcher notificationDispatcher = new(
            new DesktopNotificationService(this));
        workbench = new WorkbenchView(
            viewModel,
            appearanceSettings,
            identityService,
            notificationDispatcher);
        workbench.OpenUpdateRequested += OnOpenUpdateRequested;
        this.updateService = updateService;
        Content = workbench;
    }

    /// <summary>
    /// Releases resources owned by the shared workbench.
    /// </summary>
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            updateCancellationSource.Cancel();
            if (workbench is not null)
            {
                workbench.OpenUpdateRequested -= OnOpenUpdateRequested;
            }

            workbench?.Dispose();
            workbench = null;
            updateService = null;
            availableUpdate = null;
            updateCancellationSource.Dispose();
        }
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
        return WorkbenchView.CalculateResultWheelOffset(currentOffset, maximumOffset, wheelDelta);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = CheckForUpdateAsync();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    private async Task CheckForUpdateAsync()
    {
        IKustoApplicationUpdateService? service = updateService;
        if (service is null || Interlocked.Exchange(ref updateCheckStarted, 1) != 0)
        {
            return;
        }

        CancellationToken cancellationToken = updateCancellationSource.Token;
        try
        {
            Version? currentVersion = typeof(MainWindow).Assembly.GetName().Version;
            if (currentVersion is null)
            {
                return;
            }

            KustoApplicationUpdate? update = await service.CheckForUpdateAsync(
                currentVersion,
                cancellationToken).ConfigureAwait(true);
            if (update is not null && !cancellationToken.IsCancellationRequested)
            {
                availableUpdate = update;
                workbench?.ShowAvailableUpdate(update.TagName);
            }
        }
        catch (OperationCanceledException)
        {
            // Update discovery is optional and must never interfere with shutdown.
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

    private void OnOpenUpdateRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
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
            if (workbench?.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ReportActionStatus($"Unable to open the update page: {exception.Message}");
            }
        }
    }
}
