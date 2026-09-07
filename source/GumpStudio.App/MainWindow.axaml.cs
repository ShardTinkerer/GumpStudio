using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using Dock.Avalonia.Controls;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;

using GumpStudio.App.Controls;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Serialization;

namespace GumpStudio.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly EditorSession _session;

    private readonly GumpCanvas _canvas = null!;
    private readonly ScrollViewer _scroller = null!;
    private readonly ListBox _elementList = null!;
    private readonly StackPanel _propertyPanel = null!;
    private readonly StackPanel _pageTabs = null!;
    private readonly ItemsControl _toolbox = null!;
    private readonly TextBlock _status = null!;
    private readonly MenuItem _exportMenu = null!;
    private readonly MenuItem _moveToPageMenu = null!;
    private readonly DockControl _layout = null!;

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

    private bool _suppressSelectionSync;
    private List<Element>? _listedElements;
    private bool _layoutRestored;

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

        CanvasPanel canvasPanel = new();
        ToolboxPanel toolboxPanel = new();
        ElementsPanel elementsPanel = new();
        PropertiesPanel propertiesPanel = new();

        // Dock asks its dockables for content, so the panels are handed over
        // rather than looked up. Each is wrapped in the factory delegate Dock
        // expects, closing over the one instance the window holds: a panel
        // that came back rebuilt after a drag would leave every field here
        // pointing at controls no longer on screen.
        Fill("GumpDocument", canvasPanel);
        Fill("ToolboxTool", toolboxPanel);
        Fill("ElementsTool", elementsPanel);
        Fill("PropertiesTool", propertiesPanel);

        _canvas = canvasPanel.Canvas;
        _scroller = canvasPanel.Scroller;
        _pageTabs = canvasPanel.PageTabs;
        _toolbox = toolboxPanel.Items;
        _elementList = elementsPanel.List;
        _propertyPanel = propertiesPanel.Rows;
        _layout = this.FindControl<DockControl>("Layout")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        _exportMenu = this.FindControl<MenuItem>("MenuExport")!;
        _moveToPageMenu = this.FindControl<MenuItem>("MenuMoveToPage")!;

        // Filled as the Page menu opens rather than kept in sync: a disabled
        // item never opens its own submenu, so the enabled state has to be
        // settled one level up.
        this.FindControl<MenuItem>("MenuPageRoot")!.SubmenuOpened +=
            (_, _) => FillMoveToPageMenu(_moveToPageMenu);

        this.FindControl<MenuItem>("MenuEditRoot")!.SubmenuOpened += (_, _) => RefreshEditMenu();

        _canvas.Session = _session;
        _canvas.InteractionChanged += (_, _) => OnInteractionChanged();

        _session.ModifiedChanged += (_, _) => RefreshTitle();

        _elementList.SelectionChanged += OnElementListSelectionChanged;

        _session.DocumentChanged += (_, _) => { _listedElements = null; RefreshAll(); };
        _session.PageChanged += (_, _) => { _listedElements = null; RefreshAll(); };

        BuildToolbox();
        WireMenus();
        BindShortcuts();

        // One menu instance per host: a ContextMenu belongs to a single control.
        _canvas.ContextMenu = BuildContextMenu();
        _elementList.ContextMenu = BuildContextMenu();

        LoadGridSettings();
        RestoreWindowBounds();

        BuildExportMenu();

        RefreshAll();

        Opened += async (_, _) =>
        {
            RestorePanels();

            await EnsureClientAsync().ConfigureAwait(true);

            if (Program.StartupDocument is { } startup)
            {
                Guarded(() => _session.Open(startup));
            }
        };
    }

    /// <summary>
    /// Brings the Edit menu's items in line with the selection and the history.
    /// </summary>
    /// <remarks>
    /// Refreshed as the menu opens rather than on every change, which is both
    /// cheaper and the only reliable moment: an item is only about to be read
    /// then. The context menu has always done this; the menu bar did not, so
    /// every item in it looked available whatever was selected, and undo and
    /// redo never said what they would undo.
    /// </remarks>
    private void RefreshEditMenu()
    {
        int selected = _session.Canvas.Selection.Count;
        bool anyGroup = _session.Canvas.Selection.Any(e => e is GroupElement { IsPageRoot: false });

        Enable("MenuUndo", _session.History.CanUndo);
        Enable("MenuRedo", _session.History.CanRedo);

        // Naming what will be undone is the difference between a safe click and
        // a guess.
        Header(
            "MenuUndo",
            _session.History.UndoDescription is { } undoing ? $"_Undo {undoing}" : "_Undo");
        Header(
            "MenuRedo",
            _session.History.RedoDescription is { } redoing ? $"_Redo {redoing}" : "_Redo");

        Enable("MenuCut", selected > 0);
        Enable("MenuCopy", selected > 0);
        Enable("MenuDelete", selected > 0);
        Enable("MenuGroup", selected >= 2);
        Enable("MenuUngroup", anyGroup);
        Enable("MenuBringToFront", selected > 0);
        Enable("MenuBringForward", selected > 0);
        Enable("MenuSendBackward", selected > 0);
        Enable("MenuSendToBack", selected > 0);
        Enable("MenuArrange", selected >= 2);

        void Enable(string name, bool enabled)
        {
            if (this.FindControl<MenuItem>(name) is { } item)
            {
                item.IsEnabled = enabled;
            }
        }

        void Header(string name, string header)
        {
            if (this.FindControl<MenuItem>(name) is { } item)
            {
                item.Header = header;
            }
        }
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
    private void TogglePanel(string dockableId, string menuName)
    {
        if (_layout.Factory is not { } factory
            || this.FindControl<MenuItem>(menuName) is not { } item)
        {
            return;
        }

        bool show = item.IsChecked;

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
    private void ResetLayout()
    {
        if (_layout.Factory is not { } factory)
        {
            return;
        }

        foreach ((string dockableId, string menuName) in Panels)
        {
            factory.RestoreDockable(dockableId);

            if (this.FindControl<MenuItem>(menuName) is { } item)
            {
                item.IsChecked = true;
            }
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

    /// <summary>The hideable panels, each with the menu item that toggles it.</summary>
    private static readonly (string DockableId, string MenuName)[] Panels =
    [
        ("ToolboxTool", "MenuPanelToolbox"),
        ("ElementsTool", "MenuPanelElements"),
        ("PropertiesTool", "MenuPanelProperties"),
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
        ("CanvasPane", 0.6),
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
    private static readonly double[] ZoomLadder =
        [0.25, 1.0 / 3, 0.5, 1.0, 2.0, 3.0, 4.0];

    private void StepZoom(bool up)
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

    private void SetZoom(double zoom)
    {
        _canvas.Zoom = zoom;

        ReportZoom();
    }

    /// <summary>Scales the gump so all of it fits the visible area.</summary>
    private void ZoomToFit()
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

        foreach ((string dockableId, string menuName) in Panels)
        {
            if (this.FindControl<MenuItem>(menuName) is { IsChecked: false })
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

        foreach ((string dockableId, string menuName) in Panels)
        {
            if (this.FindControl<MenuItem>(menuName) is { } item)
            {
                item.IsChecked = !layout.HiddenPanels.Contains(dockableId);
            }
        }

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

    private void WireMenus()
    {
        ClickAsync("MenuNew", NewAsync);
        ClickAsync("MenuExit", CloseAsync);
        Click("MenuUndo", () => { _session.History.Undo(); RefreshAll(); });
        Click("MenuRedo", () => { _session.History.Redo(); RefreshAll(); });
        Click("MenuSelectAll", () => { _session.Canvas.SelectAll(); RefreshAll(); });
        Click("MenuDelete", () => { _session.Canvas.DeleteSelection(); RefreshAll(); });
        Click("MenuGroup", GroupSelection);
        Click("MenuUngroup", UngroupSelection);
        Click("MenuAlignLeft", () => Arrange(() => _session.Canvas.Align(AlignMode.Left), "Aligned lefts."));
        Click("MenuAlignRight", () => Arrange(() => _session.Canvas.Align(AlignMode.Right), "Aligned rights."));
        Click("MenuAlignTop", () => Arrange(() => _session.Canvas.Align(AlignMode.Top), "Aligned tops."));
        Click("MenuAlignBottom", () => Arrange(() => _session.Canvas.Align(AlignMode.Bottom), "Aligned bottoms."));
        Click("MenuCentreH", () => Arrange(() => _session.Canvas.Align(AlignMode.CenterHorizontally), "Centred horizontally."));
        Click("MenuCentreV", () => Arrange(() => _session.Canvas.Align(AlignMode.CenterVertically), "Centred vertically."));
        Click("MenuSpaceH", () => Arrange(() => _session.Canvas.Distribute(DistributeMode.Horizontally), "Spaced horizontally.", 3));
        Click("MenuSpaceV", () => Arrange(() => _session.Canvas.Distribute(DistributeMode.Vertically), "Spaced vertically.", 3));
        Click("MenuBringToFront", () => Reorder(_session.Canvas.BringToFront, "front"));
        Click("MenuBringForward", () => Reorder(_session.Canvas.BringForward, "forward"));
        Click("MenuSendBackward", () => Reorder(_session.Canvas.SendBackward, "backward"));
        Click("MenuSendToBack", () => Reorder(_session.Canvas.SendToBack, "back"));
        Click("MenuAddPage", AddPage);
        Click("MenuShowPage0", ToggleSharedPage);
        Click("MenuPanelToolbox", () => TogglePanel("ToolboxTool", "MenuPanelToolbox"));
        Click("MenuPanelElements", () => TogglePanel("ElementsTool", "MenuPanelElements"));
        Click("MenuPanelProperties", () => TogglePanel("PropertiesTool", "MenuPanelProperties"));
        Click("MenuResetLayout", ResetLayout);
        Click("MenuZoomIn", () => StepZoom(up: true));
        Click("MenuZoomOut", () => StepZoom(up: false));
        Click("MenuZoomReset", () => SetZoom(1.0));
        Click("MenuZoomFit", ZoomToFit);
        Click("MenuShowGrid", ApplyGridSettings);
        Click("MenuSnapToGrid", ApplyGridSettings);
        ClickAsync("MenuGridSize", ChooseGridSizeAsync);
        ClickAsync("MenuGumpProperties", EditGumpPropertiesAsync);
        Click("MenuRemovePage", RemovePage);
        Click("MenuInsertPage", InsertPage);
        Click("MenuClearPage", ClearPage);

        ClickAsync("MenuCut", () => CopyAsync(cut: true));
        ClickAsync("MenuCopy", () => CopyAsync(cut: false));
        ClickAsync("MenuPaste", PasteAsync);

        ClickAsync("MenuOpen", OpenAsync);
        ClickAsync("MenuSave", () => SaveAsync(_session.DocumentPath));
        ClickAsync("MenuSaveAs", () => SaveAsync(null));
        ClickAsync("MenuImportLegacy", ImportLegacyAsync);
        ClickAsync("MenuImportLayout", ImportLayoutAsync);
        ClickAsync("MenuSetClient", () => ChooseClientAsync(force: true));
    }

    /// <summary>
    /// Registers the keyboard shortcuts the menu advertises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>InputGesture</c> on a <see cref="MenuItem"/> only <em>draws</em> the
    /// shortcut next to the item; it does not make the key do anything. Every
    /// gesture in the menu bar was therefore decorative — Ctrl+G, Ctrl+S, Ctrl+Z
    /// and the rest all did nothing. These bindings are what actually run them.
    /// </para>
    /// <para>
    /// They live on the window, and a window <c>KeyBinding</c> in Avalonia 12
    /// runs <em>even when the focused control has already marked the key
    /// handled</em> — a <see cref="TextBox"/> sets <c>Handled</c> for Ctrl+C,
    /// Ctrl+X, Ctrl+V, Ctrl+A, Ctrl+Z and Delete and is overridden anyway. So the
    /// ones a text box owns check focus themselves; see <see cref="IsEditingText"/>.
    /// </para>
    /// </remarks>
    private void BindShortcuts()
    {
        // These belong to whatever text box has the caret when one does.
        Bind("Ctrl+Z", () => { _session.History.Undo(); RefreshAll(); }, TextEditing.Yields);
        Bind("Ctrl+Y", () => { _session.History.Redo(); RefreshAll(); }, TextEditing.Yields);
        Bind("Ctrl+Shift+Z", () => { _session.History.Redo(); RefreshAll(); }, TextEditing.Yields);
        Bind("Ctrl+A", () => { _session.Canvas.SelectAll(); RefreshAll(); }, TextEditing.Yields);
        Bind("Delete", () => { _session.Canvas.DeleteSelection(); RefreshAll(); }, TextEditing.Yields);

        BindAsync("Ctrl+X", () => CopyAsync(cut: true), TextEditing.Yields);
        BindAsync("Ctrl+C", () => CopyAsync(cut: false), TextEditing.Yields);
        BindAsync("Ctrl+V", PasteAsync, TextEditing.Yields);

        // These mean the same thing wherever the keyboard happens to be.
        //
        // Through the same NewAsync the File menu uses, not straight to
        // NewDocument: the menu item asked before discarding unsaved work and
        // the shortcut did not, so the two paths for one action disagreed about
        // whether the document was safe.
        BindAsync("Ctrl+N", NewAsync);
        Bind("Ctrl+G", GroupSelection);
        Bind("Ctrl+Shift+G", UngroupSelection);
        Bind("Ctrl+Shift+Up", () => Reorder(_session.Canvas.BringToFront, "front"));
        Bind("Ctrl+Up", () => Reorder(_session.Canvas.BringForward, "forward"));
        Bind("Ctrl+Down", () => Reorder(_session.Canvas.SendBackward, "backward"));
        Bind("Ctrl+Shift+Down", () => Reorder(_session.Canvas.SendToBack, "back"));

        BindAsync("Ctrl+O", OpenAsync);
        BindAsync("Ctrl+S", () => SaveAsync(_session.DocumentPath));
        BindAsync("Ctrl+Shift+S", () => SaveAsync(null));

        // Zoom, bound to more gestures than the menu paints. The label has to
        // name one, but the key people press for "zoom in" is Ctrl and the '+'
        // key — a shifted OemPlus — and the numeric keypad has its own codes
        // entirely, so binding only the printed gesture leaves the shortcut
        // working for nobody who reaches for the obvious key.
        //
        // These are ordinary window bindings because InputGesture on a MenuItem
        // only draws the shortcut; it binds nothing. Every gesture in this menu
        // bar was decorative once, and these three were decorative again from
        // the day they were added until someone tried them.
        Bind("Ctrl+OemPlus", () => StepZoom(up: true));
        Bind("Ctrl+Shift+OemPlus", () => StepZoom(up: true));
        Bind("Ctrl+Add", () => StepZoom(up: true));

        Bind("Ctrl+OemMinus", () => StepZoom(up: false));
        Bind("Ctrl+Subtract", () => StepZoom(up: false));

        Bind("Ctrl+D0", () => SetZoom(1.0));
        Bind("Ctrl+NumPad0", () => SetZoom(1.0));
    }

    /// <summary>Whether a shortcut steps aside while text is being edited.</summary>
    private enum TextEditing
    {
        /// <summary>The shortcut means the same thing wherever focus is.</summary>
        Ignores,

        /// <summary>A focused text box owns this key, so the window does nothing.</summary>
        Yields,
    }

    /// <summary>
    /// Whether a text box currently has the keyboard.
    /// </summary>
    /// <remarks>
    /// A window <c>KeyBinding</c> in Avalonia 12 runs <em>even when the focused
    /// control has already marked the key handled</em> — verified against a
    /// headless <see cref="TextBox"/>, which sets <c>Handled</c> for Ctrl+C,
    /// Ctrl+X, Ctrl+V, Ctrl+A, Ctrl+Z and Delete and is overridden anyway. So the
    /// shortcuts have to check focus themselves; there is nothing to opt into
    /// that makes bubbling stop.
    /// </remarks>
    private bool IsEditingText() =>
        FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Registers one shortcut.
    /// </summary>
    /// <remarks>
    /// Yielding is expressed as <c>CanExecute</c>, not as an early return from the
    /// command. A <see cref="KeyBinding"/> marks the key handled whenever it
    /// executes, so a command that runs and does nothing still swallows the
    /// keystroke — which left Ctrl+C in a property field copying nothing at all
    /// instead of copying the element. Refusing to execute lets the key reach the
    /// text box that should have had it.
    /// </remarks>
    private void Bind(string gesture, Action action, TextEditing editing = TextEditing.Ignores) =>
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse(gesture),
            Command = new RelayCommand(() => Guarded(action), () => Allows(editing)),
        });

    private void BindAsync(
        string gesture, Func<Task> action, TextEditing editing = TextEditing.Ignores) =>
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse(gesture),
            Command = new AsyncRelayCommand(() => GuardedAsync(action), () => Allows(editing)),
        });

    /// <summary>Whether a shortcut may run, given where the keyboard is.</summary>
    private bool Allows(TextEditing editing) =>
        editing == TextEditing.Ignores || !IsEditingText();

    /// <summary>
    /// Builds the canvas context menu.
    /// </summary>
    /// <remarks>
    /// The original had no context menu on the design surface at all: every
    /// action meant a trip to the menu bar. Items are never rebuilt — only their
    /// enabled state is refreshed as the menu opens, so a disabled entry still
    /// shows what is possible and where to find it.
    /// </remarks>
    private ContextMenu BuildContextMenu()
    {
        MenuItem undo = Item("Undo", () => { _session.History.Undo(); RefreshAll(); });
        MenuItem redo = Item("Redo", () => { _session.History.Redo(); RefreshAll(); });
        MenuItem group = Item("Group selection", GroupSelection);
        MenuItem ungroup = Item("Ungroup", UngroupSelection);
        MenuItem front = Item("Bring to front", () => Reorder(_session.Canvas.BringToFront, "front"));
        MenuItem forward = Item("Bring forward", () => Reorder(_session.Canvas.BringForward, "forward"));
        MenuItem backward = Item("Send backward", () => Reorder(_session.Canvas.SendBackward, "backward"));
        MenuItem back = Item("Send to back", () => Reorder(_session.Canvas.SendToBack, "back"));
        MenuItem cut = Item("Cut", () => _ = GuardedAsync(() => CopyAsync(cut: true)));
        MenuItem copy = Item("Copy", () => _ = GuardedAsync(() => CopyAsync(cut: false)));
        MenuItem paste = Item("Paste", () => _ = GuardedAsync(PasteAsync));
        MenuItem delete = Item("Delete", () => { _session.Canvas.DeleteSelection(); RefreshAll(); });
        MenuItem moveToPage = new() { Header = "Move to page" };

        MenuItem arrange = new()
        {
            Header = "Arrange",
            ItemsSource = new List<object>
            {
                Item("Align lefts", () => Arrange(() => _session.Canvas.Align(AlignMode.Left), "Aligned lefts.")),
                Item("Align rights", () => Arrange(() => _session.Canvas.Align(AlignMode.Right), "Aligned rights.")),
                Item("Align tops", () => Arrange(() => _session.Canvas.Align(AlignMode.Top), "Aligned tops.")),
                Item("Align bottoms", () => Arrange(() => _session.Canvas.Align(AlignMode.Bottom), "Aligned bottoms.")),
                new Separator(),
                Item("Centre horizontally", () => Arrange(() => _session.Canvas.Align(AlignMode.CenterHorizontally), "Centred horizontally.")),
                Item("Centre vertically", () => Arrange(() => _session.Canvas.Align(AlignMode.CenterVertically), "Centred vertically.")),
                new Separator(),
                Item("Equalise horizontal spacing", () => Arrange(() => _session.Canvas.Distribute(DistributeMode.Horizontally), "Spaced horizontally.", 3)),
                Item("Equalise vertical spacing", () => Arrange(() => _session.Canvas.Distribute(DistributeMode.Vertically), "Spaced vertically.", 3)),
            },
        };

        ContextMenu menu = new()
        {
            ItemsSource = new List<object>
            {
                undo,
                redo,
                new Separator(),
                group,
                ungroup,
                new Separator(),
                front,
                forward,
                backward,
                back,
                new Separator(),
                cut,
                copy,
                paste,
                new Separator(),
                arrange,
                moveToPage,
                new Separator(),
                delete,
                new Separator(),
                Item("Select all", () => { _session.Canvas.SelectAll(); RefreshAll(); }),
                Item("Gump properties…", () => _ = GuardedAsync(EditGumpPropertiesAsync)),
            },
        };

        menu.Opening += (_, _) =>
        {
            int selected = _session.Canvas.Selection.Count;
            bool anyGroup = _session.Canvas.Selection.Any(e => e is GroupElement { IsPageRoot: false });

            undo.IsEnabled = _session.History.CanUndo;
            redo.IsEnabled = _session.History.CanRedo;

            // Naming what will be undone is the difference between a safe click
            // and a guess.
            undo.Header = _session.History.UndoDescription is { } undoing ? $"Undo {undoing}" : "Undo";
            redo.Header = _session.History.RedoDescription is { } redoing ? $"Redo {redoing}" : "Redo";

            group.IsEnabled = selected >= 2;
            ungroup.IsEnabled = anyGroup;
            front.IsEnabled = forward.IsEnabled = backward.IsEnabled = back.IsEnabled = selected > 0;
            delete.IsEnabled = selected > 0;
            cut.IsEnabled = copy.IsEnabled = selected > 0;
            arrange.IsEnabled = selected >= 2;

            FillMoveToPageMenu(moveToPage);
        };

        return menu;

        MenuItem Item(string header, Action action)
        {
            MenuItem item = new() { Header = header };

            item.Click += (_, _) => Guarded(action);

            return item;
        }
    }

    private void Click(string name, Action action)
    {
        if (this.FindControl<MenuItem>(name) is { } item)
        {
            item.Click += (_, _) => Guarded(action);
        }
    }

    private void ClickAsync(string name, Func<Task> action)
    {
        if (this.FindControl<MenuItem>(name) is { } item)
        {
            item.Click += async (_, _) => await GuardedAsync(action).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Runs an action, reporting failures in the status bar.
    /// </summary>
    /// <remarks>
    /// The original showed a modal message box from twenty-odd catch blocks and
    /// carried on regardless. A status line is less intrusive and does not
    /// interrupt what the user was doing.
    /// </remarks>
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private async Task GuardedAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

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

            button.Click += (_, _) => AddElement(create());
            buttons.Add(button);
        }

        _toolbox.ItemsSource = buttons;
    }

    private void AddElement(Element element)
    {
        _session.History.Push(new AddElementCommand(_session.ActivePage.Root, element));
        _session.MeasureActivePage();
        _session.Canvas.Select(element);

        RefreshAll();
    }

    private void GroupSelection()
    {
        if (_session.Canvas.Group() is null)
        {
            SetStatus("Select at least two elements to group.");

            return;
        }

        RefreshAll();
    }

    private void UngroupSelection()
    {
        int dissolved = _session.Canvas.Ungroup();

        if (dissolved == 0)
        {
            SetStatus("Select a group to ungroup.");

            return;
        }

        RefreshAll();
        SetStatus(dissolved == 1 ? "Ungrouped." : $"Ungrouped {dissolved} groups.");
    }

    /// <summary>
    /// Puts the selection on the clipboard, optionally removing it.
    /// </summary>
    /// <remarks>
    /// As XML text rather than a serialised object graph. It survives between
    /// instances, can be inspected by pasting it anywhere, and cannot carry
    /// anything executable — which the original's <c>BinaryFormatter</c> payload
    /// could, and which is a large part of why that format had to go.
    /// </remarks>
    private async Task CopyAsync(bool cut)
    {
        if (_session.Canvas.Selection.Count == 0)
        {
            SetStatus("Select something to copy.");

            return;
        }

        if (Clipboard is not { } clipboard)
        {
            SetStatus("No clipboard is available.");

            return;
        }

        int count = _session.Canvas.Selection.Count;

        // Avalonia 12 replaced SetTextAsync with a data-transfer object that can
        // carry several representations; text is the only one we offer.
        //
        // Deliberately not disposed: the clipboard takes ownership and may call
        // back into it to serve the data, so releasing it here would be handing
        // the system a payload we had already torn down.
#pragma warning disable CA2000
        DataTransfer payload = new();
#pragma warning restore CA2000

        payload.Add(DataTransferItem.CreateText(GumpXmlSerializer.ToFragment(_session.Canvas.Selection)));

        await clipboard.SetDataAsync(payload).ConfigureAwait(true);

        // Hands the data to the OS so it outlives this process. Windows only;
        // elsewhere the clipboard is served by the owning application anyway and
        // the call does nothing.
        await clipboard.FlushAsync().ConfigureAwait(true);

        if (cut)
        {
            _session.Canvas.DeleteSelection();
        }

        RefreshAll();
        SetStatus(string.Create(
            CultureInfo.InvariantCulture,
            $"{(cut ? "Cut" : "Copied")} {count} element(s)."));
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clipboard)
        {
            SetStatus("No clipboard is available.");

            return;
        }

        // The transfer object owns platform resources and must be disposed.
        using IAsyncDataTransfer? transfer = await clipboard.TryGetDataAsync().ConfigureAwait(true);

        string? text = transfer is null
            ? null
            : await transfer.TryGetTextAsync().ConfigureAwait(true);

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

        RefreshAll();
        SetStatus(string.Create(CultureInfo.InvariantCulture, $"Pasted {pasted} element(s)."));
    }

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

        RefreshAll();
        SetStatus(done);
    }

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

        RefreshAll();
        SetStatus($"Moved {where}.");
    }

    /// <summary>
    /// Turns the always-visible page 0 backdrop on and off.
    /// </summary>
    /// <remarks>
    /// It is on by default because that is what the player sees; hiding it helps
    /// when a full-page background on page 0 obscures the page being edited.
    /// </remarks>
    private void ToggleSharedPage()
    {
        _canvas.ShowSharedPage = this.FindControl<MenuItem>("MenuShowPage0")?.IsChecked ?? true;

        _canvas.InvalidateVisual();
    }

    /// <summary>Reads the grid toggles back into the editing session.</summary>
    private void ApplyGridSettings()
    {
        _session.Canvas.Grid.Visible = this.FindControl<MenuItem>("MenuShowGrid")?.IsChecked ?? false;
        _session.Canvas.Grid.SnapEnabled = this.FindControl<MenuItem>("MenuSnapToGrid")?.IsChecked ?? false;

        SaveSettings();

        _canvas.InvalidateVisual();
    }

    private async Task ChooseGridSizeAsync()
    {
        GridSizeWindow dialog = new(_session.Canvas.Grid.Width, _session.Canvas.Grid.Height);

        await dialog.ShowDialog(this).ConfigureAwait(true);

        if (dialog.Result is not { } size)
        {
            return;
        }

        _session.Canvas.Grid.Width = size.Width;
        _session.Canvas.Grid.Height = size.Height;

        SaveSettings();

        _canvas.InvalidateVisual();
        SetStatus($"Grid set to {size.Width} x {size.Height}.");
    }

    private async Task EditGumpPropertiesAsync()
    {
        GumpPropertiesWindow dialog = new(_session.Document.Properties);

        await dialog.ShowDialog(this).ConfigureAwait(true);

        if (dialog.Result is not { } properties)
        {
            return;
        }

        _session.History.Push(new SetGumpPropertiesCommand(_session.Document, properties));

        RefreshAll();
        SetStatus("Gump properties updated.");
    }

    private void SaveSettings()
    {
        AppSettings settings = _session.Settings;

        settings.GridWidth = _session.Canvas.Grid.Width;
        settings.GridHeight = _session.Canvas.Grid.Height;
        settings.GridVisible = _session.Canvas.Grid.Visible;
        settings.GridSnap = _session.Canvas.Grid.SnapEnabled;

        settings.Save();
    }

    private void LoadGridSettings()
    {
        AppSettings settings = _session.Settings;

        _session.Canvas.Grid.Width = settings.GridWidth;
        _session.Canvas.Grid.Height = settings.GridHeight;
        _session.Canvas.Grid.Visible = settings.GridVisible;
        _session.Canvas.Grid.SnapEnabled = settings.GridSnap;

        if (this.FindControl<MenuItem>("MenuShowGrid") is { } show)
        {
            show.IsChecked = settings.GridVisible;
        }

        if (this.FindControl<MenuItem>("MenuSnapToGrid") is { } snap)
        {
            snap.IsChecked = settings.GridSnap;
        }
    }

    private void AddPage()
    {
        AddPageCommand command = new(_session.Document);

        _session.Apply(command);

        _session.ActivePageIndex = _session.Document.PageCount - 1;

        RefreshAll();
        SetStatus($"Added {command.Page.Name}.");
    }

    private void InsertPage()
    {
        int index = _session.ActivePageIndex;
        InsertPageCommand command = new(_session.Document, index);

        _session.Apply(command);

        _session.ActivePageIndex = index;

        RefreshAll();
        SetStatus($"Inserted a page at {index}.");
    }

    /// <summary>
    /// Removes the active page.
    /// </summary>
    /// <remarks>
    /// Through the undo history, unlike the original and unlike this editor's
    /// first version: removing a page takes every element on it, and that was
    /// the one action here with no way back.
    /// </remarks>
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

        RefreshAll();
        SetStatus($"{command.Description}. Undo brings it back with its elements.");
    }

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

        RefreshAll();
        SetStatus(command.Description + ".");
    }

    private void RefreshAll()
    {
        RefreshPages();
        RefreshElementList();
        RefreshProperties();

        _canvas.InvalidateVisual();

        RefreshTitle();

        SetStatus(
            $"{_session.Document.PageCount} page(s), "
            + $"{_session.ActivePage.Root.Children.Count} element(s) on page {_session.ActivePageIndex}"
            + (_session.DocumentPath is { } path ? $" — {Path.GetFileName(path)}" : string.Empty));
    }

    private void RefreshPages()
    {
        _pageTabs.Children.Clear();

        for (int i = 0; i < _session.Document.PageCount; i++)
        {
            int index = i;

            Button tab = new()
            {
                Content = $"Page {i}",
                Background = i == _session.ActivePageIndex
                    ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42))
                    : Brushes.Transparent,
            };

            tab.Click += (_, _) =>
            {
                _session.ActivePageIndex = index;
                RefreshAll();
            };

            _pageTabs.Children.Add(tab);
        }
    }

    /// <summary>
    /// Syncs the element list with the page and the current selection.
    /// </summary>
    /// <remarks>
    /// The item source is rebuilt only when the page's contents actually change.
    /// Reassigning it unconditionally made the list reset its own selection on
    /// every refresh, and the resulting event raced the suppression flag — the
    /// visible symptom was a selected element whose properties never appeared.
    /// </remarks>
    private void RefreshElementList()
    {
        _suppressSelectionSync = true;

        try
        {
            IReadOnlyList<Element> children = _session.ActivePage.Root.Children;

            if (_listedElements is null || !_listedElements.SequenceEqual(children))
            {
                _listedElements = [.. children];
                _elementList.ItemsSource = _listedElements;
            }

            _elementList.SelectedItem =
                _session.Canvas.Selection.Count == 1 ? _session.Canvas.Selection[0] : null;
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private bool _closeConfirmed;

    // What the inspector is currently showing, so a mid-drag update can tell
    // that the panel's shape is still right for the selection.
    private Element? _shownElement;
    private int _shownSelectionCount = -1;

    // One per editor on screen, each pushing its element's current value back
    // into the control without rebuilding it.
    private readonly List<Action> _propertyValueRefreshers = [];

    private void OnElementListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            return;
        }

        _session.Canvas.Select(_elementList.SelectedItem as Element);

        RefreshProperties();
        _canvas.InvalidateVisual();
    }

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

    private void RefreshSelection()
    {
        RefreshElementList();
        RefreshProperties();
    }

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

        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        Control editor = row.Kind switch
        {
            PropertyEditorKind.Boolean => BuildBooleanEditor(element, row),
            PropertyEditorKind.Choice => BuildChoiceEditor(element, row),
            PropertyEditorKind.GumpId => BuildBrowsableIdEditor(element, row, ArtBrowserKind.Gump),
            PropertyEditorKind.ItemId => BuildBrowsableIdEditor(element, row, ArtBrowserKind.Item),
            PropertyEditorKind.Color => BuildColorEditor(element, row),
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
    /// Drops the cached picker rows, so a different client rebuilds them.
    /// </summary>
    /// <remarks>
    /// They are built from the loaded client's hue table and fonts, so pointing
    /// at another installation would otherwise keep showing the previous one's.
    /// </remarks>
    private void ForgetPickerEntries()
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
        RefreshElementList();
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
                await GuardedAsync(() => ExportAsync(captured)).ConfigureAwait(true);

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

            item.Click += (_, _) => Guarded(() => MoveSelectionToPage(target));

            targets.Add(item);
        }

        parent.ItemsSource = targets;

        // A single-page document has nowhere to move to, and nothing selected has
        // nothing to move.
        parent.IsEnabled = targets.Count > 0 && _session.Canvas.Selection.Count > 0;
    }

    /// <summary>
    /// Moves the selection to another page and follows it there.
    /// </summary>
    /// <remarks>
    /// Following is deliberate. Pages other than 0 are mutually exclusive, so
    /// moving an element to one while looking at another makes it vanish, which
    /// reads exactly like a delete. Switching to the destination shows it arrive.
    /// </remarks>
    private void MoveSelectionToPage(int index)
    {
        int moved = _session.Canvas.MoveSelectionToPage(_session.Document.Pages[index]);

        if (moved == 0)
        {
            SetStatus("Select something to move.");

            return;
        }

        _session.ActivePageIndex = index;

        RefreshAll();
        SetStatus(moved == 1
            ? string.Create(CultureInfo.InvariantCulture, $"Moved to page {index}.")
            : string.Create(CultureInfo.InvariantCulture, $"Moved {moved} elements to page {index}."));
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync("opening another").ConfigureAwait(true))
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open gump",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("GumpStudio document") { Patterns = ["*.gump"] }],
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return;
        }

        _session.Open(files[0].Path.LocalPath);
        RefreshAll();
    }

    /// <summary>
    /// Imports a 1.8 <c>.gump</c> or <c>.gumpling</c>.
    /// </summary>
    /// <remarks>
    /// The two are not the same import: a gump replaces the document, while a
    /// gumpling is a single group added to the page that is open. Only the
    /// first discards anything, so only the first asks — and it asks after the
    /// file has been chosen, so cancelling the picker costs no question.
    /// </remarks>
    private async Task ImportLegacyAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a GumpStudio 1.8 file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("GumpStudio 1.8") { Patterns = ["*.gump", "*.gumpling"] },
            ],
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return;
        }

        string path = files[0].Path.LocalPath;

        if (Path.GetExtension(path).Equals(".gumpling", StringComparison.OrdinalIgnoreCase))
        {
            GroupElement group = _session.ImportGumpling(path);

            _session.Canvas.Select(group);
            _session.MeasureActivePage();

            RefreshAll();
            SetStatus($"Added {group.Name} to page {_session.ActivePageIndex}.");

            return;
        }

        if (!await ConfirmDiscardAsync("importing").ConfigureAwait(true))
        {
            return;
        }

        _session.ImportLegacy(path);
        RefreshAll();

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
    private async Task ImportLayoutAsync()
    {
        if (!await ConfirmDiscardAsync("importing").ConfigureAwait(true))
        {
            return;
        }

        ImportLayoutWindow dialog = new();

        await dialog.ShowDialog(this).ConfigureAwait(true);

        if (dialog.Result is not { } document)
        {
            return;
        }

        _session.AdoptImported(document);
        RefreshAll();

        int elements = document.Pages.Sum(page => page.Leaves().Count());
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Imported {elements} elements across {document.PageCount} pages.");

        SetStatus(dialog.Warnings.Count == 0
            ? summary + " Save it to keep the document."
            : $"{summary} {dialog.Warnings.Count} line(s) were skipped — see the layout for what was lost.");
    }

    private async Task SaveAsync(string? path)
    {
        if (path is null)
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save gump",
                DefaultExtension = "gump",
                SuggestedFileName = "gump.gump",
            }).ConfigureAwait(true);

            if (file is null)
            {
                return;
            }

            path = file.Path.LocalPath;
        }

        _session.Save(path);
        RefreshAll();
    }

    /// <summary>
    /// Picks a file, asks how to export, then writes it.
    /// </summary>
    /// <remarks>
    /// The file comes first because the gump name defaults to its name. Asking
    /// for the options first would leave nothing to derive that from, and would
    /// quietly change the default name of every export.
    /// </remarks>
    private async Task ExportAsync(IGumpConverter converter)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {converter.DisplayName}",
            DefaultExtension = converter.FileExtension.TrimStart('.'),
            SuggestedFileName = "gump" + converter.FileExtension,
        }).ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        AppSettings settings = _session.Settings;

        GumpExportOptions defaults = new()
        {
            GumpName = Path.GetFileNameWithoutExtension(file.Name),
            Dialect = settings.ExportDialectFor(converter.Id),
        };

        ExportOptionsWindow dialog = new(
            $"Export as {converter.DisplayName}", converter.Dialects, defaults);

        await dialog.ShowDialog(this).ConfigureAwait(true);

        if (dialog.Result is not { } options)
        {
            return;
        }

        // Remembered per converter, so choosing the layout-string form once does
        // not make it the default for every other target too.
        settings.SetExportDialect(converter.Id, options.Dialect);
        settings.Save();

        string script = converter.Export(_session.Document, options);

        await File.WriteAllTextAsync(file.Path.LocalPath, script).ConfigureAwait(true);

        SetStatus($"Exported to {file.Name}.");
    }

    /// <summary>Prompts for a client folder when none is configured yet.</summary>
    private async Task EnsureClientAsync()
    {
        string? remembered = _session.Settings.ClientPath;

        if (remembered is not null)
        {
            SetStatus("Loading client art…");

            IReadOnlyList<string> missing =
                await _session.OpenClientAsync(remembered).ConfigureAwait(true);

            if (missing.Count == 0)
            {
                ForgetPickerEntries();

                RefreshAll();

                return;
            }
        }

        await ChooseClientAsync(force: false).ConfigureAwait(true);
    }

    private async Task ChooseClientAsync(bool force)
    {
        if (!force)
        {
            SetStatus("No Ultima Online client configured — choose one to see art.");
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select your Ultima Online folder",
                AllowMultiple = false,
            }).ConfigureAwait(true);

        if (folders.Count == 0)
        {
            return;
        }

        string path = folders[0].Path.LocalPath;

        SetStatus("Loading client art…");

        IReadOnlyList<string> missing = await _session.OpenClientAsync(path).ConfigureAwait(true);

        ForgetPickerEntries();

        if (missing.Count > 0)
        {
            // Naming what is absent beats the original's crash inside a static
            // constructor with no indication of the cause.
            SetStatus($"That folder is missing: {string.Join(", ", missing)}", isError: true);

            return;
        }

        _session.Settings.ClientPath = path;
        _session.Settings.Save();

        _session.MeasureActivePage();
        RefreshAll();
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.Foreground = isError ? Brushes.IndianRed : Brushes.Silver;
    }

    /// <summary>
    /// Asks before an action that would throw away unsaved work.
    /// </summary>
    /// <param name="action">
    /// What is about to happen, as a gerund, for the question's wording.
    /// </param>
    /// <returns>False when the user chose to keep what they have.</returns>
    /// <remarks>
    /// Nothing asked before this existed: New, Open, both importers and Exit
    /// all discarded the document silently.
    /// </remarks>
    private async Task<bool> ConfirmDiscardAsync(string action)
    {
        if (!_session.IsModified)
        {
            return true;
        }

        string name = _session.DocumentPath is { } path
            ? Path.GetFileName(path)
            : "This gump";

        ConfirmWindow dialog = new(
            $"{name} has unsaved changes. Discard them before {action}?");

        await dialog.ShowDialog(this).ConfigureAwait(true);

        return dialog.Confirmed;
    }

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync("starting a new one").ConfigureAwait(true))
        {
            return;
        }

        _session.NewDocument();
    }

    /// <summary>
    /// Closes the window, asking first.
    /// </summary>
    /// <remarks>
    /// <see cref="OnClosing"/> asks as well, for the window's own close button.
    /// This path cancels before <see cref="Window.Close()"/> is reached so the
    /// close is never started, which keeps the two from asking twice.
    /// </remarks>
    private async Task CloseAsync()
    {
        if (!await ConfirmDiscardAsync("closing").ConfigureAwait(true))
        {
            return;
        }

        _closeConfirmed = true;

        Close();
    }

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
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (!await ConfirmDiscardAsync("closing").ConfigureAwait(true))
        {
            return;
        }

        _closeConfirmed = true;

        Close();
    }

    /// <summary>Shows the document name and whether it has unsaved changes.</summary>
    private void RefreshTitle()
    {
        string name = _session.DocumentPath is { } path
            ? Path.GetFileName(path)
            : "Untitled";

        Title = _session.IsModified
            ? $"GumpStudio — {name}*"
            : $"GumpStudio — {name}";
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
