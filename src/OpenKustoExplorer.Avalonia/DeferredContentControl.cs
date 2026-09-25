using Avalonia;
using Avalonia.Controls;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Materializes templated content only while the owning workflow requires it.
/// </summary>
public sealed class DeferredContentControl : ContentControl
{
    /// <summary>
    /// Defines the <see cref="IsContentMaterialized"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsContentMaterializedProperty =
        AvaloniaProperty.Register<DeferredContentControl, bool>(nameof(IsContentMaterialized));

    /// <summary>
    /// Defines the <see cref="RetainContent"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> RetainContentProperty =
        AvaloniaProperty.Register<DeferredContentControl, bool>(nameof(RetainContent));

    private bool hasMaterializedContent;

    /// <summary>
    /// Gets or sets a value indicating whether the deferred content is materialized.
    /// </summary>
    public bool IsContentMaterialized
    {
        get => GetValue(IsContentMaterializedProperty);
        set => SetValue(IsContentMaterializedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether content remains materialized after first activation.
    /// </summary>
    public bool RetainContent
    {
        get => GetValue(RetainContentProperty);
        set => SetValue(RetainContentProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (IsContentMaterialized)
        {
            Content = DataContext;
            hasMaterializedContent = true;
        }
        else if (!RetainContent)
        {
            Content = null;
            hasMaterializedContent = false;
        }
        else if (change.Property == DataContextProperty && hasMaterializedContent)
        {
            Content = DataContext;
        }
    }
}
