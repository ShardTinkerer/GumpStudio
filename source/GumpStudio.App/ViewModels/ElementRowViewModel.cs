using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using GumpStudio.Core.Elements;

namespace GumpStudio.App.ViewModels;

/// <summary>One element, as a row in the elements panel.</summary>
/// <remarks>
/// <para>
/// Holds no state of its own: the label and the selection both read through to
/// the element, which already announces its own changes. Renaming an element or
/// moving it now updates the row without anything rebuilding the list.
/// </para>
/// <para>
/// The list's own selection binds two-way to <see cref="IsSelected"/>, which is
/// what removes the flag the panel used to need. The item source was reassigned
/// on every refresh, so the list reset its own selection each time and the
/// resulting event raced a <c>_suppressSelectionSync</c> guard — the visible
/// symptom being a selected element whose properties never appeared.
/// </para>
/// </remarks>
public sealed partial class ElementRowViewModel : ObservableObject, IDisposable
{
    private readonly Element _element;
    private readonly Action<Element, bool> _select;

    /// <param name="element">The element this row stands for.</param>
    /// <param name="select">
    /// How to tell the controller the row was selected or deselected, so that the
    /// canvas and the list stay in step.
    /// </param>
    internal ElementRowViewModel(Element element, Action<Element, bool> select)
    {
        _element = element;
        _select = select;

        _element.PropertyChanged += OnElementChanged;
    }

    /// <summary>The element this row stands for.</summary>
    public Element Element => _element;

    /// <summary>What the row reads: the element's type and its name.</summary>
    public string Label => _element.ToString();

    /// <summary>
    /// Whether the element is selected.
    /// </summary>
    /// <remarks>
    /// Two-way: the list writes it when the user picks a row, and the controller
    /// writes the element when the canvas changes the selection.
    /// </remarks>
    public bool IsSelected
    {
        get => _element.IsSelected;
        set
        {
            if (_element.IsSelected == value)
            {
                return;
            }

            _select(_element, value);
        }
    }

    /// <summary>
    /// Follows the element rather than matching a property name.
    /// </summary>
    /// <remarks>
    /// <c>Element.Set</c> uses the caller's member name, so a move announces
    /// <c>Location</c> and a resize <c>Size</c> — <c>X</c>, <c>Y</c>,
    /// <c>Width</c> and <c>Height</c> are computed and never announced at all.
    /// Re-reading two strings on any change is cheaper than trying to know which
    /// names matter.
    /// </remarks>
    private void OnElementChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsSelected));
    }

    public void Dispose() => _element.PropertyChanged -= OnElementChanged;
}
