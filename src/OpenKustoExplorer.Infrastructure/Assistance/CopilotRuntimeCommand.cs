using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenKustoExplorer.Infrastructure.Assistance;

/// <summary>
/// Resolves an installed GitHub Copilot CLI command and its bootstrap arguments.
/// </summary>
internal sealed class CopilotRuntimeCommand
{
    private CopilotRuntimeCommand(string executablePath, IList<string> arguments)
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
    }

    /// <summary>
    /// Gets the executable used to host the Copilot runtime.
    /// </summary>
    internal string ExecutablePath { get; }

    /// <summary>
    /// Gets bootstrap arguments placed before SDK runtime arguments.
    /// </summary>
    internal IList<string> Arguments { get; }

    /// <summary>
    /// Finds the best available Copilot CLI runtime.
    /// </summary>
    /// <returns>The resolved runtime command.</returns>
    internal static CopilotRuntimeCommand Find()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        string? runtimePath = FindExistingFile(configuredPath)
            ?? FindAppLocalRuntime()
            ?? FindCachedRuntime()
            ?? FindRuntimeOnPath();

        if (runtimePath is not null)
        {
            return new CopilotRuntimeCommand(runtimePath, []);
        }

        CopilotRuntimeCommand? visualStudioCodeRuntime = FindVisualStudioCodeRuntime();
        return visualStudioCodeRuntime ?? throw new InvalidOperationException(
            "GitHub Copilot CLI was not found. Sign in to GitHub Copilot in VS Code, "
            + "install GitHub Copilot CLI, or set COPILOT_CLI_PATH.");
    }

    /// <summary>
    /// Creates an interactive process start request with additional CLI arguments.
    /// </summary>
    /// <param name="additionalArguments">Arguments appended after bootstrap arguments.</param>
    /// <returns>The process start request.</returns>
    internal ProcessStartInfo CreateStartInfo(params string[] additionalArguments)
    {
        ProcessStartInfo startInfo = new(ExecutablePath)
        {
            UseShellExecute = true,
        };

        foreach (string argument in Arguments.Concat(additionalArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string? FindAppLocalRuntime()
    {
        string binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        string portableRuntimeIdentifier = GetPortableRuntimeIdentifier();
        return FindExistingFile(Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            portableRuntimeIdentifier,
            "native",
            binaryName));
    }

    private static string? FindCachedRuntime()
    {
        string binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        string cacheRoot = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "github-copilot-sdk",
                "cli")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache",
                "github-copilot-sdk",
                "cli");
        string? runtimePath = null;

        if (Directory.Exists(cacheRoot))
        {
            runtimePath = Directory.EnumerateFiles(
                    cacheRoot,
                    binaryName,
                    SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        return runtimePath;
    }

    private static string? FindExistingFile(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
            ? Path.GetFullPath(filePath)
            : null;
    }

    private static string? FindRuntimeOnPath()
    {
        string binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        string? pathText = Environment.GetEnvironmentVariable("PATH");
        return pathText?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => FindExistingFile(Path.Combine(directory, binaryName)))
            .FirstOrDefault(path => path is not null);
    }

    private static CopilotRuntimeCommand? FindVisualStudioCodeRuntime()
    {
        CopilotRuntimeCommand? command = null;

        if (OperatingSystem.IsWindows())
        {
            string scriptPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Code",
                "User",
                "globalStorage",
                "github.copilot-chat",
                "copilotCli",
                "copilot.ps1");
            string powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            if (File.Exists(scriptPath) && File.Exists(powershellPath))
            {
                command = new CopilotRuntimeCommand(
                    powershellPath,
                    ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath]);
            }
        }

        return command;
    }

    private static string GetPortableRuntimeIdentifier()
    {
        string platform = "linux";

        if (OperatingSystem.IsWindows())
        {
            platform = "win";
        }
        else if (OperatingSystem.IsMacOS())
        {
            platform = "osx";
        }

        string architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };
        return $"{platform}-{architecture}";
    }
}
