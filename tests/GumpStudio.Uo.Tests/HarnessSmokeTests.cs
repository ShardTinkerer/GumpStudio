using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Proves the Phase 0 scaffolding works: tests run, and tests that need real
/// client data skip cleanly instead of failing when it is absent.
/// Deleted once Phase 1 lands genuine coverage.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public void TestHarnessRuns()
    {
        Assert.True(true);
    }

    [Fact]
    public void ClientDataIsOptional()
    {
        // Must not throw regardless of whether the variable is set.
        bool hasClient = UoTestClient.HasMulClient;

        Assert.Equal(hasClient, UoTestClient.MulPath is not null);
    }

    [Fact(SkipUnless = nameof(UoTestClient.HasMulClient),
          SkipType = typeof(UoTestClient),
          Skip = UoTestClient.NoMulClientReason)]
    public void MulClientDirectoryContainsArt()
    {
        string clientPath = UoTestClient.RequireMulPath();

        Assert.True(
            File.Exists(Path.Combine(clientPath, "art.mul")),
            $"'{clientPath}' does not contain art.mul.");
    }

    [Fact(SkipUnless = nameof(UoTestClient.HasUopClient),
          SkipType = typeof(UoTestClient),
          Skip = UoTestClient.NoUopClientReason)]
    public void UopClientDirectoryContainsGumpArt()
    {
        string clientPath = UoTestClient.RequireUopPath();

        Assert.True(
            File.Exists(Path.Combine(clientPath, "gumpartLegacyMUL.uop")),
            $"'{clientPath}' does not contain gumpartLegacyMUL.uop.");
    }
}
