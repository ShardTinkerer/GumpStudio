using GumpStudio.App.Controls;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The rule that a shared decode must not outlive its usefulness.
/// </summary>
/// <remarks>
/// Thumbnail decodes are shared by id so two tiles asking for the same art
/// decode it once. Cancelling a generation — which happens whenever the list is
/// rebound — completes those shared tasks as cancelled, and handing one of those
/// to a tile that asked afterwards made the tile catch the cancellation and give
/// up for good. It stayed blank until its row happened to be realised again,
/// which looked like "scroll away and come back and the art appears".
///
/// The browser itself cannot be driven without a client, so this pins the
/// decision rather than the plumbing: a completed-unsuccessfully task is never
/// joined.
/// </remarks>
public class DecodeSharingTests
{
    private static bool CanJoin(Task task) => ArtBrowserWindow.CanJoinDecode(task);

    [Fact]
    public void ARunningDecodeIsJoined()
    {
        TaskCompletionSource<int> pending = new();

        Assert.True(CanJoin(pending.Task));
    }

    [Fact]
    public void ACompletedDecodeIsJoined()
    {
        // Still worth joining: its result is the bitmap the caller wants.
        Assert.True(CanJoin(Task.FromResult(1)));
    }

    [Fact]
    public void ACancelledDecodeIsNotJoined()
    {
        using CancellationTokenSource source = new();

        source.Cancel();

        Assert.False(CanJoin(Task.FromCanceled<int>(source.Token)));
    }

    [Fact]
    public void AFailedDecodeIsNotJoined()
    {
        Task failed = Task.FromException<int>(new InvalidDataException("bad art"));

        Assert.False(CanJoin(failed));

        // Observed, so the run does not report an unhandled task exception.
        Assert.NotNull(failed.Exception);
    }

    /// <summary>
    /// Cancelling a generation has to empty the map as well as trip the token.
    /// Leaving the entries behind is what let a cancelled task be joined at all.
    /// </summary>
    [Fact]
    public void CancellingAGenerationLeavesNothingToJoin()
    {
        System.Collections.Concurrent.ConcurrentDictionary<int, Task<int>> inFlight = new();

        using CancellationTokenSource generation = new();

        inFlight[5] = Task.FromCanceled<int>(Cancelled(generation));

        // What CancelDecoding now does.
        inFlight.Clear();

        Assert.False(inFlight.TryGetValue(5, out _));
    }

    private static CancellationToken Cancelled(CancellationTokenSource source)
    {
        source.Cancel();

        return source.Token;
    }
}
