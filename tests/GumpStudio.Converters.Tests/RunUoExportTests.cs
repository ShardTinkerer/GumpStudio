using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;
using GumpStudio.Converters;

using Xunit;

namespace GumpStudio.Converters.Tests;

public class RunUoExportTests
{
    /// <summary>Fixed so output is reproducible and diffable.</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static GumpDocument WithElement(Element element)
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(element);

        return document;
    }

    private static string Build(GumpDocument document, RunUoExportOptions? options = null) =>
        RunUoScriptBuilder.Build(document, options, Stamp);

    private static GumpDocument BuildSample()
    {
        GumpDocument document = new();

        document.Properties.Location = new GumpPoint(50, 60);
        document.Properties.Movable = false;

        GumpPage page = document.Pages[0];

        page.Root.Add(new BackgroundElement
        {
            Name = "Frame",
            Location = GumpPoint.Origin,
            Size = new GumpSize(300, 200),
            GumpId = 5054,
        });

        page.Root.Add(new LabelElement { Location = new GumpPoint(10, 10), Text = "Hello", Hue = 88 });
        page.Root.Add(new ItemElement { Location = new GumpPoint(20, 40), ItemId = 3821, Hue = 12 });
        page.Root.Add(new ImageElement { Location = new GumpPoint(30, 50), GumpId = 1417 });
        page.Root.Add(new AlphaElement { Location = new GumpPoint(5, 5), Size = new GumpSize(50, 40) });

        page.Root.Add(new TiledElement
        {
            Location = new GumpPoint(60, 60),
            Size = new GumpSize(40, 30),
            GumpId = 4,
        });

        page.Root.Add(new ButtonElement
        {
            Name = "Okay",
            Location = new GumpPoint(15, 150),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Reply,
            Param = 7,
        });

        page.Root.Add(new CheckboxElement
        {
            Location = new GumpPoint(15, 170),
            CheckedId = 211,
            UncheckedId = 210,
            IsChecked = true,
            GroupId = 2,
        });

        page.Root.Add(new RadioElement
        {
            Location = new GumpPoint(60, 170),
            CheckedId = 208,
            UncheckedId = 209,
            GroupId = 3,
            Value = 4,
        });

        page.Root.Add(new TextEntryElement
        {
            Location = new GumpPoint(100, 100),
            Size = new GumpSize(120, 20),
            InitialText = "name",
            EntryId = 1,
        });

        page.Root.Add(new HtmlElement
        {
            Location = new GumpPoint(100, 130),
            Size = new GumpSize(150, 40),
            Html = "<b>hi</b>",
            ContentKind = HtmlContentKind.Html,
        });

        return document;
    }

    [Fact]
    public void EmitsAGumpSubclassWithTheExpectedCalls()
    {
        string script = Build(BuildSample(), new RunUoExportOptions { ClassName = "MyGump" });

        Assert.Contains("namespace Server.Gumps", script, StringComparison.Ordinal);
        Assert.Contains("public class MyGump : Gump", script, StringComparison.Ordinal);
        Assert.Contains("public MyGump() : base(50, 60)", script, StringComparison.Ordinal);
        Assert.Contains("Dragable = false;", script, StringComparison.Ordinal);
        Assert.Contains("AddPage(0);", script, StringComparison.Ordinal);
        Assert.Contains("AddBackground(0, 0, 300, 200, 5054);", script, StringComparison.Ordinal);
        Assert.Contains("AddLabel(10, 10, 88, @\"Hello\");", script, StringComparison.Ordinal);
        Assert.Contains("AddItem(20, 40, 3821, 12);", script, StringComparison.Ordinal);
        Assert.Contains("AddImage(30, 50, 1417);", script, StringComparison.Ordinal);
        Assert.Contains("AddAlphaRegion(5, 5, 50, 40);", script, StringComparison.Ordinal);
        Assert.Contains("AddImageTiled(60, 60, 40, 30, 4);", script, StringComparison.Ordinal);
        Assert.Contains("AddCheck(15, 170, 210, 211, true, 2);", script, StringComparison.Ordinal);
        Assert.Contains("AddRadio(60, 170, 209, 208, false, 4);", script, StringComparison.Ordinal);
        Assert.Contains(
            "AddTextEntry(100, 100, 120, 20, 0, 1, @\"name\");", script, StringComparison.Ordinal);
        Assert.Contains(
            "AddHtml(100, 130, 150, 40, @\"<b>hi</b>\", false, false);", script, StringComparison.Ordinal);
        Assert.Contains("public override void OnResponse(NetState sender, RelayInfo info)", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page button switches page client-side and never reaches OnResponse, so
    /// its buttonID stays zero and the target page goes in the param slot.
    /// </summary>
    [Fact]
    public void APageButtonCarriesItsTargetInTheParamSlot()
    {
        string script = Build(WithElement(new ButtonElement
        {
            Location = GumpPoint.Origin,
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Page,
            Param = 3,
        }));

        Assert.Contains("AddButton(0, 0, 1, 2, 0, GumpButtonType.Page, 3);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AReplyButtonCarriesItsIdInTheButtonSlot()
    {
        string script = Build(
            WithElement(new ButtonElement
            {
                Location = GumpPoint.Origin,
                NormalId = 1,
                PressedId = 2,
                Kind = ButtonKind.Reply,
                Param = 7,
            }),
            new RunUoExportOptions { ButtonIdStyle = RunUoButtonIdStyle.Numeric });

        Assert.Contains("AddButton(0, 0, 1, 2, 7, GumpButtonType.Reply, 0);", script, StringComparison.Ordinal);
        Assert.Contains("case 7:", script, StringComparison.Ordinal);
        Assert.DoesNotContain("enum Buttons", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original generated enum members with no values, so the ordinal
    /// silently replaced whatever response id the author had set.
    /// </summary>
    [Fact]
    public void NamedButtonIdsKeepTheAuthorsResponseValue()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new ButtonElement
        {
            Name = "Cancel",
            Kind = ButtonKind.Reply,
            Param = 0,
        });

        document.Pages[0].Root.Add(new ButtonElement
        {
            Name = "Okay",
            Kind = ButtonKind.Reply,
            Param = 7,
        });

        string script = Build(document);

        Assert.Contains("Cancel = 0,", script, StringComparison.Ordinal);
        Assert.Contains("Okay = 7,", script, StringComparison.Ordinal);
        Assert.Contains("(int)Buttons.Okay, GumpButtonType.Reply", script, StringComparison.Ordinal);
        Assert.Contains("case (int)Buttons.Okay:", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two buttons may share a response id, and two enum members may alias one
    /// value, but two case labels for the same value do not compile.
    /// </summary>
    [Fact]
    public void ButtonsSharingAResponseIdProduceOneCaseLabel()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new ButtonElement { Name = "First", Kind = ButtonKind.Reply, Param = 5 });
        document.Pages[0].Root.Add(new ButtonElement { Name = "Second", Kind = ButtonKind.Reply, Param = 5 });

        string script = Build(document);

        Assert.Contains("First = 5,", script, StringComparison.Ordinal);
        Assert.Contains("Second = 5,", script, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(script, "case (int)Buttons.First:"));
        Assert.Equal(0, Occurrences(script, "case (int)Buttons.Second:"));
    }

    [Fact]
    public void PageButtonsGetNoEnumMemberOrCaseLabel()
    {
        string script = Build(WithElement(new ButtonElement
        {
            Name = "NextPage",
            Kind = ButtonKind.Page,
            Param = 1,
        }));

        Assert.DoesNotContain("enum Buttons", script, StringComparison.Ordinal);
        Assert.DoesNotContain("NextPage", script.Replace("// NextPage", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// A checkbox reports through <c>info.Switches</c>, never through
    /// <c>info.ButtonID</c>. The original put its name in the same
    /// <c>Buttons</c> enum as the button ids and gave it a case label in the
    /// button switch, where it could also collide with a real button's id.
    /// </summary>
    [Fact]
    public void SwitchesStayOutOfTheButtonEnum()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new CheckboxElement { Name = "Agree", GroupId = 2 });
        document.Pages[0].Root.Add(new RadioElement { Name = "Choice", GroupId = 3, Value = 4 });

        string script = Build(document);

        Assert.DoesNotContain("enum Buttons", script, StringComparison.Ordinal);
        Assert.DoesNotContain("case ", script, StringComparison.Ordinal);
        Assert.Contains("AddCheck(0, 0, 209, 210, false, 2);", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original emitted verbatim literals but escaped quotes as <c>\"</c>,
    /// which is a syntax error inside one. A verbatim literal doubles the quote.
    /// </summary>
    [Fact]
    public void TextIsEscapedForAVerbatimLiteral()
    {
        string script = Build(WithElement(new LabelElement { Text = "say \"hi\" and c:\\path" }));

        Assert.Contains("@\"say \"\"hi\"\" and c:\\path\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"hi\\\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original's command handler assigned e.Mobile to an instance field from
    /// a static method, so the generated class did not compile.
    /// </summary>
    [Fact]
    public void TheCommandHandlerUsesALocalNotAField()
    {
        string script = Build(new GumpDocument(), new RunUoExportOptions { ClassName = "Shop" });

        Assert.Contains("public static void OnCommand(CommandEventArgs e)", script, StringComparison.Ordinal);
        Assert.Contains("Mobile from = e.Mobile;", script, StringComparison.Ordinal);
        Assert.Contains("from.SendGump(new Shop());", script, StringComparison.Ordinal);
        Assert.DoesNotContain("caller", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandCanBeLeftOut()
    {
        string script = Build(new GumpDocument(), new RunUoExportOptions { RegisterCommand = false });

        Assert.DoesNotContain("CommandSystem.Register", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[Usage(", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original only stripped spaces from element names, so anything else
    /// non-alphanumeric produced source that would not compile.
    /// </summary>
    [Theory]
    [InlineData("OK?", "OK")]
    [InlineData("2nd choice", "_2ndchoice")]
    [InlineData("!!!", "Button")]
    public void ElementNamesAreReducedToValidIdentifiers(string name, string expected)
    {
        string script = Build(WithElement(new ButtonElement { Name = name, Kind = ButtonKind.Reply, Param = 1 }));

        Assert.Contains($"{expected} = 1,", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateNamesAreMadeUnique()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new ButtonElement { Name = "Go", Kind = ButtonKind.Reply, Param = 1 });
        document.Pages[0].Root.Add(new ButtonElement { Name = "Go", Kind = ButtonKind.Reply, Param = 2 });

        string script = Build(document);

        Assert.Contains("Go = 1,", script, StringComparison.Ordinal);
        Assert.Contains("Go2 = 2,", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TextEntriesGetARelayInOnResponse()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new TextEntryElement { EntryId = 3, Size = new GumpSize(10, 10) });

        string script = Build(document);

        Assert.Contains("TextRelay entry3 = info.GetTextEntry(3);", script, StringComparison.Ordinal);
        Assert.Contains(
            "string text3 = entry3 == null ? \"\" : entry3.Text.Trim();", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedElementsExportAtTheirAbsolutePosition()
    {
        GumpDocument document = new();

        GroupElement outer = new() { Location = new GumpPoint(10, 20) };
        GroupElement inner = new() { Location = new GumpPoint(3, 4) };

        inner.Add(new ItemElement { Location = new GumpPoint(1, 2), ItemId = 9 });
        outer.Add(inner);
        document.Pages[0].Root.Add(outer);

        Assert.Contains("AddItem(14, 26, 9);", Build(document), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageIsEmitted()
    {
        GumpDocument document = new();

        document.AddPage();
        document.AddPage();

        string script = Build(document);

        Assert.Contains("AddPage(0);", script, StringComparison.Ordinal);
        Assert.Contains("AddPage(1);", script, StringComparison.Ordinal);
        Assert.Contains("AddPage(2);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputIsReproducibleForAFixedTimestamp()
    {
        GumpDocument document = BuildSample();

        Assert.Equal(Build(document), Build(document));
    }

    // -- Commands the client gained after 1.8 -------------------------------

    [Fact]
    public void ACroppedLabelUsesTheCroppedCall()
    {
        string script = Build(WithElement(new LabelElement
        {
            Text = "clip",
            Hue = 5,
            Cropped = true,
            Size = new GumpSize(90, 18),
        }));

        Assert.Contains("AddLabelCropped(0, 0, 90, 18, 5, @\"clip\");", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitedTextEntryPassesItsCap()
    {
        string script = Build(WithElement(new TextEntryElement
        {
            Size = new GumpSize(120, 20),
            EntryId = 2,
            MaxLength = 40,
        }));

        Assert.Contains("AddTextEntry(0, 0, 120, 20, 0, 2, @\"\", 40);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AButtonWithTileArtUsesTheTiledButtonCall()
    {
        string script = Build(
            WithElement(new ButtonElement
            {
                NormalId = 1,
                PressedId = 2,
                Kind = ButtonKind.Reply,
                Param = 7,
                TileId = 3821,
                TileHue = 33,
                TileX = 4,
                TileY = 5,
            }),
            new RunUoExportOptions { ButtonIdStyle = RunUoButtonIdStyle.Numeric });

        Assert.Contains(
            "AddImageTiledButton(0, 0, 1, 2, 7, GumpButtonType.Reply, 0, 3821, 33, 4, 5);",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void APicInPicMapsOntoTheCoreCall()
    {
        string script = Build(WithElement(new PicInPicElement
        {
            GumpId = 9000,
            Size = new GumpSize(20, 30),
            SourceX = 4,
            SourceY = 5,
        }));

        // AddSpriteImage is what both ServUO and ModernUO call it; no core has
        // ever defined AddPicInPic, which this used to emit and which does not
        // compile. Verified against a from-source build of ServUO Pub 57.
        Assert.Contains(
            "AddSpriteImage(0, 0, 9000, 20, 30, 4, 5);", script, StringComparison.Ordinal);

        Assert.DoesNotContain("AddPicInPic", script, StringComparison.Ordinal);

        // Both cores transpose these on the wire, so the note has to survive.
        Assert.Contains("transpose width/height with sx/sy", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TileArtInAGumpSlotIsReportedRatherThanFaked()
    {
        string script = Build(WithElement(new TileAsGumpElement { ItemId = 3821 }));

        // No core exposes tilepicasgumppic, so emitting a call would produce
        // source that does not compile.
        Assert.Contains("// No RunUO call for tilepicasgumppic", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "", "AddHtmlLocalized(0, 0, 200, 60, 1049004, false, false);")]
    [InlineData(32767, "", "AddHtmlLocalized(0, 0, 200, 60, 1049004, 32767, false, false);")]
    [InlineData(32767, "Bob@42", "AddHtmlLocalized(0, 0, 200, 60, 1049004, @\"Bob@42\", 32767, false, false);")]
    public void ALocalizedHtmlAreaPicksTheOverloadItNeeds(int color, string args, string expected)
    {
        string script = Build(WithElement(new HtmlElement
        {
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
            Color = color,
            Arguments = args,
        }));

        Assert.Contains(expected, script, StringComparison.Ordinal);
    }

    [Fact]
    public void TooltipsFollowTheElementTheyAttachTo()
    {
        string script = Build(WithElement(new ImageElement
        {
            GumpId = 55,
            TooltipClilocId = 1042971,
            TooltipArguments = "Bob",
            ItemPropertySerial = 1073741825,
        }));

        int image = script.IndexOf("AddImage(0, 0, 55);", StringComparison.Ordinal);
        int tooltip = script.IndexOf("AddTooltip(1042971);", StringComparison.Ordinal);
        int property = script.IndexOf("AddItemProperty(1073741825);", StringComparison.Ordinal);

        Assert.True(image >= 0 && tooltip > image && property > tooltip, script);
    }

    [Fact]
    public void GumpLevelFlagsBecomeConstructorCalls()
    {
        GumpDocument document = new();

        document.Properties.MasterGumpId = 3000;
        document.Properties.EnhancedClientInput = true;

        string script = Build(document);

        // AddMasterGump is in no RunUO or ServUO core; emitting it was a
        // compile error. ModernUO spells the command AddGumpIDOverride.
        Assert.Contains(
            "// No core call for mastergump 3000 (ModernUO: AddGumpIDOverride).",
            script,
            StringComparison.Ordinal);

        Assert.DoesNotContain("AddMasterGump", script, StringComparison.Ordinal);

        Assert.Contains("AddECHandleInput();", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// ServUO carries its two-argument <c>AddTooltip</c> commented out in its own
    /// source, so a tooltip's substitutions cannot be passed. The cliloc still
    /// renders; the export says what was dropped.
    /// </summary>
    [Fact]
    public void TooltipArgumentsAreReportedRatherThanPassed()
    {
        string script = Build(WithElement(new ImageElement
        {
            GumpId = 55,
            TooltipClilocId = 1042971,
            TooltipArguments = "Bob",
        }));

        Assert.Contains(
            "// Tooltip arguments dropped, no core overload takes them: @\"Bob\"",
            script,
            StringComparison.Ordinal);

        Assert.Contains("AddTooltip(1042971);", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both ServUO and ModernUO expose <c>AddGroup</c>. Skipping it left every
    /// radio on a page in one group, so buttons that should have been mutually
    /// exclusive were not.
    /// </summary>
    [Fact]
    public void RadioGroupsAreEmitted()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 1 });
        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 2 });

        string script = Build(document);

        int group = script.IndexOf("AddGroup(3);", StringComparison.Ordinal);
        int radio = script.IndexOf("AddRadio(", StringComparison.Ordinal);

        Assert.True(group >= 0 && radio > group, script);
    }

    /// <summary>
    /// The client resets the current group on every page, so a group used again
    /// on a later page has to be declared again there.
    /// </summary>
    [Fact]
    public void TheSameGroupIsDeclaredAgainOnTheNextPage()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 1 });
        document.AddPage().Root.Add(new RadioElement { GroupId = 3, Value = 2 });

        string script = Build(document);

        Assert.Equal(2, script.Split("AddGroup(3);").Length - 1);
    }

    [Fact]
    public void TheConverterOffersBothDialects()
    {
        RunUoConverter converter = new();

        Assert.Equal("runuo", converter.Id);
        Assert.Equal(".cs", converter.FileExtension);
        Assert.Equal(
            [RunUoConverter.Named, RunUoConverter.Numeric],
            converter.Dialects.Select(d => d.Id));

        Assert.Contains(
            "public class Test : Gump",
            converter.Export(new GumpDocument(), new GumpExportOptions { GumpName = "Test" }),
            StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;
}
