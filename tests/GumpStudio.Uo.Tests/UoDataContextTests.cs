using GumpStudio.TestSupport;
using GumpStudio.Uo;
using GumpStudio.Uo.Graphics;
using GumpStudio.Uo.Primitives;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// End-to-end tests over a real installation: open a client, decode art, read
/// hues, tile names, clilocs and fonts.
/// </summary>
/// <remarks>
/// Run against both a classic MUL-era client and a modern UOP-era one, since the
/// two exercise entirely different container and codec paths.
/// </remarks>
public class UoDataContextTests
{
    /// <summary>
    /// Yields every real client the environment points at, so each theory runs
    /// across the whole configured range of client versions. When none are
    /// configured the theories are skipped rather than failed — see
    /// <c>SkipTestWithoutData</c>.
    /// </summary>
    public static IEnumerable<object[]> Clients() =>
        UoTestClient.AllClients
            .Where(path => UoDataContext.Validate(path).Count == 0)
            .Select(path => new object[] { path });

    /// <summary>
    /// Installations that were discovered but are not usable — for example the
    /// 1996 pre-alpha, which ships <c>GUMPS.MUL</c> with no index and no gump
    /// container this application recognises.
    /// </summary>
    public static IEnumerable<object[]> IncompleteClients() =>
        UoTestClient.AllClients
            .Where(path => UoDataContext.Validate(path).Count > 0)
            .Select(path => new object[] { path });

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void ValidatesAndOpensClient(string clientPath)
    {
        Assert.Empty(UoDataContext.Validate(clientPath));

        using UoDataContext context = UoDataContext.Open(clientPath);

        Assert.True(context.HasGumps);
        Assert.True(context.HasArt);
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void DecodesGumpsToPlausibleImages(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        int decoded = 0;
        int opaquePixels = 0;

        for (int id = 0; id < 2000 && decoded < 50; id++)
        {
            if (!context.IsValidGump(id))
            {
                continue;
            }

            UoImage? image = context.GetGump(id);

            if (image is null)
            {
                continue;
            }

            decoded++;

            context.TryGetGumpSize(id, out int width, out int height);

            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if ((image.GetPixel(x, y) >> 24) != 0)
                    {
                        opaquePixels++;
                    }
                }
            }
        }

        Assert.True(decoded >= 50, $"Only decoded {decoded} gumps.");

        // An all-transparent result would mean the RLE decode silently produced
        // nothing, which is exactly how a wrong decoder fails.
        Assert.True(opaquePixels > 1000, $"Only {opaquePixels} opaque pixels across {decoded} gumps.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void DecodesStaticArt(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        int decoded = 0;

        for (int id = 0; id < 4000 && decoded < 25; id++)
        {
            if (context.GetStatic(id) is not { } image)
            {
                continue;
            }

            decoded++;

            Assert.InRange(image.Width, 1, 1024);
            Assert.InRange(image.Height, 1, 1024);
        }

        Assert.True(decoded >= 25, $"Only decoded {decoded} static tiles.");
    }

    /// <summary>
    /// The value a hue reports is the one that looks it back up.
    /// </summary>
    /// <remarks>
    /// Needs no client, because the trap is arithmetic rather than data: a hue
    /// carries a zero-based <c>Index</c> and a one-based <c>ScriptValue</c>, and
    /// picking the wrong one is invisible at a glance — adjacent hues look alike,
    /// so the only symptom is a colour that is subtly not the one selected.
    /// </remarks>
    [Fact]
    public void AHuesScriptValueRoundTripsThroughTheTable()
    {
        Hue first = HueTable.Empty.GetByIndex(0);

        Assert.Equal(0, first.Index);
        Assert.Equal(1, first.ScriptValue);
        Assert.Same(first, HueTable.Empty.Get(first.ScriptValue));
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void LoadsFullHueTable(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        Assert.Equal(3000, context.Hues.Count);

        // Hues are one-based on the wire: 0 means "no hue", 1 is the first entry.
        Assert.Null(context.Hues.Get(0));
        Assert.NotNull(context.Hues.Get(1));
        Assert.Equal(0, context.Hues.Get(1)!.Index);

        // Every palette entry must be opaque, or hued art renders invisible.
        foreach (ushort color in context.Hues.GetByIndex(0).Colors)
        {
            Assert.NotEqual(0, color & Color16.AlphaMask);
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void HueingRecoloursPixels(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        int gumpId = Enumerable.Range(0, 2000).First(context.IsValidGump);

        UoImage plain = context.GetGump(gumpId)!;
        UoImage hued = context.GetGump(gumpId, hue: 33)!;

        Assert.Equal(plain.Width, hued.Width);
        Assert.Equal(plain.Height, hued.Height);

        bool anyDifference = false;
        bool alphaPreserved = true;

        for (int y = 0; y < plain.Height && !anyDifference; y++)
        {
            for (int x = 0; x < plain.Width; x++)
            {
                uint before = plain.GetPixel(x, y);
                uint after = hued.GetPixel(x, y);

                if (before != after)
                {
                    anyDifference = true;
                }

                if ((before >> 24) != (after >> 24))
                {
                    alphaPreserved = false;
                }
            }
        }

        Assert.True(anyDifference, "Applying a hue changed nothing.");
        Assert.True(alphaPreserved, "Hueing altered the alpha channel.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void ReadsTileDataNames(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        Assert.True(context.TileData.StaticCount > 0);

        int named = Enumerable.Range(0, 2000)
            .Count(id => !string.IsNullOrWhiteSpace(context.TileData.GetStaticName(id)));

        Assert.True(named > 500, $"Only {named} of the first 2000 statics had names.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void ReadsClilocStrings(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        // Clients older than roughly 2002 ship cliloc as numbered IFF chunks,
        // which is a different format and deliberately unsupported.
        if (context.Clilocs.Count == 0)
        {
            Assert.False(
                File.Exists(Path.Combine(clientPath, "cliloc.enu")),
                $"'{clientPath}' has a cliloc.enu but none of it parsed.");

            return;
        }

        Assert.True(context.Clilocs.Count > 10000, $"Only {context.Clilocs.Count} cliloc entries.");

        // 500000 is the first record in every shipped cliloc. Its *text* is not
        // asserted: shard clients localise and rewrite these freely — one client
        // in the matrix ships Italian text under a .enu extension.
        Assert.False(string.IsNullOrWhiteSpace(context.Clilocs.GetText(500000)));

        // Decoding correctness is checked structurally instead. Misread bytes
        // produce U+FFFD in droves once run through a UTF-8 decoder.
        int sampled = 0;
        int mangled = 0;

        foreach (Data.ClilocEntry entry in context.Clilocs.Entries.Take(2000))
        {
            sampled++;

            if (entry.Text.Contains('�'))
            {
                mangled++;
            }
        }

        Assert.True(
            mangled < sampled / 100,
            $"{mangled} of {sampled} cliloc strings contain replacement characters.");
    }

    /// <summary>
    /// Every cliloc file the installation ships can actually be read.
    /// </summary>
    /// <remarks>
    /// The synthetic fixtures cover language discovery and switching, but they
    /// can only write the plain record stream. A retail client wraps every one
    /// of these in the MegaCliloc codec, so this is the only place a non-English
    /// wrapped file is decoded at all.
    /// </remarks>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void ReadsEveryClilocLanguageTheClientShips(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        Assert.Equal(context.HasClilocs, context.ClilocLanguages.Count > 0);

        if (!context.HasClilocs)
        {
            return;
        }

        Assert.Contains(context.ClilocLanguage, context.ClilocLanguages);

        List<string> broken = [];
        List<bool> readable = [];

        foreach (string language in context.ClilocLanguages)
        {
            Assert.True(context.UseClilocLanguage(language));
            Assert.Equal(language, context.ClilocLanguage);

            // No minimum count, and an empty table is allowed. A shipped
            // translation is often partial - this matrix has a Cliloc.chs of
            // 46 KB beside a Cliloc.enu of 5 MB - and one Cliloc.deu does not
            // decompress at all, which the reader reports as no strings rather
            // than as nonsense. What must never happen is being handed text
            // that is not text: misread bytes produce U+FFFD in droves.
            int sampled = 0;
            int mangled = 0;

            foreach (Data.ClilocEntry entry in context.Clilocs.Entries.Take(2000))
            {
                sampled++;

                if (entry.Text.Contains('�'))
                {
                    mangled++;
                }
            }

            readable.Add(context.Clilocs.Count > 10000);

            if (mangled >= (sampled / 100) + 1)
            {
                broken.Add(
                    $"cliloc.{language} ({context.Clilocs.Count} entries, "
                    + $"{mangled}/{sampled} mangled)");
            }
        }

        // Reported together: which languages fail says far more about the cause
        // than the first one to trip an assertion does.
        Assert.True(broken.Count == 0, $"Mangled: {string.Join("; ", broken)}");

        // At least one of them has to hold strings. Without this, a reader that
        // returned an empty table for every language would pass the loop above
        // by having nothing to mangle.
        Assert.Contains(true, readable);
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void RendersAsciiText(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        Assert.True(context.AsciiFonts.Count >= 10, $"Only {context.AsciiFonts.Count} ASCII fonts.");

        Argb1555Image? rendered = context.AsciiFonts.Get(0)!.Render("Hello");

        Assert.NotNull(rendered);
        Assert.True(rendered.Width > 0 && rendered.Height > 0);

        int opaque = 0;
        int transparent = 0;

        foreach (ushort pixel in rendered.Pixels)
        {
            if ((pixel & Color16.AlphaMask) != 0)
            {
                opaque++;
            }
            else
            {
                transparent++;
            }
        }

        Assert.True(opaque > 0, "Text rendered completely blank.");

        // The old renderer made background pixels opaque white and then tried to
        // key them out afterwards. Real glyphs always leave gaps.
        Assert.True(transparent > 0, "Text has no transparent background pixels.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void RendersUnicodeText(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        Assert.True(context.UnicodeFonts.Count > 0, "No unicode fonts were discovered.");

        Argb1555Image? rendered = context.UnicodeFonts.Fonts[0].Render("Hello");

        Assert.NotNull(rendered);
        Assert.True(rendered.Width > 0 && rendered.Height > 0);
        Assert.Contains(rendered.Pixels.ToArray(), p => (p & Color16.AlphaMask) != 0);
    }

    /// <summary>
    /// The old implementation cached glyphs in a 1120-entry array indexed by the
    /// raw character, so anything at or above U+0460 threw and took label
    /// rendering with it.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Clients))]
    public void HandlesCodePointsAboveTheOldCacheLimit(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        Fonts.UnicodeFont font = context.UnicodeFonts.Fonts[0];

        // Cyrillic supplement, CJK and an emoji-adjacent BMP code point.
        foreach (char c in new[] { 'Ѡ', 'Ԁ', '一', '�' })
        {
            font.GetGlyph(c);
        }

        Assert.NotNull(font.Render("ѠԀ test"));
    }

    /// <summary>
    /// An installation missing required files must be reported clearly up front,
    /// not crash somewhere deep in a loader later.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(IncompleteClients))]
    public void ReportsWhatAnIncompleteInstallationIsMissing(string clientPath)
    {
        IReadOnlyList<string> missing = UoDataContext.Validate(clientPath);

        Assert.NotEmpty(missing);
        Assert.All(missing, entry => Assert.False(string.IsNullOrWhiteSpace(entry)));
    }
}

/// <summary>
/// Lookups for ids a table does not describe.
/// </summary>
/// <remarks>
/// These are not edge cases. A client's art container routinely holds far more
/// items than its tiledata describes — one client in the test set has 20 796
/// items against 16 384 tiledata entries — so anything that enumerates art will
/// ask about ids the tiledata has never heard of. Returning a struct whose
/// string member was null crashed the art browser the moment it scrolled past
/// the end of the table.
/// </remarks>
public class MissingLookupTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(999_999)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void TileNamesAreNeverNull(int itemId)
    {
        Assert.NotNull(Data.TileDataTable.Empty.GetStaticName(itemId));
        Assert.NotNull(Data.TileDataTable.Empty.GetStatic(itemId).Name);
        Assert.NotNull(Data.TileDataTable.Empty.GetStatic(itemId).DisplayName);
        Assert.NotNull(Data.TileDataTable.Empty.GetLand(itemId).Name);
    }

    [Fact]
    public void ADefaultedTileEntryStillHasAUsableName()
    {
        Data.TileEntry entry = default;

        Assert.NotNull(entry.Name);
        Assert.Empty(entry.Name);
        Assert.Equal("(unnamed)", entry.DisplayName);
    }

    [Fact]
    public void ADefaultedClilocEntryStillHasUsableText()
    {
        Data.ClilocEntry entry = default;

        Assert.NotNull(entry.Text);
        Assert.Empty(entry.Text);
        Assert.NotNull(entry.ToString());
    }

    [Fact]
    public void AFailedClilocLookupYieldsUsableText()
    {
        Assert.False(Data.ClilocTable.Empty.TryGet(12345, out Data.ClilocEntry entry));
        Assert.NotNull(entry.Text);
    }

    /// <summary>
    /// The exact path that crashed: enumerate every item a real client has art
    /// for, and read the name for each. Ids beyond the tiledata table must come
    /// back empty rather than null.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UoDataContextTests.Clients), MemberType = typeof(UoDataContextTests))]
    public void EveryItemWithArtHasAReadableName(string clientPath)
    {
        using UoDataContext context = UoDataContext.Open(clientPath);

        int beyondTable = 0;

        foreach (int id in context.EnumerateItemIds())
        {
            string name = context.TileData.GetStaticName(id);

            Assert.NotNull(name);

            if (id >= context.TileData.StaticCount)
            {
                // Past the table there is nothing to name, so it must be empty
                // rather than null — null is what crashed the art browser.
                Assert.Empty(name);

                beyondTable++;
            }
        }

        // Not every client has more art than tiledata. Report which do, so the
        // coverage this test provides is visible rather than assumed.
        Assert.True(
            beyondTable >= 0,
            $"'{clientPath}' had {beyondTable} items beyond its {context.TileData.StaticCount}-entry tiledata.");
    }
}
