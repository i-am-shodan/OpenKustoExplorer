using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Appearance;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Infrastructure.Assistance;
using OpenKustoExplorer.Infrastructure.Automations;
using OpenKustoExplorer.Infrastructure.Connections;
using OpenKustoExplorer.Infrastructure.Dashboards;
using OpenKustoExplorer.Infrastructure.Documents;
using OpenKustoExplorer.Infrastructure.Execution;
using OpenKustoExplorer.Infrastructure.Graph;
using OpenKustoExplorer.Infrastructure.Language;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Provides the process entry point for Open Kusto Explorer.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        ServiceProvider? serviceProvider = null;
        int exitCode = 1;
        UnhandledExceptionEventHandler unhandledExceptionHandler = (_, eventArguments) =>
        {
            if (eventArguments.ExceptionObject is Exception exception)
            {
                CrashReportFallback.Show(exception);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;

        try
        {
            ServiceCollection services = CreateServices();
            serviceProvider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            AppBuilder applicationBuilder = BuildAvaloniaApp(serviceProvider);
            exitCode = applicationBuilder.StartWithClassicDesktopLifetime(arguments);
        }
        catch (Exception exception)
        {
            CrashReportFallback.Show(exception);
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;

            if (serviceProvider is not null)
            {
                try
                {
                    // Dispose asynchronously so IAsyncDisposable singletons release SDK sessions and child processes.
                    serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    CrashReportFallback.Show(exception);
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }

    private static ServiceCollection CreateServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<KustoQueryService>();
        services.AddSingleton<IKustoQueryService>(serviceProvider => serviceProvider.GetRequiredService<KustoQueryService>());
        services.AddSingleton<IKustoGraphQueryService>(serviceProvider => serviceProvider.GetRequiredService<KustoQueryService>());
        services.AddSingleton<IKustoIdentityService>(serviceProvider => serviceProvider.GetRequiredService<KustoQueryService>());
        services.AddSingleton<IKustoCatalogService>(serviceProvider => serviceProvider.GetRequiredService<KustoQueryService>());
        services.AddSingleton<IKustoConnectionStore, FileKustoConnectionStore>();
        services.AddSingleton<IKustoExplorerImportService, KustoExplorerImportService>();
        services.AddSingleton<IKustoDocumentStore, FileKustoDocumentStore>();
        services.AddSingleton<IKustoDashboardStore, FileKustoDashboardStore>();
        services.AddSingleton<IKustoAutomationStore, FileKustoAutomationStore>();
        services.AddSingleton<SqliteGraphStore>();
        services.AddSingleton<IGraphStore>(serviceProvider => serviceProvider.GetRequiredService<SqliteGraphStore>());
        services.AddSingleton<IGraphQueryService>(serviceProvider => serviceProvider.GetRequiredService<SqliteGraphStore>());
        services.AddSingleton<IGraphLayoutService, MsaglGraphLayoutService>();
        services.AddSingleton<IKustoGraphIngestionService, KustoGraphIngestionService>();
        services.AddSingleton<IKustoCopilotService, GitHubCopilotKustoService>();
        services.AddSingleton<IKustoLanguageService, KustoLanguageService>();
        services.AddSingleton<AppearanceSettings>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(serviceProvider =>
            new MainWindow(
                serviceProvider.GetRequiredService<MainWindowViewModel>(),
                serviceProvider.GetRequiredService<AppearanceSettings>(),
                serviceProvider.GetRequiredService<IKustoIdentityService>()));

        return services;
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider serviceProvider)
    {
        AppBuilder applicationBuilder = AppBuilder
            .Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .LogToTrace();

        return applicationBuilder;
    }
}
