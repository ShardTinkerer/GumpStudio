using CommunityToolkit.Mvvm.ComponentModel;

using GumpStudio.Core.Document;

namespace GumpStudio.App.ViewModels;

/// <summary>One page, as its tab above the canvas.</summary>
/// <remarks>
/// The strip used to be cleared and rebuilt from scratch on every refresh — a
/// fresh <c>Button</c> per page, with its handler, whenever anything about the
/// document changed. These are made once per page and kept.
/// </remarks>
public sealed partial class PageTabViewModel : ObservableObject
{
    private readonly GumpPage _page;

    internal PageTabViewModel(GumpPage page, int index, bool isActive)
    {
        _page = page;

        Index = index;
        _isActive = isActive;
    }

    /// <summary>Which page this is, and what selecting the tab switches to.</summary>
    public int Index { get; }

    /// <summary>The page this tab stands for.</summary>
    internal GumpPage Page => _page;

    /// <summary>
    /// The tab's label.
    /// </summary>
    /// <remarks>
    /// A page can carry a name — the move-to-page menu shows it — but cannot be
    /// renamed yet, so in practice this is the number.
    /// </remarks>
    public string Label => string.IsNullOrEmpty(_page.Name)
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Page {Index}")
        : _page.Name;

    /// <summary>Whether this is the page being edited.</summary>
    [ObservableProperty]
    private bool _isActive;
}
