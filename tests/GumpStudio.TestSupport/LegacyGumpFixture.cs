namespace GumpStudio.TestSupport;

/// <summary>
/// Builds synthesized GumpStudio 1.8 <c>.gump</c> payloads.
/// </summary>
/// <remarks>
/// The member names and ordering mirror the original's <c>GetObjectData</c>
/// implementations exactly, as recovered by decompiling the shipped binary. If a
/// real 1.8 file ever turns up it should be added to the fixture set alongside
/// these, since only a genuine file can confirm the recovered layout.
/// </remarks>
public static class LegacyGumpFixture
{
    /// <summary>Assembly the element types lived in.</summary>
    public const string Library = "GumpStudioCore, Version=1.8.3.0, Culture=neutral, PublicKeyToken=null";

    private const string PointType = "System.Drawing.Point";
    private const string SizeType = "System.Drawing.Size";
    private const string ArrayListType = "System.Collections.ArrayList";

    /// <summary>A <c>System.Drawing.Point</c>.</summary>
    public static NrbfObject Point(int x, int y) =>
        new(PointType, null, ("x", new NrbfInt(x)), ("y", new NrbfInt(y)));

    /// <summary>A <c>System.Drawing.Size</c>.</summary>
    public static NrbfObject Size(int width, int height) =>
        new(SizeType, null, ("width", new NrbfInt(width)), ("height", new NrbfInt(height)));

    /// <summary>An <c>ArrayList</c>, which serialises as items plus a live count.</summary>
    public static NrbfObject ArrayList(params NrbfValue[] items) =>
        new(
            ArrayListType,
            null,
            ("_items", new NrbfObjectArray(items)),
            ("_size", new NrbfInt(items.Length)),
            ("_version", new NrbfInt(items.Length)));

    /// <summary>The members every element wrote, in the original's order.</summary>
    private static (string, NrbfValue)[] BaseMembers(
        string name, int x, int y, int width, int height, string comment) =>
        [
            ("BaseElementVersion", new NrbfInt(2)),
            ("Name", new NrbfString(name)),
            ("Location", Point(x, y)),
            ("Size", Size(width, height)),
            ("Parent", new NrbfNull("GumpStudio.Elements.GroupElement", Library)),
            ("Comment", new NrbfString(comment)),
        ];

    private static NrbfObject Element(
        string typeName,
        string name,
        int x,
        int y,
        int width,
        int height,
        string comment,
        params (string, NrbfValue)[] extra) =>
        new(
            $"GumpStudio.Elements.{typeName}",
            Library,
            [.. BaseMembers(name, x, y, width, height, comment), .. extra]);

    public static NrbfObject Group(int x, int y, params NrbfValue[] children) =>
        Element(
            "GroupElement",
            "Group",
            x,
            y,
            0,
            0,
            string.Empty,
            ("GroupElementVersion", new NrbfInt(1)),
            ("Elements", ArrayList(children)),
            ("IsBaseWindow", new NrbfBool(false)));

    public static NrbfObject PageRoot(params NrbfValue[] children) =>
        Element(
            "GroupElement",
            "Base",
            0,
            0,
            0,
            0,
            string.Empty,
            ("GroupElementVersion", new NrbfInt(1)),
            ("Elements", ArrayList(children)),
            ("IsBaseWindow", new NrbfBool(true)));

    public static NrbfObject Label(int x, int y, string text, int hueIndex = 0, int fontIndex = 0) =>
        Element(
            "LabelElement",
            "Label 1",
            x,
            y,
            0,
            0,
            string.Empty,
            ("LabelElementVersion", new NrbfInt(2)),
            ("Text", new NrbfString(text)),
            ("HueIndex", new NrbfInt(hueIndex)),
            ("FontIndex", new NrbfInt(fontIndex)),
            ("Cropped", new NrbfBool(false)));

    public static NrbfObject Background(int x, int y, int width, int height, int gumpId) =>
        Element(
            "BackgroundElement",
            "Background 1",
            x,
            y,
            width,
            height,
            string.Empty,
            ("BackgroundElementVersion", new NrbfInt(1)),
            ("GumpID", new NrbfInt(gumpId)));

    public static NrbfObject Image(int x, int y, int gumpId, int hueIndex = 0) =>
        Element(
            "ImageElement",
            "Image 1",
            x,
            y,
            0,
            0,
            string.Empty,
            ("ImageElementVersion", new NrbfInt(2)),
            ("GumpID", new NrbfInt(gumpId)),
            ("HueIndex", new NrbfInt(hueIndex)));

    public static NrbfObject Item(int x, int y, int itemId, int hueIndex = 0) =>
        Element(
            "ItemElement",
            "Item 1",
            x,
            y,
            0,
            0,
            string.Empty,
            ("ItemElementVersion", new NrbfInt(1)),
            ("ItemID", new NrbfInt(itemId)),
            ("HueIndex", new NrbfInt(hueIndex)));

    public static NrbfObject Button(int x, int y, int normalId, int pressedId, int type, int param) =>
        Element(
            "ButtonElement",
            "Button 1",
            x,
            y,
            0,
            0,
            string.Empty,
            ("ButtonElementVersion", new NrbfInt(1)),
            ("PressedID", new NrbfInt(pressedId)),
            ("NormalID", new NrbfInt(normalId)),
            ("Type", new NrbfInt(type)),
            ("State", new NrbfInt(0)),
            ("CodeBehind", new NrbfString(string.Empty)),
            ("Param", new NrbfInt(param)));

    public static NrbfObject Checkbox(int x, int y, bool isChecked, int groupId) =>
        Element(
            "CheckboxElement",
            "Checkbox 1",
            x,
            y,
            0,
            0,
            string.Empty,
            ("CheckboxVersion", new NrbfInt(1)),
            ("Checked", new NrbfBool(isChecked)),
            ("CheckedID", new NrbfInt(211)),
            ("UncheckedID", new NrbfInt(210)),
            ("GroupID", new NrbfInt(groupId)));

    public static NrbfObject Radio(int x, int y, bool isChecked, int groupId, int value) =>
        Element(
            "RadioElement",
            "Radio 1",
            x,
            y,
            0,
            0,
            string.Empty,
            ("CheckboxVersion", new NrbfInt(1)),
            ("Checked", new NrbfBool(isChecked)),
            ("CheckedID", new NrbfInt(208)),
            ("UncheckedID", new NrbfInt(209)),
            ("GroupID", new NrbfInt(groupId)),
            ("RadioElementVersion", new NrbfInt(2)),
            ("Value", new NrbfInt(value)));

    public static NrbfObject TextEntry(int x, int y, int width, int height, string text, int entryId) =>
        Element(
            "TextEntryElement",
            "TextEntry 1",
            x,
            y,
            width,
            height,
            string.Empty,
            ("ResizableElementVersion", new NrbfInt(1)),
            ("TextEntryElementVersion", new NrbfInt(2)),
            ("Text", new NrbfString(text)),
            ("HueIndex", new NrbfInt(0)),
            ("ID", new NrbfInt(entryId)),
            ("MaxLength", new NrbfInt(0)));

    public static NrbfObject Html(int x, int y, int width, int height, string html, int clilocId, int textType) =>
        Element(
            "HTMLElement",
            "Html 1",
            x,
            y,
            width,
            height,
            string.Empty,
            ("HTMLElementVersion", new NrbfInt(1)),
            ("HTML", new NrbfString(html)),
            ("ClilocID", new NrbfInt(clilocId)),
            ("Scrollbar", new NrbfBool(true)),
            ("Background", new NrbfBool(true)),
            ("TextType", new NrbfInt(textType)));

    public static NrbfObject Alpha(int x, int y, int width, int height) =>
        Element(
            "AlphaElement",
            "Alpha 1",
            x,
            y,
            width,
            height,
            string.Empty,
            ("AlphaElementVersion", new NrbfInt(1)));

    public static NrbfObject Tiled(int x, int y, int width, int height, int gumpId, int hueIndex = 0) =>
        Element(
            "TiledElement",
            "TiledElement 1",
            x,
            y,
            width,
            height,
            string.Empty,
            ("TiledElementVersion", new NrbfInt(2)),
            ("GumpID", new NrbfInt(gumpId)),
            ("HueIndex", new NrbfInt(hueIndex)));

    /// <summary>The gump-level properties payload, written after the page list.</summary>
    public static NrbfObject Properties(
        int x, int y, bool movable = true, bool closable = true, bool disposable = true, int type = 0) =>
        new(
            "GumpStudio.Elements.GumpProperties",
            Library,
            ("Version", new NrbfInt(1)),
            ("Location", Point(x, y)),
            ("Moveable", new NrbfBool(movable)),
            ("Closeable", new NrbfBool(closable)),
            ("Disposeable", new NrbfBool(disposable)),
            ("Type", new NrbfInt(type)));

    /// <summary>
    /// Assembles a legacy <c>.gumpling</c> file: one saved group, on its own.
    /// </summary>
    /// <remarks>
    /// Unlike a <c>.gump</c> there is no page list and no properties record —
    /// the group is the whole file, which is why it loads as an insertion
    /// rather than as a document.
    /// </remarks>
    public static byte[] BuildGumpling(int x = 10, int y = 20, params NrbfValue[] children)
    {
        NrbfFixtureWriter writer = new();

        return writer.WriteAll(Group(x, y, children.Length > 0 ? children : [Label(1, 2, "inside")]));
    }

    /// <summary>Writes a <see cref="BuildGumpling"/> payload to a file.</summary>
    public static void WriteGumpling(string path, int x = 10, int y = 20, params NrbfValue[] children)
    {
        ArgumentNullException.ThrowIfNull(path);

        File.WriteAllBytes(path, BuildGumpling(x, y, children));
    }

    /// <summary>Assembles a complete legacy <c>.gump</c> file.</summary>
    public static byte[] BuildDocument(IEnumerable<NrbfValue> pages, NrbfObject? properties = null)
    {
        ArgumentNullException.ThrowIfNull(pages);

        NrbfFixtureWriter writer = new();
        NrbfValue stacks = ArrayList([.. pages]);

        return properties is null
            ? writer.WriteAll(stacks)
            : writer.WriteAll(stacks, properties);
    }
}
