using System.Globalization;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using GumpStudio.Rendering;

using SkiaSharp;

namespace GumpStudio.App.Controls;

/// <summary>
/// Picks the text colour an HTML gump command carries.
/// </summary>
/// <remarks>
/// <para>
/// Channels run 0 to 31, not 0 to 255, because that is the client's real
/// resolution: the colour slot is RGB555. A picker offering eight-bit channels
/// would promise sixteen million colours where there are 32,768, and two nearby
/// picks would come out identical.
/// </para>
/// <para>
/// This is not a hue. A hue indexes <c>hues.mul</c> and recolours art; this
/// value is a plain colour and only ever tints text.
/// </para>
/// </remarks>
public sealed partial class ColorPickerWindow : Window
{
    /// <summary>Colours worth reaching for without dragging three sliders.</summary>
    /// <remarks>
    /// White first: it is what almost every captured gump uses, and the one an
    /// author wants back after experimenting.
    /// </remarks>
    private static readonly (string Name, int Red, int Green, int Blue)[] Palette =
    [
        ("White", 31, 31, 31),
        ("Silver", 24, 24, 24),
        ("Grey", 15, 15, 15),
        ("Black", 0, 0, 0),
        ("Red", 31, 0, 0),
        ("Orange", 31, 20, 0),
        ("Yellow", 31, 31, 0),
        ("Green", 0, 31, 0),
        ("Cyan", 0, 31, 31),
        ("Blue", 0, 0, 31),
        ("Purple", 20, 0, 31),
        ("Pink", 31, 16, 24),
    ];

    private readonly Slider _red = null!;
    private readonly Slider _green = null!;
    private readonly Slider _blue = null!;
    private readonly Border _preview = null!;
    private readonly TextBlock _summary = null!;
    private readonly TextBlock _redValue = null!;
    private readonly TextBlock _greenValue = null!;
    private readonly TextBlock _blueValue = null!;

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public ColorPickerWindow()
        : this(0)
    {
    }

    public ColorPickerWindow(int value)
    {
        AvaloniaXamlLoader.Load(this);

        _red = this.FindControl<Slider>("RedSlider")!;
        _green = this.FindControl<Slider>("GreenSlider")!;
        _blue = this.FindControl<Slider>("BlueSlider")!;
        _preview = this.FindControl<Border>("Preview")!;
        _summary = this.FindControl<TextBlock>("Summary")!;
        _redValue = this.FindControl<TextBlock>("RedValue")!;
        _greenValue = this.FindControl<TextBlock>("GreenValue")!;
        _blueValue = this.FindControl<TextBlock>("BlueValue")!;

        // An unset colour opens on white rather than black: black text on the
        // dark ground a gump usually has would look like nothing at all.
        (int red, int green, int blue) = value == 0
            ? (31, 31, 31)
            : GumpColor.ToChannels(value);

        _red.Value = red;
        _green.Value = green;
        _blue.Value = blue;

        _red.PropertyChanged += (_, e) => OnSlider(e.Property.Name);
        _green.PropertyChanged += (_, e) => OnSlider(e.Property.Name);
        _blue.PropertyChanged += (_, e) => OnSlider(e.Property.Name);

        BuildPresets();
        Refresh();

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) =>
        {
            Result = Current;

            Close();
        };

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    /// <summary>The chosen value, or null when cancelled.</summary>
    public int? Result { get; private set; }

    private int Current => GumpColor.Pack((int)_red.Value, (int)_green.Value, (int)_blue.Value);

    private void OnSlider(string property)
    {
        if (property == nameof(Slider.Value))
        {
            Refresh();
        }
    }

    private void BuildPresets()
    {
        List<Control> swatches = [];

        foreach ((string name, int red, int green, int blue) in Palette)
        {
            SKColor colour = GumpColor.ToSkColor(GumpColor.Pack(red, green, blue))
                ?? new SKColor(0, 0, 0);

            // Bordered, or the black preset is invisible against the dark ground
            // the dialog sits on.
            Button swatch = new()
            {
                Width = 24,
                Height = 24,
                Margin = new Avalonia.Thickness(0, 0, 4, 4),
                Padding = new Avalonia.Thickness(0),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = Brushes.Gray,
                Background = new SolidColorBrush(
                    Color.FromRgb(colour.Red, colour.Green, colour.Blue)),
                [ToolTip.TipProperty] = name,
            };

            swatch.Click += (_, _) =>
            {
                _red.Value = red;
                _green.Value = green;
                _blue.Value = blue;
            };

            swatches.Add(swatch);
        }

        this.FindControl<ItemsControl>("Presets")!.ItemsSource = swatches;
    }

    private void Refresh()
    {
        int value = Current;

        if (GumpColor.ToSkColor(value) is { } colour)
        {
            _preview.Background = new SolidColorBrush(
                Color.FromRgb(colour.Red, colour.Green, colour.Blue));
        }

        _redValue.Text = ((int)_red.Value).ToString(CultureInfo.InvariantCulture);
        _greenValue.Text = ((int)_green.Value).ToString(CultureInfo.InvariantCulture);
        _blueValue.Text = ((int)_blue.Value).ToString(CultureInfo.InvariantCulture);

        _summary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Writes {value} (0x{value:X4}). The client also accepts the 24-bit form.");
    }
}
