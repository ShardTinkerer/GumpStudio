using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using GumpStudio.Core.Document;
using GumpStudio.Core.Primitives;

namespace GumpStudio.App.Controls;

/// <summary>
/// Edits the gump-level flags: where the window opens, how it can be dismissed,
/// and the client-parser toggles that apply to the whole definition.
/// </summary>
/// <remarks>
/// These had no editor at all until now — the flags round-tripped through save
/// and load and reached the exporters, but the only way to change one was to
/// hand-edit the file.
/// </remarks>
public sealed partial class GumpPropertiesWindow : Window
{
    private readonly NumericUpDown _x = null!;
    private readonly NumericUpDown _y = null!;
    private readonly NumericUpDown _typeId = null!;
    private readonly NumericUpDown _masterGump = null!;
    private readonly CheckBox _movable = null!;
    private readonly CheckBox _closable = null!;
    private readonly CheckBox _disposable = null!;
    private readonly CheckBox _upperWordCase = null!;
    private readonly CheckBox _croppedText = null!;
    private readonly CheckBox _enhancedInput = null!;

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public GumpPropertiesWindow()
        : this(new GumpProperties())
    {
    }

    public GumpPropertiesWindow(GumpProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        AvaloniaXamlLoader.Load(this);

        _x = this.FindControl<NumericUpDown>("XBox")!;
        _y = this.FindControl<NumericUpDown>("YBox")!;
        _typeId = this.FindControl<NumericUpDown>("TypeIdBox")!;
        _masterGump = this.FindControl<NumericUpDown>("MasterGumpBox")!;
        _movable = this.FindControl<CheckBox>("MovableBox")!;
        _closable = this.FindControl<CheckBox>("ClosableBox")!;
        _disposable = this.FindControl<CheckBox>("DisposableBox")!;
        _upperWordCase = this.FindControl<CheckBox>("UpperWordCaseBox")!;
        _croppedText = this.FindControl<CheckBox>("CroppedTextBox")!;
        _enhancedInput = this.FindControl<CheckBox>("EnhancedInputBox")!;

        _x.Value = properties.Location.X;
        _y.Value = properties.Location.Y;
        _typeId.Value = properties.TypeId;
        _masterGump.Value = properties.MasterGumpId;
        _movable.IsChecked = properties.Movable;
        _closable.IsChecked = properties.Closable;
        _disposable.IsChecked = properties.Disposable;
        _upperWordCase.IsChecked = properties.UpperWordCase;
        _croppedText.IsChecked = properties.CroppedText;
        _enhancedInput.IsChecked = properties.EnhancedClientInput;

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) =>
        {
            Result = new GumpProperties
            {
                Location = new GumpPoint(Number(_x), Number(_y)),
                TypeId = Number(_typeId),
                MasterGumpId = Number(_masterGump),
                Movable = _movable.IsChecked == true,
                Closable = _closable.IsChecked == true,
                Disposable = _disposable.IsChecked == true,
                UpperWordCase = _upperWordCase.IsChecked == true,
                CroppedText = _croppedText.IsChecked == true,
                EnhancedClientInput = _enhancedInput.IsChecked == true,
            };

            Close();
        };

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    /// <summary>The edited properties, or null when cancelled.</summary>
    public GumpProperties? Result { get; private set; }

    private static int Number(NumericUpDown box) => (int)(box.Value ?? 0);
}
