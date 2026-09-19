using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Adds focus containment, restoration, and dialog semantics to an overlay control.
/// </summary>
[SuppressMessage(
    "Major Code Smell",
    "S3453:Classes with only static members should not have public constructors",
    Justification = "Avalonia attached-property owners derive from AvaloniaObject but are never instantiated.")]
public sealed class ModalDialog : AvaloniaObject
{
    /// <summary>
    /// Identifies whether a control is a modal dialog overlay.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ModalDialog, Control, bool>("IsEnabled");

    private static readonly ConditionalWeakTable<Control, ModalState> States = new();

    static ModalDialog()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>(OnIsVisibleChanged);
    }

    private ModalDialog()
    {
    }

    /// <summary>
    /// Gets whether the supplied control is a modal dialog overlay.
    /// </summary>
    /// <param name="control">The overlay control.</param>
    /// <returns><see langword="true"/> when modal behavior is enabled.</returns>
    public static bool GetIsEnabled(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(IsEnabledProperty);
    }

    /// <summary>
    /// Sets whether the supplied control is a modal dialog overlay.
    /// </summary>
    /// <param name="control">The overlay control.</param>
    /// <param name="value">Whether to enable modal behavior.</param>
    public static void SetIsEnabled(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs eventArguments)
    {
        if (eventArguments.NewValue is true)
        {
            KeyboardNavigation.SetTabNavigation(control, KeyboardNavigationMode.Cycle);
            AutomationProperties.SetAccessibilityView(control, AccessibilityView.Control);
            AutomationProperties.SetControlTypeOverride(control, AutomationControlType.Window);

            if (control.IsVisible)
            {
                Open(control);
            }
        }
        else
        {
            Close(control);
            States.Remove(control);
        }
    }

    private static void OnIsVisibleChanged(Control control, AvaloniaPropertyChangedEventArgs eventArguments)
    {
        if (!GetIsEnabled(control))
        {
            return;
        }

        if (eventArguments.NewValue is true)
        {
            Open(control);
        }
        else
        {
            Close(control);
        }
    }

    private static void Open(Control control)
    {
        ModalState state = States.GetOrCreateValue(control);
        if (state.IsOpen)
        {
            return;
        }

        state.IsOpen = true;
        if (TopLevel.GetTopLevel(control)?.FocusManager.GetFocusedElement() is InputElement focusedElement
            && !control.IsVisualAncestorOf(focusedElement))
        {
            state.PreviousFocus = new WeakReference<InputElement>(focusedElement);
        }

        Dispatcher.UIThread.Post(() => FocusDialog(control), DispatcherPriority.Input);
    }

    private static void Close(Control control)
    {
        if (!States.TryGetValue(control, out ModalState? state) || !state.IsOpen)
        {
            return;
        }

        state.IsOpen = false;
        WeakReference<InputElement>? previousFocus = state.PreviousFocus;
        state.PreviousFocus = null;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (previousFocus?.TryGetTarget(out InputElement? target) == true
                    && target.IsVisible
                    && target.IsEffectivelyEnabled)
                {
                    target.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Input);
    }

    private static void FocusDialog(Control control)
    {
        if (!control.IsVisible || !GetIsEnabled(control))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
        {
            string name = control.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(textBlock => textBlock.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                ?? "Dialog";
            AutomationProperties.SetName(control, name);
        }

        InputElement? target = control.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(element => element.Focusable && element.IsVisible && element.IsEffectivelyEnabled);
        (target ?? control).Focus(NavigationMethod.Tab);
    }

    private sealed class ModalState
    {
        internal bool IsOpen { get; set; }

        internal WeakReference<InputElement>? PreviousFocus { get; set; }
    }
}
