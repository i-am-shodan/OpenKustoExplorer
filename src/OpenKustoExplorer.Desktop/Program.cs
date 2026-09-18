using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Application.Updates;
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
using OpenKustoExplorer.Infrastructure.Sessions;
using OpenKustoExplorer.Infrastructure.Updates;
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
        KustoPerformanceTrace.RecordStartupMilestone("startup.main.entered");
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
            ServiceCollection services;
            using (KustoPerformanceTrace.Measure("startup.services.register"))
            {
                services = CreateServices();
            }

            using (KustoPerformanceTrace.Measure("startup.services.build"))
            {
                serviceProvider = services.BuildServiceProvider(
                    new ServiceProviderOptions
                    {
                        ValidateOnBuild = true,
                        ValidateScopes = true,
                    });
            }

            AppBuilder applicationBuilder;
            using (KustoPerformanceTrace.Measure("startup.avalonia.configure"))
            {
                applicationBuilder = BuildAvaloniaApp(serviceProvider);
            }

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

            KustoPerformanceTrace.Flush();
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
        services.AddSingleton<SqliteKustoRecordedSessionStore>();
        services.AddSingleton<IKustoRecordedSessionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteKustoRecordedSessionStore>());
        services.AddSingleton<KustoRecordedSessionArchiveService>();
        services.AddSingleton<IKustoRecordedSessionArchiveService>(serviceProvider =>
            serviceProvider.GetRequiredService<KustoRecordedSessionArchiveService>());
        services.AddSingleton<IKustoPredicateInterestExtractor, KustoPredicateInterestExtractor>();
        services.AddSingleton<IKustoRecordedRelationExtractor, KustoRecordedRelationExtractor>();
        services.AddSingleton<IKustoRecordedChainSearcher, KustoRecordedChainSearcher>();
        services.AddSingleton<IKustoRecordedRelationPlanner, KustoRecordedRelationPlanner>();
        services.AddSingleton<IKustoRecordedChainQueryGenerator, KustoRecordedChainQueryGenerator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppearanceSettings>();
        services.AddSingleton<IKustoAIProviderConfiguration>(serviceProvider =>
            serviceProvider.GetRequiredService<AppearanceSettings>());
        services.AddSingleton<GitHubCopilotKustoService>();
        services.AddSingleton<IKustoCopilotService>(serviceProvider =>
            new ConfigurableKustoCopilotService(
                serviceProvider.GetRequiredService<IKustoAIProviderConfiguration>(),
                serviceProvider.GetRequiredService<GitHubCopilotKustoService>(),
                serviceProvider.GetRequiredService<IGraphStore>(),
                serviceProvider.GetRequiredService<IGraphQueryService>(),
                serviceProvider.GetRequiredService<IKustoRecordedSessionStore>(),
                serviceProvider.GetRequiredService<IKustoRecordedChainSearcher>(),
                serviceProvider.GetRequiredService<IKustoRecordedRelationPlanner>(),
                serviceProvider.GetRequiredService<IKustoRecordedChainQueryGenerator>()));
        services.AddSingleton<IKustoLanguageService, KustoLanguageService>();
        services.AddSingleton<IKustoApplicationUpdateService, GitHubKustoApplicationUpdateService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(serviceProvider =>
            new MainWindow(
                serviceProvider.GetRequiredService<MainWindowViewModel>(),
                serviceProvider.GetRequiredService<AppearanceSettings>(),
                serviceProvider.GetRequiredService<IKustoIdentityService>(),
                serviceProvider.GetRequiredService<IKustoApplicationUpdateService>()));

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
