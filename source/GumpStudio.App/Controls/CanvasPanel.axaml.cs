using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>The page strip and the gump being edited.</summary>
public sealed partial class CanvasPanel : UserControl
{
    public CanvasPanel()
    {
        AvaloniaXamlLoader.Load(this);

        PageTabs = this.FindControl<StackPanel>("_pageTabs")!;
        Canvas = this.FindControl<GumpCanvas>("_canvas")!;
        Scroller = this.FindControl<ScrollViewer>("_scroller")!;
    }

    /// <summary>One button per page in the gump.</summary>
    public StackPanel PageTabs { get; }

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
}
