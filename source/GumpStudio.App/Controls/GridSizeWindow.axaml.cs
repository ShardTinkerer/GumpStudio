using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using GumpStudio.Core.Primitives;

namespace GumpStudio.App.Controls;

/// <summary>Asks for the design-grid spacing.</summary>
public sealed partial class GridSizeWindow : Window
{
    private readonly NumericUpDown _width = null!;
    private readonly NumericUpDown _height = null!;

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public GridSizeWindow()
        : this(5, 5)
    {
    }

    public GridSizeWindow(int width, int height)
    {
        AvaloniaXamlLoader.Load(this);

        _width = this.FindControl<NumericUpDown>("WidthBox")!;
        _height = this.FindControl<NumericUpDown>("HeightBox")!;

        _width.Value = width;
        _height.Value = height;

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) =>
        {
            Result = new GumpSize((int)(_width.Value ?? 5), (int)(_height.Value ?? 5));

            Close();
        };

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    /// <summary>The chosen spacing, or null when cancelled.</summary>
    public GumpSize? Result { get; private set; }
}
