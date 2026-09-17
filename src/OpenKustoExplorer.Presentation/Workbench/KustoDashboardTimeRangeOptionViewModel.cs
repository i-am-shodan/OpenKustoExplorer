namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one dashboard time-range preset or the custom-range action.
/// </summary>
public sealed class KustoDashboardTimeRangeOptionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardTimeRangeOptionViewModel"/> class.
    /// </summary>
    /// <param name="label">The concise display label.</param>
    /// <param name="duration">The relative duration, or <see langword="null"/> for Custom.</param>
    public KustoDashboardTimeRangeOptionViewModel(string label, TimeSpan? duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (duration is not null && duration.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        Label = label;
        Duration = duration;
    }

    /// <summary>
    /// Gets the concise display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the relative duration, or <see langword="null"/> for Custom.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Gets a value indicating whether this option opens the custom-range editor.
    /// </summary>
    public bool IsCustom => Duration is null;
}
