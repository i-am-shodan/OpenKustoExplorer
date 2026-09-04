using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one typed openCypher result value without discarding graph identity.
/// </summary>
public sealed class GraphQueryCellViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryCellViewModel"/> class.
    /// </summary>
    /// <param name="value">The immutable query value.</param>
    /// <param name="displayWidth">The shared column width.</param>
    /// <param name="selectAction">Selects graph identities projected by this cell.</param>
    public GraphQueryCellViewModel(
        GraphQueryValue value,
        double displayWidth,
        Action<GraphQueryCellViewModel> selectAction)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(selectAction);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayWidth);
        Text = value.Kind == GraphQueryValueKind.Null ? "null" : value.DisplayText;
        Kind = value.Kind;
        Entity = value.Entity;
        Relationship = value.Relationship;
        DisplayWidth = displayWidth;
        SelectCommand = new RelayCommand(() => selectAction(this));
    }

    /// <summary>
    /// Gets invariant display text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the typed value kind.
    /// </summary>
    public GraphQueryValueKind Kind { get; }

    /// <summary>
    /// Gets the entity identity when this is an entity result.
    /// </summary>
    public GraphEntityKey? Entity { get; }

    /// <summary>
    /// Gets the relationship identity when this is a relationship result.
    /// </summary>
    public GraphRelationshipKey? Relationship { get; }

    /// <summary>
    /// Gets the shared column width.
    /// </summary>
    public double DisplayWidth { get; }

    /// <summary>
    /// Gets the command that selects this cell's entity or relationship on the canvas.
    /// </summary>
    public IRelayCommand SelectCommand { get; }
}
