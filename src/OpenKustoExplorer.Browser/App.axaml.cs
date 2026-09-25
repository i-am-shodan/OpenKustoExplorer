using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Owns the browser application's single-view lifetime.
/// </summary>
public sealed class App : Avalonia.Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        BrowserInterop.MarkPerformance("avalonia.xaml.start");
        AvaloniaXamlLoader.Load(this);
        BrowserInterop.MarkPerformance("avalonia.xaml.complete");
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        BrowserInterop.MarkPerformance("avalonia.framework.ready");
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            BrowserInterop.MarkPerformance("workbench.create.start");
            singleViewLifetime.MainView = BrowserWorkbenchFactory.Create(this, Program.BootstrapContext);
            BrowserInterop.MarkPerformance("workbench.create.complete");
        }

        base.OnFrameworkInitializationCompleted();
        BrowserInterop.MarkPerformance("avalonia.framework.complete");
        BrowserInterop.CompleteStartup();
    }
}
