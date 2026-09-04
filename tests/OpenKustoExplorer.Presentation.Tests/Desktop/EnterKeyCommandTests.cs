using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies Enter submits only eligible single-line inputs.
/// </summary>
public sealed class EnterKeyCommandTests
{
    /// <summary>
    /// Verifies plain Enter invokes and handles an enabled attached command.
    /// </summary>
    [Fact]
    public void PlainEnterExecutesEnabledCommand()
    {
        int executionCount = 0;
        TextBox textBox = new();
        EnterKeyCommand.SetCommand(textBox, new RelayCommand(() => executionCount++));
        KeyEventArgs eventArguments = CreateEnterEvent();

        textBox.RaiseEvent(eventArguments);

        Assert.Equal(1, executionCount);
        Assert.True(eventArguments.Handled);
    }

    /// <summary>
    /// Verifies Enter leaves a disabled command untouched and unhandled.
    /// </summary>
    [Fact]
    public void PlainEnterHonorsCanExecute()
    {
        int executionCount = 0;
        TextBox textBox = new();
        EnterKeyCommand.SetCommand(
            textBox,
            new RelayCommand(() => executionCount++, () => false));
        KeyEventArgs eventArguments = CreateEnterEvent();

        textBox.RaiseEvent(eventArguments);

        Assert.Equal(0, executionCount);
        Assert.False(eventArguments.Handled);
    }

    /// <summary>
    /// Verifies Enter remains available for line breaks in multiline inputs.
    /// </summary>
    [Fact]
    public void PlainEnterDoesNotSubmitMultilineTextBox()
    {
        int executionCount = 0;
        TextBox textBox = new() { AcceptsReturn = true };
        EnterKeyCommand.SetCommand(textBox, new RelayCommand(() => executionCount++));
        KeyEventArgs eventArguments = CreateEnterEvent();

        textBox.RaiseEvent(eventArguments);

        Assert.Equal(0, executionCount);
        Assert.False(eventArguments.Handled);
    }

    private static KeyEventArgs CreateEnterEvent()
    {
        return new KeyEventArgs
        {
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.None,
            RoutedEvent = InputElement.KeyDownEvent,
        };
    }
}
