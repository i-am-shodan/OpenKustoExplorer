using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Desktop.Graphs;
using OpenKustoExplorer.Graph;
using SkiaSharp;

namespace OpenKustoExplorer.Desktop.Controls;

/// <summary>
/// Renders a bounded investigation graph through one interactive Skia drawing operation per frame.
/// </summary>
public sealed class KustoGraphControl : Control
{
    /// <summary>
    /// Identifies the <see cref="Layout"/> styled property.
    /// </summary>
    public static readonly StyledProperty<GraphLayout?> LayoutProperty =
        AvaloniaProperty.Register<KustoGraphControl, GraphLayout?>(nameof(Layout));

    /// <summary>
    /// Identifies the <see cref="SelectedEntity"/> styled property.
    /// </summary>
    public static readonly StyledProperty<GraphEntityKey?> SelectedEntityProperty =
        AvaloniaProperty.Register<KustoGraphControl, GraphEntityKey?>(
            nameof(SelectedEntity),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Identifies the <see cref="SelectedEntities"/> styled property.
    /// </summary>
    public static readonly StyledProperty<IList<GraphEntityKey>?> SelectedEntitiesProperty =
        AvaloniaProperty.Register<KustoGraphControl, IList<GraphEntityKey>?>(nameof(SelectedEntities));

    /// <summary>
    /// Identifies the <see cref="SelectedRelationship"/> styled property.
    /// </summary>
    public static readonly StyledProperty<GraphRelationshipKey?> SelectedRelationshipProperty =
        AvaloniaProperty.Register<KustoGraphControl, GraphRelationshipKey?>(
            nameof(SelectedRelationship),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Identifies the <see cref="ContextEntity"/> styled property.
    /// </summary>
    public static readonly StyledProperty<GraphEntityKey?> ContextEntityProperty =
        AvaloniaProperty.Register<KustoGraphControl, GraphEntityKey?>(
            nameof(ContextEntity),
            defaultBindingMode: BindingMode.TwoWay);

    private const double DragThreshold = 4;
    private const double FitPadding = 36;
    private const double MaximumScale = 3.5;
    private const double MinimumScale = 0.12;

    private readonly Dictionary<GraphRelationshipKey, KustoGraphCurveSegment[]> edgeCurves = [];
    private readonly Dictionary<GraphEntityKey, Vector> nodeOffsets = [];
    private INotifyCollectionChanged? selectedEntitiesNotifier;
    private Point dragOrigin;
    private GraphEntityKey? draggedEntity;
    private GraphEntityKey? hoveredEntity;
    private GraphRelationshipKey? hoveredRelationship;
    private GraphRelationshipKey? pressedRelationship;
    private Vector draggedNodeStartOffset;
    private Vector dragStartOffset;
    private bool edgeCurvesDirty = true;
    private bool fitPending = true;
    private bool hasDragged;
    private bool isDragging;
    private Size lastRenderSize;
    private Vector offset;
    private double scale = 1;

    static KustoGraphControl()
    {
        AffectsRender<KustoGraphControl>(LayoutProperty);
        AffectsRender<KustoGraphControl>(SelectedEntityProperty);
        AffectsRender<KustoGraphControl>(SelectedEntitiesProperty);
        AffectsRender<KustoGraphControl>(SelectedRelationshipProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphControl"/> class.
    /// </summary>
    public KustoGraphControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>
    /// Gets or sets the immutable bounded graph layout.
    /// </summary>
    public GraphLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>
    /// Gets or sets the graph entity highlighted on the canvas.
    /// </summary>
    public GraphEntityKey? SelectedEntity
    {
        get => GetValue(SelectedEntityProperty);
        set => SetValue(SelectedEntityProperty, value);
    }

    /// <summary>
    /// Gets or sets the graph entities highlighted on the canvas.
    /// </summary>
    public IList<GraphEntityKey>? SelectedEntities
    {
        get => GetValue(SelectedEntitiesProperty);
        set => SetValue(SelectedEntitiesProperty, value);
    }

    /// <summary>
    /// Gets or sets the graph relationship highlighted on the canvas.
    /// </summary>
    public GraphRelationshipKey? SelectedRelationship
    {
        get => GetValue(SelectedRelationshipProperty);
        set => SetValue(SelectedRelationshipProperty, value);
    }

    /// <summary>
    /// Gets or sets the graph entity targeted by the context menu.
    /// </summary>
    public GraphEntityKey? ContextEntity
    {
        get => GetValue(ContextEntityProperty);
        set => SetValue(ContextEntityProperty, value);
    }

    /// <summary>
    /// Fits the current graph layout inside the control bounds.
    /// </summary>
    public void FitToView()
    {
        fitPending = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Clears manual node positions and fits the automatic layout inside the control bounds.
    /// </summary>
    public void ResetNodePositions()
    {
        nodeOffsets.Clear();
        edgeCurvesDirty = true;
        FitToView();
    }

    /// <summary>
    /// Zooms in around the center of the control.
    /// </summary>
    public void ZoomIn() => ZoomAt(GetControlCenter(), 1.2);

    /// <summary>
    /// Zooms out around the center of the control.
    /// </summary>
    public void ZoomOut() => ZoomAt(GetControlCenter(), 1 / 1.2);

    /// <summary>
    /// Creates an immutable layout snapshot with the current manual node positions applied.
    /// </summary>
    /// <returns>The adjusted visible layout, or <see langword="null"/> when no layout is displayed.</returns>
    public GraphLayout? CreateExportLayout()
    {
        return Layout is GraphLayout graphLayout
            ? KustoGraphLayoutSnapshot.Create(graphLayout, nodeOffsets)
            : null;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        GraphLayout? graphLayout = Layout;

        if (graphLayout is null || graphLayout.IsEmpty || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        if (fitPending || lastRenderSize != Bounds.Size)
        {
            FitLayout(graphLayout);
            fitPending = false;
            lastRenderSize = Bounds.Size;
        }

        GraphPalette palette = CreatePalette();
        HashSet<GraphEntityKey> selectedEntities = SelectedEntities?.ToHashSet() ?? [];

        if (selectedEntities.Count == 0 && SelectedEntity is GraphEntityKey selectedEntity)
        {
            selectedEntities.Add(selectedEntity);
        }

        context.Custom(new GraphDrawOperation(
            new Rect(Bounds.Size),
            graphLayout,
            selectedEntities,
            SelectedRelationship,
            new Dictionary<GraphEntityKey, Vector>(nodeOffsets),
            new Dictionary<GraphRelationshipKey, KustoGraphCurveSegment[]>(GetEdgeCurves()),
            scale,
            offset,
            palette));
    }

    /// <summary>
    /// Finds the nearest graph node in one keyboard navigation direction.
    /// </summary>
    /// <param name="layout">The current graph layout.</param>
    /// <param name="selectedEntity">The current entity, or <see langword="null"/>.</param>
    /// <param name="key">An arrow key or <see cref="Key.Home"/>.</param>
    /// <param name="nodeOffsets">Optional manual node offsets.</param>
    /// <returns>The next entity, or <see langword="null"/> when no candidate exists.</returns>
    internal static GraphEntityKey? FindDirectionalNode(
        GraphLayout layout,
        GraphEntityKey? selectedEntity,
        Key key,
        IReadOnlyDictionary<GraphEntityKey, Vector>? nodeOffsets = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Nodes.Count == 0)
        {
            return null;
        }

        GraphLayoutNode firstNode = layout.Nodes
            .OrderBy(node => GetAdjustedCenter(node, nodeOffsets).Y)
            .ThenBy(node => GetAdjustedCenter(node, nodeOffsets).X)
            .First();
        if (key == Key.Home || selectedEntity is null)
        {
            return firstNode.Entity.Entity;
        }

        GraphLayoutNode? selectedNode = layout.Nodes.FirstOrDefault(
            node => node.Entity.Entity == selectedEntity.Value);
        if (selectedNode is null)
        {
            return firstNode.Entity.Entity;
        }

        Vector direction = key switch
        {
            Key.Left => new Vector(-1, 0),
            Key.Right => new Vector(1, 0),
            Key.Up => new Vector(0, -1),
            Key.Down => new Vector(0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        GraphLayoutPoint origin = GetAdjustedCenter(selectedNode, nodeOffsets);
        return layout.Nodes
            .Where(node => node.Entity.Entity != selectedEntity.Value)
            .Select(node =>
            {
                GraphLayoutPoint center = GetAdjustedCenter(node, nodeOffsets);
                Vector delta = new(center.X - origin.X, center.Y - origin.Y);
                double forward = (delta.X * direction.X) + (delta.Y * direction.Y);
                double perpendicular = Math.Abs((delta.X * direction.Y) - (delta.Y * direction.X));
                return new { Node = node, Forward = forward, Perpendicular = perpendicular };
            })
            .Where(candidate => candidate.Forward > 0)
            .OrderBy(candidate => candidate.Perpendicular <= candidate.Forward ? 0 : 1)
            .ThenBy(candidate =>
                (candidate.Forward * candidate.Forward)
                + (candidate.Perpendicular * candidate.Perpendicular))
            .ThenBy(candidate => candidate.Perpendicular / candidate.Forward)
            .Select(candidate => (GraphEntityKey?)candidate.Node.Entity.Entity)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
            case Key.Home:
                e.Handled = MoveKeyboardSelection(e.Key);
                break;
            case Key.Enter:
                e.Handled = SelectedEntity is not null || MoveKeyboardSelection(Key.Home);
                if (SelectedEntity is GraphEntityKey selectedEntity)
                {
                    ContextEntity = selectedEntity;
                }

                break;
            case Key.Apps:
                e.Handled = OpenKeyboardContextMenu();
                break;
            case Key.F10 when e.KeyModifiers == KeyModifiers.Shift:
                e.Handled = OpenKeyboardContextMenu();
                break;
            case Key.Add:
            case Key.OemPlus:
                ZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut();
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                FitToView();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (!isDragging)
        {
            hoveredEntity = null;
            hoveredRelationship = null;
            ToolTip.SetIsOpen(this, false);
        }

        base.OnPointerExited(e);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        Point position = e.GetPosition(this);

        if (isDragging)
        {
            Vector delta = position - dragOrigin;
            hasDragged |= Math.Abs(delta.X) >= DragThreshold || Math.Abs(delta.Y) >= DragThreshold;

            if (draggedEntity is GraphEntityKey entity)
            {
                nodeOffsets[entity] = draggedNodeStartOffset + new Vector(delta.X / scale, delta.Y / scale);
                edgeCurvesDirty = true;
            }
            else
            {
                offset = dragStartOffset + delta;
            }

            UpdateHoverTooltip(null, null);
            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            GraphLayoutNode? node = FindNode(position);
            GraphLayoutEdge? edge = node is null ? FindRelationship(position) : null;
            UpdateHoverTooltip(node, edge);
        }

        base.OnPointerMoved(e);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerPoint pointerPoint = e.GetCurrentPoint(this);

        if (pointerPoint.Properties.IsRightButtonPressed)
        {
            GraphLayoutNode? contextNode = FindNode(pointerPoint.Position);
            ContextEntity = contextNode?.Entity.Entity;

            if (ContextEntity is GraphEntityKey entity)
            {
                SelectNode(entity, e.KeyModifiers, true);
            }
            else if (FindRelationship(pointerPoint.Position) is GraphLayoutEdge edge)
            {
                SelectRelationship(edge.Relationship);
            }

            InvalidateVisual();
        }

        if (pointerPoint.Properties.IsLeftButtonPressed)
        {
            Focus();
            dragOrigin = pointerPoint.Position;
            GraphLayoutNode? node = FindNode(pointerPoint.Position);
            draggedEntity = node?.Entity.Entity;
            pressedRelationship = draggedEntity is null
                ? FindRelationship(pointerPoint.Position)?.Relationship
                : null;
            draggedNodeStartOffset = draggedEntity is GraphEntityKey entity
                && nodeOffsets.TryGetValue(entity, out Vector nodeOffset)
                    ? nodeOffset
                    : default;
            dragStartOffset = offset;
            hasDragged = false;
            isDragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        base.OnPointerPressed(e);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (isDragging)
        {
            Point position = e.GetPosition(this);
            isDragging = false;
            e.Pointer.Capture(null);

            if (!hasDragged)
            {
                GraphEntityKey? releasedEntity = draggedEntity ?? FindNode(position)?.Entity.Entity;
                if (releasedEntity is GraphEntityKey entity)
                {
                    SelectNode(entity, e.KeyModifiers, false);
                }
                else if (pressedRelationship is GraphRelationshipKey relationship)
                {
                    SelectRelationship(relationship);
                }
                else
                {
                    SelectNode(null, e.KeyModifiers, false);
                }
            }
            else if (draggedEntity is GraphEntityKey entity)
            {
                SelectNode(entity, e.KeyModifiers, false);
            }

            draggedEntity = null;
            pressedRelationship = null;
            InvalidateVisual();
            e.Handled = true;
        }

        base.OnPointerReleased(e);
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        double factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        ZoomAt(e.GetPosition(this), factor);
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LayoutProperty)
        {
            edgeCurvesDirty = true;
            fitPending = true;
            hoveredEntity = null;
            hoveredRelationship = null;
            ToolTip.SetIsOpen(this, false);
            RetainVisibleNodeOffsets();

            if (SelectedRelationship is GraphRelationshipKey relationship
                && Layout?.Edges.Any(edge => edge.Relationship == relationship) != true)
            {
                SelectedRelationship = null;
            }
        }
        else if (change.Property == SelectedEntitiesProperty)
        {
            if (selectedEntitiesNotifier is not null)
            {
                selectedEntitiesNotifier.CollectionChanged -= OnSelectedEntitiesCollectionChanged;
            }

            selectedEntitiesNotifier = SelectedEntities as INotifyCollectionChanged;

            if (selectedEntitiesNotifier is not null)
            {
                selectedEntitiesNotifier.CollectionChanged += OnSelectedEntitiesCollectionChanged;
            }
        }
    }

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

    private static GraphLayoutPoint GetAdjustedCenter(
        GraphLayoutNode node,
        IReadOnlyDictionary<GraphEntityKey, Vector>? nodeOffsets)
    {
        Vector nodeOffset = nodeOffsets is not null
            && nodeOffsets.TryGetValue(node.Entity.Entity, out Vector offsetValue)
                ? offsetValue
                : default;
        return new GraphLayoutPoint(node.Center.X + nodeOffset.X, node.Center.Y + nodeOffset.Y);
    }

    private void UpdateHoverTooltip(GraphLayoutNode? node, GraphLayoutEdge? edge)
    {
        GraphEntityKey? updatedEntity = node?.Entity.Entity;
        GraphRelationshipKey? updatedRelationship = edge?.Relationship;

        if (Nullable.Equals(hoveredEntity, updatedEntity)
            && Nullable.Equals(hoveredRelationship, updatedRelationship))
        {
            return;
        }

        hoveredEntity = updatedEntity;
        hoveredRelationship = updatedRelationship;

        if (node is not null)
        {
            ToolTip.SetTip(
                this,
                $"{node.Entity.DisplayLabel}{Environment.NewLine}{node.Entity.Entity}{Environment.NewLine}{node.Entity.Degree:N0} links");
            ToolTip.SetPlacement(this, PlacementMode.Pointer);
            ToolTip.SetIsOpen(this, true);
        }
        else if (edge is not null)
        {
            ToolTip.SetTip(
                this,
                $"{edge.Relationship.TypeName}{Environment.NewLine}{edge.Relationship.Source}{Environment.NewLine}to {edge.Relationship.Target}");
            ToolTip.SetPlacement(this, PlacementMode.Pointer);
            ToolTip.SetIsOpen(this, true);
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
        }
    }

    private void OnSelectedEntitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        InvalidateVisual();
    }

    private void SelectNode(
        GraphEntityKey? entity,
        KeyModifiers modifiers,
        bool preserveExistingContextSelection)
    {
        SelectedRelationship = null;
        IList<GraphEntityKey>? selectedEntities = SelectedEntities;
        bool isAdditive = modifiers.HasFlag(KeyModifiers.Control)
            || modifiers.HasFlag(KeyModifiers.Shift);

        if (entity is null)
        {
            selectedEntities?.Clear();
            SelectedEntity = null;
        }
        else if (selectedEntities is null)
        {
            SelectedEntity = entity;
        }
        else if (preserveExistingContextSelection && selectedEntities.Contains(entity.Value))
        {
            SelectedEntity = entity;
        }
        else if (isAdditive)
        {
            if (selectedEntities.Contains(entity.Value))
            {
                selectedEntities.Remove(entity.Value);
                SelectedEntity = selectedEntities.Count > 0 ? selectedEntities[^1] : null;
            }
            else
            {
                selectedEntities.Add(entity.Value);
                SelectedEntity = entity;
            }
        }
        else
        {
            selectedEntities.Clear();
            selectedEntities.Add(entity.Value);
            SelectedEntity = entity;
        }
    }

    private void SelectRelationship(GraphRelationshipKey relationship)
    {
        SelectedEntities?.Clear();
        SelectedEntity = null;
        SelectedRelationship = relationship;
    }

    private bool MoveKeyboardSelection(Key key)
    {
        if (Layout is not GraphLayout graphLayout)
        {
            return false;
        }

        GraphEntityKey? target = FindDirectionalNode(graphLayout, SelectedEntity, key, nodeOffsets);
        if (target is not GraphEntityKey entity)
        {
            return false;
        }

        SelectNode(entity, KeyModifiers.None, preserveExistingContextSelection: false);
        ContextEntity = entity;
        GraphLayoutNode? node = graphLayout.Nodes.FirstOrDefault(candidate => candidate.Entity.Entity == entity);
        if (node is not null)
        {
            AutomationProperties.SetHelpText(
                this,
                $"Selected {node.Entity.DisplayLabel}. Use arrow keys to navigate nodes, Enter to select, plus or minus to zoom, and zero to fit.");
        }

        InvalidateVisual();
        return true;
    }

    private bool OpenKeyboardContextMenu()
    {
        if (SelectedEntity is null && !MoveKeyboardSelection(Key.Home))
        {
            return false;
        }

        ContextEntity = SelectedEntity;
        if (ContextMenu is not ContextMenu contextMenu)
        {
            return false;
        }

        contextMenu.Open(this);
        return true;
    }

    private GraphPalette CreatePalette()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        return new GraphPalette(
            ResolveColor("DecorativeDividerBrush", dark ? "#3D4652" : "#CBD2DA"),
            ResolveColor("TextPrimaryBrush", dark ? "#F3F6F8" : "#18212B"),
            ResolveColor("TextSecondaryBrush", dark ? "#B7C0C9" : "#536170"),
            ResolveColor("AccentBrush", dark ? "#6AB7FF" : "#1769AA"),
            dark,
            dark ? new SKColor(28, 34, 42, 120) : new SKColor(255, 255, 255, 150));
    }

    private GraphLayoutNode? FindNode(Point screenPoint)
    {
        GraphLayout? graphLayout = Layout;
        if (graphLayout is null || scale <= 0)
        {
            return null;
        }

        double layoutX = (screenPoint.X - offset.X) / scale;
        double layoutY = (screenPoint.Y - offset.Y) / scale;

        for (int index = graphLayout.Nodes.Count - 1; index >= 0; index--)
        {
            GraphLayoutNode node = graphLayout.Nodes[index];
            Vector nodeOffset = GetNodeOffset(node.Entity.Entity);
            double nodeX = node.Center.X + nodeOffset.X;
            double nodeY = node.Center.Y + nodeOffset.Y;
            if (Math.Abs(layoutX - nodeX) <= node.Width / 2
                && Math.Abs(layoutY - nodeY) <= node.Height / 2)
            {
                return node;
            }
        }

        return null;
    }

    private GraphLayoutEdge? FindRelationship(Point screenPoint)
    {
        GraphLayout? graphLayout = Layout;
        GraphLayoutEdge? closestEdge = null;

        if (graphLayout is not null && scale > 0)
        {
            Dictionary<GraphRelationshipKey, KustoGraphCurveSegment[]> curves = GetEdgeCurves();
            GraphLayoutPoint point = new(
                (screenPoint.X - offset.X) / scale,
                (screenPoint.Y - offset.Y) / scale);
            double maximumDistance = 8 / scale;
            double closestDistanceSquared = maximumDistance * maximumDistance;

            foreach (GraphLayoutEdge edge in graphLayout.Edges)
            {
                double distanceSquared = KustoGraphCurveGeometry.GetDistanceSquared(
                    point,
                    curves[edge.Relationship]);

                if (distanceSquared <= closestDistanceSquared)
                {
                    closestEdge = edge;
                    closestDistanceSquared = distanceSquared;
                }
            }
        }

        return closestEdge;
    }

    private Dictionary<GraphRelationshipKey, KustoGraphCurveSegment[]> GetEdgeCurves()
    {
        if (edgeCurvesDirty)
        {
            edgeCurves.Clear();
            if (Layout is GraphLayout graphLayout)
            {
                Dictionary<GraphEntityKey, GraphLayoutNode> nodes = graphLayout.Nodes
                    .ToDictionary(node => node.Entity.Entity);
                foreach (GraphLayoutEdge edge in graphLayout.Edges)
                {
                    edgeCurves.Add(
                        edge.Relationship,
                        KustoGraphCurveGeometry.CreateSegments(
                            edge,
                            nodes[edge.Relationship.Source],
                            nodes[edge.Relationship.Target],
                            nodeOffsets));
                }
            }

            edgeCurvesDirty = false;
        }

        return edgeCurves;
    }

    private void FitLayout(GraphLayout graphLayout)
    {
        Rect layoutBounds = GetLayoutBounds(graphLayout);
        double availableWidth = Math.Max(1, Bounds.Width - (FitPadding * 2));
        double availableHeight = Math.Max(1, Bounds.Height - (FitPadding * 2));
        double widthScale = layoutBounds.Width > 0 ? availableWidth / layoutBounds.Width : 1;
        double heightScale = layoutBounds.Height > 0 ? availableHeight / layoutBounds.Height : 1;
        scale = Math.Clamp(Math.Min(widthScale, heightScale), MinimumScale, MaximumScale);
        offset = new Vector(
            ((Bounds.Width - (layoutBounds.Width * scale)) / 2) - (layoutBounds.X * scale),
            ((Bounds.Height - (layoutBounds.Height * scale)) / 2) - (layoutBounds.Y * scale));
    }

    private Rect GetLayoutBounds(GraphLayout graphLayout)
    {
        double left = 0;
        double top = 0;
        double right = graphLayout.Width;
        double bottom = graphLayout.Height;

        foreach (GraphLayoutNode node in graphLayout.Nodes)
        {
            Vector nodeOffset = GetNodeOffset(node.Entity.Entity);
            left = Math.Min(left, node.Center.X + nodeOffset.X - (node.Width / 2));
            top = Math.Min(top, node.Center.Y + nodeOffset.Y - (node.Height / 2));
            right = Math.Max(right, node.Center.X + nodeOffset.X + (node.Width / 2));
            bottom = Math.Max(bottom, node.Center.Y + nodeOffset.Y + (node.Height / 2));
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private Vector GetNodeOffset(GraphEntityKey entity)
    {
        return nodeOffsets.TryGetValue(entity, out Vector nodeOffset) ? nodeOffset : default;
    }

    private Point GetControlCenter() => new(Bounds.Width / 2, Bounds.Height / 2);

    private SKColor ResolveColor(string resourceKey, string fallback)
    {
        Color color = Color.Parse(fallback);
        if (TryGetResource(resourceKey, ActualThemeVariant, out object? resource)
            && resource is SolidColorBrush brush)
        {
            color = brush.Color;
        }

        return ToSkColor(color);
    }

    private void RetainVisibleNodeOffsets()
    {
        HashSet<GraphEntityKey> visibleEntities = Layout?.Nodes
            .Select(node => node.Entity.Entity)
            .ToHashSet() ?? [];
        GraphEntityKey[] removedEntities = nodeOffsets.Keys
            .Where(entity => !visibleEntities.Contains(entity))
            .ToArray();

        foreach (GraphEntityKey entity in removedEntities)
        {
            nodeOffsets.Remove(entity);
        }
    }

    private void ZoomAt(Point screenAnchor, double factor)
    {
        GraphLayout? graphLayout = Layout;
        if (graphLayout is null || graphLayout.IsEmpty)
        {
            return;
        }

        double previousScale = scale;
        double updatedScale = Math.Clamp(previousScale * factor, MinimumScale, MaximumScale);
        double layoutX = (screenAnchor.X - offset.X) / previousScale;
        double layoutY = (screenAnchor.Y - offset.Y) / previousScale;
        scale = updatedScale;
        offset = new Vector(
            screenAnchor.X - (layoutX * updatedScale),
            screenAnchor.Y - (layoutY * updatedScale));
        fitPending = false;
        InvalidateVisual();
    }

    private readonly struct GraphPalette
    {
        internal GraphPalette(
            SKColor edge,
            SKColor primaryText,
            SKColor secondaryText,
            SKColor accent,
            bool isDark,
            SKColor labelBackground)
        {
            Edge = edge;
            PrimaryText = primaryText;
            SecondaryText = secondaryText;
            Accent = accent;
            IsDark = isDark;
            LabelBackground = labelBackground;
        }

        internal SKColor Accent { get; }

        internal SKColor Edge { get; }

        internal bool IsDark { get; }

        internal SKColor LabelBackground { get; }

        internal SKColor PrimaryText { get; }

        internal SKColor SecondaryText { get; }
    }

    private sealed class GraphDrawOperation : ICustomDrawOperation
    {
        private readonly IReadOnlyDictionary<GraphRelationshipKey, KustoGraphCurveSegment[]> edgeCurves;
        private readonly GraphLayout layout;
        private readonly IReadOnlyDictionary<GraphEntityKey, Vector> nodeOffsets;
        private readonly Vector offset;
        private readonly GraphPalette palette;
        private readonly double scale;
        private readonly IReadOnlySet<GraphEntityKey> selectedEntities;
        private readonly GraphRelationshipKey? selectedRelationship;

        internal GraphDrawOperation(
            Rect bounds,
            GraphLayout layout,
            IReadOnlySet<GraphEntityKey> selectedEntities,
            GraphRelationshipKey? selectedRelationship,
            IReadOnlyDictionary<GraphEntityKey, Vector> nodeOffsets,
            IReadOnlyDictionary<GraphRelationshipKey, KustoGraphCurveSegment[]> edgeCurves,
            double scale,
            Vector offset,
            GraphPalette palette)
        {
            Bounds = bounds;
            this.layout = layout;
            this.selectedEntities = selectedEntities;
            this.selectedRelationship = selectedRelationship;
            this.nodeOffsets = nodeOffsets;
            this.edgeCurves = edgeCurves;
            this.scale = scale;
            this.offset = offset;
            this.palette = palette;
        }

        public Rect Bounds { get; }

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            int restoreCount = canvas.Save();

            try
            {
                canvas.ClipRect(SKRect.Create((float)Bounds.Width, (float)Bounds.Height));
                DrawGrid(canvas);
                canvas.Translate((float)offset.X, (float)offset.Y);
                canvas.Scale((float)scale);
                DrawEdges(canvas);
                DrawNodes(canvas);
            }
            finally
            {
                canvas.RestoreToCount(restoreCount);
            }
        }

        private static SKColor GetAutomaticAccent(int colorIndex, bool dark)
        {
            return (dark, colorIndex) switch
            {
                (true, 0) => new SKColor(104, 174, 235),
                (true, 1) => new SKColor(91, 190, 135),
                (true, 2) => new SKColor(235, 112, 116),
                (true, 3) => new SKColor(224, 177, 75),
                (true, 4) => new SKColor(178, 136, 232),
                (true, 5) => new SKColor(80, 190, 199),
                (true, 6) => new SKColor(220, 115, 174),
                (true, 7) => new SKColor(163, 185, 86),
                (true, 8) => new SKColor(132, 145, 238),
                (true, 9) => new SKColor(224, 139, 83),
                (false, 0) => new SKColor(42, 112, 171),
                (false, 1) => new SKColor(38, 125, 82),
                (false, 2) => new SKColor(180, 66, 71),
                (false, 3) => new SKColor(151, 103, 14),
                (false, 4) => new SKColor(112, 72, 163),
                (false, 5) => new SKColor(32, 122, 130),
                (false, 6) => new SKColor(160, 58, 112),
                (false, 7) => new SKColor(98, 119, 30),
                (false, 8) => new SKColor(72, 85, 173),
                _ => new SKColor(164, 83, 38),
            };
        }

        private static SKColor GetAutomaticFill(int colorIndex, bool dark)
        {
            return (dark, colorIndex) switch
            {
                (true, 0) => new SKColor(30, 67, 105),
                (true, 1) => new SKColor(31, 78, 58),
                (true, 2) => new SKColor(100, 43, 46),
                (true, 3) => new SKColor(91, 68, 22),
                (true, 4) => new SKColor(69, 49, 99),
                (true, 5) => new SKColor(20, 75, 82),
                (true, 6) => new SKColor(91, 42, 75),
                (true, 7) => new SKColor(68, 76, 30),
                (true, 8) => new SKColor(48, 55, 104),
                (true, 9) => new SKColor(96, 55, 32),
                (false, 0) => new SKColor(217, 235, 253),
                (false, 1) => new SKColor(216, 244, 228),
                (false, 2) => new SKColor(255, 224, 222),
                (false, 3) => new SKColor(255, 240, 199),
                (false, 4) => new SKColor(238, 226, 252),
                (false, 5) => new SKColor(211, 243, 245),
                (false, 6) => new SKColor(249, 222, 239),
                (false, 7) => new SKColor(237, 244, 207),
                (false, 8) => new SKColor(226, 230, 253),
                _ => new SKColor(252, 229, 213),
            };
        }

        private static int GetNodeColorIndex(GraphEntityKey entity)
        {
            return GraphTypeColor.GetIndex(entity.TypeName);
        }

        private static int GetRelationshipColorIndex(GraphRelationshipKey relationship)
        {
            return GraphTypeColor.GetIndex(relationship.TypeName);
        }

        private static string TrimText(string text, float maximumWidth, SKFont font, SKPaint paint)
        {
            if (font.MeasureText(text, paint) <= maximumWidth)
            {
                return text;
            }

            const string Suffix = "...";
            int length = text.Length;
            while (length > 1 && font.MeasureText(string.Concat(text.AsSpan(0, length), Suffix), paint) > maximumWidth)
            {
                length--;
            }

            return string.Concat(text.AsSpan(0, length), Suffix);
        }

        private void DrawEdgeLabel(
            SKCanvas canvas,
            GraphLayoutEdge edge,
            KustoGraphCurveSegment[] segments,
            SKFont font,
            SKPaint textPaint,
            SKPaint backgroundPaint)
        {
            GraphLayoutPoint midpoint = FindEdgeLabelPoint(segments);
            string label = TrimText(edge.Relationship.TypeName, 120, font, textPaint);
            float width = font.MeasureText(label, textPaint);
            SKRect background = SKRect.Create(
                (float)midpoint.X - (width / 2) - 4,
                (float)midpoint.Y - 10,
                width + 8,
                15);
            canvas.DrawRoundRect(background, 3, 3, backgroundPaint);
            canvas.DrawText(label, (float)midpoint.X - (width / 2), (float)midpoint.Y + 1, font, textPaint);
        }

        private GraphLayoutPoint FindEdgeLabelPoint(KustoGraphCurveSegment[] segments)
        {
            GraphLayoutPoint bestPoint = KustoGraphCurveGeometry.GetPoint(
                segments[segments.Length / 2],
                0.5);
            double bestClearance = double.NegativeInfinity;

            foreach (KustoGraphCurveSegment segment in segments)
            {
                GraphLayoutPoint candidate = KustoGraphCurveGeometry.GetPoint(segment, 0.5);
                double minimumClearance = double.PositiveInfinity;

                foreach (GraphLayoutNode node in layout.Nodes)
                {
                    GraphLayoutPoint nodeCenter = GetNodeCenter(node);
                    double deltaX = Math.Max(0, Math.Abs(candidate.X - nodeCenter.X) - (node.Width / 2));
                    double deltaY = Math.Max(0, Math.Abs(candidate.Y - nodeCenter.Y) - (node.Height / 2));
                    double clearance = (deltaX * deltaX) + (deltaY * deltaY);
                    minimumClearance = Math.Min(minimumClearance, clearance);
                }

                if (minimumClearance > bestClearance)
                {
                    bestPoint = candidate;
                    bestClearance = minimumClearance;
                }
            }

            return bestPoint;
        }

        private void DrawArrow(
            SKCanvas canvas,
            KustoGraphCurveSegment segment,
            SKPaint paint)
        {
            GraphLayoutPoint end = segment.End;
            double deltaX = end.X - segment.SecondControl.X;
            double deltaY = end.Y - segment.SecondControl.Y;
            double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

            if (length <= double.Epsilon)
            {
                return;
            }

            double directionX = deltaX / length;
            double directionY = deltaY / length;
            double arrowLength = 8 / scale;
            double arrowWidth = 4 / scale;
            float baseX = (float)(end.X - (directionX * arrowLength));
            float baseY = (float)(end.Y - (directionY * arrowLength));
            using SKPath arrow = new();
            arrow.MoveTo((float)end.X, (float)end.Y);
            arrow.LineTo(
                (float)(baseX + (-directionY * arrowWidth)),
                (float)(baseY + (directionX * arrowWidth)));
            arrow.LineTo(
                (float)(baseX - (-directionY * arrowWidth)),
                (float)(baseY - (directionX * arrowWidth)));
            arrow.Close();
            SKPaintStyle previousStyle = paint.Style;
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawPath(arrow, paint);
            paint.Style = previousStyle;
        }

        private void DrawEdges(SKCanvas canvas)
        {
            float strokeWidth = (float)(1.35 / scale);
            using SKPaint edgePaint = new()
            {
                Color = palette.Edge,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };
            bool showLabels = scale >= 0.58 && layout.Edges.Count <= 100;
            using SKFont labelFont = new(SKTypeface.Default, (float)(10 / scale));
            using SKPaint labelPaint = new()
            {
                Color = palette.SecondaryText,
                IsAntialias = true,
            };
            using SKPaint labelBackgroundPaint = new()
            {
                Color = palette.LabelBackground,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            foreach (GraphLayoutEdge edge in layout.Edges)
            {
                bool isSelected = selectedRelationship == edge.Relationship;
                SKColor relationshipColor = GetAutomaticAccent(
                    GetRelationshipColorIndex(edge.Relationship),
                    palette.IsDark);
                SKColor selectedColor = relationshipColor.WithAlpha(palette.IsDark ? (byte)210 : (byte)190);
                double selectedWidth = 1.35;

                if (isSelected)
                {
                    selectedColor = palette.Accent;
                    selectedWidth = 3.2;
                }

                edgePaint.Color = selectedColor;
                edgePaint.StrokeWidth = (float)(selectedWidth / scale);
                labelPaint.Color = selectedColor;
                KustoGraphCurveSegment[] segments = edgeCurves[edge.Relationship];
                using SKPath path = new();
                GraphLayoutPoint first = segments[0].Start;
                path.MoveTo((float)first.X, (float)first.Y);

                foreach (KustoGraphCurveSegment segment in segments)
                {
                    path.CubicTo(
                        (float)segment.FirstControl.X,
                        (float)segment.FirstControl.Y,
                        (float)segment.SecondControl.X,
                        (float)segment.SecondControl.Y,
                        (float)segment.End.X,
                        (float)segment.End.Y);
                }

                canvas.DrawPath(path, edgePaint);
                DrawArrow(canvas, segments[^1], edgePaint);

                if (showLabels)
                {
                    DrawEdgeLabel(canvas, edge, segments, labelFont, labelPaint, labelBackgroundPaint);
                }
            }
        }

        private void DrawGrid(SKCanvas canvas)
        {
            const float Spacing = 32;
            SKColor gridColor = palette.Edge.WithAlpha(palette.IsDark ? (byte)35 : (byte)45);
            using SKPaint gridPaint = new()
            {
                Color = gridColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            float startX = (float)(offset.X % Spacing);
            float startY = (float)(offset.Y % Spacing);
            for (float x = startX; x < Bounds.Width; x += Spacing)
            {
                for (float y = startY; y < Bounds.Height; y += Spacing)
                {
                    canvas.DrawCircle(x, y, 0.7f, gridPaint);
                }
            }
        }

        private void DrawNodes(SKCanvas canvas)
        {
            float borderWidth = (float)(1.2 / scale);
            using SKPaint fillPaint = new()
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using SKPaint borderPaint = new()
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = borderWidth,
            };
            using SKFont labelFont = new(SKTypeface.Default, 12);
            using SKFont metadataFont = new(SKTypeface.Default, 9);
            using SKPaint primaryTextPaint = new()
            {
                Color = palette.PrimaryText,
                IsAntialias = true,
            };
            using SKPaint secondaryTextPaint = new()
            {
                Color = palette.SecondaryText,
                IsAntialias = true,
            };

            foreach (GraphLayoutNode node in layout.Nodes)
            {
                GraphLayoutPoint nodeCenter = GetNodeCenter(node);
                int colorIndex = GetNodeColorIndex(node.Entity.Entity);
                bool isSelected = selectedEntities.Contains(node.Entity.Entity);
                fillPaint.Color = GetAutomaticFill(colorIndex, palette.IsDark);
                borderPaint.Color = isSelected
                    ? palette.Accent
                    : GetAutomaticAccent(colorIndex, palette.IsDark);
                double nodeBorderWidth = 1.2;

                if (isSelected)
                {
                    nodeBorderWidth = 2.6;
                }
                else if (node.IsCenter)
                {
                    nodeBorderWidth = 1.8;
                }

                borderPaint.StrokeWidth = (float)(nodeBorderWidth / scale);
                SKRect rectangle = SKRect.Create(
                    (float)(nodeCenter.X - (node.Width / 2)),
                    (float)(nodeCenter.Y - (node.Height / 2)),
                    (float)node.Width,
                    (float)node.Height);
                canvas.DrawRoundRect(rectangle, 7, 7, fillPaint);
                canvas.DrawRoundRect(rectangle, 7, 7, borderPaint);

                if (scale >= 0.34)
                {
                    float textLeft = rectangle.Left + 10;
                    float textWidth = rectangle.Width - 20;
                    string label = TrimText(node.Entity.DisplayLabel, textWidth, labelFont, primaryTextPaint);
                    string metadata = $"{node.Entity.Entity.TypeName} · {node.Entity.Degree:N0} links";
                    metadata = TrimText(metadata, textWidth, metadataFont, secondaryTextPaint);
                    canvas.DrawText(label, textLeft, rectangle.Top + 21, labelFont, primaryTextPaint);
                    canvas.DrawText(metadata, textLeft, rectangle.Top + 39, metadataFont, secondaryTextPaint);
                }
            }
        }

        private GraphLayoutPoint GetNodeCenter(GraphLayoutNode node)
        {
            Vector nodeOffset = GetNodeOffset(node.Entity.Entity);
            return new GraphLayoutPoint(node.Center.X + nodeOffset.X, node.Center.Y + nodeOffset.Y);
        }

        private Vector GetNodeOffset(GraphEntityKey entity)
        {
            return nodeOffsets.TryGetValue(entity, out Vector nodeOffset) ? nodeOffset : default;
        }
    }
}
