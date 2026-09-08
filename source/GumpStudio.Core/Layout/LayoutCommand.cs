using GumpStudio.Core.Elements;

namespace GumpStudio.Core.Layout;

/// <summary>
/// One command the client's gump parser understands.
/// </summary>
/// <remarks>
/// <para>
/// The set mirrors the client's own command table, so a converter's job is to
/// spell each one in its target's syntax rather than to work out what a document
/// means. Coordinates are already absolute: <see cref="GumpLayoutBuilder"/> is
/// the only caller of <see cref="Element.GetAbsolutePosition"/>, so a converter
/// cannot reintroduce the nested-group defect by reading a relative one.
/// </para>
/// <para>
/// Fields are typed and semantic, not pre-rendered slots. A button carries its
/// <see cref="ButtonKind"/> and parameter, not <c>quit</c>/<c>page</c>/<c>return</c>:
/// two targets pack those slots the same way and RunUO packs them inversely, so
/// the packing belongs to each writer.
/// </para>
/// </remarks>
public abstract record LayoutCommand(CommandOrigin Origin);

/// <summary>Starts a page. Every element after it belongs to that page.</summary>
public sealed record PageCommand(CommandOrigin Origin, int Page)
    : LayoutCommand(Origin);

/// <summary>
/// Opens a radio group.
/// </summary>
/// <remarks>
/// Unrelated to <see cref="GroupElement"/>, which is an editor construct that
/// <see cref="GroupElement.Leaves"/> flattens away. This is the client's own
/// <c>group</c>, which decides which radio buttons are mutually exclusive, and it
/// is reset by every page.
/// </remarks>
public sealed record GroupCommand(CommandOrigin Origin, int Group)
    : LayoutCommand(Origin);

/// <summary>Closes the open radio group at the end of a page.</summary>
public sealed record EndGroupCommand(CommandOrigin Origin)
    : LayoutCommand(Origin);

/// <summary>A nine-slice background. The client's <c>resizepic</c>.</summary>
public sealed record ResizePicCommand(
    CommandOrigin Origin, int X, int Y, int GumpId, int Width, int Height)
    : LayoutCommand(Origin);

/// <summary>A translucent rectangle. The client's <c>checkertrans</c>.</summary>
public sealed record CheckerTransCommand(
    CommandOrigin Origin, int X, int Y, int Width, int Height)
    : LayoutCommand(Origin);

/// <summary>
/// A gump image tiled to fill a rectangle. The client's <c>gumppictiled</c>.
/// </summary>
/// <remarks>
/// The command takes no hue, so <see cref="TiledElement.Hue"/> reaches no output.
/// That is the existing behaviour of every exporter, preserved deliberately.
/// </remarks>
public sealed record GumpPicTiledCommand(
    CommandOrigin Origin, int X, int Y, int Width, int Height, int GumpId)
    : LayoutCommand(Origin);

/// <summary>
/// A single gump image, plain or tinted.
/// </summary>
/// <remarks>
/// Three client commands in one: <c>gumppic</c>, <c>gumppic</c> with a hue, and
/// <c>gumppicphued</c>, which tints only the grayscale pixels so that art which
/// already carries colour is left alone.
/// </remarks>
public sealed record GumpPicCommand(
    CommandOrigin Origin, int X, int Y, int GumpId, int Hue, bool PartialHue)
    : LayoutCommand(Origin);

/// <summary>A cropped region of a gump image. The client's <c>picinpic</c> family.</summary>
public sealed record PicInPicCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int GumpId,
    int SourceX,
    int SourceY,
    int Width,
    int Height,
    int Hue,
    bool PartialHue)
    : LayoutCommand(Origin);

/// <summary>An item graphic. The client's <c>tilepic</c> and <c>tilepichue</c>.</summary>
public sealed record TilePicCommand(
    CommandOrigin Origin, int X, int Y, int ItemId, int Hue)
    : LayoutCommand(Origin);

/// <summary>
/// An item graphic drawn as gump art. The client's <c>tilepicasgumppic</c>.
/// </summary>
/// <remarks>
/// <paramref name="ParamB"/> and <paramref name="ParamC"/> have no established
/// meaning even from the client binary, so they are passed through unchanged
/// rather than guessed at.
/// </remarks>
public sealed record TileAsGumpPicCommand(
    CommandOrigin Origin, int X, int Y, int ItemId, int LinkId, int ParamB, int ParamC)
    : LayoutCommand(Origin);

/// <summary>A line of text. The client's <c>text</c>.</summary>
public sealed record TextCommand(
    CommandOrigin Origin, int X, int Y, int Hue, TextRef Text)
    : LayoutCommand(Origin);

/// <summary>
/// Text clipped to its own rectangle. The client's <c>croppedtext</c>.
/// </summary>
/// <remarks>
/// Unlike a plain label, this one owns a width and height — which is why a
/// cropped label is resizable in the editor and a plain one is measured.
/// </remarks>
public sealed record CroppedTextCommand(
    CommandOrigin Origin, int X, int Y, int Width, int Height, int Hue, TextRef Text)
    : LayoutCommand(Origin);

/// <summary>
/// An editable field. The client's <c>textentry</c> and <c>textentrylimited</c>.
/// </summary>
/// <remarks>
/// A non-zero <c>MaxLength</c> selects the limited form, which carries the cap as
/// an extra trailing slot.
/// </remarks>
public sealed record TextEntryCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int Width,
    int Height,
    int Hue,
    int EntryId,
    TextRef Text,
    int MaxLength)
    : LayoutCommand(Origin);

/// <summary>A literal-markup area. The client's <c>htmlgump</c>.</summary>
public sealed record HtmlGumpCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int Width,
    int Height,
    TextRef Text,
    bool Background,
    bool Scrollbar)
    : LayoutCommand(Origin);

/// <summary>
/// A localised text area, in whichever of its three forms applies.
/// </summary>
/// <remarks>
/// <para>
/// <c>xmfhtmltok</c> is not the colour form with arguments appended: its
/// background and scrollbar flags come <em>before</em> the colour and its cliloc
/// id comes last. Writing it the other way produces a gump that renders the wrong
/// string, so the distinction is kept in the data rather than left to each writer
/// to remember.
/// </para>
/// <para>
/// <c>Color</c> is a packed RGB value, or 0 to leave the colour to the client;
/// <c>Arguments</c> holds tab-separated substitutions, or is empty for none.
/// Arguments select the token form, and a colour alone selects the colour form.
/// </para>
/// </remarks>
public sealed record XmfHtmlCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int Width,
    int Height,
    int ClilocId,
    bool Background,
    bool Scrollbar,
    int Color,
    string Arguments)
    : LayoutCommand(Origin);

/// <summary>
/// A button. The client's <c>button</c> and <c>buttontileart</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Kind</c> says whether pressing it switches page or replies and closes, and
/// <c>Param</c> is the target page or the response id accordingly. The client
/// expresses this through three slots, which the 1.8 exporter filled in wrongly —
/// its own source carries a <c>// TODO: Page or Reply???</c> comment at the spot.
/// Keeping the intent rather than the slots is what stops that being re-derived.
/// </para>
/// <para>
/// <c>Tile</c> is art drawn over the button, or null for a plain one.
/// <c>CodeBehind</c> is script attached to a reply button's handler: no client
/// command carries it, and only the Sphere converter emits it.
/// </para>
/// </remarks>
public sealed record ButtonCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int NormalId,
    int PressedId,
    ButtonKind Kind,
    int Param,
    ButtonTile? Tile,
    string CodeBehind)
    : LayoutCommand(Origin);

/// <summary>Tile art overlaid on a button.</summary>
public readonly record struct ButtonTile(int ItemId, int Hue, int X, int Y);

/// <summary>A checkbox. The client's <c>checkbox</c>.</summary>
public sealed record CheckboxCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int UncheckedId,
    int CheckedId,
    bool IsChecked,
    int Group)
    : LayoutCommand(Origin);

/// <summary>
/// A radio button. The client's <c>radio</c>.
/// </summary>
/// <remarks>
/// Which group it belongs to is carried by the preceding
/// <see cref="GroupCommand"/>, exactly as the client reads it.
/// </remarks>
public sealed record RadioCommand(
    CommandOrigin Origin,
    int X,
    int Y,
    int UncheckedId,
    int CheckedId,
    bool IsChecked,
    int Value)
    : LayoutCommand(Origin);

/// <summary>
/// A tooltip on the element before it. The client's <c>tooltip</c>.
/// </summary>
/// <remarks>
/// It has no coordinates: the client applies it to whichever element it created
/// most recently. The builder therefore emits it directly after its own element's
/// command, so a converter cannot attach it to the wrong element by reordering.
/// </remarks>
public sealed record TooltipCommand(
    CommandOrigin Origin, int ClilocId, string Arguments)
    : LayoutCommand(Origin);

/// <summary>An item-property tooltip on the element before it.</summary>
public sealed record ItemPropertyCommand(CommandOrigin Origin, int Serial)
    : LayoutCommand(Origin);
