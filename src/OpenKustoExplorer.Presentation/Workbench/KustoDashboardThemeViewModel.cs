namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one coordinated dashboard widget color theme.
/// </summary>
public sealed class KustoDashboardThemeViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardThemeViewModel"/> class.
    /// </summary>
    /// <param name="name">The theme name.</param>
    /// <param name="backgroundColor">The surface color.</param>
    /// <param name="foregroundColor">The text color.</param>
    /// <param name="accentColor">The accent color.</param>
    public KustoDashboardThemeViewModel(
        string name,
        string backgroundColor,
        string foregroundColor,
        string accentColor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(backgroundColor);
        ArgumentException.ThrowIfNullOrWhiteSpace(foregroundColor);
        ArgumentException.ThrowIfNullOrWhiteSpace(accentColor);
        Name = name;
        BackgroundColor = backgroundColor;
        ForegroundColor = foregroundColor;
        AccentColor = accentColor;
    }

    /// <summary>
    /// Gets the theme name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the surface color.
    /// </summary>
    public string BackgroundColor { get; }

    /// <summary>
    /// Gets the text color.
    /// </summary>
    public string ForegroundColor { get; }

    /// <summary>
    /// Gets the accent color.
    /// </summary>
    public string AccentColor { get; }
}
