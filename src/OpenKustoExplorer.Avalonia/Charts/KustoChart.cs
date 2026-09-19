using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop.Charts;

/// <summary>
/// Renders Kusto visualizations with theme-aware drawing primitives and hover values.
/// </summary>
public sealed class KustoChart : Control
{
    /// <summary>
    /// Identifies the <see cref="Visualization"/> styled property.
    /// </summary>
    public static readonly StyledProperty<KustoVisualizationViewModel?> VisualizationProperty =
        AvaloniaProperty.Register<KustoChart, KustoVisualizationViewModel?>(nameof(Visualization));

    /// <summary>
    /// Identifies the <see cref="Revision"/> styled property.
    /// </summary>
    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<KustoChart, int>(nameof(Revision));

    private readonly List<ChartHit> hits = [];
    private Point? hoveredAnchor;
    private string? hoveredText;

    static KustoChart()
    {
        AffectsRender<KustoChart>(VisualizationProperty);
        AffectsRender<KustoChart>(RevisionProperty);
    }

    /// <summary>
    /// Gets or sets the visualization to render.
    /// </summary>
    public KustoVisualizationViewModel? Visualization
    {
        get => GetValue(VisualizationProperty);
        set => SetValue(VisualizationProperty, value);
    }

    /// <summary>
    /// Gets or sets the interactive visualization revision that triggers redraws.
    /// </summary>
    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        hits.Clear();
        KustoVisualizationViewModel? visualization = Visualization;
        Rect plotBounds = GetPlotBounds();

        if (visualization is not null && plotBounds.Width > 1 && plotBounds.Height > 1)
        {
            using (context.PushClip(plotBounds))
            {
                DrawVisualization(context, plotBounds, visualization);
                DrawHoverIndicator(context, plotBounds);
            }
        }
    }

    /// <summary>
    /// Maps a normalized category to the center of its plot slot.
    /// </summary>
    /// <param name="start">The plot-axis origin.</param>
    /// <param name="length">The plot-axis length.</param>
    /// <param name="normalizedPosition">The category position from zero through one.</param>
    /// <param name="slotLength">The full category-slot length.</param>
    /// <returns>The centered plot-axis coordinate.</returns>
    internal static double MapCategoricalPosition(
        double start,
        double length,
        double normalizedPosition,
        double slotLength)
    {
        double boundedSlotLength = Math.Clamp(slotLength, 0, length);
        double inset = boundedSlotLength / 2;
        double availableLength = Math.Max(0, length - boundedSlotLength);
        return start + inset + (Math.Clamp(normalizedPosition, 0, 1) * availableLength);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        bool hoverChanged = hoveredAnchor is not null;
        hoveredAnchor = null;
        hoveredText = null;
        ToolTip.SetIsOpen(this, false);
        if (hoverChanged)
        {
            InvalidateVisual();
        }

        base.OnPointerExited(e);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        Point position = e.GetPosition(this);
        ChartHit? hit = FindHit(position);
        Point? updatedAnchor = hit?.Anchor;
        string? updatedText = hit?.Text;
        bool hoverChanged = hoveredAnchor != updatedAnchor;

        if (hit is not null)
        {
            if (!string.Equals(hoveredText, updatedText, StringComparison.Ordinal))
            {
                ToolTip.SetTip(this, updatedText);
                ToolTip.SetPlacement(this, PlacementMode.Pointer);
                ToolTip.SetIsOpen(this, true);
            }
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
        }

        hoveredAnchor = updatedAnchor;
        hoveredText = updatedText;
        if (hoverChanged)
        {
            InvalidateVisual();
        }

        base.OnPointerMoved(e);
    }

    private static double Normalize(double value, double minimum, double maximum, bool logarithmic)
    {
        double normalized;

        if (logarithmic && value > 0 && minimum > 0 && maximum > 0)
        {
            normalized = NormalizeLinear(Math.Log10(value), Math.Log10(minimum), Math.Log10(maximum));
        }
        else
        {
            normalized = NormalizeLinear(value, minimum, maximum);
        }

        return normalized;
    }

    private static double NormalizeLinear(double value, double minimum, double maximum)
    {
        double range = maximum - minimum;
        double normalized = Math.Abs(range) < double.Epsilon ? 0.5 : (value - minimum) / range;
        return Math.Clamp(normalized, 0, 1);
    }

    private static SolidColorBrush CreateFill(string colorHex, byte alpha = byte.MaxValue)
    {
        Color color = Color.Parse(colorHex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static string CreateToolTip(
        KustoChartSeriesViewModel series,
        KustoChartPointViewModel point)
    {
        return $"{point.XText}{Environment.NewLine}{series.Name}: {point.ValueText}";
    }

    private static StreamGeometry CreatePieGeometry(
        Point center,
        double radius,
        double startAngle,
        double endAngle)
    {
        Point start = GetCirclePoint(center, radius, startAngle);
        Point end = GetCirclePoint(center, radius, endAngle);
        bool isLargeArc = endAngle - startAngle > Math.PI;
        StreamGeometry geometry = new();

        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(center, true);
            geometryContext.LineTo(start);
            geometryContext.ArcTo(
                end,
                new Size(radius, radius),
                0,
                isLargeArc,
                SweepDirection.Clockwise);
            geometryContext.LineTo(center);
            geometryContext.EndFigure(true);
        }

        return geometry;
    }

    private static Point GetCirclePoint(Point center, double radius, double angle)
    {
        return new Point(
            center.X + (Math.Cos(angle) * radius),
            center.Y + (Math.Sin(angle) * radius));
    }

    private static Dictionary<double, double> GetTotalsByX(KustoVisualizationViewModel visualization)
    {
        Dictionary<double, double> totals = [];

        foreach (KustoChartPointViewModel point in visualization.VisibleSeries.SelectMany(series => series.Points))
        {
            totals.TryGetValue(point.X, out double total);
            totals[point.X] = total + Math.Max(0, point.Value);
        }

        return totals;
    }

    private static Rect GetColumnRect(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        KustoChartPointViewModel point,
        int seriesIndex,
        int seriesCount)
    {
        double categoryCount = Math.Max(1, visualization.VisibleSeries.SelectMany(series => series.Points).Select(item => item.X).Distinct().Count());
        double categorySlotWidth = plotBounds.Width / categoryCount;
        double groupWidth = Math.Min(categorySlotWidth, Math.Max(8, categorySlotWidth * 0.7));
        double normalizedX = Normalize(
            point.X,
            visualization.XMinimum,
            visualization.XMaximum,
            visualization.XAxisLogarithmic);
        double x = MapCategoricalPosition(plotBounds.Left, plotBounds.Width, normalizedX, categorySlotWidth);
        double y = MapY(plotBounds, visualization, point.Value);
        double baseline = MapY(plotBounds, visualization, 0);
        double columnWidth = Math.Max(2, groupWidth / Math.Max(1, seriesCount));
        double left = x - (groupWidth / 2) + (seriesIndex * columnWidth);

        return new Rect(left, Math.Min(y, baseline), columnWidth - 1, Math.Abs(baseline - y));
    }

    private static Rect GetBarRect(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        KustoChartPointViewModel point,
        int seriesIndex,
        int seriesCount)
    {
        double categoryCount = Math.Max(1, visualization.VisibleSeries.SelectMany(series => series.Points).Select(item => item.X).Distinct().Count());
        double categorySlotHeight = plotBounds.Height / categoryCount;
        double groupHeight = Math.Min(categorySlotHeight, Math.Max(8, categorySlotHeight * 0.7));
        double normalizedY = Normalize(
            point.X,
            visualization.XMinimum,
            visualization.XMaximum,
            visualization.XAxisLogarithmic);
        double x = MapYAsX(plotBounds, visualization, point.Value);
        double baseline = MapYAsX(plotBounds, visualization, 0);
        double y = MapCategoricalPosition(plotBounds.Top, plotBounds.Height, normalizedY, categorySlotHeight);
        double barHeight = Math.Max(2, groupHeight / Math.Max(1, seriesCount));
        double top = y - (groupHeight / 2) + (seriesIndex * barHeight);

        return new Rect(Math.Min(x, baseline), top, Math.Abs(x - baseline), barHeight - 1);
    }

    private static Point MapPoint(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        KustoChartPointViewModel point)
    {
        return new Point(
            MapX(plotBounds, visualization, point.X),
            MapY(plotBounds, visualization, point.Value));
    }

    private static double MapX(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        double value)
    {
        double position = Normalize(
            value,
            visualization.XMinimum,
            visualization.XMaximum,
            visualization.XAxisLogarithmic);
        return plotBounds.Left + (position * plotBounds.Width);
    }

    private static double MapY(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        double value)
    {
        double position = Normalize(
            value,
            visualization.YMinimum,
            visualization.YMaximum,
            visualization.YAxisLogarithmic);
        return plotBounds.Bottom - (position * plotBounds.Height);
    }

    private static double MapYAsX(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        double value)
    {
        double position = Normalize(
            value,
            visualization.YMinimum,
            visualization.YMaximum,
            visualization.YAxisLogarithmic);
        return plotBounds.Left + (position * plotBounds.Width);
    }

    private static void DrawAreaGeometry(
        DrawingContext context,
        string colorHex,
        List<Point> topPoints,
        List<Point> basePoints)
    {
        if (topPoints.Count > 0)
        {
            StreamGeometry geometry = new();
            using (StreamGeometryContext geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(topPoints[0], true);
                foreach (Point point in topPoints.Skip(1))
                {
                    geometryContext.LineTo(point);
                }

                foreach (Point point in basePoints.AsEnumerable().Reverse())
                {
                    geometryContext.LineTo(point);
                }

                geometryContext.EndFigure(true);
            }

            context.DrawGeometry(CreateFill(colorHex, 70), new Pen(CreateFill(colorHex), 1.5), geometry);
        }
    }

    private static Rect GetStackedColumnRect(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        KustoChartPointViewModel point,
        Dictionary<double, double> cumulativeValues,
        Dictionary<double, double> totals)
    {
        cumulativeValues.TryGetValue(point.X, out double baselineValue);
        double topValue = baselineValue + point.Value;
        cumulativeValues[point.X] = topValue;

        if (visualization.IsStacked100 && totals.TryGetValue(point.X, out double total) && total > 0)
        {
            baselineValue = (baselineValue / total) * 100;
            topValue = (topValue / total) * 100;
        }

        double categoryCount = Math.Max(1, visualization.VisibleSeries.SelectMany(series => series.Points).Select(item => item.X).Distinct().Count());
        double categorySlotWidth = plotBounds.Width / categoryCount;
        double width = Math.Min(categorySlotWidth, Math.Max(4, categorySlotWidth * 0.65));
        double normalizedX = Normalize(
            point.X,
            visualization.XMinimum,
            visualization.XMaximum,
            visualization.XAxisLogarithmic);
        double x = MapCategoricalPosition(plotBounds.Left, plotBounds.Width, normalizedX, categorySlotWidth);
        double top = MapY(plotBounds, visualization, topValue);
        double bottom = MapY(plotBounds, visualization, baselineValue);

        return new Rect(x - (width / 2), Math.Min(top, bottom), width, Math.Abs(bottom - top));
    }

    private static Rect GetStackedBarRect(
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        KustoChartPointViewModel point,
        Dictionary<double, double> cumulativeValues,
        Dictionary<double, double> totals)
    {
        cumulativeValues.TryGetValue(point.X, out double baselineValue);
        double rightValue = baselineValue + point.Value;
        cumulativeValues[point.X] = rightValue;

        if (visualization.IsStacked100 && totals.TryGetValue(point.X, out double total) && total > 0)
        {
            baselineValue = (baselineValue / total) * 100;
            rightValue = (rightValue / total) * 100;
        }

        double categoryCount = Math.Max(1, visualization.VisibleSeries.SelectMany(series => series.Points).Select(item => item.X).Distinct().Count());
        double categorySlotHeight = plotBounds.Height / categoryCount;
        double height = Math.Min(categorySlotHeight, Math.Max(4, categorySlotHeight * 0.65));
        double normalizedY = Normalize(
            point.X,
            visualization.XMinimum,
            visualization.XMaximum,
            visualization.XAxisLogarithmic);
        double left = MapYAsX(plotBounds, visualization, baselineValue);
        double right = MapYAsX(plotBounds, visualization, rightValue);
        double y = MapCategoricalPosition(plotBounds.Top, plotBounds.Height, normalizedY, categorySlotHeight);

        return new Rect(Math.Min(left, right), y - (height / 2), Math.Abs(right - left), height);
    }

    private void DrawVisualization(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        if (visualization.Kind is not KustoVisualizationKind.PieChart and not KustoVisualizationKind.TreeMap)
        {
            DrawGrid(context, plotBounds);
        }

        switch (visualization.Kind)
        {
            case KustoVisualizationKind.AreaChart:
                DrawAreas(context, plotBounds, visualization, false);
                break;
            case KustoVisualizationKind.StackedAreaChart:
                DrawAreas(context, plotBounds, visualization, true);
                break;
            case KustoVisualizationKind.BarChart:
                DrawBars(context, plotBounds, visualization);
                break;
            case KustoVisualizationKind.ColumnChart:
            case KustoVisualizationKind.PivotChart:
                DrawColumns(context, plotBounds, visualization);
                break;
            case KustoVisualizationKind.PieChart:
                DrawPie(context, plotBounds, visualization);
                break;
            case KustoVisualizationKind.ScatterChart:
            case KustoVisualizationKind.TimePivot:
                DrawPoints(context, plotBounds, visualization);
                break;
            case KustoVisualizationKind.TreeMap:
                DrawTreeMap(context, plotBounds, visualization);
                break;
            default:
                DrawLines(context, plotBounds, visualization);
                break;
        }
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        IBrush gridBrush = ResolveBrush("DecorativeDividerBrush", "#D4D8DE");
        Pen gridPen = new(gridBrush, 1);

        for (int division = 0; division <= 4; division++)
        {
            double x = bounds.Left + (bounds.Width * division / 4);
            double y = bounds.Top + (bounds.Height * division / 4);
            context.DrawLine(gridPen, new Point(x, bounds.Top), new Point(x, bounds.Bottom));
            context.DrawLine(gridPen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }
    }

    private void DrawLines(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        foreach (KustoChartSeriesViewModel series in visualization.VisibleSeries)
        {
            IBrush seriesBrush = CreateFill(series.ColorHex);
            Pen seriesPen = new(seriesBrush, 2);
            StreamGeometry geometry = new();

            using (StreamGeometryContext geometryContext = geometry.Open())
            {
                for (int index = 0; index < series.Points.Count; index++)
                {
                    Point point = MapPoint(plotBounds, visualization, series.Points[index]);
                    if (index == 0)
                    {
                        geometryContext.BeginFigure(point, false);
                    }
                    else
                    {
                        geometryContext.LineTo(point);
                    }
                }
            }

            context.DrawGeometry(null, seriesPen, geometry);
            DrawSeriesPoints(context, plotBounds, visualization, series, seriesBrush);
        }
    }

    private void DrawAreas(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        bool stacked)
    {
        Dictionary<double, double> cumulativeValues = [];
        Dictionary<double, double> totals = GetTotalsByX(visualization);

        foreach (KustoChartSeriesViewModel series in visualization.VisibleSeries)
        {
            List<Point> topPoints = [];
            List<Point> basePoints = [];

            foreach (KustoChartPointViewModel point in series.Points)
            {
                cumulativeValues.TryGetValue(point.X, out double baselineValue);
                double plottedValue = stacked ? baselineValue + point.Value : point.Value;
                double plottedBaseline = stacked ? baselineValue : Math.Clamp(0, visualization.YMinimum, visualization.YMaximum);

                if (visualization.IsStacked100 && totals.TryGetValue(point.X, out double total) && total > 0)
                {
                    plottedValue = (plottedValue / total) * 100;
                    plottedBaseline = (plottedBaseline / total) * 100;
                }

                topPoints.Add(new Point(MapX(plotBounds, visualization, point.X), MapY(plotBounds, visualization, plottedValue)));
                basePoints.Add(new Point(MapX(plotBounds, visualization, point.X), MapY(plotBounds, visualization, plottedBaseline)));
                cumulativeValues[point.X] = baselineValue + point.Value;
            }

            DrawAreaGeometry(context, series.ColorHex, topPoints, basePoints);
            DrawSeriesPoints(context, plotBounds, visualization, series, CreateFill(series.ColorHex));
        }
    }

    private void DrawSeriesPoints(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization,
        KustoChartSeriesViewModel series,
        IBrush seriesBrush)
    {
        foreach (KustoChartPointViewModel point in series.Points)
        {
            Point screenPoint = MapPoint(plotBounds, visualization, point);
            double radius = point.IsAnomaly ? 5 : 2.5;
            IBrush pointBrush = point.IsAnomaly
                ? ResolveBrush("ErrorBrush", "#D64545")
                : seriesBrush;
            context.DrawEllipse(pointBrush, null, screenPoint, radius, radius);
            hits.Add(ChartHit.ForRectangle(
                new Rect(screenPoint.X - 7, screenPoint.Y - 7, 14, 14),
                screenPoint,
                CreateToolTip(series, point)));
        }
    }

    private void DrawPoints(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        foreach (KustoChartSeriesViewModel series in visualization.VisibleSeries)
        {
            DrawSeriesPoints(context, plotBounds, visualization, series, CreateFill(series.ColorHex));
        }
    }

    private void DrawColumns(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        ObservableCollection<KustoChartSeriesViewModel> visibleSeries = visualization.VisibleSeries;
        int seriesCount = visualization.IsStacked ? 1 : visibleSeries.Count;
        Dictionary<double, double> cumulativeValues = [];
        Dictionary<double, double> totals = GetTotalsByX(visualization);

        for (int seriesIndex = 0; seriesIndex < visibleSeries.Count; seriesIndex++)
        {
            KustoChartSeriesViewModel series = visibleSeries[seriesIndex];
            IBrush fill = CreateFill(series.ColorHex, 220);

            foreach (KustoChartPointViewModel point in series.Points)
            {
                Rect rectangle = visualization.IsStacked
                    ? GetStackedColumnRect(plotBounds, visualization, point, cumulativeValues, totals)
                    : GetColumnRect(plotBounds, visualization, point, seriesIndex, seriesCount);
                context.DrawRectangle(fill, null, rectangle);
                hits.Add(ChartHit.ForRectangle(rectangle, rectangle.Center, CreateToolTip(series, point)));
            }
        }
    }

    private void DrawBars(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        ObservableCollection<KustoChartSeriesViewModel> visibleSeries = visualization.VisibleSeries;
        int seriesCount = visualization.IsStacked ? 1 : visibleSeries.Count;
        Dictionary<double, double> cumulativeValues = [];
        Dictionary<double, double> totals = GetTotalsByX(visualization);

        for (int seriesIndex = 0; seriesIndex < visibleSeries.Count; seriesIndex++)
        {
            KustoChartSeriesViewModel series = visibleSeries[seriesIndex];
            IBrush fill = CreateFill(series.ColorHex, 220);

            foreach (KustoChartPointViewModel point in series.Points)
            {
                Rect rectangle = visualization.IsStacked
                    ? GetStackedBarRect(plotBounds, visualization, point, cumulativeValues, totals)
                    : GetBarRect(plotBounds, visualization, point, seriesIndex, seriesCount);
                context.DrawRectangle(fill, null, rectangle);
                hits.Add(ChartHit.ForRectangle(rectangle, rectangle.Center, CreateToolTip(series, point)));
            }
        }
    }

    private void DrawPie(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        ObservableCollection<KustoChartSeriesViewModel> visibleSeries = visualization.VisibleSeries;
        double total = visibleSeries.Sum(series => Math.Max(0, series.Points[0].Value));
        Point center = plotBounds.Center;
        double radius = Math.Max(1, Math.Min(plotBounds.Width, plotBounds.Height) * 0.46);
        double startAngle = -Math.PI / 2;

        if (visibleSeries.Count == 1)
        {
            KustoChartSeriesViewModel series = visibleSeries[0];
            KustoChartPointViewModel point = series.Points[0];
            context.DrawEllipse(CreateFill(series.ColorHex), null, center, radius, radius);
            hits.Add(ChartHit.ForPie(
                center,
                radius,
                startAngle,
                startAngle + (Math.PI * 2),
                center,
                CreateToolTip(series, point)));
        }

        foreach (KustoChartSeriesViewModel series in visibleSeries.Skip(visibleSeries.Count == 1 ? 1 : 0))
        {
            KustoChartPointViewModel point = series.Points[0];
            double sweep = total > 0 ? Math.Max(0, point.Value) / total * Math.PI * 2 : 0;
            double endAngle = startAngle + sweep;
            StreamGeometry geometry = CreatePieGeometry(center, radius, startAngle, endAngle);
            context.DrawGeometry(CreateFill(series.ColorHex), new Pen(ResolveBrush("SurfaceBrush", "#FFFFFF"), 1), geometry);
            Point anchor = GetCirclePoint(center, radius * 0.65, startAngle + (sweep / 2));
            hits.Add(ChartHit.ForPie(center, radius, startAngle, endAngle, anchor, CreateToolTip(series, point)));
            startAngle = endAngle;
        }
    }

    private void DrawTreeMap(
        DrawingContext context,
        Rect plotBounds,
        KustoVisualizationViewModel visualization)
    {
        IReadOnlyList<KustoChartSeriesViewModel> visibleSeries = visualization.VisibleSeries;
        double total = visibleSeries.Sum(series => Math.Max(0, series.Points[0].Value));
        double left = plotBounds.Left;

        foreach (KustoChartSeriesViewModel series in visibleSeries)
        {
            KustoChartPointViewModel point = series.Points[0];
            double width = total > 0 ? Math.Max(0, point.Value) / total * plotBounds.Width : 0;
            Rect rectangle = new(left, plotBounds.Top, width, plotBounds.Height);
            context.DrawRectangle(CreateFill(series.ColorHex), new Pen(ResolveBrush("SurfaceBrush", "#FFFFFF"), 1), rectangle);
            hits.Add(ChartHit.ForRectangle(rectangle, rectangle.Center, CreateToolTip(series, point)));
            left += width;
        }
    }

    private void DrawHoverIndicator(DrawingContext context, Rect plotBounds)
    {
        if (hoveredAnchor is Point anchor)
        {
            IBrush focusBrush = ResolveBrush("FocusBrush", "#3675C8");
            context.DrawLine(
                new Pen(focusBrush, 1),
                new Point(anchor.X, plotBounds.Top),
                new Point(anchor.X, plotBounds.Bottom));
            context.DrawEllipse(focusBrush, null, anchor, 4, 4);
        }
    }

    private ChartHit? FindHit(Point position)
    {
        ChartHit? hit = hits.FirstOrDefault(candidate => candidate.Contains(position));

        if (hit is null && hits.Count > 0)
        {
            hit = hits.MinBy(candidate => Math.Abs(candidate.Anchor.X - position.X));
        }

        return hit;
    }

    private Rect GetPlotBounds()
    {
        const double Padding = 6;
        return new Rect(
            Padding,
            Padding,
            Math.Max(0, Bounds.Width - (Padding * 2)),
            Math.Max(0, Bounds.Height - (Padding * 2)));
    }

    private IBrush ResolveBrush(string resourceKey, string fallbackColor)
    {
        IBrush brush = new SolidColorBrush(Color.Parse(fallbackColor));
        if (TryGetResource(resourceKey, ActualThemeVariant, out object? resource)
            && resource is IBrush resourceBrush)
        {
            brush = resourceBrush;
        }

        return brush;
    }

    private sealed class ChartHit
    {
        private ChartHit(
            Rect bounds,
            Point anchor,
            string text,
            Point? pieCenter,
            double pieRadius,
            double startAngle,
            double endAngle)
        {
            Bounds = bounds;
            Anchor = anchor;
            Text = text;
            PieCenter = pieCenter;
            PieRadius = pieRadius;
            StartAngle = startAngle;
            EndAngle = endAngle;
        }

        public Rect Bounds { get; }

        public Point Anchor { get; }

        public string Text { get; }

        public Point? PieCenter { get; }

        public double PieRadius { get; }

        public double StartAngle { get; }

        public double EndAngle { get; }

        public static ChartHit ForRectangle(Rect bounds, Point anchor, string text)
        {
            return new ChartHit(bounds, anchor, text, null, 0, 0, 0);
        }

        public static ChartHit ForPie(
            Point center,
            double radius,
            double startAngle,
            double endAngle,
            Point anchor,
            string text)
        {
            Rect bounds = new(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            return new ChartHit(bounds, anchor, text, center, radius, startAngle, endAngle);
        }

        public bool Contains(Point point)
        {
            bool contains = Bounds.Contains(point);

            if (contains && PieCenter is Point center)
            {
                double deltaX = point.X - center.X;
                double deltaY = point.Y - center.Y;
                double distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                double angle = Math.Atan2(deltaY, deltaX);
                if (angle < -Math.PI / 2)
                {
                    angle += Math.PI * 2;
                }

                contains = distance <= PieRadius && angle >= StartAngle && angle <= EndAngle;
            }

            return contains;
        }
    }
}
