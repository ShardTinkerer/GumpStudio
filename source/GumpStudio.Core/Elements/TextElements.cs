namespace GumpStudio.Core.Elements;

/// <summary>
/// Which of the client's two font families text is previewed in.
/// </summary>
/// <remarks>
/// A preview choice only. The gump protocol's text commands carry no font at all,
/// so nothing an exporter writes changes with it — but a designer needs to see
/// roughly what the player will, and the two families look nothing alike.
/// </remarks>
public enum GumpFontFamily
{
    /// <summary>The <c>unifont*.mul</c> faces. Thirteen on a current client.</summary>
    Unicode,

    /// <summary>The ten <c>fonts.mul</c> faces, which are the older UO look.</summary>
    Ascii,
}

/// <summary>
/// An element whose text is previewed in a chosen font.
/// </summary>
/// <remarks>
/// The gump protocol's text commands carry no font, so this changes nothing an
/// exporter writes — it decides only what the designer sees. Before it existed
/// the renderer drew every localised area and text entry in Unicode font 0, the
/// ornate blackletter face, which is a poor default for reading and is not what
/// the client uses for body text.
/// </remarks>
public interface IFontedElement
{
    /// <summary>Which family the face comes from.</summary>
    GumpFontFamily FontFamily { get; set; }

    /// <summary>Index of the face within its family.</summary>
    int FontIndex { get; set; }
}

/// <summary>Shared defaults for the elements that carry text.</summary>
public static class TextElementDefaults
{
    /// <summary>
    /// The face new text elements preview in.
    /// </summary>
    /// <remarks>
    /// Unicode 1, the plain face. Unicode 0 is an ornate blackletter that is hard
    /// to read at gump sizes, and it was what every localised area and text entry
    /// was drawn in before the font could be chosen at all.
    /// </remarks>
    public const int FontIndex = 1;
}

/// <summary>Whether an HTML area holds literal markup or a cliloc reference.</summary>
public enum HtmlContentKind
{
    /// <summary>Literal HTML supplied by the script.</summary>
    Html = 0,

    /// <summary>A localised string looked up by cliloc id.</summary>
    Localized = 1,
}

/// <summary>A scrollable HTML area, optionally backed by a cliloc string.</summary>
public sealed class HtmlElement : ResizableElement, IFontedElement
{
    private int _fontIndex = TextElementDefaults.FontIndex;
    private GumpFontFamily _fontFamily;

    private string _html = string.Empty;
    private int _clilocId = 1000000;
    private bool _showScrollbar;
    private bool _showBackground;
    private HtmlContentKind _contentKind = HtmlContentKind.Html;
    private int _color;
    private string _arguments = string.Empty;

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

    /// <summary>
    /// Text colour as a packed RGB value, or 0 to leave it to the client.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a localised area: it selects the client's
    /// <c>xmfhtmlgumpcolor</c> form over plain <c>xmfhtmlgump</c>. Literal markup
    /// carries its own colour in a <c>BASEFONT</c> tag instead.
    /// </remarks>
    public int Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    /// <summary>
    /// Substitution arguments for the cliloc, separated by <c>@</c>, or empty for
    /// none.
    /// </summary>
    /// <remarks>
    /// Fills the <c>~1_THING~</c> placeholders a cliloc string can contain, and
    /// selects the client's <c>xmfhtmltok</c> form. Stored without the
    /// surrounding delimiters: <c>Bob@42</c> is emitted as <c>@Bob@42@</c>.
    /// </remarks>
    public string Arguments
    {
        get => _arguments;
        set => Set(ref _arguments, value ?? string.Empty);
    }

    /// <inheritdoc />
    public int FontIndex
    {
        get => _fontIndex;
        set => Set(ref _fontIndex, value);
    }

    /// <inheritdoc />
    public GumpFontFamily FontFamily
    {
        get => _fontFamily;
        set => Set(ref _fontFamily, value);
    }

    protected override void CopyTo(Element target)
    {
        HtmlElement clone = (HtmlElement)target;

        clone._fontIndex = _fontIndex;
        clone._fontFamily = _fontFamily;
        clone._html = _html;
        clone._clilocId = _clilocId;
        clone._showScrollbar = _showScrollbar;
        clone._showBackground = _showBackground;
        clone._contentKind = _contentKind;
        clone._color = _color;
        clone._arguments = _arguments;
    }

    protected override Element CreateInstance() => new HtmlElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}

/// <summary>A run of text drawn in one of the client's bitmap fonts.</summary>
public sealed class LabelElement : Element, IFontedElement
{
    private string _text = "Label";
    private int _hue;
    private int _fontIndex = TextElementDefaults.FontIndex;
    private GumpFontFamily _fontFamily;
    private bool _cropped;

    public override string TypeName => "Label";

    /// <inheritdoc />
    /// <remarks>
    /// A plain label is exactly as big as its rendered text, so there is nothing
    /// to drag. A cropped one is the client's <c>croppedtext</c>, which carries
    /// its own width and height and clips the text to them — so the rectangle
    /// becomes the thing being edited, and the element becomes resizable.
    /// </remarks>
    public override bool IsResizable => _cropped;

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

    /// <inheritdoc />
    public int FontIndex
    {
        get => _fontIndex;
        set => Set(ref _fontIndex, value);
    }

    /// <inheritdoc />
    public GumpFontFamily FontFamily
    {
        get => _fontFamily;
        set => Set(ref _fontFamily, value);
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
        clone._fontFamily = _fontFamily;
        clone._cropped = _cropped;
    }

    protected override Element CreateInstance() => new LabelElement();

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}
