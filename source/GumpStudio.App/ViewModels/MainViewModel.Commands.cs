using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;
using GumpStudio.Core.Serialization;

namespace GumpStudio.App.ViewModels;

/// <summary>
/// Every action the shell offers, defined once.
/// </summary>
/// <remarks>
/// <para>
/// This file is what <c>docs/architecture.md</c> used to describe as "every
/// action is registered three times over — menu, shortcut, context menu". Each
/// one was a <c>Click("MenuName", …)</c> call keyed on a control name, a
/// <c>Bind("Ctrl+X", …)</c> call, and a hand-built <c>MenuItem</c>, with no
/// compiler check that the three agreed or that the name was spelled right.
/// </para>
/// <para>
/// They are commands now, and the menu bar, the context menu and the keyboard
/// all bind to the same ones. A misspelled binding is a build error, and the
/// enabled state cannot differ between the two menus because both read the same
/// property.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    // ---- File ----------------------------------------------------------

    /// <summary>Starts a new document, asking about unsaved work first.</summary>
    /// <remarks>
    /// The shortcut goes through here rather than straight to
    /// <see cref="EditorSession.NewDocument"/>: the menu item asked before
    /// discarding and the shortcut did not, so the two paths for one action
    /// disagreed about whether the document was safe.
    /// </remarks>
    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync("starting a new one").ConfigureAwait(true))
        {
            return;
        }

        _session.NewDocument();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync("opening another").ConfigureAwait(true))
        {
            return;
        }

        string? path = await _dialogs.PickOpenPathAsync(new FilePickerRequest(
            "Open gump",
            FilterName: "GumpStudio document",
            Patterns: ["*.gump"])).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        _session.Open(path);

        _view.RefreshDocumentView();
    }

    [RelayCommand]
    private Task SaveAsync() => SaveToAsync(_session.DocumentPath);

    [RelayCommand]
    private Task SaveAsAsync() => SaveToAsync(null);

    private async Task SaveToAsync(string? path)
    {
        if (path is null)
        {
            path = await _dialogs.PickSavePathAsync(new FilePickerRequest(
                "Save gump",
                DefaultExtension: "gump",
                SuggestedFileName: "gump.gump")).ConfigureAwait(true);

            if (path is null)
            {
                return;
            }
        }

        _session.Save(path);

        _view.RefreshDocumentView();
    }

    /// <summary>
    /// Imports a 1.8 <c>.gump</c> or <c>.gumpling</c>.
    /// </summary>
    /// <remarks>
    /// The two are not the same import: a gump replaces the document, while a
    /// gumpling is a single group added to the page that is open. Only the first
    /// discards anything, so only the first asks — and it asks after the file has
    /// been chosen, so cancelling the picker costs no question.
    /// </remarks>
    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        string? path = await _dialogs.PickOpenPathAsync(new FilePickerRequest(
            "Import a GumpStudio 1.8 file",
            FilterName: "GumpStudio 1.8",
            Patterns: ["*.gump", "*.gumpling"])).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        if (Path.GetExtension(path).Equals(".gumpling", StringComparison.OrdinalIgnoreCase))
        {
            GroupElement group = _session.ImportGumpling(path);

            _session.Canvas.Select(group);
            _session.MeasureActivePage();

            _view.RefreshDocumentView();
            SetStatus($"Added {group.Name} to page {_session.ActivePageIndex}.");

            return;
        }

        if (!await ConfirmDiscardAsync("importing").ConfigureAwait(true))
        {
            return;
        }

        _session.ImportLegacy(path);

        _view.RefreshDocumentView();
        SetStatus("Imported. Save it to store the document in the current format.");
    }

    /// <summary>
    /// Imports a gump from the layout text a capture tool produced.
    /// </summary>
    /// <remarks>
    /// The dialog starts with the clipboard's contents when they look like a
    /// layout, which is the case this exists for: someone has just copied a gump
    /// out of a sniffer and wants to edit it.
    /// </remarks>
    [RelayCommand]
    private async Task ImportLayoutAsync()
    {
        if (!await ConfirmDiscardAsync("importing").ConfigureAwait(true))
        {
            return;
        }

        if (await _dialogs.ImportLayoutAsync().ConfigureAwait(true) is not { } imported)
        {
            return;
        }

        (GumpDocument document, IReadOnlyList<string> warnings) = imported;

        _session.AdoptImported(document);

        _view.RefreshDocumentView();

        int elements = document.Pages.Sum(page => page.Leaves().Count());
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Imported {elements} elements across {document.PageCount} pages.");

        SetStatus(warnings.Count == 0
            ? summary + " Save it to keep the document."
            : $"{summary} {warnings.Count} line(s) were skipped — see the layout for what was lost.");
    }

    /// <summary>
    /// Picks a file, asks how to export, then writes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file comes first because the gump name defaults to its name. Asking
    /// for the options first would leave nothing to derive that from, and would
    /// quietly change the default name of every export.
    /// </para>
    /// <para>
    /// A method rather than a command: the export menu is built from
    /// <see cref="EditorSession.Converters"/> at runtime, so its items carry the
    /// converter with them and call this.
    /// </para>
    /// </remarks>
    public async Task ExportAsync(IGumpConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);

        string? path = await _dialogs.PickSavePathAsync(new FilePickerRequest(
            $"Export as {converter.DisplayName}",
            DefaultExtension: converter.FileExtension.TrimStart('.'),
            SuggestedFileName: "gump" + converter.FileExtension)).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        AppSettings settings = _session.Settings;

        GumpExportOptions defaults = new()
        {
            GumpName = Path.GetFileNameWithoutExtension(path),
            Dialect = settings.ExportDialectFor(converter.Id),
        };

        GumpExportOptions? chosen = await _dialogs.ChooseExportOptionsAsync(
            $"Export as {converter.DisplayName}", converter.Dialects, defaults).ConfigureAwait(true);

        if (chosen is not { } options)
        {
            return;
        }

        // Remembered per converter, so choosing the layout-string form once does
        // not make it the default for every other target too.
        settings.SetExportDialect(converter.Id, options.Dialect);
        settings.Save();

        string script = converter.Export(_session.Document, options);

        await File.WriteAllTextAsync(path, script).ConfigureAwait(true);

        SetStatus($"Exported to {Path.GetFileName(path)}.");
    }

    [RelayCommand]
    private Task SetClientAsync() => ChooseClientAsync(force: true);

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (!await ConfirmDiscardAsync("closing").ConfigureAwait(true))
        {
            return;
        }

        _view.CloseShell();
    }

    /// <summary>
    /// Asks before an action that would throw away unsaved work.
    /// </summary>
    /// <param name="action">
    /// What is about to happen, as a gerund, for the question's wording.
    /// </param>
    /// <returns>False when the user chose to keep what they have.</returns>
    /// <remarks>
    /// Nothing asked before this existed: New, Open, both importers and Exit all
    /// discarded the document silently.
    /// </remarks>
    public async Task<bool> ConfirmDiscardAsync(string action)
    {
        if (!_session.IsModified)
        {
            return true;
        }

        string name = _session.DocumentPath is { } path
            ? Path.GetFileName(path)
            : "This gump";

        return await _dialogs.ConfirmAsync(
            $"{name} has unsaved changes. Discard them before {action}?").ConfigureAwait(true);
    }

    // ---- Edit ----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _session.History.Undo();

        _view.RefreshDocumentView();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _session.History.Redo();

        _view.RefreshDocumentView();
    }

    [RelayCommand]
    private void SelectAll()
    {
        _session.Canvas.SelectAll();

        _view.RefreshDocumentView();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        _session.Canvas.DeleteSelection();

        _view.RefreshDocumentView();
    }

    [RelayCommand(CanExecute = nameof(CanCut))]
    private Task CutAsync() => CopyToClipboardAsync(cut: true);

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private Task CopyAsync() => CopyToClipboardAsync(cut: false);

    /// <summary>
    /// Puts the selection on the clipboard, optionally removing it.
    /// </summary>
    /// <remarks>
    /// As XML text rather than a serialised object graph. It survives between
    /// instances, can be inspected by pasting it anywhere, and cannot carry
    /// anything executable — which the original's <c>BinaryFormatter</c> payload
    /// could, and which is a large part of why that format had to go.
    /// </remarks>
    private async Task CopyToClipboardAsync(bool cut)
    {
        if (_session.Canvas.Selection.Count == 0)
        {
            SetStatus("Select something to copy.");

            return;
        }

        if (!_clipboard.IsAvailable)
        {
            SetStatus("No clipboard is available.");

            return;
        }

        int count = _session.Canvas.Selection.Count;

        await _clipboard.SetTextAsync(
            GumpXmlSerializer.ToFragment(_session.Canvas.Selection)).ConfigureAwait(true);

        if (cut)
        {
            _session.Canvas.DeleteSelection();
        }

        _view.RefreshDocumentView();
        SetStatus(string.Create(
            CultureInfo.InvariantCulture,
            $"{(cut ? "Cut" : "Copied")} {count} element(s)."));
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (!_clipboard.IsAvailable)
        {
            SetStatus("No clipboard is available.");

            return;
        }

        string? text = await _clipboard.GetTextAsync().ConfigureAwait(true);

        IReadOnlyList<Element> elements = GumpXmlSerializer.FromFragment(text);

        if (elements.Count == 0)
        {
            // The clipboard holds whatever the user last copied anywhere, so text
            // that is not ours is an ordinary outcome, not a failure.
            SetStatus("Nothing on the clipboard to paste.");

            return;
        }

        int pasted = _session.Canvas.Paste(elements);

        _session.MeasureActivePage();

        _view.RefreshDocumentView();
        SetStatus(string.Create(CultureInfo.InvariantCulture, $"Pasted {pasted} element(s)."));
    }

    [RelayCommand(CanExecute = nameof(CanGroup))]
    private void Group()
    {
        if (_session.Canvas.Group() is null)
        {
            SetStatus("Select at least two elements to group.");

            return;
        }

        _view.RefreshDocumentView();
    }

    [RelayCommand(CanExecute = nameof(CanUngroup))]
    private void Ungroup()
    {
        int dissolved = _session.Canvas.Ungroup();

        if (dissolved == 0)
        {
            SetStatus("Select a group to ungroup.");

            return;
        }

        _view.RefreshDocumentView();
        SetStatus(dissolved == 1 ? "Ungrouped." : $"Ungrouped {dissolved} groups.");
    }

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private void BringToFront() => Reorder(_session.Canvas.BringToFront, "front");

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private void BringForward() => Reorder(_session.Canvas.BringForward, "forward");

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private void SendBackward() => Reorder(_session.Canvas.SendBackward, "backward");

    [RelayCommand(CanExecute = nameof(CanReorder))]
    private void SendToBack() => Reorder(_session.Canvas.SendToBack, "back");

    /// <summary>Applies a drawing-order change and says what happened.</summary>
    /// <remarks>
    /// Silence when nothing moves is ambiguous — an element already at the front
    /// looks the same as a shortcut that is not wired up. Saying so distinguishes
    /// them.
    /// </remarks>
    private void Reorder(Func<bool> operation, string where)
    {
        if (_session.Canvas.Selection.Count == 0)
        {
            SetStatus("Select something to reorder.");

            return;
        }

        if (!operation())
        {
            SetStatus($"Already at the {where}.");

            return;
        }

        _view.RefreshDocumentView();
        SetStatus($"Moved {where}.");
    }

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void AlignLeft() => Arrange(() => _session.Canvas.Align(AlignMode.Left), "Aligned lefts.");

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void AlignRight() => Arrange(() => _session.Canvas.Align(AlignMode.Right), "Aligned rights.");

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void AlignTop() => Arrange(() => _session.Canvas.Align(AlignMode.Top), "Aligned tops.");

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void AlignBottom() => Arrange(() => _session.Canvas.Align(AlignMode.Bottom), "Aligned bottoms.");

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void CentreHorizontally() =>
        Arrange(() => _session.Canvas.Align(AlignMode.CenterHorizontally), "Centred horizontally.");

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void CentreVertically() =>
        Arrange(() => _session.Canvas.Align(AlignMode.CenterVertically), "Centred vertically.");

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void SpaceHorizontally() =>
        Arrange(() => _session.Canvas.Distribute(DistributeMode.Horizontally), "Spaced horizontally.", 3);

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void SpaceVertically() =>
        Arrange(() => _session.Canvas.Distribute(DistributeMode.Vertically), "Spaced vertically.", 3);

    /// <summary>
    /// Applies an alignment or spacing pass and says what happened.
    /// </summary>
    /// <remarks>
    /// Spacing needs three elements, not two: with two there is nothing between
    /// them to even out. Saying so beats a command that looks broken.
    /// </remarks>
    private void Arrange(Func<bool> operation, string done, int required = 2)
    {
        if (_session.Canvas.Selection.Count < required)
        {
            SetStatus(required > 2
                ? "Select at least three elements to space them evenly."
                : "Select at least two elements to align them.");

            return;
        }

        if (!operation())
        {
            SetStatus("Already arranged.");

            return;
        }

        _view.RefreshDocumentView();
        SetStatus(done);
    }

    // ---- Gump ----------------------------------------------------------

    [RelayCommand]
    private async Task EditGumpPropertiesAsync()
    {
        GumpProperties? chosen = await _dialogs
            .EditGumpPropertiesAsync(_session.Document.Properties).ConfigureAwait(true);

        if (chosen is not { } properties)
        {
            return;
        }

        _session.History.Push(new SetGumpPropertiesCommand(_session.Document, properties));

        _view.RefreshDocumentView();
        SetStatus("Gump properties updated.");
    }

    // ---- Pages ---------------------------------------------------------

    [RelayCommand]
    private void AddPage()
    {
        AddPageCommand command = new(_session.Document);

        _session.Apply(command);

        _session.ActivePageIndex = _session.Document.PageCount - 1;

        _view.RefreshDocumentView();
        SetStatus($"Added {command.Page.Name}.");
    }

    [RelayCommand]
    private void InsertPage()
    {
        int index = _session.ActivePageIndex;
        InsertPageCommand command = new(_session.Document, index);

        _session.Apply(command);

        _session.ActivePageIndex = index;

        _view.RefreshDocumentView();
        SetStatus($"Inserted a page at {index}.");
    }

    /// <summary>
    /// Removes the active page.
    /// </summary>
    /// <remarks>
    /// Through the undo history, unlike the original and unlike this editor's
    /// first version: removing a page takes every element on it, and that was the
    /// one action here with no way back.
    /// </remarks>
    [RelayCommand]
    private void RemovePage()
    {
        if (_session.Document.PageCount == 1)
        {
            SetStatus("A gump must keep at least one page.");

            return;
        }

        RemovePageCommand command = new(_session.Document, _session.ActivePageIndex);
        int active = command.ActiveIndexAfterRemoval;

        _session.Apply(command);

        _session.ActivePageIndex = active;

        _view.RefreshDocumentView();
        SetStatus($"{command.Description}. Undo brings it back with its elements.");
    }

    [RelayCommand]
    private void ClearPage()
    {
        ClearPageCommand command = new(_session.ActivePage);

        if (!command.HasContent)
        {
            SetStatus("That page is already empty.");

            return;
        }

        _session.Canvas.ClearSelection();
        _session.Apply(command);

        _view.RefreshDocumentView();
        SetStatus(command.Description + ".");
    }

    /// <summary>
    /// Moves the selection to another page and follows it there.
    /// </summary>
    /// <remarks>
    /// Following is deliberate. Pages other than 0 are mutually exclusive, so
    /// moving an element to one while looking at another makes it vanish, which
    /// reads exactly like a delete. Switching to the destination shows it arrive.
    /// </remarks>
    public void MoveSelectionToPage(int index)
    {
        int moved = _session.Canvas.MoveSelectionToPage(_session.Document.Pages[index]);

        if (moved == 0)
        {
            SetStatus("Select something to move.");

            return;
        }

        _session.ActivePageIndex = index;

        _view.RefreshDocumentView();
        SetStatus(moved == 1
            ? string.Create(CultureInfo.InvariantCulture, $"Moved to page {index}.")
            : string.Create(CultureInfo.InvariantCulture, $"Moved {moved} elements to page {index}."));
    }

    // ---- Toolbox -------------------------------------------------------

    /// <summary>Adds a new element to the active page and selects it.</summary>
    public void AddElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        _session.History.Push(new AddElementCommand(_session.ActivePage.Root, element));
        _session.MeasureActivePage();
        _session.Canvas.Select(element);

        _view.RefreshDocumentView();
    }

    // ---- View ----------------------------------------------------------

    [RelayCommand]
    private void ZoomIn() => _view.StepZoom(up: true);

    [RelayCommand]
    private void ZoomOut() => _view.StepZoom(up: false);

    [RelayCommand]
    private void ZoomReset() => _view.SetZoom(1.0);

    [RelayCommand]
    private void ZoomToFit() => _view.ZoomToFit();

    [RelayCommand]
    private void ResetLayout() => _view.ResetLayout();

    [RelayCommand]
    private async Task ChooseGridSizeAsync()
    {
        GumpSize? chosen = await _dialogs.ChooseGridSizeAsync(
            _session.Canvas.Grid.Width, _session.Canvas.Grid.Height).ConfigureAwait(true);

        if (chosen is not { } size)
        {
            return;
        }

        // Mutated rather than replaced: the canvas decides whether to rebuild its
        // render options by comparing the settings object by reference, so a new
        // instance would stop it noticing grid changes at all.
        _session.Canvas.Grid.Width = size.Width;
        _session.Canvas.Grid.Height = size.Height;

        SaveGridSettings();

        _view.InvalidateCanvas();
        SetStatus($"Grid set to {size.Width} x {size.Height}.");
    }

    [RelayCommand]
    private Task ShowAboutAsync() => _dialogs.ShowAboutAsync();

    // ---- Client --------------------------------------------------------

    /// <summary>Prompts for a client folder when none is configured yet.</summary>
    public async Task EnsureClientAsync()
    {
        string? remembered = _session.Settings.ClientPath;

        if (remembered is not null)
        {
            SetStatus("Loading client art…");

            IReadOnlyList<string> missing =
                await _session.OpenClientAsync(remembered).ConfigureAwait(true);

            if (missing.Count == 0)
            {
                _view.ForgetPickerEntries();
                _view.RefreshDocumentView();

                ClientOpened?.Invoke(this, EventArgs.Empty);

                return;
            }
        }

        await ChooseClientAsync(force: false).ConfigureAwait(true);
    }

    /// <summary>
    /// Raised when a client installation has been opened.
    /// </summary>
    /// <remarks>
    /// The cliloc panel starts its own background read from this, which is
    /// deliberately not part of opening a client: the table is a MegaCliloc
    /// decode and around 124,000 strings.
    /// </remarks>
    public event EventHandler? ClientOpened;

    private async Task ChooseClientAsync(bool force)
    {
        if (!force)
        {
            SetStatus("No Ultima Online client configured — choose one to see art.");
        }

        string? path = await _dialogs
            .PickFolderAsync("Select your Ultima Online folder").ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        SetStatus("Loading client art…");

        IReadOnlyList<string> missing = await _session.OpenClientAsync(path).ConfigureAwait(true);

        _view.ForgetPickerEntries();

        if (missing.Count > 0)
        {
            // Naming what is absent beats the original's crash inside a static
            // constructor with no indication of the cause.
            SetStatus($"That folder is missing: {string.Join(", ", missing)}", isError: true);

            return;
        }

        _session.Settings.ClientPath = path;
        _session.Settings.Save();

        // The open document's art-derived sizes belonged to the client that was
        // loaded before. Not needed when a remembered client is opened at
        // startup, where the document is either empty or about to be replaced by
        // one that measures itself.
        _session.MeasureActivePage();

        _view.RefreshDocumentView();

        ClientOpened?.Invoke(this, EventArgs.Empty);
    }

    // ---- Settings ------------------------------------------------------

    /// <summary>Writes the grid back to the settings file.</summary>
    private void SaveGridSettings()
    {
        AppSettings settings = _session.Settings;

        settings.GridWidth = _session.Canvas.Grid.Width;
        settings.GridHeight = _session.Canvas.Grid.Height;
        settings.GridVisible = _session.Canvas.Grid.Visible;
        settings.GridSnap = _session.Canvas.Grid.SnapEnabled;

        settings.Save();
    }

    /// <summary>Reads the remembered grid back at startup.</summary>
    /// <remarks>
    /// Mutates the settings object the controller already holds, for the reason
    /// given on <see cref="ChooseGridSizeAsync"/>, and announces the two toggles
    /// so the menu's checkmarks follow.
    /// </remarks>
    public void LoadGridSettings()
    {
        AppSettings settings = _session.Settings;

        _session.Canvas.Grid.Width = settings.GridWidth;
        _session.Canvas.Grid.Height = settings.GridHeight;
        _session.Canvas.Grid.Visible = settings.GridVisible;
        _session.Canvas.Grid.SnapEnabled = settings.GridSnap;

        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(SnapToGrid));
    }
}
