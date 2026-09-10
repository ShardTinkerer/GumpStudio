using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>The page strip and the gump being edited.</summary>
public sealed partial class CanvasPanel : UserControl
{
    public CanvasPanel()
    {
        AvaloniaXamlLoader.Load(this);

        Canvas = this.FindControl<GumpCanvas>("_canvas")!;
        Scroller = this.FindControl<ScrollViewer>("_scroller")!;
        PageTabs = this.FindControl<ItemsControl>("_pageTabs")!;
    }


    /// <summary>The design surface.</summary>
    public GumpCanvas Canvas { get; }

    /// <summary>
    /// What scrolls the canvas.
    /// </summary>
    /// <remarks>
    /// Exposed because fitting the gump to the window needs the size of the
    /// area actually on screen, which only the scroller knows.
    /// </remarks>
    public ScrollViewer Scroller { get; }

    /// <summary>The page strip, which binds one tab per page.</summary>
    public ItemsControl PageTabs { get; }
}
