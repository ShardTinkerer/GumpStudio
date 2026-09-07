using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>The elements on the current page, in drawing order.</summary>
public sealed partial class ElementsPanel : UserControl
{
    public ElementsPanel()
    {
        AvaloniaXamlLoader.Load(this);

        List = this.FindControl<ListBox>("_list")!;
    }

    /// <summary>Selection here and on the canvas are kept in step.</summary>
    public ListBox List { get; }
}
