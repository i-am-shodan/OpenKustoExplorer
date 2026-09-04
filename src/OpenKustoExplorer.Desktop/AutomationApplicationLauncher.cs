using System.Diagnostics;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Starts user-configured automation applications as direct hidden child processes.
/// </summary>
internal sealed class AutomationApplicationLauncher : IAutomationApplicationLauncher
{
    /// <inheritdoc />
    public void Launch(string applicationPath, string? arguments)
    {
        using Process process = Process.Start(CreateStartInfo(applicationPath, arguments))
            ?? throw new InvalidOperationException($"Unable to start {applicationPath}.");
    }

    /// <summary>
    /// Creates a shell-free process request for an automation application action.
    /// </summary>
    /// <param name="applicationPath">The executable path.</param>
    /// <param name="arguments">The optional command-line arguments.</param>
    /// <returns>The process start request.</returns>
    internal static ProcessStartInfo CreateStartInfo(string applicationPath, string? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        return new ProcessStartInfo(applicationPath)
        {
            Arguments = arguments ?? string.Empty,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
    }
}
