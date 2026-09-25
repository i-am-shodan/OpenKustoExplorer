using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Exposes the KQL document through the cross-platform automation value pattern.
/// </summary>
internal sealed class KustoTextEditorAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private readonly AccessibleKustoTextEditor editor;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoTextEditorAutomationPeer"/> class.
    /// </summary>
    /// <param name="editor">The editor represented by this peer.</param>
    public KustoTextEditorAutomationPeer(AccessibleKustoTextEditor editor)
        : base(editor)
    {
        this.editor = editor;
        editor.TextChanged += OnEditorTextChanged;
    }

    /// <inheritdoc />
    bool IValueProvider.IsReadOnly => editor.IsReadOnly;

    /// <inheritdoc />
    string? IValueProvider.Value => editor.Text;

    /// <inheritdoc />
    void IValueProvider.SetValue(string? value)
    {
        if (!editor.IsReadOnly)
        {
            editor.Text = value ?? string.Empty;
        }
    }

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Edit;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(AccessibleKustoTextEditor);
    }

    /// <inheritdoc />
    protected override bool HasKeyboardFocusCore()
    {
        return editor.TextArea.IsKeyboardFocusWithin;
    }

    /// <inheritdoc />
    protected override bool IsKeyboardFocusableCore()
    {
        return true;
    }

    /// <inheritdoc />
    protected override void SetFocusCore()
    {
        editor.TextArea.Focus();
    }

    private void OnEditorTextChanged(object? sender, EventArgs eventArguments)
    {
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, null, editor.Text);
    }
}
