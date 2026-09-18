using OpenKustoExplorer.Infrastructure.Assistance;

namespace OpenKustoExplorer.Infrastructure.Tests.Assistance;

/// <summary>
/// Verifies GitHub Copilot CLI command resolution.
/// </summary>
public sealed class CopilotRuntimeCommandTests
{
    /// <summary>
    /// Verifies the configured runtime takes precedence and preserves additional argument boundaries.
    /// </summary>
    [Fact]
    public void FindUsesConfiguredRuntimeAndCreatesInteractiveStartInfo()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-CopilotRuntime-{Guid.NewGuid():N}");
        string runtimePath = Path.Combine(
            directoryPath,
            OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        string? originalPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(runtimePath, string.Empty);

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_CLI_PATH", runtimePath);

            CopilotRuntimeCommand command = CopilotRuntimeCommand.Find();
            System.Diagnostics.ProcessStartInfo startInfo = command.CreateStartInfo(
                "--stdio",
                "argument with spaces");

            Assert.Equal(Path.GetFullPath(runtimePath), command.ExecutablePath);
            Assert.Empty(command.Arguments);
            Assert.Equal(command.ExecutablePath, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Equal(["--stdio", "argument with spaces"], startInfo.ArgumentList);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_CLI_PATH", originalPath);
            Directory.Delete(directoryPath, true);
        }
    }
}
