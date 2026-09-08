using GumpStudio.TestSupport;
using GumpStudio.Uo.Data;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Choosing which cliloc file to read.
/// </summary>
/// <remarks>
/// <para>
/// The old code opened <c>cliloc.enu</c> and nothing else, so a German- or
/// Russian-only shard install showed no strings at all and no explanation. The
/// 1.8 editor had a language combo, but it was populated and then never read.
/// </para>
/// <para>
/// These are hermetic: <see cref="UoDataContext.Open"/> needs only the directory
/// to exist, so a temporary folder holding nothing but cliloc files is enough.
/// </para>
/// </remarks>
public class ClilocLanguageTests
{
    private static ClilocFixture English =>
        new ClilocFixture().Add(1000, "hammer pick").Add(1001, "vendor price");

    private static ClilocFixture German =>
        new ClilocFixture().Add(1000, "Hammerspitzhacke").Add(1001, "Händlerpreis");

    [Fact]
    public void DiscoversEveryClilocFileAsALanguage()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");
        German.Write(directory.Path, "deu");
        English.Write(directory.Path, "jpn");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.Equal(["enu", "deu", "jpn"], context.ClilocLanguages);
        Assert.True(context.HasClilocs);
    }

    [Fact]
    public void PrefersEnglishWhenSeveralArePresent()
    {
        using TempDirectory directory = new();

        // Written first, so directory order cannot be what decides this.
        German.Write(directory.Path, "deu");
        English.Write(directory.Path, "enu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.Equal("enu", context.ClilocLanguage);
        Assert.Equal("hammer pick", context.Clilocs.GetText(1000));
    }

    /// <summary>
    /// A client with no English file still shows its strings.
    /// </summary>
    /// <remarks>
    /// This is a deliberate behaviour change. The hardcoded <c>cliloc.enu</c>
    /// meant a non-English-only installation resolved nothing at all.
    /// </remarks>
    [Fact]
    public void FallsBackToWhateverIsPresentWhenThereIsNoEnglish()
    {
        using TempDirectory directory = new();

        German.Write(directory.Path, "deu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.Equal("deu", context.ClilocLanguage);
        Assert.Equal("Hammerspitzhacke", context.Clilocs.GetText(1000));
    }

    /// <summary>The numbered IFF chunks are not cliloc files this can read.</summary>
    [Fact]
    public void IgnoresTheNumberedIffChunkFiles()
    {
        using TempDirectory directory = new();

        English.WriteAs(directory.Path, "cliloc1.enu");
        English.WriteAs(directory.Path, "cliloc2.enu");
        English.WriteAs(directory.Path, "cliloc.enu.bak");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.Empty(context.ClilocLanguages);
        Assert.False(context.HasClilocs);
        Assert.Null(context.ClilocLanguage);
    }

    [Fact]
    public void NormalisesLanguageCodesToLowerCase()
    {
        using TempDirectory directory = new();

        German.WriteAs(directory.Path, "CLILOC.DEU");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.Equal(["deu"], context.ClilocLanguages);
        Assert.True(context.UseClilocLanguage("DEU"));
        Assert.Equal("deu", context.ClilocLanguage);
    }

    [Fact]
    public void SwitchingLanguageChangesTheStringsRead()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");
        German.Write(directory.Path, "deu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.Equal("hammer pick", context.Clilocs.GetText(1000));

        Assert.True(context.UseClilocLanguage("deu"));

        Assert.Equal("deu", context.ClilocLanguage);
        Assert.Equal("Hammerspitzhacke", context.Clilocs.GetText(1000));
    }

    [Fact]
    public void SwitchingToAnUnknownLanguageIsRefusedAndChangesNothing()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.False(context.UseClilocLanguage("kor"));
        Assert.Equal("enu", context.ClilocLanguage);
        Assert.Equal("hammer pick", context.Clilocs.GetText(1000));
    }

    /// <summary>Re-selecting the current language must not re-parse it.</summary>
    [Fact]
    public void SwitchingToTheCurrentLanguageKeepsTheParsedTable()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        ClilocTable before = context.Clilocs;

        Assert.True(context.UseClilocLanguage("ENU"));
        Assert.Same(before, context.Clilocs);
    }

    [Fact]
    public void OpenHonoursAnInitialLanguage()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");
        German.Write(directory.Path, "deu");

        using UoDataContext context = UoDataContext.Open(directory.Path, "deu");

        Assert.Equal("deu", context.ClilocLanguage);
        Assert.Equal("Händlerpreis", context.Clilocs.GetText(1001));
    }

    /// <summary>A remembered choice must not stop another client from opening.</summary>
    [Fact]
    public void OpenIgnoresAnInitialLanguageTheClientDoesNotHave()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");

        using UoDataContext context = UoDataContext.Open(directory.Path, "kor");

        Assert.Equal("enu", context.ClilocLanguage);
    }

    [Fact]
    public void AClientWithNoClilocFileStillOpens()
    {
        using TempDirectory directory = new();
        using UoDataContext context = UoDataContext.Open(directory.Path);

        Assert.False(context.HasClilocs);
        Assert.Empty(context.ClilocLanguages);
        Assert.Null(context.ClilocLanguage);
        Assert.Same(ClilocTable.Empty, context.Clilocs);
        Assert.False(context.UseClilocLanguage("enu"));
    }

    /// <summary>
    /// A missing cliloc is not a broken installation.
    /// </summary>
    /// <remarks>
    /// Pinned so that nobody later "completes" <c>Validate</c> and makes every
    /// cliloc-less client refuse to open.
    /// </remarks>
    [Fact]
    public void AMissingClilocIsNotAValidationFailure()
    {
        using TempDirectory directory = new();

        Assert.DoesNotContain(
            UoDataContext.Validate(directory.Path),
            message => message.Contains("cliloc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreloadingLeavesTheSameTableForTheNextReader()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        context.PreloadClilocs();

        ClilocTable warm = context.Clilocs;

        context.PreloadClilocs();

        Assert.Same(warm, context.Clilocs);
        Assert.True(warm.Count > 0);
    }

    /// <summary>
    /// Reading while the language changes never tears.
    /// </summary>
    /// <remarks>
    /// A smoke test, not a proof: it exercises the volatile swap of the
    /// language-and-table pair, which is what lets background art decodes
    /// resolve text with no lock on the read path.
    /// </remarks>
    [Fact]
    public async Task ReadingClilocsWhileSwitchingLanguageNeverTearsOrThrows()
    {
        using TempDirectory directory = new();

        English.Write(directory.Path, "enu");
        German.Write(directory.Path, "deu");

        using UoDataContext context = UoDataContext.Open(directory.Path);

        string[] allowed = ["hammer pick", "Hammerspitzhacke"];
        bool stop = false;

        Task switcher = Task.Run(
            () =>
            {
                for (int i = 0; i < 200; i++)
                {
                    context.UseClilocLanguage(i % 2 == 0 ? "deu" : "enu");
                }

                Volatile.Write(ref stop, true);
            },
            TestContext.Current.CancellationToken);

        while (!Volatile.Read(ref stop))
        {
            Assert.Contains(context.Clilocs.GetText(1000), allowed);
        }

        await switcher;
    }
}
