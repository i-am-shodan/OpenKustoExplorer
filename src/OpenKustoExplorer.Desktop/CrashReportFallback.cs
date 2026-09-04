using System.ComponentModel;
using System.Diagnostics;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Provides a non-Avalonia fallback for failures that prevent application initialization.
/// </summary>
internal static class CrashReportFallback
{
    private static int hasReported;

    /// <summary>
    /// Writes and opens a crash report when the application UI is unavailable.
    /// </summary>
    /// <param name="exception">The startup or shutdown exception.</param>
    internal static void Show(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Interlocked.Exchange(ref hasReported, 1) != 0)
        {
            return;
        }

        string reportText = CrashReportFormatter.Create(exception);
        string? reportPath = CrashReportFormatter.TryWriteTemporaryReport(reportText);
        Console.Error.WriteLine(reportText);

        if (reportPath is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
            }
            catch (Exception openException) when (openException is InvalidOperationException or Win32Exception)
            {
                Console.Error.WriteLine(
                    $"Crash report saved to {CrashReportFormatter.SanitizeForDisplay(reportPath)}");
            }
        }
    }
}
