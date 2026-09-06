using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.TestSupport;

/// <summary>
/// Documents used to pin exporter output.
/// </summary>
/// <remarks>
/// <see cref="Full"/> is deliberately exhaustive rather than realistic: it is the
/// fixture the golden files are generated from, so anything it does not contain
/// is a change no golden can catch. It covers every element type, both button
/// kinds, every HTML content shape, nested groups, several pages, and the
/// awkward cases the exporters have historically got wrong.
/// </remarks>
public static class SampleDocuments
{
    /// <summary>A document exercising every element type and export edge case.</summary>
    public static GumpDocument Full()
    {
        GumpDocument document = new();

        // Every gump-level flag off its default, so each one reaches output.
        document.Properties.Location = new GumpPoint(50, 60);
        document.Properties.Movable = false;
        document.Properties.Closable = false;
        document.Properties.Disposable = false;
        document.Properties.TypeId = 42;
        document.Properties.MasterGumpId = 7;
        document.Properties.UpperWordCase = true;
        document.Properties.CroppedText = true;
        document.Properties.EnhancedClientInput = true;

        BuildFirstPage(document.Pages[0]);
        BuildSecondPage(document.AddPage("Page 1"));

        return document;
    }

    private static void BuildFirstPage(GumpPage page)
    {
        page.Root.Add(new BackgroundElement
        {
            Name = "Frame",
            Comment = "The window frame",
            Location = new GumpPoint(0, 0),
            Size = new GumpSize(300, 200),
            GumpId = 5054,
        });

        page.Root.Add(new AlphaElement
        {
            Name = "Wash",
            Location = new GumpPoint(5, 5),
            Size = new GumpSize(290, 190),
        });

        page.Root.Add(new TiledElement
        {
            Name = "Tiled",
            Location = new GumpPoint(10, 10),
            Size = new GumpSize(80, 20),
            GumpId = 5124,
            Hue = 33,
        });

        // A quote in both name and text: every exporter escapes, and each does it
        // differently. An unescaped quote produced source that would not build.
        page.Root.Add(new LabelElement
        {
            Name = "Title",
            Comment = "Says \"hello\"",
            Location = new GumpPoint(20, 20),
            Text = "He said \"hi\"",
            Hue = 88,
        });

        // Cropped must be set before Size: IsResizable is false until it is, so
        // an earlier Size assignment would be silently discarded.
        LabelElement cropped = new()
        {
            Name = "Cropped",
            Location = new GumpPoint(20, 35),
            Text = "Clipped text",
            Hue = 90,
            Cropped = true,
        };

        cropped.Size = new GumpSize(120, 18);
        page.Root.Add(cropped);

        // Empty text drives the placeholder path, which differs per dialect.
        // Text has to be cleared explicitly: it defaults to "Label", so an
        // untouched label never reaches that path.
        page.Root.Add(new LabelElement
        {
            Name = "Empty",
            Location = new GumpPoint(20, 55),
            Text = string.Empty,
        });

        page.Root.Add(new ItemElement
        {
            Name = "Pack",
            Location = new GumpPoint(20, 70),
            ItemId = 3821,
        });

        page.Root.Add(new ItemElement
        {
            Name = "HuedPack",
            Location = new GumpPoint(45, 70),
            ItemId = 3821,
            Hue = 12,
        });

        page.Root.Add(new ImageElement { Name = "Plain", Location = new GumpPoint(70, 70), GumpId = 1417 });

        page.Root.Add(new ImageElement
        {
            Name = "Hued",
            Location = new GumpPoint(95, 70),
            GumpId = 1417,
            Hue = 22,
        });

        page.Root.Add(new ImageElement
        {
            Name = "PartialHued",
            Location = new GumpPoint(120, 70),
            GumpId = 1417,
            Hue = 22,
            PartialHue = true,
        });

        page.Root.Add(new PicInPicElement
        {
            Name = "Crop",
            Location = new GumpPoint(20, 95),
            Size = new GumpSize(60, 24),
            GumpId = 1417,
            SourceX = 10,
            SourceY = 20,
        });

        page.Root.Add(new PicInPicElement
        {
            Name = "CropHued",
            Location = new GumpPoint(85, 95),
            Size = new GumpSize(60, 24),
            GumpId = 1417,
            SourceX = 10,
            SourceY = 20,
            Hue = 15,
            PartialHue = true,
        });

        page.Root.Add(new TileAsGumpElement
        {
            Name = "Icon",
            Location = new GumpPoint(150, 95),
            ItemId = 3823,
            LinkId = 2,
            ParamB = 3,
            ParamC = 4,
        });

        // Tooltip and item property attach to the element before them, so this
        // element produces three commands but only one comment.
        page.Root.Add(new ButtonElement
        {
            Name = "Go to page 1",
            Comment = "Switches page",
            Location = new GumpPoint(20, 125),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Page,
            Param = 1,
            TooltipClilocId = 1011036,
            TooltipArguments = "one\tTWO",
            ItemPropertySerial = 12345,
        });

        page.Root.Add(new ButtonElement
        {
            Name = "Accept",
            Location = new GumpPoint(60, 125),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Reply,
            Param = 5,
            CodeBehind = "SRC.SYSMESSAGE Accepted",
        });

        // A second reply button with the SAME response id. RunUO must emit one
        // case label for the two; Sphere currently emits two ON= blocks.
        page.Root.Add(new ButtonElement
        {
            Name = "Accept again",
            Location = new GumpPoint(100, 125),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Reply,
            Param = 5,
        });

        page.Root.Add(new ButtonElement
        {
            Name = "Tiled button",
            Location = new GumpPoint(140, 125),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Reply,
            Param = 6,
            TileId = 3823,
            TileHue = 8,
            TileX = 4,
            TileY = 2,
        });

        page.Root.Add(new CheckboxElement
        {
            Name = "Agree",
            Location = new GumpPoint(20, 150),
            CheckedId = 211,
            UncheckedId = 210,
            IsChecked = true,
        });

        page.Root.Add(new RadioElement
        {
            Name = "First",
            Location = new GumpPoint(60, 150),
            CheckedId = 209,
            UncheckedId = 208,
            GroupId = 3,
            Value = 1,
        });

        page.Root.Add(new RadioElement
        {
            Name = "Second",
            Location = new GumpPoint(100, 150),
            CheckedId = 209,
            UncheckedId = 208,
            GroupId = 3,
            Value = 2,
        });

        TextEntryElement entry = new()
        {
            Name = "Name",
            Location = new GumpPoint(20, 170),
            InitialText = "type here",
            Hue = 5,
            EntryId = 1,
        };

        entry.Size = new GumpSize(120, 20);
        page.Root.Add(entry);

        TextEntryElement limited = new()
        {
            Name = "Limited",
            Location = new GumpPoint(150, 170),
            Hue = 5,
            EntryId = 2,
            MaxLength = 16,
        };

        limited.Size = new GumpSize(120, 20);
        page.Root.Add(limited);

        // A group at an offset containing a nested group: the case every 1.8
        // exporter got wrong by emitting parent-relative coordinates.
        GroupElement outer = new() { Name = "Nested", Location = new GumpPoint(150, 60) };
        GroupElement inner = new() { Name = "Inner", Location = new GumpPoint(10, 10) };

        inner.Add(new ImageElement { Name = "Deep", Location = new GumpPoint(5, 5), GumpId = 1417 });
        outer.Add(inner);
        page.Root.Add(outer);
    }

    private static void BuildSecondPage(GumpPage page)
    {
        // Same group id as page 0. `page` resets the client's current group, so
        // this must emit its own `group 3`; tracking the last group across the
        // whole document silently dropped it.
        page.Root.Add(new RadioElement
        {
            Name = "Third",
            Location = new GumpPoint(20, 20),
            CheckedId = 209,
            UncheckedId = 208,
            GroupId = 3,
            Value = 3,
        });

        HtmlElement markup = new()
        {
            Name = "Markup",
            Location = new GumpPoint(20, 40),
            Html = "<basefont color=#ffffff>literal \"markup\"",
            ContentKind = HtmlContentKind.Html,
            ShowBackground = true,
            ShowScrollbar = true,
        };

        markup.Size = new GumpSize(200, 60);
        page.Root.Add(markup);

        HtmlElement localised = new()
        {
            Name = "Localised",
            Location = new GumpPoint(20, 105),
            ClilocId = 1049004,
            ContentKind = HtmlContentKind.Localized,
        };

        localised.Size = new GumpSize(200, 40);
        page.Root.Add(localised);

        HtmlElement coloured = new()
        {
            Name = "Coloured",
            Location = new GumpPoint(20, 150),
            ClilocId = 1049005,
            ContentKind = HtmlContentKind.Localized,
            Color = 0x00FF00,
            ShowBackground = true,
        };

        coloured.Size = new GumpSize(200, 40);
        page.Root.Add(coloured);

        // xmfhtmltok: its parameter order genuinely differs from the colour
        // form, so both belong in the fixture.
        HtmlElement tokenised = new()
        {
            Name = "Tokenised",
            Location = new GumpPoint(20, 195),
            ClilocId = 1049006,
            ContentKind = HtmlContentKind.Localized,
            Color = 0x00FF00,
            Arguments = "alpha\tbeta",
            ShowScrollbar = true,
        };

        tokenised.Size = new GumpSize(200, 40);
        page.Root.Add(tokenised);

        page.Root.Add(new ButtonElement
        {
            Name = "Back",
            Location = new GumpPoint(20, 240),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Page,
            Param = 0,
        });
    }
}
