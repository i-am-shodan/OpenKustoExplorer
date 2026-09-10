using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Hosts reusable modeless KQL syntax help while leaving the query editor interactive.
/// </summary>
internal sealed class KustoSyntaxHelpWindow : Window
{
    private bool allowClose;
    private bool isClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoSyntaxHelpWindow"/> class.
    /// </summary>
    internal KustoSyntaxHelpWindow()
    {
        CanResize = true;
        Height = 280;
        MinHeight = 190;
        MinWidth = 380;
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closed += OnClosed;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Occurs when F1 is pressed while the help window has keyboard focus.
    /// </summary>
    internal event EventHandler? RefreshRequested;

    /// <summary>
    /// Updates and presents help without activating or blocking the owning editor window.
    /// </summary>
    /// <param name="help">The contextual syntax help.</param>
    /// <param name="owner">The editor window that owns this tool window.</param>
    /// <param name="reveal">Whether to force the native window back in front of its owner.</param>
    internal void ShowHelp(KustoSyntaxHelp help, Window owner, bool reveal = false)
    {
        ArgumentNullException.ThrowIfNull(help);
        ArgumentNullException.ThrowIfNull(owner);

        ShowContent(
            new KustoSyntaxHelpContent(help, isCompact: false)
            {
                Margin = new Thickness(18),
            },
            $"KQL help - {help.Title}",
            owner,
            reveal);
    }

    /// <summary>
    /// Presents the help window immediately while contextual help is being resolved.
    /// </summary>
    /// <param name="owner">The editor window that owns this tool window.</param>
    internal void ShowLoading(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ProgressBar progress = new()
        {
            Width = 240,
            IsIndeterminate = true,
        };
        TextBlock status = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "Loading KQL help",
        };
        StackPanel content = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
        };
        content.Children.Add(progress);
        content.Children.Add(status);
        ShowContent(content, "KQL help", owner, reveal: true);
    }

    /// <summary>
    /// Reports that no contextual help is available at the requested position.
    /// </summary>
    /// <param name="owner">The editor window that owns this tool window.</param>
    internal void ShowUnavailable(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        TextBlock status = new()
        {
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "No KQL help is available for the syntax at the caret.",
            TextWrapping = TextWrapping.Wrap,
        };
        ShowContent(status, "KQL help", owner, reveal: false);
    }

    /// <summary>
    /// Hides the reusable help window immediately.
    /// </summary>
    internal void Dismiss()
    {
        Hide();
    }

    /// <summary>
    /// Permanently closes the native window during editor disposal.
    /// </summary>
    internal void ClosePermanently()
    {
        if (isClosed)
        {
            return;
        }

        allowClose = true;
        Closed -= OnClosed;
        Closing -= OnClosing;
        KeyDown -= OnKeyDown;
        Close();
    }

    private void ShowContent(Control content, string title, Window owner, bool reveal)
    {
        Title = title;
        Content = content;

        if (reveal && IsVisible)
        {
            Hide();
        }

        if (!IsVisible)
        {
            Show(owner);
        }

        if (reveal)
        {
            Activate();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        isClosed = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArguments)
    {
        _ = sender;
        if (!allowClose && eventArguments.CloseReason != WindowCloseReason.OwnerWindowClosing)
        {
            eventArguments.Cancel = true;
            Hide();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        _ = sender;
        if (KustoSyntaxHelpInteraction.IsRequestKey(
            eventArguments.Key,
            eventArguments.KeyModifiers))
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
            eventArguments.Handled = true;
            return;
        }

        if (KustoSyntaxHelpInteraction.IsDismissKey(
            eventArguments.Key,
            eventArguments.KeyModifiers))
        {
            Dismiss();
            eventArguments.Handled = true;
        }
    }
}
