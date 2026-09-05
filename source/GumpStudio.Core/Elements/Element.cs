using System.ComponentModel;

using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Elements;

/// <summary>
/// Base class for everything that can appear on a gump.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately data-only. The old <c>BaseElement</c> also knew how to render
/// itself, hit-test itself, build its own context menus and reach back into a
/// global reference to the main form to push undo points. Rendering lives in
/// <c>GumpStudio.Rendering</c>, hit testing in
/// <c>GumpStudio.Core.Geometry.HandleGeometry</c>, and mutation goes through
/// commands.
/// </para>
/// <para>
/// There is no <c>[Serializable]</c> and no <c>ISerializable</c>: the save format
/// is an explicit DTO layer, so renaming a class never breaks a saved file.
/// </para>
/// </remarks>
public abstract class Element : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _comment = string.Empty;
    private GumpPoint _location;
    private GumpSize _size;

    protected Element()
    {
        _name = TypeName;
    }

    /// <summary>Stable identifier for the element type, used in the save format and by exporters.</summary>
    /// <remarks>
    /// Deliberately not <c>GetType().Name</c>: the on-disk format must survive a
    /// class rename.
    /// </remarks>
    public abstract string TypeName { get; }

    /// <summary>Whether the user can resize this element, as opposed to only moving it.</summary>
    public virtual bool IsResizable => false;

    /// <summary>A name shown in the editor. Ultima Online never sees it.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>A free-text note, carried through save and load.</summary>
    public string Comment
    {
        get => _comment;
        set => Set(ref _comment, value ?? string.Empty);
    }

    /// <summary>Position relative to the parent group.</summary>
    public GumpPoint Location
    {
        get => _location;
        set => Set(ref _location, value);
    }

    /// <summary>
    /// Size in pixels. For a non-resizable element this is derived from its
    /// content and setting it is ignored.
    /// </summary>
    public virtual GumpSize Size
    {
        get => _size;
        set
        {
            if (IsResizable)
            {
                Set(ref _size, value);
            }
        }
    }

    /// <summary>The group this element belongs to, or null for a page root.</summary>
    public GroupElement? Parent { get; internal set; }

    /// <summary>True when the element is part of the current selection.</summary>
    /// <remarks>Editor state, not saved.</remarks>
    public bool IsSelected { get; set; }

    public int X => Location.X;

    public int Y => Location.Y;

    public int Width => Size.Width;

    public int Height => Size.Height;

    /// <summary>Bounds in parent-relative coordinates.</summary>
    public GumpRect Bounds => new(Location, Size);

    /// <summary>
    /// Position in page coordinates, accumulating every parent group's offset.
    /// </summary>
    /// <remarks>
    /// <strong>Exporters and the renderer must use this, never <see cref="Location"/>.</strong>
    /// The original defined an equivalent method, documented it as the one
    /// exporters should call, and then never called it from anywhere — so every
    /// element nested inside a group exported at the wrong coordinates. That bug
    /// shipped in GumpStudio 1.8 and survived into the C# port.
    /// </remarks>
    public GumpPoint GetAbsolutePosition()
    {
        GumpPoint position = Location;

        for (GroupElement? parent = Parent; parent is not null; parent = parent.Parent)
        {
            position = position.Offset(parent.X, parent.Y);
        }

        return position;
    }

    /// <summary>Bounds in page coordinates.</summary>
    public GumpRect GetAbsoluteBounds() => new(GetAbsolutePosition(), Size);

    /// <summary>Creates an independent copy, without a parent.</summary>
    public Element Clone()
    {
        Element clone = CreateInstance();

        clone._name = _name;
        clone._comment = _comment;
        clone._location = _location;
        clone._size = _size;

        CopyTo(clone);

        return clone;
    }

    /// <summary>Creates an empty instance of the concrete type.</summary>
    protected abstract Element CreateInstance();

    /// <summary>Copies subclass state onto <paramref name="target"/>.</summary>
    protected virtual void CopyTo(Element target)
    {
    }

    /// <summary>Dispatches to the matching <see cref="IElementVisitor"/> method.</summary>
    /// <remarks>
    /// Exporters and the renderer are visitors, so adding an element type makes
    /// every consumer fail to compile until it handles the new case — rather than
    /// silently skipping it, as a <c>switch</c> with a default arm would.
    /// </remarks>
    public abstract void Accept(IElementVisitor visitor);

    /// <summary>Sets the initial size for a newly created element.</summary>
    protected void SetInitialSize(int width, int height) => _size = new GumpSize(width, height);

    /// <summary>
    /// Sets a size measured from the element's content.
    /// </summary>
    /// <remarks>
    /// <see cref="Size"/> deliberately ignores writes on a non-resizable element,
    /// because the user must not be able to drag it to an arbitrary size. Its
    /// extent still has to come from somewhere, though: an image is as big as its
    /// art and a label as big as its rendered text. The renderer measures those
    /// and reports them here.
    /// </remarks>
    public void SetContentSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        Set(ref _size, new GumpSize(width, height), nameof(Size));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public override string ToString() => $"{TypeName} \"{Name}\"";
}

/// <summary>An element the user can resize as well as move.</summary>
public abstract class ResizableElement : Element
{
    public override bool IsResizable => true;
}
