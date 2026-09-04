using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Displays selectable diagnostic details after an unexpected application failure.
/// </summary>
internal sealed class CrashReportWindow : Window
{
    private readonly TextBlock copyStatus;
    private readonly string reportText;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrashReportWindow"/> class.
    /// </summary>
    /// <param name="reportText">The sanitized diagnostic report.</param>
    /// <param name="reportPath">The optional recovery-file path.</param>
    internal CrashReportWindow(string reportText, string? reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportText);
        this.reportText = reportText;
        Title = "Open Kusto Explorer - Unexpected error";
        Width = 900;
        Height = 650;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        TextBlock heading = new()
        {
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Text = "Open Kusto Explorer encountered an unexpected error",
        };
        TextBlock guidance = new()
        {
            Text = "Copy the diagnostic details into a bug report, then exit the application. "
                + "Common personal paths and credential-shaped values have been redacted, but review the report before sharing it.",
            TextWrapping = TextWrapping.Wrap,
        };
        TextBox details = new()
        {
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11,
            IsReadOnly = true,
            Text = reportText,
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(details, "Crash report details");
        ScrollViewer.SetHorizontalScrollBarVisibility(details, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(details, ScrollBarVisibility.Auto);

        copyStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = reportPath is null
                ? "Select the report text and press Ctrl+C if the copy button is unavailable."
                : $"Recovery copy: {CrashReportFormatter.SanitizeForDisplay(reportPath)}",
            TextWrapping = TextWrapping.Wrap,
        };
        Button copyButton = new()
        {
            Content = "Copy details",
            MinWidth = 110,
        };
        AutomationProperties.SetName(copyButton, "Copy crash report details");
        copyButton.Click += OnCopyDetailsClick;
        Button exitButton = new()
        {
            Content = "Exit",
            MinWidth = 80,
        };
        exitButton.Click += (_, _) => Close();

        StackPanel buttons = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(exitButton);

        Grid footer = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
        };
        footer.Children.Add(copyStatus);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        Grid content = new()
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12,
        };
        content.Children.Add(heading);
        Grid.SetRow(guidance, 1);
        content.Children.Add(guidance);
        Grid.SetRow(details, 2);
        content.Children.Add(details);
        Grid.SetRow(footer, 3);
        content.Children.Add(footer);
        Content = content;
    }

    [SuppressMessage(
        "Major Code Smell",
        "S3168",
        Justification = "Avalonia click handlers require void; all exceptions are contained here.")]
    private async void OnCopyDetailsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        try
        {
            IClipboard clipboard = Clipboard
                ?? throw new InvalidOperationException("The system clipboard is unavailable.");
            DataTransfer transfer = new();
            transfer.Add(DataTransferItem.CreateText(reportText));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
            copyStatus.Text = "Copied diagnostic details.";
        }
        catch (Exception exception)
        {
            copyStatus.Text = $"Copy failed: {exception.Message}. Select the report text and press Ctrl+C.";
        }
    }
}
