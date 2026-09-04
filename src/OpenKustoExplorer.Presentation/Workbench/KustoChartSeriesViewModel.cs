using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one inferred or explicitly selected chart series.
/// </summary>
public sealed class KustoChartSeriesViewModel : ObservableObject
{
    private bool isVisible = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoChartSeriesViewModel"/> class.
    /// </summary>
    /// <param name="name">The series display name.</param>
    /// <param name="colorHex">The accessible series color.</param>
    /// <param name="points">The measured points.</param>
    public KustoChartSeriesViewModel(
        string name,
        string colorHex,
        IEnumerable<KustoChartPointViewModel> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        ArgumentNullException.ThrowIfNull(points);

        Name = name;
        ColorHex = colorHex;
        Points = Array.AsReadOnly(points.OrderBy(point => point.X).ToArray());
        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);
    }

    /// <summary>
    /// Gets the series display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the accessible series color.
    /// </summary>
    public string ColorHex { get; }

    /// <summary>
    /// Gets measured points ordered by their x-axis coordinate.
    /// </summary>
    public IReadOnlyList<KustoChartPointViewModel> Points { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the series is rendered.
    /// </summary>
    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (SetProperty(ref isVisible, value))
            {
                OnPropertyChanged(nameof(LegendOpacity));
                OnPropertyChanged(nameof(VisibilityActionText));
            }
        }
    }

    /// <summary>
    /// Gets the legend opacity reflecting current visibility.
    /// </summary>
    public double LegendOpacity => IsVisible ? 1 : 0.45;

    /// <summary>
    /// Gets the accessible legend action text.
    /// </summary>
    public string VisibilityActionText => IsVisible ? $"Hide {Name}" : $"Show {Name}";

    /// <summary>
    /// Gets the command that toggles series visibility.
    /// </summary>
    public IRelayCommand ToggleVisibilityCommand { get; }
}
