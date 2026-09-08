using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>
/// Asks a yes-or-no question before something irreversible.
/// </summary>
/// <remarks>
/// The project has no message-box abstraction, deliberately: errors go to the
/// status bar rather than a modal, which is what replaced the original's twenty
/// or so <c>MessageBox.Show(ex.Message)</c> sites. This is the other case — a
/// question that has to be answered before the action can proceed — so it
/// follows the same shape as every other dialog here, with the answer read from
/// <see cref="Confirmed"/> after the dialog closes.
/// </remarks>
public sealed partial class ConfirmWindow : Window
{
    private readonly TextBlock _message = null!;

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public ConfirmWindow()
        : this("Discard the current gump?", "Discard")
    {
    }

    /// <param name="message">The question to ask.</param>
    /// <param name="acceptText">Label for the button that proceeds.</param>
    public ConfirmWindow(string message, string acceptText = "Discard")
    {
        AvaloniaXamlLoader.Load(this);

        _message = this.FindControl<TextBlock>("MessageText")!;
        _message.Text = message;

        Button accept = this.FindControl<Button>("AcceptButton")!;
        accept.Content = acceptText;

        accept.Click += (_, _) =>
        {
            Confirmed = true;

            Close();
        };

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    /// <summary>Whether the question was answered in the affirmative.</summary>
    public bool Confirmed { get; private set; }
}
