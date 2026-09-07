using GumpStudio.App;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// Whether the editor knows the document has unsaved work.
/// </summary>
/// <remarks>
/// Nothing tracked this before, so New, Open, both importers and Exit all
/// discarded the document without asking.
/// </remarks>
public class DirtyTrackingTests
{
    private static EditorSession NewSession(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    private static LabelElement Label() =>
        new() { Text = "text", Location = new GumpPoint(4, 5) };

    [Fact]
    public void AFreshSessionIsClean()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        Assert.False(session.IsModified);
    }

    [Fact]
    public void ApplyingAChangeMakesItDirty()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));

        Assert.True(session.IsModified);
    }

    [Fact]
    public void SavingMakesItCleanAgain()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));
        session.Save(Path.Combine(directory.Path, "doc.gump"));

        Assert.False(session.IsModified);
    }

    [Fact]
    public void UndoingBackToTheSavedStateReportsCleanAgain()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));
        session.Save(Path.Combine(directory.Path, "doc.gump"));

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));

        Assert.True(session.IsModified);

        session.History.Undo();

        // The point of deriving this from the history rather than a flag: the
        // document really is what was saved again.
        Assert.False(session.IsModified);

        session.History.Redo();

        Assert.True(session.IsModified);
    }

    /// <summary>
    /// The case a bare undo cursor gets wrong: undo one step, then apply a
    /// different change. The cursor returns to the number it had when the file
    /// was saved, over a document that no longer matches it.
    /// </summary>
    [Fact]
    public void ADivergentChangeAtTheSavedDepthIsStillDirty()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));
        session.Save(Path.Combine(directory.Path, "doc.gump"));

        session.History.Undo();
        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));

        Assert.True(session.IsModified);
    }

    [Fact]
    public void OpeningAndStartingANewDocumentBothStartClean()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);
        string path = Path.Combine(directory.Path, "doc.gump");

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));
        session.Save(path);

        session.NewDocument();

        Assert.False(session.IsModified);

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));
        session.Open(path);

        Assert.False(session.IsModified);
    }

    [Fact]
    public void RemovingAPageIsUndoableThroughTheSession()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        session.Apply(new AddPageCommand(session.Document));
        session.Document.Pages[1].Root.Add(Label());

        session.Apply(new RemovePageCommand(session.Document, 1));

        Assert.Equal(1, session.Document.PageCount);

        session.History.Undo();

        Assert.Equal(2, session.Document.PageCount);
        Assert.Single(session.Document.Pages[1].Root.Children);
    }

    [Fact]
    public void ModifiedChangedFiresWhenDirtinessMoves()
    {
        using TempDirectory directory = new();
        using EditorSession session = NewSession(directory);

        int raised = 0;
        session.ModifiedChanged += (_, _) => raised++;

        session.Apply(new AddElementCommand(session.ActivePage.Root, Label()));

        Assert.True(raised > 0);

        raised = 0;
        session.Save(Path.Combine(directory.Path, "doc.gump"));

        Assert.True(raised > 0);
    }
}
