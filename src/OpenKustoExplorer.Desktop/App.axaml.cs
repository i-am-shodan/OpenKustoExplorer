using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Desktop.Appearance;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Owns the Avalonia application lifetime and resolves the main window from dependency injection.
/// </summary>
public sealed class App : Avalonia.Application
{
    private readonly IServiceProvider serviceProvider;
    private int isShowingCrashReport;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public App(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        this.serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            try
            {
                using (KustoPerformanceTrace.Measure("startup.main_window.resolve"))
                {
                    AppearanceSettings appearanceSettings = serviceProvider.GetRequiredService<AppearanceSettings>();
                    appearanceSettings.Initialize(this);
                    desktopLifetime.MainWindow = serviceProvider.GetRequiredService<MainWindow>();
                }
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref isShowingCrashReport, 1);
                desktopLifetime.MainWindow = CreateCrashReportWindow(exception, desktopLifetime);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static CrashReportWindow CreateCrashReportWindow(
        Exception exception,
        IClassicDesktopStyleApplicationLifetime desktopLifetime)
    {
        string reportText = CrashReportFormatter.Create(exception);
        string? reportPath = CrashReportFormatter.TryWriteTemporaryReport(reportText);
        CrashReportWindow crashWindow = new(reportText, reportPath);
        crashWindow.Closed += (_, _) => desktopLifetime.Shutdown(1);
        return crashWindow;
    }

    private void OnDispatcherUnhandledException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs eventArguments)
    {
        _ = sender;
        eventArguments.Handled = true;

        if (Interlocked.Exchange(ref isShowingCrashReport, 1) != 0)
        {
            return;
        }

        Exception exception = eventArguments.Exception;
        Dispatcher.UIThread.Post(() => ShowCrashReport(exception));
    }

    private void ShowCrashReport(Exception exception)
    {
        try
        {
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                CrashReportFallback.Show(exception);
                return;
            }

            CrashReportWindow crashWindow = CreateCrashReportWindow(exception, desktopLifetime);
            Window? owner = desktopLifetime.MainWindow;

            if (owner is not null && owner.IsVisible)
            {
                owner.IsEnabled = false;
                crashWindow.Show(owner);
            }
            else
            {
                desktopLifetime.MainWindow = crashWindow;
                crashWindow.Show();
            }
        }
        catch (Exception reportingException)
        {
            CrashReportFallback.Show(new AggregateException(exception, reportingException));
        }
    }
}
