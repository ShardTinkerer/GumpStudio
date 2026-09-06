using System.Globalization;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.Input;

using GumpStudio.App.Controls;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Serialization;
using GumpStudio.Plugins;

namespace GumpStudio.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly EditorSession _session = new();

    private readonly GumpCanvas _canvas = null!;
    private readonly ListBox _elementList = null!;
    private readonly StackPanel _propertyPanel = null!;
    private readonly StackPanel _pageTabs = null!;
    private readonly ItemsControl _toolbox = null!;
    private readonly TextBlock _status = null!;
    private readonly MenuItem _exportMenu = null!;
    private readonly MenuItem _pluginsMenu = null!;
    private readonly MenuItem _moveToPageMenu = null!;

    private bool _suppressSelectionSync;
    private List<Element>? _listedElements;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _canvas = this.FindControl<GumpCanvas>("Canvas")!;
        _elementList = this.FindControl<ListBox>("ElementList")!;
        _propertyPanel = this.FindControl<StackPanel>("PropertyPanel")!;
        _pageTabs = this.FindControl<StackPanel>("PageTabs")!;
        _toolbox = this.FindControl<ItemsControl>("Toolbox")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        _exportMenu = this.FindControl<MenuItem>("MenuExport")!;
        _pluginsMenu = this.FindControl<MenuItem>("MenuPlugins")!;
        _moveToPageMenu = this.FindControl<MenuItem>("MenuMoveToPage")!;

        // Filled as the Page menu opens rather than kept in sync: a disabled
        // item never opens its own submenu, so the enabled state has to be
        // settled one level up.
        this.FindControl<MenuItem>("MenuPageRoot")!.SubmenuOpened +=
            (_, _) => FillMoveToPageMenu(_moveToPageMenu);

        _canvas.Session = _session;
        _canvas.InteractionChanged += (_, _) => RefreshSelection();

        _elementList.SelectionChanged += OnElementListSelectionChanged;

        _session.DocumentChanged += (_, _) => { _listedElements = null; RefreshAll(); };
        _session.PageChanged += (_, _) => { _listedElements = null; RefreshAll(); };
        _session.Notified += (_, n) => SetStatus(n.Message);

        BuildToolbox();
        WireMenus();
        BindShortcuts();

        // One menu instance per host: a ContextMenu belongs to a single control.
        _canvas.ContextMenu = BuildContextMenu();
        _elementList.ContextMenu = BuildContextMenu();

        LoadGridSettings();

        LoadExternalPlugins();
        BuildExportMenu();
        BuildPluginMenu();

        RefreshAll();

        Opened += async (_, _) =>
        {
            await EnsureClientAsync().ConfigureAwait(true);

            if (Program.StartupDocument is { } startup)
            {
                Guarded(() => _session.Open(startup));
            }
        };
    }

    private void WireMenus()
    {
        Click("MenuNew", () => _session.NewDocument());
        Click("MenuExit", Close);
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
        Click("MenuAddPage", () => { _session.Document.AddPage(); RefreshAll(); });
        Click("MenuShowPage0", ToggleSharedPage);
        Click("MenuShowGrid", ApplyGridSettings);
        Click("MenuSnapToGrid", ApplyGridSettings);
        ClickAsync("MenuGridSize", ChooseGridSizeAsync);
        ClickAsync("MenuGumpProperties", EditGumpPropertiesAsync);
        Click("MenuRemovePage", RemovePage);

        ClickAsync("MenuCut", () => CopyAsync(cut: true));
        ClickAsync("MenuCopy", () => CopyAsync(cut: false));
        ClickAsync("MenuPaste", PasteAsync);

        ClickAsync("MenuOpen", OpenAsync);
        ClickAsync("MenuSave", () => SaveAsync(_session.DocumentPath));
        ClickAsync("MenuSaveAs", () => SaveAsync(null));
        ClickAsync("MenuImportLegacy", ImportLegacyAsync);
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
    /// They live on the window, so a control that has already handled the key —
    /// a text box swallowing Ctrl+A or Delete while the caret is in it — stops
    /// the event before it arrives here.
    /// </para>
    /// </remarks>
    private void BindShortcuts()
    {
        Bind("Ctrl+N", () => _session.NewDocument());
        Bind("Ctrl+Z", () => { _session.History.Undo(); RefreshAll(); });
        Bind("Ctrl+Y", () => { _session.History.Redo(); RefreshAll(); });
        Bind("Ctrl+Shift+Z", () => { _session.History.Redo(); RefreshAll(); });
        Bind("Ctrl+A", () => { _session.Canvas.SelectAll(); RefreshAll(); });
        Bind("Delete", () => { _session.Canvas.DeleteSelection(); RefreshAll(); });
        Bind("Ctrl+G", GroupSelection);
        Bind("Ctrl+Shift+G", UngroupSelection);
        Bind("Ctrl+Shift+Up", () => Reorder(_session.Canvas.BringToFront, "front"));
        Bind("Ctrl+Up", () => Reorder(_session.Canvas.BringForward, "forward"));
        Bind("Ctrl+Down", () => Reorder(_session.Canvas.SendBackward, "backward"));
        Bind("Ctrl+Shift+Down", () => Reorder(_session.Canvas.SendToBack, "back"));

        BindAsync("Ctrl+X", () => CopyAsync(cut: true));
        BindAsync("Ctrl+C", () => CopyAsync(cut: false));
        BindAsync("Ctrl+V", PasteAsync);

        BindAsync("Ctrl+O", OpenAsync);
        BindAsync("Ctrl+S", () => SaveAsync(_session.DocumentPath));
        BindAsync("Ctrl+Shift+S", () => SaveAsync(null));
    }

    private void Bind(string gesture, Action action) =>
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse(gesture),
            Command = new RelayCommand(() => Guarded(action)),
        });

    private void BindAsync(string gesture, Func<Task> action) =>
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse(gesture),
            Command = new AsyncRelayCommand(() => GuardedAsync(action)),
        });

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
        AppSettings settings = AppSettings.Load();

        settings.GridWidth = _session.Canvas.Grid.Width;
        settings.GridHeight = _session.Canvas.Grid.Height;
        settings.GridVisible = _session.Canvas.Grid.Visible;
        settings.GridSnap = _session.Canvas.Grid.SnapEnabled;

        AppSettings.Save(settings);
    }

    private void LoadGridSettings()
    {
        AppSettings settings = AppSettings.Load();

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

    private void RemovePage()
    {
        if (_session.Document.PageCount == 1)
        {
            SetStatus("A gump must keep at least one page.");

            return;
        }

        _session.ActivePageIndex = _session.Document.RemovePage(_session.ActivePageIndex);
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshPages();
        RefreshElementList();
        RefreshProperties();

        _canvas.InvalidateVisual();

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

    private void RefreshSelection()
    {
        RefreshElementList();
        RefreshProperties();
    }

    private void RefreshProperties()
    {
        _propertyPanel.Children.Clear();

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

            ArtBrowserWindow browser = new(_session.Data, kind, current);

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

    private void BuildExportMenu()
    {
        List<MenuItem> items = [];

        foreach (IGumpExporter exporter in _session.Exporters)
        {
            MenuItem item = new() { Header = exporter.DisplayName };

            item.Click += async (_, _) => await GuardedAsync(() => ExportAsync(exporter)).ConfigureAwait(true);
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

    private void BuildPluginMenu()
    {
        List<MenuItem> items = [];

        foreach (MenuCommandDescriptor descriptor in _session.MenuCommands)
        {
            MenuItem item = new() { Header = descriptor.Title };

            item.Click += (_, _) => Guarded(descriptor.Execute);
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = "(no plugins loaded)", IsEnabled = false });
        }

        _pluginsMenu.ItemsSource = items;
    }

    private void LoadExternalPlugins()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Plugins");

        IReadOnlyList<DiscoveredPlugin> discovered = _session.LoadPlugins(directory);

        foreach (DiscoveredPlugin failed in discovered.Where(p => !p.IsUsable))
        {
            SetStatus($"{failed.Info.Name} could not load: {failed.Error}", isError: true);
        }
    }

    private async Task OpenAsync()
    {
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

        _session.ImportLegacy(files[0].Path.LocalPath);
        RefreshAll();

        SetStatus("Imported. Save it to store the document in the current format.");
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

    private async Task ExportAsync(IGumpExporter exporter)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {exporter.DisplayName}",
            DefaultExtension = exporter.FileExtension.TrimStart('.'),
            SuggestedFileName = "gump" + exporter.FileExtension,
        }).ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        string script = exporter.Export(_session.Document, new GumpExportOptions
        {
            GumpName = Path.GetFileNameWithoutExtension(file.Name),
        });

        await File.WriteAllTextAsync(file.Path.LocalPath, script).ConfigureAwait(true);

        SetStatus($"Exported to {file.Name}.");
    }

    /// <summary>Prompts for a client folder when none is configured yet.</summary>
    private async Task EnsureClientAsync()
    {
        string? remembered = AppSettings.Load().ClientPath;

        if (remembered is not null && _session.OpenClient(remembered).Count == 0)
        {
            RefreshAll();

            return;
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
        IReadOnlyList<string> missing = _session.OpenClient(path);

        if (missing.Count > 0)
        {
            // Naming what is absent beats the original's crash inside a static
            // constructor with no indication of the cause.
            SetStatus($"That folder is missing: {string.Join(", ", missing)}", isError: true);

            return;
        }

        AppSettings.Save(new AppSettings { ClientPath = path });

        _session.MeasureActivePage();
        RefreshAll();
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.Foreground = isError ? Brushes.IndianRed : Brushes.Silver;
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
