using System.Globalization;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Dock.Avalonia.Controls;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;

using GumpStudio.App.Controls;
using GumpStudio.App.ViewModels;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;

namespace GumpStudio.App;

public sealed partial class MainWindow : Window, IDisposable, IShellView
{
    private readonly EditorSession _session;
    private readonly MainViewModel _viewModel;

    private readonly GumpCanvas _canvas = null!;
    private readonly ScrollViewer _scroller = null!;
    private readonly ListBox _elementList = null!;
    private readonly StackPanel _propertyPanel = null!;
    private readonly ItemsControl _toolbox = null!;
    private readonly ItemsControl _pageTabs = null!;
    private readonly MenuItem _exportMenu = null!;
    private readonly MenuItem _moveToPageMenu = null!;
    private readonly DockControl _layout = null!;
    private readonly ClilocPanel _clilocPanel = null!;

    /// <summary>
    /// Width of a row in the hue and font dropdowns.
    /// </summary>
    /// <remarks>
    /// Fixed so the popup cannot resize while it is scrolled. Wide enough for a
    /// swatch and the longest hue name that is worth reading in full.
    /// </remarks>
    private const double PickerRowWidth = 240;

    /// <summary>Widest a font sample may draw before it is scaled down.</summary>
    private const double PickerSampleWidth = 130;

    /// <summary>
    /// Width of the preview beside a picker field.
    /// </summary>
    /// <remarks>
    /// A hue needs only a swatch, but a font sample is a line of text and is
    /// unreadable in the same space — so the two are sized differently rather
    /// than sharing one width that suits neither.
    /// </remarks>
    private const double HuePreviewWidth = 46;

    private const double FontPreviewWidth = 124;

    private IReadOnlyList<PickerEntry>? _hueEntries;
    private IReadOnlyList<PickerEntry>? _fontEntries;

    private bool _layoutRestored;

    /// <summary>
    /// The cliloc field whose browse button was clicked, if any.
    /// </summary>
    /// <remarks>
    /// The row and its element, never the <c>TextBox</c>: every refresh rebuilds
    /// the property panel, so a remembered control can already be an orphan by
    /// the time the browser answers. A <see cref="PropertyRow"/> closes over the
    /// element type rather than an instance, so it cannot go stale.
    /// </remarks>
    private (Element Element, PropertyRow Row)? _pendingCliloc;

    public MainWindow()
        : this(new EditorSession())
    {
    }

    /// <param name="session">
    /// The session this window edits. Injected so a test can supply one whose
    /// settings point at a scratch file rather than the real application-data
    /// one, and which has no client attached.
    /// </param>
    public MainWindow(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        AvaloniaXamlLoader.Load(this);

        // The window owns the dialogs and the clipboard because both need a
        // window - to be modal against, and to read - and it is the view model's
        // view for the handful of operations only a window can carry out.
        _viewModel = new MainViewModel(
            session, new EditorDialogs(this), new WindowClipboard(this), this);

        CanvasPanel canvasPanel = new();
        ToolboxPanel toolboxPanel = new();
        ElementsPanel elementsPanel = new();
        PropertiesPanel propertiesPanel = new();
        ClilocPanel clilocPanel = new();

        // Dock asks its dockables for content, so the panels are handed over
        // rather than looked up. Each is wrapped in the factory delegate Dock
        // expects, closing over the one instance the window holds: a panel
        // that came back rebuilt after a drag would leave every field here
        // pointing at controls no longer on screen.
        Fill("GumpDocument", canvasPanel);
        Fill("ToolboxTool", toolboxPanel);
        Fill("ElementsTool", elementsPanel);
        Fill("PropertiesTool", propertiesPanel);
        Fill("ClilocTool", clilocPanel);

        _canvas = canvasPanel.Canvas;
        _scroller = canvasPanel.Scroller;
        _toolbox = toolboxPanel.Items;
        _pageTabs = canvasPanel.PageTabs;
        _elementList = elementsPanel.List;
        _propertyPanel = propertiesPanel.Rows;
        _clilocPanel = clilocPanel;
        _layout = this.FindControl<DockControl>("Layout")!;
        _exportMenu = this.FindControl<MenuItem>("MenuExport")!;
        _moveToPageMenu = this.FindControl<MenuItem>("MenuMoveToPage")!;

        // After the panels exist, not before: the View menu's checkmarks bind to
        // toggles that read the canvas, so a binding resolving any earlier would
        // reach a field still holding null.
        DataContext = _viewModel;

        // Dock builds a tool's content outside this window's name scope, so
        // nothing inherits a DataContext down to a panel. Handed over
        // explicitly rather than relied upon - the context menus need it.
        canvasPanel.DataContext = _viewModel;
        elementsPanel.DataContext = _viewModel;

        // Filled as the Page menu opens rather than kept in sync: a disabled
        // item never opens its own submenu, so the enabled state has to be
        // settled one level up.
        this.FindControl<MenuItem>("MenuPageRoot")!.SubmenuOpened +=
            (_, _) => FillMoveToPageMenu(_moveToPageMenu);

        _canvas.Session = _session;
        _canvas.InteractionChanged += (_, _) => OnInteractionChanged();

        _session.DocumentChanged += (_, _) =>
        {
            _pendingCliloc = null;

            RefreshAll();
        };
        _session.PageChanged += (_, _) => RefreshAll();

        // Its own event, not RefreshAll: that runs on every page switch, and
        // re-binding 124,000 rows each time would be felt.
        _session.ClilocsChanged += (_, _) => RefreshClilocPanel();

        _clilocPanel.EntryChosen += (_, id) => Guarded(() => ApplyChosenCliloc(id));
        _clilocPanel.LanguageChosen += (_, code) =>
            _ = GuardedAsync(() => _session.UseClilocLanguageAsync(code));

        _clilocPanel.ShowStatus("No client loaded.");

        _viewModel.ClientOpened += (_, _) => StartClilocWarmup();

        BuildToolbox();

        // Every gesture the menu paints, bound from the menu itself. After the
        // DataContext, because it reads each item's Command.
        BindPaintedGestures();
        BindAliasGestures();

        // One menu instance per host: a ContextMenu belongs to a single control.
        _canvas.ContextMenu = EditorMenu();
        _elementList.ContextMenu = EditorMenu();

        _viewModel.LoadGridSettings();
        RestoreWindowBounds();

        BuildExportMenu();

        RefreshAll();

        Opened += async (_, _) =>
        {
            RestorePanels();

            await GuardedAsync(_viewModel.EnsureClientAsync).ConfigureAwait(true);

            if (Program.StartupDocument is { } startup)
            {
                Guarded(() => _session.Open(startup));
            }
        };
    }

    /// <summary>
    /// The cliloc browser, for tests.
    /// </summary>
    /// <remarks>
    /// Exposed rather than found in the tree: Dock builds a tool's content
    /// through a deferred content control, so the panel is not reliably a
    /// logical descendant before the window is shown.
    /// </remarks>
    internal ClilocPanel Cliloc => _clilocPanel;

    /// <summary>The property rows currently built, for tests.</summary>
    internal StackPanel Properties => _propertyPanel;

    /// <summary>
    /// The toolbox buttons, for tests that need an element on the page.
    /// </summary>
    /// <remarks>
    /// Clicking one is the path a test can drive end to end: it adds the
    /// element, selects it and rebuilds the panels, where the canvas raises its
    /// selection event from pointer input a headless test cannot produce.
    /// </remarks>
    internal ItemsControl Toolbox => _toolbox;

    /// <summary>
    /// The two context menus, for tests.
    /// </summary>
    /// <remarks>
    /// Exposed for the same reason as the panels above: their hosts are built by
    /// Dock through a deferred content control, so neither is reliably a logical
    /// descendant before the window is shown. A test that wants to prove the
    /// context menu and the menu bar agree cannot go looking for it in the tree.
    /// </remarks>
    /// <summary>
    /// The two bound panels, for tests.
    /// </summary>
    /// <remarks>
    /// Exposed for the same reason as the panels above: Dock builds a tool's
    /// content through a deferred content control, so neither is reliably a
    /// logical descendant. The objects here are the ones the application uses,
    /// and their bindings resolve because the window hands each panel a
    /// DataContext rather than relying on inheritance.
    /// </remarks>
    internal ListBox ElementList => _elementList;

    /// <inheritdoc cref="ElementList"/>
    internal ItemsControl PageTabs => _pageTabs;

    internal IReadOnlyList<EditorContextMenu> ContextMenus =>
        [(EditorContextMenu)_canvas.ContextMenu!, (EditorContextMenu)_elementList.ContextMenu!];

    /// <summary>
    /// Brings the cliloc browser forward and points it at an id.
    /// </summary>
    /// <remarks>
    /// The browse button on a cliloc field opens this panel rather than a
    /// dialog, because the panel is where the language selector and the whole
    /// table already live - and because a modal list of 124,000 strings cannot
    /// be left open beside the layout it is being used to edit.
    /// </remarks>
    private void RevealCliloc(int seedId)
    {
        // Through the toggle, so restoring and saving the layout stay in one
        // place. The menu's checkmark follows it rather than driving it.
        _viewModel.ClilocVisible = true;

        // Restoring is not enough when the tool is tabbed behind another one.
        if (_layout.Factory is { } factory
            && this.FindNameScope()?.Find("ClilocTool") is IDockable dockable)
        {
            factory.SetActiveDockable(dockable);
        }

        _clilocPanel.SeedFilter(seedId);
        _clilocPanel.FocusFilter();
    }

    /// <summary>Fills the cliloc browser from whatever the session has read.</summary>
    private void RefreshClilocPanel()
    {
        _clilocPanel.Load(
            _session.ClilocStrings ?? [], _session.ClilocLanguages, _session.ClilocLanguage);

        if (_session.ClilocStrings is null)
        {
            _clilocPanel.ShowStatus(
                _session.Data is null ? "No client loaded." : "Reading cliloc strings...");
        }

        ShowClilocTarget();

        // A language switch changes what every localised element draws. The
        // property rows need no rebuild: their hover cards are built as they
        // open, so they read the new language on their own.
        _canvas.InvalidateVisual();
    }

    /// <summary>
    /// Starts reading the cliloc table, without waiting for it.
    /// </summary>
    /// <remarks>
    /// Not awaited on purpose. The read is a MegaCliloc decode and
    /// around 124,000 strings; awaiting it here would hold a document named on
    /// the command line behind a table nothing has asked for yet. It runs off
    /// the UI thread and the panel fills in when it lands.
    /// </remarks>
    private void StartClilocWarmup()
    {
        if (_session.Data is null || _session.AreClilocsReady)
        {
            return;
        }

        _clilocPanel.ShowStatus("Reading cliloc strings...");

        _ = GuardedAsync(_session.LoadClilocsAsync);
    }

    /// <summary>
    /// What the browser's Apply would write to.
    /// </summary>
    /// <remarks>
    /// A browse button names its own row, which is unambiguous. Without one
    /// there is exactly one row worth guessing at: a localised area's whole
    /// content <em>is</em> a cliloc, so someone who selects one and picks a
    /// string means that. Nothing is guessed for "Tooltip cliloc", which every
    /// element has - choosing between it and "Cliloc id" on the author's behalf
    /// is the sort of surprise a browse button exists to avoid.
    /// </remarks>
    private (Element Element, PropertyRow Row)? ClilocTarget()
    {
        if (_pendingCliloc is { } pending
            && _session.Canvas.Selection.Count == 1
            && ReferenceEquals(_session.Canvas.Selection[0], pending.Element))
        {
            return pending;
        }

        return _session.Canvas.Selection is [HtmlElement { ContentKind: HtmlContentKind.Localized } html]
            && PropertyRow.For(html).FirstOrDefault(
                r => r.Kind == PropertyEditorKind.Cliloc) is { } row
            ? (html, row)
            : null;
    }

    /// <summary>Tells the browser which field it would write to.</summary>
    private void ShowClilocTarget() =>
        _clilocPanel.ShowTarget(ClilocTarget() is { } target ? target.Row.Name : null);

    private void ApplyChosenCliloc(int clilocId)
    {
        if (ClilocTarget() is not { } target)
        {
            SetStatus("Select a cliloc field first, or use its browse button.");

            return;
        }

        _pendingCliloc = null;

        ApplyProperty(target.Element, target.Row, clilocId);
        RefreshProperties();

        SetStatus(string.Create(
            CultureInfo.InvariantCulture, $"{target.Row.Name} set to {clilocId}."));
    }

    /// <summary>The hideable panels, each with the menu item that toggles it.</summary>
    /// <summary>
    /// The hideable panels, each with the toggle that reports whether it shows.
    /// </summary>
    /// <remarks>
    /// The visibility used to be read off the menu item's own <c>IsChecked</c>,
    /// which made the menu the place the state lived. It is the view model's
    /// now, and the checkmark is a two-way binding to it.
    /// </remarks>
    private static readonly (string DockableId, Func<MainViewModel, bool> Shows)[] Panels =
    [
        ("ToolboxTool", vm => vm.ToolboxVisible),
        ("ElementsTool", vm => vm.ElementsVisible),
        ("PropertiesTool", vm => vm.PropertiesVisible),
        ("ClilocTool", vm => vm.ClilocVisible),
    ];

    /// <summary>
    /// The proportions declared in <c>MainWindow.axaml</c>.
    /// </summary>
    /// <remarks>
    /// Duplicated from the markup because Dock overwrites them in place as the
    /// user drags a splitter, so once a layout has been restored the declared
    /// values are no longer readable from the model.
    /// </remarks>
    private static readonly (string PaneId, double Proportion)[] DefaultProportions =
    [
        ("ToolboxPane", 0.13),
        ("CenterPane", 0.6),
        ("CanvasPane", 0.75),
        ("ClilocPane", 0.25),
        ("RightPane", 0.27),
        ("ElementsPane", 0.35),
        ("PropertiesPane", 0.65),
    ];

    /// <summary>
    /// The zoom levels the in and out commands step through.
    /// </summary>
    /// <remarks>
    /// A fixed ladder rather than a multiplier, and every step below 1 is an
    /// exact reciprocal of a whole number. Gump art is pixel art: an arbitrary
    /// factor resamples it into a blur, while these land art pixels on whole
    /// screen pixels.
    /// </remarks>
    /// <summary>
    /// Tells the panels to re-read the document.
    /// </summary>
    /// <remarks>
    /// The whole-world rebuild, reached through the interface so that a command
    /// living in the view model can still ask for it. It shrinks as granular
    /// notification replaces it: the page strip and the element list come off it
    /// first, leaving the property panel.
    /// </remarks>
    public void RefreshDocumentView() => RefreshAll();

    public void InvalidateCanvas() => _canvas.InvalidateVisual();

    /// <inheritdoc />
    public bool ShowSharedPage
    {
        get => _canvas.ShowSharedPage;
        set => _canvas.ShowSharedPage = value;
    }

    /// <summary>
    /// Hides or restores one of the side panels.
    /// </summary>
    /// <remarks>
    /// Through <c>HideDockable</c> and <c>RestoreDockable</c>, which move the
    /// dockable between its owner and the root's hidden list. Dock's
    /// <c>CloseDockable</c> would remove it from its owner outright and leave
    /// nothing to restore, which is why the tabs are still not closable.
    /// </remarks>
    public void ShowPanel(string dockableId, bool show)
    {
        if (_layout.Factory is not { } factory)
        {
            return;
        }

        if (show)
        {
            factory.RestoreDockable(dockableId);
        }
        else
        {
            factory.HideDockable(dockableId);
        }

        SaveLayout();
    }

    /// <summary>Brings every hidden panel back and restores the declared sizes.</summary>
    public void ResetLayout()
    {
        if (_layout.Factory is not { } factory)
        {
            return;
        }

        foreach ((string dockableId, _) in Panels)
        {
            factory.RestoreDockable(dockableId);
        }

        foreach ((string paneId, double proportion) in DefaultProportions)
        {
            if (this.FindNameScope()?.Find(paneId) is IDock pane)
            {
                pane.Proportion = proportion;
            }
        }

        SaveLayout();
        SetStatus("Panel layout reset.");
    }

    /// <summary>Closes the window, the question about unsaved work already asked.</summary>
    public void CloseShell()
    {
        _closeConfirmed = true;

        Close();
    }

    /// <summary>
    /// Gestures the menu deliberately does not paint.
    /// </summary>
    /// <remarks>
    /// A menu item can show one shortcut, but people reach for more than one:
    /// zooming in is Ctrl and the '+' key, which is a shifted <c>OemPlus</c>,
    /// and the numeric keypad has its own key codes entirely. Redo answers to
    /// both Ctrl+Y and Ctrl+Shift+Z, and Save As has no painted gesture at all.
    ///
    /// Disjoint from the painted set on purpose, and a test asserts it: two
    /// bindings for one gesture both fire, so a duplicate runs its action twice.
    /// </remarks>
    private static readonly (string Gesture, Func<MainViewModel, ICommand> Pick)[] AliasGestures =
    [
        ("Ctrl+Shift+Z", vm => vm.RedoCommand),
        ("Ctrl+Shift+S", vm => vm.SaveAsCommand),
        ("Ctrl+Shift+OemPlus", vm => vm.ZoomInCommand),
        ("Ctrl+Add", vm => vm.ZoomInCommand),
        ("Ctrl+Subtract", vm => vm.ZoomOutCommand),
        ("Ctrl+NumPad0", vm => vm.ZoomResetCommand),
    ];

    /// <summary>
    /// Gestures a focused text box owns, which the window steps aside for.
    /// </summary>
    /// <remarks>
    /// A <see cref="TextBox"/> marks these handled itself, and a window
    /// <c>KeyBinding</c> in Avalonia 12 runs anyway - so the window has to
    /// decline them rather than rely on the key being consumed.
    /// </remarks>
    private static readonly string[] TextEditingGestures =
        ["Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z", "Ctrl+A", "Ctrl+X", "Ctrl+C", "Ctrl+V", "Delete"];

    /// <summary>
    /// Binds every gesture the menus advertise, from the menus themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>InputGesture</c> on a <see cref="MenuItem"/> only <em>draws</em> the
    /// shortcut next to the item; it does not make the key do anything. Every
    /// gesture in the menu bar was decorative once - Ctrl+G, Ctrl+S, Ctrl+Z and
    /// the rest all did nothing - and the zoom items added later reintroduced
    /// exactly the same defect, three painted labels with no binding behind them.
    /// </para>
    /// <para>
    /// Walking the menu rather than repeating it by hand is what makes that
    /// impossible rather than merely tested: a painted gesture and its binding
    /// now come from the same place, so one cannot exist without the other.
    /// </para>
    /// </remarks>
    private void BindPaintedGestures()
    {
        foreach (MenuItem item in this.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (item.InputGesture is not { } gesture || item.Command is not { } command)
            {
                continue;
            }

            if (KeyBindings.Any(binding => Equals(binding.Gesture, gesture)))
            {
                continue;
            }

            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = Wrap(gesture, command) });
        }
    }

    /// <summary>Binds the gestures no menu item paints.</summary>
    private void BindAliasGestures()
    {
        foreach ((string gesture, Func<MainViewModel, ICommand> pick) in AliasGestures)
        {
            KeyGesture parsed = KeyGesture.Parse(gesture);

            if (KeyBindings.Any(binding => Equals(binding.Gesture, parsed)))
            {
                continue;
            }

            KeyBindings.Add(new KeyBinding
            {
                Gesture = parsed,
                Command = Wrap(parsed, pick(_viewModel)),
            });
        }
    }

    /// <summary>
    /// Wraps a command so a focused text box keeps the keys that are its own.
    /// </summary>
    /// <remarks>
    /// Only for the gestures in <see cref="TextEditingGestures"/>; everything
    /// else means the same thing wherever the keyboard happens to be.
    /// </remarks>
    private ICommand Wrap(KeyGesture gesture, ICommand command) =>
        Array.Exists(TextEditingGestures, g => KeyGesture.Parse(g).Equals(gesture))
            ? new FocusAwareCommand(command, () => FocusManager?.GetFocusedElement() is not TextBox)
            : command;

    private static readonly double[] ZoomLadder =
        [0.25, 1.0 / 3, 0.5, 1.0, 2.0, 3.0, 4.0];

    public void StepZoom(bool up)
    {
        double current = _canvas.Zoom;

        double? next = up
            ? ZoomLadder.FirstOrDefault(z => z > current + 0.0001)
            : ZoomLadder.LastOrDefault(z => z < current - 0.0001);

        if (next is null or 0)
        {
            SetStatus(up ? "Already at the largest zoom." : "Already at the smallest zoom.");

            return;
        }

        SetZoom(next.Value);
    }

    public void SetZoom(double zoom)
    {
        _canvas.Zoom = zoom;

        ReportZoom();
    }

    /// <summary>Scales the gump so all of it fits the visible area.</summary>
    public void ZoomToFit()
    {
        _canvas.ZoomToFit(_scroller.Viewport);

        ReportZoom();
    }

    private void ReportZoom() =>
        SetStatus(string.Create(
            CultureInfo.InvariantCulture,
            $"Zoom {_canvas.Zoom * 100:0.#}%."));

    /// <summary>
    /// Records the window placement and panel arrangement.
    /// </summary>
    /// <remarks>
    /// A restore has to run before this can overwrite anything, or opening the
    /// window would save the declared layout over the stored one.
    /// </remarks>
    private void SaveLayout()
    {
        if (!_layoutRestored)
        {
            return;
        }

        WindowLayout layout = _session.Settings.Layout;

        layout.Maximized = WindowState == WindowState.Maximized;

        // Only meaningful while the window is in its normal state: a maximised
        // window would otherwise store the screen size as its restore size.
        if (WindowState == WindowState.Normal)
        {
            layout.Width = Width;
            layout.Height = Height;
            layout.X = Position.X;
            layout.Y = Position.Y;
        }

        layout.Proportions.Clear();

        foreach ((string paneId, _) in DefaultProportions)
        {
            if (this.FindNameScope()?.Find(paneId) is IDock { Proportion: var proportion }
                && !double.IsNaN(proportion))
            {
                layout.Proportions[paneId] = proportion;
            }
        }

        layout.HiddenPanels.Clear();

        foreach ((string dockableId, Func<MainViewModel, bool> shows) in Panels)
        {
            if (!shows(_viewModel))
            {
                layout.HiddenPanels.Add(dockableId);
            }
        }

        _session.Settings.Save();
    }

    /// <summary>
    /// Puts the window and its panels back where they were left.
    /// </summary>
    /// <remarks>
    /// Every part is optional and independently validated: a stored layout that
    /// no longer matches the declared dockables, or a position on a monitor that
    /// is no longer attached, falls back to the default rather than opening a
    /// window that cannot be reached.
    /// </remarks>
    private void RestoreWindowBounds()
    {
        WindowLayout layout = _session.Settings.Layout;

        if (WindowLayout.IsUsableSize(layout.Width, layout.Height)
            && layout is { Width: { } width, Height: { } height })
        {
            Width = width;
            Height = height;
        }

        if (layout.X is { } x && layout.Y is { } y && IsOnAScreen(x, y))
        {
            Position = new PixelPoint(x, y);
        }

        // Applied after the size, so the stored restore size is not replaced by
        // the maximised one.
        if (layout.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Restores the panel arrangement.
    /// </summary>
    /// <remarks>
    /// Separate from the window bounds, and run once the window is open: the
    /// bounds are wanted before the window is first shown, while the dock model
    /// is only reliably built and initialised by then.
    /// </remarks>
    private void RestorePanels()
    {
        WindowLayout layout = _session.Settings.Layout;

        foreach ((string paneId, double proportion) in layout.Proportions)
        {
            if (WindowLayout.IsUsableProportion(proportion)
                && this.FindNameScope()?.Find(paneId) is IDock pane)
            {
                pane.Proportion = proportion;
            }
        }

        if (_layout.Factory is { } factory)
        {
            foreach (string dockableId in layout.HiddenPanels)
            {
                if (Array.Exists(Panels, p => p.DockableId == dockableId))
                {
                    factory.HideDockable(dockableId);
                }
            }
        }

        // Seeded rather than assigned one by one: a toggle that acted on its own
        // change would undo the restore in progress and then save over it.
        _viewModel.AdoptPanelVisibility(
            toolbox: !layout.HiddenPanels.Contains("ToolboxTool"),
            elements: !layout.HiddenPanels.Contains("ElementsTool"),
            properties: !layout.HiddenPanels.Contains("PropertiesTool"),
            cliloc: !layout.HiddenPanels.Contains("ClilocTool"));

        _layoutRestored = true;
    }

    /// <summary>Whether a saved position still lands on an attached monitor.</summary>
    private bool IsOnAScreen(int x, int y)
    {
        if (Screens is not { } screens)
        {
            return false;
        }

        foreach (Screen screen in screens.All)
        {
            if (screen.Bounds.Contains(new PixelPoint(x, y)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One context menu, ready for a host.
    /// </summary>
    /// <remarks>
    /// Only the page submenu is filled here; everything else is bound. Pages are
    /// added and removed while the editor is open, so a stale entry would point
    /// at a page that no longer exists.
    /// </remarks>
    private EditorContextMenu EditorMenu()
    {
        EditorContextMenu menu = new() { DataContext = _viewModel };

        menu.Opening += (_, _) => FillMoveToPageMenu(menu.MoveToPageItem);

        return menu;
    }

    /// <summary>Gives a dockable declared in the layout the view it shows.</summary>
    private void Fill(string dockableName, Control view)
    {
        object content = new Func<IServiceProvider, object>(_ => view);

        switch (this.FindNameScope()?.Find(dockableName))
        {
            case Tool tool:
                tool.Content = content;

                break;

            case Document document:
                document.Content = content;

                break;

            default:
                throw new InvalidOperationException(
                    $"The layout has no dockable named '{dockableName}'.");
        }
    }

    /// <summary>
    /// Forwards to the view model, which owns the policy.
    /// </summary>
    /// <remarks>
    /// Kept as private helpers rather than replaced at each call site: the parts
    /// of the shell still driving the session directly - the cliloc hand-off, the
    /// toolbox, the two runtime-built menus and the property grid - all report a
    /// failure the same way, and there is no reason for them to say so twice.
    /// </remarks>
    private void Guarded(Action action) => _viewModel.RunGuarded(action);

    /// <inheritdoc cref="Guarded"/>
    private Task GuardedAsync(Func<Task> action) => _viewModel.RunGuardedAsync(action);

    private void BuildToolbox()
    {
        (string Label, Func<Element> Create)[] entries =
        [
            ("Background", () => new BackgroundElement()),
            ("Image", () => new ImageElement()),
            ("Tiled", () => new TiledElement()),
            ("Pic in pic", () => new PicInPicElement()),
            ("Item", () => new ItemElement()),
            ("Item as pic", () => new TileAsGumpElement()),
            ("Label", () => new LabelElement()),
            ("Button", () => new ButtonElement()),
            ("Checkbox", () => new CheckboxElement()),
            ("Radio", () => new RadioElement()),
            ("Text entry", () => new TextEntryElement()),
            ("HTML", () => new HtmlElement()),
            ("Alpha", () => new AlphaElement()),
        ];

        List<Button> buttons = [];

        foreach ((string label, Func<Element> create) in entries)
        {
            Button button = new() { Content = label, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

            button.Click += (_, _) => Guarded(() => _viewModel.AddElement(create()));
            buttons.Add(button);
        }

        _toolbox.ItemsSource = buttons;
    }

    private void RefreshAll()
    {
        RefreshProperties();

        _canvas.InvalidateVisual();

        SetStatus(
            $"{_session.Document.PageCount} page(s), "
            + $"{_session.ActivePage.Root.Children.Count} element(s) on page {_session.ActivePageIndex}"
            + (_session.DocumentPath is { } path ? $" — {Path.GetFileName(path)}" : string.Empty));
    }

    private bool _closeConfirmed;

    // What the inspector is currently showing, so a mid-drag update can tell
    // that the panel's shape is still right for the selection.
    private Element? _shownElement;
    private int _shownSelectionCount = -1;

    // One per editor on screen, each pushing its element's current value back
    // into the control without rebuilding it.
    private readonly List<Action> _propertyValueRefreshers = [];

    /// <summary>
    /// Reacts to a canvas gesture.
    /// </summary>
    /// <remarks>
    /// The canvas raises this on every pointer move during a drag. Rebuilding
    /// the inspector each time meant tearing down and reallocating a grid, a
    /// label, a tooltip and an editor with its handlers for every property of
    /// the selected element, per mouse-move event — the single most expensive
    /// thing the editor did while dragging. Mid-gesture the shape of the panel
    /// cannot change, only the numbers in it, so the values are pushed into the
    /// editors already on screen.
    ///
    /// The selection is still compared, because pressing a different element
    /// begins a drag and changes the selection in the same gesture.
    /// </remarks>
    private void OnInteractionChanged()
    {
        int count = _session.Canvas.Selection.Count;
        Element? single = count == 1 ? _session.Canvas.Selection[0] : null;

        if (_session.Canvas.IsDragging
            && count == _shownSelectionCount
            && ReferenceEquals(single, _shownElement))
        {
            UpdatePropertyValues();

            return;
        }

        RefreshSelection();
    }

    /// <summary>
    /// Re-reads each editor's value from the element without rebuilding the row.
    /// </summary>
    /// <remarks>
    /// A focused field is left alone: it holds what is being typed, which the
    /// element does not have yet.
    /// </remarks>
    private void UpdatePropertyValues()
    {
        foreach (Action refresh in _propertyValueRefreshers)
        {
            refresh();
        }
    }

    private void RefreshSelection() => RefreshProperties();

    private void RefreshProperties()
    {
        _propertyPanel.Children.Clear();
        _propertyValueRefreshers.Clear();

        _shownSelectionCount = _session.Canvas.Selection.Count;
        _shownElement = _shownSelectionCount == 1 ? _session.Canvas.Selection[0] : null;

        if (_session.Canvas.Selection.Count != 1)
        {
            _propertyPanel.Children.Add(new TextBlock
            {
                Text = _session.Canvas.Selection.Count == 0
                    ? "Nothing selected."
                    : $"{_session.Canvas.Selection.Count} elements selected.",
                Foreground = Brushes.Gray,
            });

            return;
        }

        Element element = _session.Canvas.Selection[0];

        _propertyPanel.Children.Add(new TextBlock
        {
            Text = element.TypeName,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.LightSkyBlue,
        });

        foreach (PropertyRow row in PropertyRow.For(element))
        {
            _propertyPanel.Children.Add(BuildRow(element, row));
        }

        ShowClilocTarget();
    }

    private Grid BuildRow(Element element, PropertyRow row)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
        };

        // The column is a fixed width, so a long name has to wrap rather than be
        // cut off mid-glyph with nothing to say it was truncated.
        TextBlock label = new()
        {
            Text = row.Name,
            Foreground = Brushes.Silver,
            Margin = new Avalonia.Thickness(0, 4, 6, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        ToolTip.SetTip(label, row.Description ?? row.Name);

        if (row.Kind == PropertyEditorKind.Cliloc)
        {
            AttachClilocTip(label, element, row);
        }

        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        Control editor = row.Kind switch
        {
            PropertyEditorKind.Boolean => BuildBooleanEditor(element, row),
            PropertyEditorKind.Choice => BuildChoiceEditor(element, row),
            PropertyEditorKind.GumpId => BuildBrowsableIdEditor(element, row, ArtBrowserKind.Gump),
            PropertyEditorKind.ItemId => BuildBrowsableIdEditor(element, row, ArtBrowserKind.Item),
            PropertyEditorKind.Color => BuildColorEditor(element, row),
            PropertyEditorKind.Cliloc => BuildClilocEditor(element, row),
            PropertyEditorKind.Hue => BuildPickerEditor(element, row, HueEntries()),
            PropertyEditorKind.Font => BuildPickerEditor(element, row, FontEntries()),
            _ => BuildTextEditor(element, row),
        };

        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        return grid;
    }

    private TextBox BuildTextEditor(Element element, PropertyRow row)
    {
        TextBox box = new()
        {
            Text = Convert.ToString(row.Read(element), CultureInfo.InvariantCulture) ?? string.Empty,
        };

        box.LostFocus += (_, _) => ApplyProperty(element, row, box.Text ?? string.Empty);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                ApplyProperty(element, row, box.Text ?? string.Empty);
            }
        };

        _propertyValueRefreshers.Add(() =>
        {
            if (box.IsFocused)
            {
                return;
            }

            string current = Convert.ToString(row.Read(element), CultureInfo.InvariantCulture)
                ?? string.Empty;

            if (!string.Equals(box.Text, current, StringComparison.Ordinal))
            {
                box.Text = current;
            }
        });

        return box;
    }

    /// <summary>
    /// A number field with a browse button beside it.
    /// </summary>
    /// <remarks>
    /// Typing raw ids is unusable — nobody remembers that 5054 is a stone frame —
    /// which is why the original shipped art browsers. The field stays editable
    /// for anyone who does know the number.
    /// </remarks>
    private Grid BuildBrowsableIdEditor(Element element, PropertyRow row, ArtBrowserKind kind)
    {
        Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        TextBox box = BuildTextEditor(element, row);

        Grid.SetColumn(box, 0);
        layout.Children.Add(box);

        Button browse = new()
        {
            Content = "…",
            Width = 30,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            IsEnabled = _session.Data is not null,
        };

        browse.Click += async (_, _) =>
        {
            int current = row.Read(element) is int id ? id : 0;

            ArtBrowserWindow browser = new(_session.Data, kind, current, _session.Settings);

            await browser.ShowDialog(this).ConfigureAwait(true);

            if (browser.SelectedId is { } chosen)
            {
                box.Text = chosen.ToString(CultureInfo.InvariantCulture);

                ApplyProperty(element, row, chosen);
                RefreshProperties();
            }
        };

        Grid.SetColumn(browse, 1);
        layout.Children.Add(browse);

        return layout;
    }

    /// <summary>
    /// A cliloc id, with a browse button that reveals the cliloc panel.
    /// </summary>
    /// <remarks>
    /// No dialog, unlike the art fields: the browser is a dockable panel, so the
    /// button brings it forward and seeds its filter instead of blocking on a
    /// modal. The field stays typeable for anyone who knows the number.
    /// </remarks>
    private Grid BuildClilocEditor(Element element, PropertyRow row)
    {
        Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        TextBox box = BuildTextEditor(element, row);

        AttachClilocTip(box, element, row);

        Grid.SetColumn(box, 0);
        layout.Children.Add(box);

        Button browse = new()
        {
            Content = "…",
            Width = 30,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            IsEnabled = _session.Data is not null,
        };

        browse.Click += (_, _) =>
        {
            _pendingCliloc = (element, row);

            ShowClilocTarget();
            StartClilocWarmup();
            RevealCliloc(row.Read(element) is int id ? id : 0);
        };

        Grid.SetColumn(browse, 1);
        layout.Children.Add(browse);

        return layout;
    }

    /// <summary>
    /// Gives a control a cliloc card that is built as it opens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lazily, not eagerly. The card depends on four things that move
    /// independently - the id, the arguments, the language, and whether the
    /// table has been read yet - so building it on open is the only version that
    /// is never stale, and it needs no entry in
    /// <see cref="_propertyValueRefreshers"/> and no rebuild on a language
    /// switch. It is also the cheap way round: the property panel is rebuilt on
    /// every selection change, and a card three text blocks deep for a row
    /// nobody hovers is exactly the per-rebuild cost the drag path avoids.
    /// </para>
    /// <para>
    /// The placeholder is not decoration. Avalonia raises no opening event at
    /// all for a control whose tip is unset, so there has to be something there
    /// to replace.
    /// </para>
    /// </remarks>
    private void AttachClilocTip(Control host, Element element, PropertyRow row)
    {
        if (ToolTip.GetTip(host) is null)
        {
            ToolTip.SetTip(host, row.Description ?? row.Name);
        }

        ToolTip.AddToolTipOpeningHandler(host, (_, _) =>
        {
            // Hovering must never be what pays for the table: start the read
            // and say so, rather than freezing the pointer over the row.
            StartClilocWarmup();

            ToolTip.SetTip(host, ClilocTip.Build(
                row.Read(element) is int id ? id : 0,
                row.ReadArguments?.Invoke(element) ?? string.Empty,
                _session.ClilocLanguage,
                _session.ResolveCliloc,
                _session.Data is null
                    ? "No client loaded."
                    : _session.AreClilocsReady ? null : "Reading the client's cliloc strings..."));
        });
    }

    /// <summary>
    /// Drops the cached picker rows, so a different client rebuilds them.
    /// </summary>
    /// <remarks>
    /// They are built from the loaded client's hue table and fonts, so pointing
    /// at another installation would otherwise keep showing the previous one's.
    /// </remarks>
    public void ForgetPickerEntries()
    {
        _hueEntries = null;
        _fontEntries = null;
    }

    /// <summary>
    /// A colour field with a swatch and a picker beside it.
    /// </summary>
    /// <remarks>
    /// The field stays editable, because a colour copied out of a server script
    /// arrives as a number and the two spellings in the wild — RGB555 and 24-bit
    /// — both read correctly. The picker writes RGB555.
    /// </remarks>
    private Grid BuildColorEditor(Element element, PropertyRow row)
    {
        Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        TextBox box = BuildTextEditor(element, row);

        Border swatch = new()
        {
            Width = 26,
            Height = 20,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Brushes.Gray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = ColorBrush(row.Read(element)),
        };

        Button pick = new()
        {
            Content = "…",
            Width = 30,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
        };

        pick.Click += async (_, _) =>
        {
            int current = row.Read(element) is int value ? value : 0;

            ColorPickerWindow picker = new(current);

            await picker.ShowDialog(this).ConfigureAwait(true);

            if (picker.Result is not { } chosen)
            {
                return;
            }

            box.Text = chosen.ToString(CultureInfo.InvariantCulture);
            swatch.Background = ColorBrush(chosen);

            ApplyProperty(element, row, chosen);
            _canvas.InvalidateVisual();
        };

        box.LostFocus += (_, _) => swatch.Background = ColorBrush(row.Read(element));

        Grid.SetColumn(box, 0);
        Grid.SetColumn(swatch, 1);
        Grid.SetColumn(pick, 2);
        layout.Children.Add(box);
        layout.Children.Add(swatch);
        layout.Children.Add(pick);

        return layout;
    }

    /// <summary>The brush a colour value paints, or none when it is unset.</summary>
    private static SolidColorBrush? ColorBrush(object? value)
    {
        if (value is not int number || Rendering.GumpColor.ToSkColor(number) is not { } colour)
        {
            return null;
        }

        return new SolidColorBrush(Color.FromRgb(colour.Red, colour.Green, colour.Blue));
    }

    /// <summary>Hue rows, built once per client because there are thousands.</summary>
    private IReadOnlyList<PickerEntry> HueEntries() =>
        _hueEntries ??= PickerEntries.Hues(_session.Data);

    /// <summary>Font rows, built once per client.</summary>
    private IReadOnlyList<PickerEntry> FontEntries() =>
        _fontEntries ??= PickerEntries.Fonts(_session.Data);

    /// <summary>
    /// A field that filters a list of previews as you type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hues and fonts are both meaningless as numbers — nobody knows what hue
    /// 1153 looks like — so each row is drawn: a hue as its own colour ramp, a
    /// font as a line of text set in it. Typing filters by index or by name, so
    /// "blue" finds the blue hues and "11" narrows to those numbers.
    /// </para>
    /// <para>
    /// The swatch beside the field shows the current value without opening the
    /// list, which is the state you are in most of the time.
    /// </para>
    /// </remarks>
    private Grid BuildPickerEditor(
        Element element, PropertyRow row, IReadOnlyList<PickerEntry> entries)
    {
        Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        object? current = row.Read(element);
        PickerEntry? selected = entries.FirstOrDefault(e => Equals(e.Value, current));

        bool samples = entries.Any(e => e.Sample is not null);

        ContentControl preview = new()
        {
            Width = samples ? FontPreviewWidth : HuePreviewWidth,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Content = Swatch(selected, samples ? FontPreviewWidth : PickerSampleWidth),
        };

        AutoCompleteBox box = new()
        {
            ItemsSource = entries,
            FilterMode = AutoCompleteFilterMode.Custom,
            ItemFilter = (search, item) => item is PickerEntry entry && entry.Matches(search),
            MinimumPrefixLength = 0,
            IsTextCompletionEnabled = false,
            MaxDropDownHeight = 320,
            ItemTemplate = PickerTemplate(),
            Text = selected?.Display ?? Convert.ToString(current, CultureInfo.InvariantCulture),
        };

        // Two things happen on focus. The field shows "1152 — ice_hue_2" when it
        // is idle, which is not a search term — typing into it would filter on
        // that whole label and match nothing — so it is emptied. And the list is
        // opened explicitly: an AutoCompleteBox drops down when its text changes,
        // which meant browsing was impossible without first typing something.
        box.GotFocus += (_, _) =>
        {
            if (box.Text?.Length > 0)
            {
                box.Text = string.Empty;
            }

            box.IsDropDownOpen = true;
        };

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not PickerEntry chosen)
            {
                return;
            }

            selected = chosen;
            preview.Content = Swatch(chosen, samples ? FontPreviewWidth : PickerSampleWidth);

            ApplyProperty(element, row, chosen.Value);
        };

        // Clicking a row in the list takes focus off the field before the
        // selection is reported, so doing any of this synchronously would undo
        // the click: the search term is not a number, so the field would be put
        // back to the previous value and the choice lost. Posting it lets
        // SelectionChanged land first, after which there is nothing to correct.
        box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (box.IsFocused)
            {
                return;
            }

            // A bare number still works, so an id copied out of a script can be
            // pasted straight in.
            if (Typed(entries, box.Text) is { } match)
            {
                selected = match;
                preview.Content = Swatch(match, samples ? FontPreviewWidth : PickerSampleWidth);

                ApplyProperty(element, row, match.Value);
            }
            else if (TypedWithoutEntries(row.Read(element), box.Text) is { } raw)
            {
                // With no client loaded there are no rows to match against, and
                // the field would otherwise silently discard what was typed.
                ApplyProperty(element, row, raw);
            }

            box.Text = selected?.Display ?? box.Text;
        });

        Grid.SetColumn(box, 0);
        Grid.SetColumn(preview, 1);
        layout.Children.Add(box);
        layout.Children.Add(preview);

        return layout;
    }

    /// <summary>The row a typed-in number names, or null.</summary>
    private static PickerEntry? Typed(IReadOnlyList<PickerEntry> entries, string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? entries.FirstOrDefault(e => e.Index == index)
            : null;

    /// <summary>
    /// A typed number applied without a row to match it against.
    /// </summary>
    /// <remarks>
    /// The rows come from the loaded client, so with none configured the hue and
    /// font lists are empty. Typing an id has to keep working regardless: it is
    /// how the field behaved before it became a picker, and how a value copied
    /// out of a server script gets in.
    /// </remarks>
    private static object? TypedWithoutEntries(object? current, string? text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return null;
        }

        return current switch
        {
            int => index,
            FontChoice font => font with { Index = index },
            _ => null,
        };
    }

    /// <summary>
    /// Draws one row: its preview, then its number and name.
    /// </summary>
    /// <remarks>
    /// Every row is the same fixed width, and the label is trimmed rather than
    /// allowed to push it wider. The list virtualises, so a row is only measured
    /// once it scrolls into view — with rows free to size themselves the popup
    /// grew and shrank as it was scrolled, since hue names range from <c>none</c>
    /// to <c>Hue (2054→24191)</c>.
    /// </remarks>
    private static FuncDataTemplate<PickerEntry> PickerTemplate() =>
        new((entry, _) =>
        {
            if (entry is null)
            {
                // A template is also asked to build while a container is being
                // cleared, with no item.
                return new Border();
            }

            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Width = PickerRowWidth,
                Height = 22,
            };

            Control swatch = Swatch(entry, PickerSampleWidth);

            swatch.Margin = new Avalonia.Thickness(0, 0, 8, 0);

            TextBlock label = new()
            {
                Text = entry.Display,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(swatch, 0);
            Grid.SetColumn(label, 1);
            row.Children.Add(swatch);
            row.Children.Add(label);

            return row;
        });

    /// <summary>A hue's ramp or a font's sample, sized to sit in a row.</summary>
    private static Control Swatch(PickerEntry? entry, double sampleWidth)
    {
        if (entry?.Sample is { } sample)
        {
            // Scaled down only when it will not fit, so a face is shown at its
            // real size wherever there is room for it.
            Image image = new()
            {
                Source = sample,
                MaxWidth = sampleWidth,
                MaxHeight = 22,
                Stretch = Avalonia.Media.Stretch.Uniform,
                StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
            };

            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);

            return image;
        }

        Border swatch = new()
        {
            Width = 40,
            Height = 16,
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Avalonia.Media.Brushes.Gray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        if (entry?.Ramp is { Count: > 0 } ramp)
        {
            LinearGradientBrush brush = new()
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative),
            };

            for (int i = 0; i < ramp.Count; i++)
            {
                brush.GradientStops.Add(
                    new GradientStop(ramp[i], (double)i / (ramp.Count - 1)));
            }

            swatch.Background = brush;
        }

        return swatch;
    }

    private CheckBox BuildBooleanEditor(Element element, PropertyRow row)
    {
        CheckBox box = new() { IsChecked = row.Read(element) is true };

        box.IsCheckedChanged += (_, _) => ApplyProperty(element, row, box.IsChecked is true);

        return box;
    }

    private ComboBox BuildChoiceEditor(Element element, PropertyRow row)
    {
        ComboBox combo = new()
        {
            ItemsSource = row.Choices,
            SelectedItem = Convert.ToString(row.Read(element), CultureInfo.InvariantCulture),
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string choice)
            {
                ApplyProperty(element, row, choice);
            }
        };

        return combo;
    }

    private void ApplyProperty(Element element, PropertyRow row, object? value)
    {
        if (row.CreateSetCommand(element, value) is not { } command)
        {
            return;
        }

        _session.History.Push(command);
        _session.MeasureActivePage();

        _canvas.InvalidateVisual();

        // Switching an area between markup and a localised string changes
        // whether the browser has anything to write to.
        ShowClilocTarget();
    }

    /// <summary>
    /// Fills the export menu, one entry per converter.
    /// </summary>
    /// <remarks>
    /// One entry per converter, not per dialect. Six entries used to read as six
    /// unrelated formats; the dialect is a property of the export, so it is asked
    /// for in the options dialog instead.
    /// </remarks>
    private void BuildExportMenu()
    {
        List<MenuItem> items = [];

        foreach (IGumpConverter converter in EditorSession.Converters)
        {
            IGumpConverter captured = converter;
            MenuItem item = new() { Header = converter.DisplayName + "…" };

            item.Click += async (_, _) =>
                await GuardedAsync(() => _viewModel.ExportAsync(captured)).ConfigureAwait(true);

            items.Add(item);
        }

        _exportMenu.ItemsSource = items;
        _exportMenu.IsEnabled = items.Count > 0;
    }

    /// <summary>
    /// Fills a submenu with every page except the one being edited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilt on demand rather than kept in sync, because pages are added and
    /// removed while the editor is open and a stale entry would point at a page
    /// that no longer exists.
    /// </para>
    /// <para>
    /// The current page is left out because moving something to the page it is
    /// already on is a no-op, and offering it invites the click.
    /// </para>
    /// </remarks>
    private void FillMoveToPageMenu(MenuItem parent)
    {
        List<MenuItem> targets = [];

        for (int index = 0; index < _session.Document.PageCount; index++)
        {
            if (index == _session.ActivePageIndex)
            {
                continue;
            }

            int target = index;
            GumpPage page = _session.Document.Pages[index];

            MenuItem item = new()
            {
                Header = string.IsNullOrEmpty(page.Name)
                    ? string.Create(CultureInfo.InvariantCulture, $"Page {target}")
                    : page.Name,
            };

            item.Click += (_, _) => Guarded(() => _viewModel.MoveSelectionToPage(target));

            targets.Add(item);
        }

        parent.ItemsSource = targets;

        // A single-page document has nowhere to move to, and nothing selected has
        // nothing to move.
        parent.IsEnabled = targets.Count > 0 && _session.Canvas.Selection.Count > 0;
    }

    /// <summary>Forwards to the view model, which the status line binds to.</summary>
    /// <remarks>Kept for the same reason as <see cref="Guarded"/>.</remarks>
    private void SetStatus(string message, bool isError = false) =>
        _viewModel.SetStatus(message, isError);

    /// <summary>
    /// Intercepts the window's own close button to ask about unsaved work.
    /// </summary>
    /// <remarks>
    /// The close has to be cancelled first and re-issued after the answer,
    /// because a dialog cannot be awaited inside the synchronous
    /// <c>Closing</c> handler.
    /// </remarks>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnClosing(e);

        SaveLayout();

        if (e.Cancel || _closeConfirmed || !_session.IsModified)
        {
            return;
        }

        e.Cancel = true;

        _ = ConfirmAndCloseAsync();

        async Task ConfirmAndCloseAsync()
        {
            if (!await _viewModel.ConfirmDiscardAsync("closing").ConfigureAwait(true))
            {
                return;
            }

            _closeConfirmed = true;

            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        Dispose();
    }

    /// <summary>Releases the session and the canvas surface.</summary>
    public void Dispose()
    {
        _canvas.Dispose();
        _session.Dispose();
    }
}
