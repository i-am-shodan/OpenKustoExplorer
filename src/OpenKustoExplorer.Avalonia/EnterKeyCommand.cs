using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Invokes an attached command when plain Enter is pressed in a single-line input.
/// </summary>
public sealed class EnterKeyCommand : AvaloniaObject
{
    /// <summary>
    /// Identifies the command invoked by Enter.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<EnterKeyCommand, InputElement, ICommand?>("Command");

    static EnterKeyCommand()
    {
        CommandProperty.Changed.AddClassHandler<InputElement>(OnCommandChanged);
    }

    private EnterKeyCommand()
    {
    }

    /// <summary>
    /// Gets the command attached to an input element.
    /// </summary>
    /// <param name="element">The input element.</param>
    /// <returns>The command invoked by Enter.</returns>
    public static ICommand? GetCommand(InputElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(CommandProperty);
    }

    /// <summary>
    /// Sets the command attached to an input element.
    /// </summary>
    /// <param name="element">The input element.</param>
    /// <param name="command">The command invoked by Enter.</param>
    public static void SetCommand(InputElement element, ICommand? command)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CommandProperty, command);
    }

    private static void OnCommandChanged(
        InputElement element,
        AvaloniaPropertyChangedEventArgs eventArguments)
    {
        _ = eventArguments;
        element.KeyDown -= OnKeyDown;

        if (GetCommand(element) is not null)
        {
            element.KeyDown += OnKeyDown;
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        if (eventArguments.Handled
            || eventArguments.Key != Key.Enter
            || eventArguments.KeyModifiers != KeyModifiers.None
            || sender is TextBox { AcceptsReturn: true }
            || sender is ComboBox { IsDropDownOpen: true }
            || sender is not InputElement element)
        {
            return;
        }

        ICommand? command = GetCommand(element);

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            eventArguments.Handled = true;
        }
    }
}
