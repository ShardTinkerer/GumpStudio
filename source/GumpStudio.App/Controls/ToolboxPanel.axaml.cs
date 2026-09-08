using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>The list of element kinds that can be added to a gump.</summary>
public sealed partial class ToolboxPanel : UserControl
{
    public ToolboxPanel()
    {
        AvaloniaXamlLoader.Load(this);

        Items = this.FindControl<ItemsControl>("_items")!;
    }

    /// <summary>Holds one button per element kind.</summary>
    public ItemsControl Items { get; }
}
