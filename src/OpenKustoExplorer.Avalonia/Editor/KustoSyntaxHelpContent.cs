using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Presents concise, theme-aware contextual KQL help.
/// </summary>
internal sealed class KustoSyntaxHelpContent : Grid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoSyntaxHelpContent"/> class.
    /// </summary>
    /// <param name="help">The syntax help to present.</param>
    /// <param name="isCompact">Whether to use the compact hover presentation.</param>
    internal KustoSyntaxHelpContent(KustoSyntaxHelp help, bool isCompact)
    {
        ArgumentNullException.ThrowIfNull(help);

        MaxWidth = isCompact ? 420 : double.PositiveInfinity;
        MinWidth = isCompact ? 300 : 0;
        RowDefinitions = new RowDefinitions(isCompact ? "Auto,Auto,*" : "Auto,Auto,*,Auto");
        RowSpacing = isCompact ? 6 : 12;

        TextBlock title = new()
        {
            FontSize = isCompact ? 14 : 18,
            FontWeight = FontWeight.SemiBold,
            Text = help.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetName(title, $"KQL help for {help.Title}");

        TextBlock kind = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = help.Kind.ToUpperInvariant(),
        };
        kind.Classes.Add("sectionLabel");

        Grid heading = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
        };
        heading.Children.Add(title);
        Grid.SetColumn(kind, 1);
        heading.Children.Add(kind);
        Children.Add(heading);

        TextBox signature = new()
        {
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Noto Sans Mono, monospace"),
            FontSize = isCompact ? 11 : 12,
            IsReadOnly = true,
            IsVisible = help.Signature is not null,
            Padding = new Thickness(8, 5),
            Text = help.Signature,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(signature, "KQL syntax signature");
        Grid.SetRow(signature, 1);
        Children.Add(signature);

        TextBlock description = new()
        {
            FontSize = isCompact ? 12 : 13,
            Text = help.Description,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(description, "KQL syntax description");
        Grid.SetRow(description, 2);
        Children.Add(description);

        if (!isCompact && help.DocumentationUri is not null)
        {
            HyperlinkButton documentationLink = new()
            {
                Content = $"Open {help.Title} documentation on Microsoft Learn",
                HorizontalAlignment = HorizontalAlignment.Left,
                NavigateUri = help.DocumentationUri,
            };
            AutomationProperties.SetName(
                documentationLink,
                $"Open {help.Title} documentation on Microsoft Learn");
            Grid.SetRow(documentationLink, 3);
            Children.Add(documentationLink);
        }
    }
}
