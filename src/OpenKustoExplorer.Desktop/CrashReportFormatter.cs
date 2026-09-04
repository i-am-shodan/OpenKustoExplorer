using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Creates privacy-conscious diagnostic text for unexpected application failures.
/// </summary>
internal static partial class CrashReportFormatter
{
    /// <summary>
    /// Creates a sanitized diagnostic report for an unexpected exception.
    /// </summary>
    /// <param name="exception">The unexpected exception.</param>
    /// <returns>The report text shown to the user.</returns>
    internal static string Create(Exception exception)
    {
        return Create(exception, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a sanitized diagnostic report with a deterministic occurrence time.
    /// </summary>
    /// <param name="exception">The unexpected exception.</param>
    /// <param name="occurredAtUtc">When the failure occurred.</param>
    /// <returns>The report text shown to the user.</returns>
    internal static string Create(Exception exception, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Assembly assembly = typeof(App).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        StringBuilder report = new();
        report.AppendLine("Open Kusto Explorer crash report");
        report.Append("UTC: ").AppendLine(occurredAtUtc.ToUniversalTime().ToString("O"));
        report.Append("Version: ").AppendLine(version);
        report.Append("OS: ").AppendLine(RuntimeInformation.OSDescription);
        report.Append("Runtime: ").AppendLine(RuntimeInformation.FrameworkDescription);
        report.Append("Architecture: ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString());
        report.AppendLine();
        report.AppendLine("Unexpected exception:");
        report.AppendLine(Sanitize(exception.ToString()));
        return report.ToString();
    }

    /// <summary>
    /// Removes common personal paths and credential-shaped values from display text.
    /// </summary>
    /// <param name="value">The text to sanitize.</param>
    /// <returns>The sanitized text.</returns>
    internal static string SanitizeForDisplay(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sanitize(value);
    }

    /// <summary>
    /// Writes a report to a temporary recovery location when possible.
    /// </summary>
    /// <param name="reportText">The complete report text.</param>
    /// <returns>The report path, or <see langword="null"/> when writing failed.</returns>
    internal static string? TryWriteTemporaryReport(string reportText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportText);

        try
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("OpenKustoExplorer-Crash-");
            string filePath = Path.Combine(
                directory.FullName,
                $"OpenKustoExplorer-crash-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.txt");
            File.WriteAllText(filePath, reportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return filePath;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return null;
        }
    }

    [SuppressMessage(
        "Security",
        "S5443",
        Justification = "The temporary path is read only so it can be redacted from diagnostic text.")]
    private static string Sanitize(string value)
    {
        string sanitized = value;
        (string Value, string Placeholder)[] replacements =
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Path.TrimEndingDirectorySeparator(Path.GetTempPath()), "%TEMP%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.UserName, "%USERNAME%"),
            (Environment.MachineName, "%COMPUTERNAME%"),
        ];

        foreach ((string path, string placeholder) in replacements
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .OrderByDescending(item => item.Value.Length))
        {
            sanitized = sanitized.Replace(path, placeholder, StringComparison.OrdinalIgnoreCase);
            string alternatePath = path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(path, alternatePath, StringComparison.Ordinal))
            {
                sanitized = sanitized.Replace(alternatePath, placeholder, StringComparison.OrdinalIgnoreCase);
            }
        }

        sanitized = BearerTokenRegex().Replace(sanitized, "$1[REDACTED]");
        sanitized = SecretAssignmentRegex().Replace(sanitized, "$1$2[REDACTED]");
        return EmailAddressRegex().Replace(sanitized, "[REDACTED]@$1");
    }

    [GeneratedRegex(@"(?i)\b(Bearer\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        @"(?i)\b(access_token|refresh_token|id_token|client_secret|password|api[_-]?key)(\s*[:=]\s*)[^\s,;&]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(
        @"(?i)\b[A-Z0-9._%+-]+@([A-Z0-9.-]+\.[A-Z]{2,})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressRegex();
}
