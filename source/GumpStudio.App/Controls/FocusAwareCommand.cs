using System.Windows.Input;

namespace GumpStudio.App.Controls;

/// <summary>
/// A command that steps aside while the keyboard belongs to something else.
/// </summary>
/// <remarks>
/// <para>
/// Yielding is expressed as <c>CanExecute</c>, not as an early return from the
/// command. A <c>KeyBinding</c> marks the key handled whenever it executes, so
/// a command that runs and does nothing still swallows the keystroke — which
/// left Ctrl+C in a property field copying nothing at all instead of copying
/// the element. Refusing to execute lets the key reach the text box that should
/// have had it.
/// </para>
/// <para>
/// A decorator rather than a check inside the command, because where the
/// keyboard is belongs to the view: the same command is invoked from the menu
/// bar and the context menu, and neither should be greyed out because a text
/// box happens to have the caret.
/// </para>
/// </remarks>
/// <param name="inner">The command to run when the keyboard is free.</param>
/// <param name="allows">Whether the window may act on the key right now.</param>
internal sealed class FocusAwareCommand(ICommand inner, Func<bool> allows) : ICommand
{
    private readonly ICommand _inner = inner
        ?? throw new ArgumentNullException(nameof(inner));

    private readonly Func<bool> _allows = allows
        ?? throw new ArgumentNullException(nameof(allows));

    /// <summary>Follows the wrapped command, so a bound menu item still updates.</summary>
    public event EventHandler? CanExecuteChanged
    {
        add => _inner.CanExecuteChanged += value;
        remove => _inner.CanExecuteChanged -= value;
    }

    public bool CanExecute(object? parameter) =>
        _allows() && _inner.CanExecute(parameter);

    public void Execute(object? parameter) => _inner.Execute(parameter);
}
