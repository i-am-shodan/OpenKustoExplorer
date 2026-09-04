using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies unexpected-error reports remain useful without exposing common personal data or credentials.
/// </summary>
public sealed class CrashReportFormatterTests
{
    /// <summary>
    /// Verifies report metadata and exception chains are preserved while sensitive values are removed.
    /// </summary>
    [Fact]
    public void CreateRedactsPersonalPathsAndCredentials()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string sensitivePath = Path.Combine(userProfile, "private", "query.kql");
        InvalidOperationException exception = new(
            $"Failed at {sensitivePath} for analyst@example.com with Bearer abcdefghijklmnop and password=hunter2",
            new IOException("The persisted file is unavailable."));

        string report = CrashReportFormatter.Create(
            exception,
            new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero));

        Assert.Contains("Open Kusto Explorer crash report", report, StringComparison.Ordinal);
        Assert.Contains("2026-09-04T12:30:00.0000000+00:00", report, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), report, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), report, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%", report, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]@example.com", report, StringComparison.Ordinal);
        Assert.DoesNotContain(userProfile, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcdefghijklmnop", report, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the recovery copy can be written independently of application local storage.
    /// </summary>
    [Fact]
    public void TryWriteTemporaryReportCreatesReadableFile()
    {
        string? filePath = CrashReportFormatter.TryWriteTemporaryReport("diagnostic details");

        try
        {
            Assert.NotNull(filePath);
            Assert.Equal("diagnostic details", File.ReadAllText(filePath));
        }
        finally
        {
            if (filePath is not null)
            {
                string? directoryPath = Path.GetDirectoryName(filePath);
                File.Delete(filePath);

                if (directoryPath is not null)
                {
                    Directory.Delete(directoryPath);
                }
            }
        }
    }
}
