using GumpStudio.App;
using GumpStudio.Core.Elements;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The two 1.8 import shapes, which are not the same operation.
/// </summary>
/// <remarks>
/// The picker offered <c>*.gumpling</c> from the start, but every chosen file
/// went to the whole-document importer, so picking one could only fail.
/// </remarks>
public class LegacyImportRoutingTests
{
    private static EditorSession NewSession(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    [Fact]
    public void AGumplingIsAddedToTheOpenDocumentRatherThanReplacingIt()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        string path = Path.Combine(directory.Path, "sample.gumpling");
        LegacyGumpFixture.WriteGumpling(path);

        session.Document.AddPage();
        session.ActivePageIndex = 1;

        GroupElement group = session.ImportGumpling(path);

        // Added, not replaced: the second page is still there and still active.
        Assert.Equal(2, session.Document.PageCount);
        Assert.Equal(1, session.ActivePageIndex);
        Assert.Contains(group, session.ActivePage.Root.Children);
        Assert.Null(session.DocumentPath);
    }

    [Fact]
    public void ImportingAGumplingIsUndoable()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        string path = Path.Combine(directory.Path, "sample.gumpling");
        LegacyGumpFixture.WriteGumpling(path);

        session.ImportGumpling(path);

        Assert.Single(session.ActivePage.Root.Children);
        Assert.True(session.IsModified);

        session.History.Undo();

        Assert.Empty(session.ActivePage.Root.Children);
    }

    [Fact]
    public void AGumplingCarriesItsChildrenIn()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        string path = Path.Combine(directory.Path, "sample.gumpling");
        LegacyGumpFixture.WriteGumpling(path);

        GroupElement group = session.ImportGumpling(path);

        Assert.NotEmpty(group.Children);
    }
}
