using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>The editable properties of whatever is selected.</summary>
public sealed partial class PropertiesPanel : UserControl
{
    public PropertiesPanel()
    {
        AvaloniaXamlLoader.Load(this);

        Rows = this.FindControl<StackPanel>("_rows")!;
    }

    /// <summary>Rebuilt from scratch whenever the selection changes.</summary>
    public StackPanel Rows { get; }
}
