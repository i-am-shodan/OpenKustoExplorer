using Avalonia.Automation.Peers;
using AvaloniaEdit;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Provides the KQL editor with a desktop accessibility automation peer.
/// </summary>
public sealed class AccessibleKustoTextEditor : TextEditor
{
    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        KustoTextEditorAutomationPeer automationPeer = new(this);
        return automationPeer;
    }
}
