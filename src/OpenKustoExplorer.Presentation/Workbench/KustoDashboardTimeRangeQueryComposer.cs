using System.Globalization;
using OpenKustoExplorer.Application.Dashboards;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Adds ADX-compatible dashboard time declarations to a stored widget query.
/// </summary>
internal static class KustoDashboardTimeRangeQueryComposer
{
    /// <summary>
    /// Prepends fixed UTC time declarations without modifying the stored query body.
    /// </summary>
    /// <param name="queryText">The persisted widget query.</param>
    /// <param name="bounds">The resolved dashboard bounds.</param>
    /// <returns>The executable query.</returns>
    internal static string Compose(string queryText, KustoDashboardTimeRangeBounds bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentNullException.ThrowIfNull(bounds);
        return string.Concat(
            "let _startTime = datetime(",
            FormatUtc(bounds.StartUtc),
            ");\nlet _endTime = datetime(",
            FormatUtc(bounds.EndUtc),
            ");\n",
            queryText);
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture);
    }
}
