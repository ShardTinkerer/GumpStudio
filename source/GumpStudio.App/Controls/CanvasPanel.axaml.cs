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
    }

    /// <summary>One button per page in the gump.</summary>
    public StackPanel PageTabs { get; }

    /// <summary>The design surface.</summary>
    public GumpCanvas Canvas { get; }
}
