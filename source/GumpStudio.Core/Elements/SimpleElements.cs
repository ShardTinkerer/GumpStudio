namespace GumpStudio.Core.Elements;

/// <summary>A translucent rectangle. Exports as an alpha region.</summary>
public sealed class AlphaElement : ResizableElement
{
    public AlphaElement() => SetInitialSize(100, 100);

    public override string TypeName => "Alpha";

    protected override Element CreateInstance() => new AlphaElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A resizable nine-slice background. Exports as a resize-pic.</summary>
public sealed class BackgroundElement : ResizableElement
{
    private int _gumpId = 9200;

    public BackgroundElement() => SetInitialSize(100, 100);

    public override string TypeName => "Background";

    /// <summary>
    /// The first of nine consecutive gump ids forming the frame.
    /// </summary>
    /// <remarks>
    /// Deliberately not validated by loading art here. The original's setter
    /// loaded and disposed nine bitmaps on every keystroke in the property grid.
    /// Whether the art exists is a rendering concern.
    /// </remarks>
    public int GumpId
    {
        get => _gumpId;
        set => Set(ref _gumpId, value);
    }

    protected override void CopyTo(Element target) => ((BackgroundElement)target)._gumpId = _gumpId;

    protected override Element CreateInstance() => new BackgroundElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A tiled gump image filling its bounds.</summary>
public sealed class TiledElement : ResizableElement
{
    private int _gumpId = 5124;
    private int _hue;

    public TiledElement() => SetInitialSize(100, 100);

    public override string TypeName => "Tiled";

    public int GumpId
    {
        get => _gumpId;
        set => Set(ref _gumpId, value);
    }

    /// <summary>One-based hue, or 0 for none.</summary>
    public int Hue
    {
        get => _hue;
        set => Set(ref _hue, value);
    }

    protected override void CopyTo(Element target)
    {
        TiledElement clone = (TiledElement)target;

        clone._gumpId = _gumpId;
        clone._hue = _hue;
    }

    protected override Element CreateInstance() => new TiledElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A single gump image, sized by its art.</summary>
public sealed class ImageElement : Element
{
    private int _gumpId = 100;
    private int _hue;
    private bool _partialHue;

    public override string TypeName => "Image";

    public int GumpId
    {
        get => _gumpId;
        set => Set(ref _gumpId, value);
    }

    /// <summary>One-based hue, or 0 for none.</summary>
    public int Hue
    {
        get => _hue;
        set => Set(ref _hue, value);
    }

    /// <summary>
    /// Tints only the grayscale pixels, leaving coloured ones alone.
    /// </summary>
    /// <remarks>
    /// The difference between the client's <c>gumppichued</c> and
    /// <c>gumppicphued</c>. Art drawn with a dye channel needs the partial form;
    /// a full tint flattens it to one colour.
    /// </remarks>
    public bool PartialHue
    {
        get => _partialHue;
        set => Set(ref _partialHue, value);
    }

    protected override void CopyTo(Element target)
    {
        ImageElement clone = (ImageElement)target;

        clone._gumpId = _gumpId;
        clone._hue = _hue;
        clone._partialHue = _partialHue;
    }

    protected override Element CreateInstance() => new ImageElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A world item tile, sized by its art.</summary>
public sealed class ItemElement : Element
{
    private int _itemId = 1;
    private int _hue;

    public override string TypeName => "Item";

    public int ItemId
    {
        get => _itemId;
        set => Set(ref _itemId, value);
    }

    /// <summary>One-based hue, or 0 for none.</summary>
    public int Hue
    {
        get => _hue;
        set => Set(ref _hue, value);
    }

    protected override void CopyTo(Element target)
    {
        ItemElement clone = (ItemElement)target;

        clone._itemId = _itemId;
        clone._hue = _hue;
    }

    protected override Element CreateInstance() => new ItemElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>
/// A rectangular crop of a gump image, drawn as if it were its own picture.
/// </summary>
/// <remarks>
/// The client's <c>picinpic</c> family, added for the modern UI: it blits a
/// <see cref="Element.Size"/> region starting at (<see cref="SourceX"/>,
/// <see cref="SourceY"/>) inside <see cref="GumpId"/>. Sprite sheets packed into
/// a single gump id are the usual reason to want it.
/// </remarks>
public sealed class PicInPicElement : ResizableElement
{
    private int _gumpId = 100;
    private int _sourceX;
    private int _sourceY;
    private int _hue;
    private bool _partialHue;

    public PicInPicElement() => SetInitialSize(50, 50);

    public override string TypeName => "PicInPic";

    /// <summary>The gump image to take the region from.</summary>
    public int GumpId
    {
        get => _gumpId;
        set => Set(ref _gumpId, value);
    }

    /// <summary>Left edge of the region within the source image.</summary>
    public int SourceX
    {
        get => _sourceX;
        set => Set(ref _sourceX, value);
    }

    /// <summary>Top edge of the region within the source image.</summary>
    public int SourceY
    {
        get => _sourceY;
        set => Set(ref _sourceY, value);
    }

    /// <summary>One-based hue, or 0 for none.</summary>
    public int Hue
    {
        get => _hue;
        set => Set(ref _hue, value);
    }

    /// <inheritdoc cref="ImageElement.PartialHue" />
    public bool PartialHue
    {
        get => _partialHue;
        set => Set(ref _partialHue, value);
    }

    protected override void CopyTo(Element target)
    {
        PicInPicElement clone = (PicInPicElement)target;

        clone._gumpId = _gumpId;
        clone._sourceX = _sourceX;
        clone._sourceY = _sourceY;
        clone._hue = _hue;
        clone._partialHue = _partialHue;
    }

    protected override Element CreateInstance() => new PicInPicElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>
/// World-item artwork drawn through the gump-picture blitter.
/// </summary>
/// <remarks>
/// <para>
/// The client's <c>tilepicasgumppic</c>. It shows the same graphic an
/// <see cref="ItemElement"/> does, but routed through the gump-art path, which
/// servers use when they want an item icon to behave like a picture rather than
/// like a tile.
/// </para>
/// <para>
/// The three trailing parameters are carried through unchanged.
/// <see cref="LinkId"/> is decremented by one inside the client, which is why it
/// reads as a one-based link. What <see cref="ParamB"/> and <see cref="ParamC"/>
/// mean is not established even from the client binary, so they are exposed
/// as-is rather than guessed at; leave them at zero unless a server template
/// already sets them.
/// </para>
/// </remarks>
public sealed class TileAsGumpElement : Element
{
    private int _itemId = 1;
    private int _linkId;
    private int _paramB;
    private int _paramC;

    public override string TypeName => "TileAsGump";

    public int ItemId
    {
        get => _itemId;
        set => Set(ref _itemId, value);
    }

    /// <summary>One-based link id, or 0 for none.</summary>
    public int LinkId
    {
        get => _linkId;
        set => Set(ref _linkId, value);
    }

    /// <summary>Opaque trailing parameter. See the remarks on the type.</summary>
    public int ParamB
    {
        get => _paramB;
        set => Set(ref _paramB, value);
    }

    /// <summary>Opaque trailing parameter. See the remarks on the type.</summary>
    public int ParamC
    {
        get => _paramC;
        set => Set(ref _paramC, value);
    }

    protected override void CopyTo(Element target)
    {
        TileAsGumpElement clone = (TileAsGumpElement)target;

        clone._itemId = _itemId;
        clone._linkId = _linkId;
        clone._paramB = _paramB;
        clone._paramC = _paramC;
    }

    protected override Element CreateInstance() => new TileAsGumpElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}
