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

    protected override void CopyTo(Element target)
    {
        ImageElement clone = (ImageElement)target;

        clone._gumpId = _gumpId;
        clone._hue = _hue;
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
