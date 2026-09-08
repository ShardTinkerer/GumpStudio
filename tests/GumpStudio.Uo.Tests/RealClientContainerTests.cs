using GumpStudio.TestSupport;
using GumpStudio.Uo.Files;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Tests that run against a genuine Ultima Online installation.
/// </summary>
/// <remarks>
/// Client files are not redistributable, so these skip unless
/// <c>GUMPSTUDIO_TEST_CLIENT</c> (a MUL-era install) or
/// <c>GUMPSTUDIO_TEST_CLIENT_UOP</c> (a UOP-era install) is set. They are the
/// only tests that can prove the UOP path hash matches what real clients use —
/// a synthetic package built by our own writer cannot, because it would share
/// any mistake in the hash.
/// </remarks>
public class RealClientContainerTests
{
    private const string GumpPattern = "build/gumpartlegacymul/{0:D8}.tga";
    private const string ArtPattern = "build/artlegacymul/{0:D8}.tga";

    [Fact(SkipUnless = nameof(UoTestClient.HasMulClient),
          SkipType = typeof(UoTestClient),
          Skip = UoTestClient.NoMulClientReason)]
    public void ReadsGumpsFromARealMulClient()
    {
        string client = UoTestClient.RequireMulPath();

        using MulFileProvider provider = MulFileProvider.Open(
            Path.Combine(client, "gumpidx.mul"),
            Path.Combine(client, "gumpart.mul"),
            UoFileKind.Gumps);

        Assert.True(provider.Count > 1000, $"Only {provider.Count} gump slots found.");

        int present = CountUsableGumps(provider);

        Assert.True(present > 500, $"Only {present} usable gumps found in the MUL client.");
    }

    [Fact(SkipUnless = nameof(UoTestClient.HasUopClient),
          SkipType = typeof(UoTestClient),
          Skip = UoTestClient.NoUopClientReason)]
    public void ResolvesGumpsFromARealUopClient()
    {
        string client = UoTestClient.RequireUopPath();

        using UopFileProvider provider = UopFileProvider.Open(
            Path.Combine(client, "gumpartLegacyMUL.uop"),
            GumpPattern,
            maxEntries: 0x10000,
            hasExtraDimensions: true);

        int present = CountUsableGumps(provider);

        // If the path hash were wrong, every lookup would miss and this would be
        // zero. A real client ships thousands of gumps.
        Assert.True(present > 500, $"Only {present} gumps resolved from the UOP package.");
    }

    [Fact(SkipUnless = nameof(UoTestClient.HasUopClient),
          SkipType = typeof(UoTestClient),
          Skip = UoTestClient.NoUopClientReason)]
    public void ResolvesStaticArtFromARealUopClient()
    {
        string client = UoTestClient.RequireUopPath();

        using UopFileProvider provider = UopFileProvider.Open(
            Path.Combine(client, "artLegacyMUL.uop"),
            ArtPattern,
            maxEntries: 0x14000);

        int present = 0;

        for (int i = 0; i < provider.Count; i++)
        {
            if (provider.GetEntry(i).Exists)
            {
                present++;
            }
        }

        Assert.True(present > 500, $"Only {present} art entries resolved from the UOP package.");
    }

    /// <summary>
    /// The same gump index must produce the same payload whichever container it
    /// came from. This is the strongest available check that the UOP reader is
    /// addressing and unwrapping entries correctly.
    /// </summary>
    [Fact(SkipUnless = nameof(UoTestClient.HasBothClients),
          SkipType = typeof(UoTestClient),
          Skip = UoTestClient.NoBothClientsReason)]
    public void GumpPayloadsAgreeBetweenMulAndUopClients()
    {
        using MulFileProvider mul = MulFileProvider.Open(
            Path.Combine(UoTestClient.RequireMulPath(), "gumpidx.mul"),
            Path.Combine(UoTestClient.RequireMulPath(), "gumpart.mul"),
            UoFileKind.Gumps);

        using UopFileProvider uop = UopFileProvider.Open(
            Path.Combine(UoTestClient.RequireUopPath(), "gumpartLegacyMUL.uop"),
            GumpPattern,
            maxEntries: 0x10000,
            hasExtraDimensions: true);

        int compared = 0;
        int matched = 0;

        for (int i = 0; i < Math.Min(mul.Count, uop.Count) && compared < 400; i++)
        {
            UoFileEntry mulEntry = mul.GetEntry(i);
            UoFileEntry uopEntry = uop.GetEntry(i);

            if (!mulEntry.Exists || !uopEntry.Exists)
            {
                continue;
            }

            compared++;

            if (mulEntry.ExtraWidth == uopEntry.ExtraWidth
                && mulEntry.ExtraHeight == uopEntry.ExtraHeight
                && mul.Read(i).Span.SequenceEqual(uop.Read(i).Span))
            {
                matched++;
            }
        }

        Assert.True(compared > 50, $"Only {compared} gumps were present in both clients.");

        // The two installs are different client versions, so some art legitimately
        // differs. A correct reader still agrees on the overwhelming majority.
        double agreement = (double)matched / compared;

        Assert.True(
            agreement > 0.80,
            $"Only {matched}/{compared} ({agreement:P0}) gumps matched between MUL and UOP.");
    }

    private static int CountUsableGumps(IUoFileProvider provider)
    {
        int present = 0;

        for (int i = 0; i < provider.Count; i++)
        {
            UoFileEntry entry = provider.GetEntry(i);

            if (entry.Exists && entry.ExtraWidth is > 0 and <= 4096 && entry.ExtraHeight is > 0 and <= 4096)
            {
                present++;
            }
        }

        return present;
    }
}
