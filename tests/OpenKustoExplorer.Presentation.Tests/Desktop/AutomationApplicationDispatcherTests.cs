using System.Diagnostics;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies matched automation application actions are launched without a command shell.
/// </summary>
public sealed class AutomationApplicationDispatcherTests
{
    /// <summary>
    /// Verifies a matching action sends expanded arguments to the configured application.
    /// </summary>
    [Fact]
    public void DispatchLaunchesConfiguredApplicationWithExpandedArguments()
    {
        RecordingLauncher launcher = new();
        AutomationApplicationDispatcher dispatcher = new(launcher);
        KustoAutomationNotification notification = CreateNotification();

        string? error = dispatcher.Dispatch(notification);

        Assert.Null(error);
        Assert.Equal("C:\\Tools\\handle-empty-result.exe", launcher.ApplicationPath);
        Assert.Equal("--rows 0 --name \"Empty result monitor\"", launcher.Arguments);
    }

    /// <summary>
    /// Verifies executable startup bypasses the shell and does not create a console window.
    /// </summary>
    [Fact]
    public void CreateStartInfoUsesDirectHiddenProcessLaunch()
    {
        ProcessStartInfo startInfo = AutomationApplicationLauncher.CreateStartInfo(
            "C:\\Tools\\handle-empty-result.exe",
            "--rows 0");

        Assert.Equal("C:\\Tools\\handle-empty-result.exe", startInfo.FileName);
        Assert.Equal("--rows 0", startInfo.Arguments);
        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.UseShellExecute);
    }

    private static KustoAutomationNotification CreateNotification()
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        KustoAutomationNotificationSettings settings = new(
            false,
            KustoAutomationRowCountComparison.Equals,
            0,
            false,
            false,
            null,
            null,
            null,
            587,
            true,
            "{name}: {row_count}",
            "{query}",
            true,
            "C:\\Tools\\handle-empty-result.exe",
            "--rows {row_count} --name \"{name}\"");
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Empty result monitor",
            new Uri("https://adx.example.com"),
            "Telemetry",
            "Events | where false",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddMinutes(5),
            null,
            true,
            [],
            settings);
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Value", "long")],
            []);
        KustoAutomationRun run = new(
            Guid.NewGuid(),
            startedAtUtc,
            startedAtUtc.AddMilliseconds(5),
            KustoAutomationRunStatus.Succeeded,
            null,
            new KustoQueryResult([table], TimeSpan.FromMilliseconds(5)));
        return Assert.IsType<KustoAutomationNotification>(KustoAutomationNotification.TryCreate(
            automation,
            run,
            null));
    }

    private sealed class RecordingLauncher : IAutomationApplicationLauncher
    {
        internal string? ApplicationPath { get; private set; }

        internal string? Arguments { get; private set; }

        public void Launch(string applicationPath, string? arguments)
        {
            ApplicationPath = applicationPath;
            Arguments = arguments;
        }
    }
}
