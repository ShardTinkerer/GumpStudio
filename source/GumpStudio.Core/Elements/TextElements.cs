namespace GumpStudio.Core.Elements;

/// <summary>Whether an HTML area holds literal markup or a cliloc reference.</summary>
public enum HtmlContentKind
{
    /// <summary>Literal HTML supplied by the script.</summary>
    Html = 0,

    /// <summary>A localised string looked up by cliloc id.</summary>
    Localized = 1,
}

/// <summary>A scrollable HTML area, optionally backed by a cliloc string.</summary>
public sealed class HtmlElement : ResizableElement
{
    private string _html = string.Empty;
    private int _clilocId = 1000000;
    private bool _showScrollbar;
    private bool _showBackground;
    private HtmlContentKind _contentKind = HtmlContentKind.Html;

    public HtmlElement() => SetInitialSize(200, 100);

    public override string TypeName => "Html";

    /// <summary>Literal markup, used when <see cref="ContentKind"/> is Html.</summary>
    public string Html
    {
        get => _html;
        set => Set(ref _html, value ?? string.Empty);
    }

    /// <summary>Cliloc id, used when <see cref="ContentKind"/> is Localized.</summary>
    public int ClilocId
    {
        get => _clilocId;
        set => Set(ref _clilocId, value);
    }

    public bool ShowScrollbar
    {
        get => _showScrollbar;
        set => Set(ref _showScrollbar, value);
    }

    public bool ShowBackground
    {
        get => _showBackground;
        set => Set(ref _showBackground, value);
    }

    public HtmlContentKind ContentKind
    {
        get => _contentKind;
        set => Set(ref _contentKind, value);
    }

    protected override void CopyTo(Element target)
    {
        HtmlElement clone = (HtmlElement)target;

        clone._html = _html;
        clone._clilocId = _clilocId;
        clone._showScrollbar = _showScrollbar;
        clone._showBackground = _showBackground;
        clone._contentKind = _contentKind;
    }

    protected override Element CreateInstance() => new HtmlElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A run of text drawn in one of the client's bitmap fonts.</summary>
public sealed class LabelElement : Element
{
    private string _text = "Label";
    private int _hue;
    private int _fontIndex;
    private bool _cropped;

    public override string TypeName => "Label";

    public string Text
    {
        get => _text;
        set => Set(ref _text, value ?? string.Empty);
    }

    /// <summary>One-based hue, or 0 for none.</summary>
    public int Hue
    {
        get => _hue;
        set => Set(ref _hue, value);
    }

    /// <summary>Index into the client's Unicode font set.</summary>
    public int FontIndex
    {
        get => _fontIndex;
        set => Set(ref _fontIndex, value);
    }

    /// <summary>True to clip the text to the element's bounds rather than let it overflow.</summary>
    public bool Cropped
    {
        get => _cropped;
        set => Set(ref _cropped, value);
    }

    protected override void CopyTo(Element target)
    {
        LabelElement clone = (LabelElement)target;

        clone._text = _text;
        clone._hue = _hue;
        clone._fontIndex = _fontIndex;
        clone._cropped = _cropped;
    }

    protected override Element CreateInstance() => new LabelElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }

    /// <summary>
    /// Applies a size measured by the renderer.
    /// </summary>
    /// <remarks>
    /// A label is not user-resizable — its extent comes from the rendered text —
    /// but the renderer must still be able to report what that extent is.
    /// </remarks>
    public void SetMeasuredSize(int width, int height) => SetInitialSize(width, height);
}
