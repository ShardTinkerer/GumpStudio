namespace GumpStudio.Core.Elements;

/// <summary>
/// Visitor over the element types.
/// </summary>
/// <remarks>
/// Exporters and the renderer implement this rather than switching on type. The
/// old exporters each carried their own near-identical <c>switch</c> with a
/// default arm, so a new element type was silently skipped instead of causing a
/// compile error.
/// </remarks>
public interface IElementVisitor
{
    void Visit(GroupElement element);

    void Visit(AlphaElement element);

    void Visit(BackgroundElement element);

    void Visit(ButtonElement element);

    void Visit(CheckboxElement element);

    void Visit(RadioElement element);

    void Visit(HtmlElement element);

    void Visit(ImageElement element);

    void Visit(ItemElement element);

    void Visit(LabelElement element);

    void Visit(PicInPicElement element);

    void Visit(TileAsGumpElement element);

    void Visit(TextEntryElement element);

    void Visit(TiledElement element);
}
