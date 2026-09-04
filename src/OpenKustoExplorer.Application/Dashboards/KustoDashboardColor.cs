namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Validates persisted dashboard color values.
/// </summary>
internal static class KustoDashboardColor
{
    /// <summary>
    /// Validates an RGB or ARGB hexadecimal color.
    /// </summary>
    /// <param name="color">The color text.</param>
    /// <param name="parameterName">The source parameter name.</param>
    /// <returns>The validated color text.</returns>
    internal static string Validate(string color, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            throw new ArgumentException("A dashboard color is required.", parameterName);
        }

        bool valid = color.Length is 7 or 9
            && color[0] == '#'
            && color.Skip(1).All(Uri.IsHexDigit);

        return valid
            ? color
            : throw new ArgumentException(
                "Dashboard colors must use #RRGGBB or #AARRGGBB format.",
                parameterName);
    }
}
