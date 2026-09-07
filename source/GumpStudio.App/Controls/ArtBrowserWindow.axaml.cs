using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

using GumpStudio.Uo;
using GumpStudio.Uo.Primitives;

using SkiaSharp;

namespace GumpStudio.App.Controls;

/// <summary>Which art library a browser is showing.</summary>
public enum ArtBrowserKind
{
    Gump,
    Item,
}

/// <summary>
/// A picker for gump and item art, as a list of rows or a grid of tiles.
/// </summary>
/// <remarks>
/// <para>
/// The original had browsers like this and they are what make an id field
/// usable — nobody remembers that 5054 is a stone frame. This one lists only
/// ids that actually have art, filters by id or tile name, and previews the
/// selection at full size.
/// </para>
/// <para>
/// The id list is built with the cheap existence probe rather than by decoding,
/// because on a UOP client resolving a gump's dimensions costs a full inflate
/// plus Burrows-Wheeler pass. Thumbnails are decoded only for the tiles the
/// panel actually realises, and off the UI thread.
/// </para>
/// </remarks>
public sealed partial class ArtBrowserWindow : Window, IDisposable
{
    /// <summary>Smallest and largest thumbnail the size control offers.</summary>
    private const int MinTileSize = 32;
    private const int MaxTileSize = 320;

    /// <summary>
    /// Roughly how much memory the thumbnail cache may hold.
    /// </summary>
    /// <remarks>
    /// A count would be the wrong unit: at 32 pixels a thousand thumbnails are
    /// four megabytes, and at 320 they are four hundred.
    /// </remarks>
    private const int ThumbnailCacheBudget = 24 * 1024 * 1024;

    private readonly List<ArtEntry> _all = [];
    private readonly UoDataContext? _data;
    private readonly ArtBrowserKind _kind;
    private readonly AppSettings _settings;

    private readonly TextBox _filter = null!;
    private readonly ListBox _results = null!;
    private readonly ToggleButton _galleryToggle = null!;
    private readonly NumericUpDown _tileSizeBox = null!;
    private readonly TextBlock _count = null!;
    private readonly TextBlock _previewTitle = null!;
    private readonly TextBlock _previewDetail = null!;
    private readonly Image _preview = null!;
    private readonly Grid _panes = null!;

    /// <summary>
    /// Decoded thumbnails, so scrolling back over art costs nothing.
    /// </summary>
    /// <remarks>
    /// Only ever touched from the UI thread, which is why it needs no lock.
    /// Entries are dropped rather than disposed: an evicted bitmap may still be
    /// on screen, and disposing one out from under a realised <see cref="Image"/>
    /// tears a hole in the panel.
    /// </remarks>
    private readonly Dictionary<int, Bitmap> _thumbnails = [];

    // Recency order for the thumbnail cache, most recent last, with each id's
    // node beside it so a touch does not scan the list.
    private readonly LinkedList<int> _thumbnailOrder = new();
    private readonly Dictionary<int, LinkedListNode<int>> _thumbnailNodes = [];

    // Bitmaps a tile-size change invalidated, still referenced by tiles on
    // screen, released when the window closes.
    private readonly List<Bitmap> _retired = [];

    private DispatcherTimer? _filterDebounce;

    /// <summary>
    /// How many thumbnails may decode at once.
    /// </summary>
    /// <remarks>
    /// Decoding used to be strictly serial, for a good reason at the time: the
    /// UOP reader memoised exactly one decompressed entry, and reading a gump
    /// takes two passes over it — one for its dimensions and one for its pixels.
    /// Running those in parallel meant every thread evicted every other one's
    /// memo, so each gump inflated twice instead of once on a flooded pool.
    ///
    /// That memo is now a byte-budgeted window of recently decoded payloads, so
    /// concurrent readers no longer fight over a single slot. A small bound
    /// rather than none: a gallery realises a whole screenful of tiles at once,
    /// around seventy, and there is nothing to gain from seventy decodes in
    /// flight for a screen that holds seventy images.
    /// </remarks>
    private const int ConcurrentDecodes = 3;

    /// <summary>
    /// Requests waiting for a decode slot, and how many are using one.
    /// </summary>
    /// <remarks>
    /// A queue and a counter on the UI thread rather than a
    /// <see cref="SemaphoreSlim"/>, which cost a window that would not close.
    /// Disposing a semaphore while anything is still waiting on it makes the
    /// pending <c>Release</c> calls throw <see cref="ObjectDisposedException"/>
    /// from inside a <c>finally</c>; that faulted the decode task, whose awaiter
    /// resumed on the dispatcher and rethrew there. An unhandled exception in a
    /// dispatcher continuation takes the dispatcher with it, so closing a
    /// gallery that had been scrolled left the main window unable to close at
    /// all.
    ///
    /// Nothing here needs disposing, and the bookkeeping is confined to the UI
    /// thread like the cache it feeds.
    /// </remarks>
    private readonly Queue<(int Id, Image Target)> _pending = new();

    private int _running;
    private bool _disposed;

    /// <summary>
    /// Decodes already running, by id.
    /// </summary>
    /// <remarks>
    /// Concurrency makes deduplication a correctness matter rather than an
    /// efficiency one. Two tiles can ask for the same id, and a second decode
    /// would hand <see cref="Remember"/> a replacement for a bitmap already
    /// assigned as an <c>Image.Source</c>. Sharing the task means one decode and
    /// one cache entry per id.
    ///
    /// Touched only on the UI thread, like the cache it feeds.
    /// </remarks>
    private readonly Dictionary<int, Task<Bitmap?>> _inFlight = [];

    /// <summary>
    /// Cancels decoding for a visible set that no longer exists.
    /// </summary>
    /// <remarks>
    /// Reset when the list is rebound — a filter change, a view-mode switch, a
    /// tile-size change — and cancelled when the window closes. Deliberately
    /// <em>not</em> on scrolling: the visible set changes continuously there, and
    /// cancelling would throw away work that is about to be wanted. Per-tile
    /// staleness during a scroll is what the tag protocol in
    /// <see cref="BuildThumbnail"/> is for.
    /// </remarks>
    private CancellationTokenSource _decodeGeneration = new();

    private List<ArtEntry> _matches = [];
    private ArtEntry? _selected;
    private Bitmap? _previewBitmap;

    // Which preview request is current. A superseded decode drops its result
    // rather than overwriting a newer one, which is what arrow-keying through
    // the list produces.
    private int _previewGeneration;
    private int _columns = 1;
    private int _tileSize = 144;
    private double _chunkedWidth = -1;
    private bool _reflowPending;

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public ArtBrowserWindow()
        : this(null, ArtBrowserKind.Gump, 0)
    {
    }

    /// <param name="settings">
    /// The editor's shared settings. Null loads a private copy, which is what
    /// the XAML designer's parameterless constructor needs.
    /// </param>
    public ArtBrowserWindow(
        UoDataContext? data,
        ArtBrowserKind kind,
        int initialId,
        AppSettings? settings = null)
    {
        AvaloniaXamlLoader.Load(this);

        _data = data;
        _kind = kind;
        _settings = settings ?? AppSettings.Load();

        _filter = this.FindControl<TextBox>("FilterBox")!;
        _results = this.FindControl<ListBox>("Results")!;
        _galleryToggle = this.FindControl<ToggleButton>("GalleryToggle")!;
        _tileSizeBox = this.FindControl<NumericUpDown>("TileSizeBox")!;
        _count = this.FindControl<TextBlock>("CountText")!;
        _previewTitle = this.FindControl<TextBlock>("PreviewTitle")!;
        _previewDetail = this.FindControl<TextBlock>("PreviewDetail")!;
        _preview = this.FindControl<Image>("PreviewImage")!;
        _panes = this.FindControl<Grid>("Panes")!;

        RestorePreviewWidth();

        Title = kind == ArtBrowserKind.Gump ? "Browse gump art" : "Browse item art";

        _results.SelectionChanged += OnListSelectionChanged;
        _results.DoubleTapped += (_, _) => Accept();

        _filter.TextChanged += (_, _) => ScheduleFilter();

        // Re-chunking is what keeps a gallery filling the window instead of
        // leaving a ragged column of empty space. LayoutUpdated as well as
        // SizeChanged, because the first chunking happens in this constructor,
        // when the panel has no width yet and every row would hold one tile.
        _results.SizeChanged += (_, _) => ReflowIfNeeded();
        _results.LayoutUpdated += (_, _) => ReflowIfNeeded();

        _galleryToggle.IsCheckedChanged += (_, _) => ApplyViewMode(remember: true);

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) => Accept();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();

        _tileSize = Math.Clamp(_settings.ArtBrowserTileSize, MinTileSize, MaxTileSize);
        _tileSizeBox.Value = _tileSize;
        _tileSizeBox.ValueChanged += (_, _) => ApplyTileSize();

        _galleryToggle.IsChecked = _settings.ArtBrowserGallery;

        ApplyViewMode(remember: false);
        Populate(initialId);
    }

    /// <summary>The chosen id, or null when the dialog was cancelled.</summary>
    public int? SelectedId { get; private set; }

    /// <summary>The preview pane, in pixels, as it is on screen.</summary>
    internal double PreviewWidth => _panes.ColumnDefinitions[PreviewColumn].Width.Value;

    /// <summary>Which grid column the preview occupies.</summary>
    private const int PreviewColumn = 2;

    /// <summary>
    /// Sizes the preview pane from the remembered width, and keeps it.
    /// </summary>
    /// <remarks>
    /// Saved as the drag finishes rather than on close, so it survives the
    /// window being dismissed with Escape, and matches how the tile size and the
    /// view mode are already remembered the moment they change.
    /// </remarks>
    private void RestorePreviewWidth()
    {
        _panes.ColumnDefinitions[PreviewColumn].Width =
            new GridLength(_settings.UsablePreviewWidth(), GridUnitType.Pixel);

        if (this.FindControl<GridSplitter>("PreviewSplitter") is { } splitter)
        {
            splitter.DragCompleted += (_, _) => SavePreviewWidth();
        }
    }

    private void SavePreviewWidth()
    {
        int width = (int)Math.Round(_panes.ColumnDefinitions[PreviewColumn].ActualWidth);

        if (width < AppSettings.MinPreviewWidth || width > AppSettings.MaxPreviewWidth)
        {
            return;
        }

        _settings.ArtBrowserPreviewWidth = width;
        _settings.Save();
    }

    private bool IsGallery => _galleryToggle.IsChecked == true;

    /// <summary>Footprint of a gallery tile: the image, its caption and margins.</summary>
    private int CellWidth => _tileSize + 12;

    private int CellHeight => _tileSize + 28;

    private int ThumbnailCacheLimit =>
        Math.Max(64, ThumbnailCacheBudget / (_tileSize * _tileSize * 4));

    private void Populate(int initialId)
    {
        if (_data is null)
        {
            _count.Text = "No client loaded.";

            return;
        }

        IEnumerable<int> ids = _kind == ArtBrowserKind.Gump
            ? _data.EnumerateGumpIds()
            : _data.EnumerateItemIds();

        foreach (int id in ids)
        {
            _all.Add(new ArtEntry(
                id,
                _kind == ArtBrowserKind.Item ? _data.TileData.GetStaticName(id) : string.Empty));
        }

        ApplyFilter();

        if (_all.Find(e => e.Id == initialId) is { } initial)
        {
            SetSelected(initial);
            ScrollTo(initial);
        }
    }

    /// <summary>
    /// Switches between the row list and the tile grid.
    /// </summary>
    /// <remarks>
    /// Both modes drive the same <see cref="ListBox"/>: only the item template
    /// and what the items <em>are</em> differ. In list mode each item is one
    /// entry; in gallery mode each item is a whole row of entries, which is what
    /// makes the grid virtualize — Avalonia has no virtualizing wrap panel, and a
    /// plain one would realise all forty-odd thousand item tiles at once.
    /// </remarks>
    private void ApplyViewMode(bool remember)
    {
        if (IsGallery)
        {
            _results.ItemTemplate = new FuncDataTemplate<ArtEntry[]>(
                (row, _) => BuildGalleryRow(row),
                supportsRecycling: false);

            // Rows are not selectable in gallery mode; the tiles inside them are.
            _results.SelectionMode = SelectionMode.Single;
        }
        else
        {
            _results.ItemTemplate = new FuncDataTemplate<ArtEntry>(
                (entry, _) => BuildListRow(entry),
                supportsRecycling: true);

            _results.SelectionMode = SelectionMode.Single;
        }

        _results.Classes.Set("gallery", IsGallery);

        Rebind();

        if (_selected is not null)
        {
            ScrollTo(_selected);
        }

        if (remember)
        {
            _settings.ArtBrowserGallery = IsGallery;
            _settings.Save();
        }
    }

    /// <summary>Applies a new thumbnail size and rebuilds what is on screen.</summary>
    /// <remarks>
    /// The cache is dropped rather than reused: its bitmaps were scaled to the
    /// old size, and reusing them would leave every tile either blurry or
    /// bordered by dead space until it happened to be decoded again.
    /// </remarks>
    private void ApplyTileSize()
    {
        int requested = Math.Clamp((int)(_tileSizeBox.Value ?? _tileSize), MinTileSize, MaxTileSize);

        if (requested == _tileSize)
        {
            return;
        }

        _tileSize = requested;

        RetireThumbnails();

        Rebind();

        if (_selected is not null)
        {
            ScrollTo(_selected);
        }

        _settings.ArtBrowserTileSize = _tileSize;
        _settings.Save();
    }

    /// <summary>
    /// Re-filters after a short pause in typing.
    /// </summary>
    /// <remarks>
    /// The item browser holds forty thousand entries, and filtering walks every
    /// one, rebuilds the match list and re-chunks every gallery row. Running
    /// that on each keystroke made typing a four-character id feel like four
    /// separate stalls.
    /// </remarks>
    private void ScheduleFilter()
    {
        _filterDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };

        _filterDebounce.Stop();
        _filterDebounce.Tick -= OnFilterTick;
        _filterDebounce.Tick += OnFilterTick;
        _filterDebounce.Start();
    }

    private void OnFilterTick(object? sender, EventArgs e)
    {
        _filterDebounce?.Stop();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = (_filter.Text ?? string.Empty).Trim();

        _matches = query.Length == 0
            ? _all
            : [.. _all.Where(e => Matches(e, query))];

        Rebind();

        _count.Text = string.Create(CultureInfo.InvariantCulture, $"{_matches.Count} of {_all.Count}");
    }

    /// <summary>Feeds the current matches to the list in the shape the mode needs.</summary>
    /// <remarks>
    /// The source is cleared before the new one is assigned. Handing the panel a
    /// replacement directly leaves it reconciling one set of rows against
    /// another, and a container realised for the old set can be left parented and
    /// visible with nothing to remove it — a row of tiles from a previous
    /// chunking, stranded on screen at a different column pitch, surviving every
    /// later re-chunk.
    /// </remarks>
    private void Rebind()
    {
        // Whatever was queued was for a visible set that no longer exists.
        CancelDecoding();

        _results.ItemsSource = null;

        if (!IsGallery)
        {
            _results.ItemsSource = _matches;
            _results.SelectedItem = _selected;

            return;
        }

        _columns = ColumnCount();
        _chunkedWidth = _results.Bounds.Width;

        _results.SelectedItem = null;
        _results.ItemsSource = Chunk(_matches, _columns);
    }

    private int ColumnCount()
    {
        // Leave room for the scrollbar, or the last column clips and the panel
        // grows a horizontal scrollbar nobody wants.
        double usable = _results.Bounds.Width - 24;

        return Math.Max(1, (int)(usable / CellWidth));
    }

    /// <summary>
    /// Re-chunks when the panel's width has changed enough to fit a different
    /// number of columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work is posted rather than done inline, because this runs from layout
    /// notifications: replacing the item source in the middle of a layout pass is
    /// what stranded rows from a previous chunking on screen.
    /// </para>
    /// <para>
    /// It is also gated on the width itself, not just on the resulting column
    /// count. Rebinding changes the content, which can change whether a scroll
    /// bar is needed, which changes the width — and two widths that disagree
    /// about the column count would otherwise re-chunk each other forever.
    /// </para>
    /// </remarks>
    private void ReflowIfNeeded()
    {
        if (!IsGallery || _reflowPending)
        {
            return;
        }

        double width = _results.Bounds.Width;

        if (Math.Abs(width - _chunkedWidth) < 1 || ColumnCount() == _columns)
        {
            return;
        }

        _reflowPending = true;

        Dispatcher.UIThread.Post(
            () =>
            {
                _reflowPending = false;

                if (!IsGallery || ColumnCount() == _columns)
                {
                    _chunkedWidth = _results.Bounds.Width;

                    return;
                }

                Rebind();

                if (_selected is not null)
                {
                    ScrollTo(_selected);
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>Splits the matches into rows of <paramref name="columns"/> tiles.</summary>
    /// <remarks>
    /// Copied by range rather than with <c>Skip</c>/<c>Take</c>: skipping walks
    /// the list from the start every time, which on forty thousand entries is
    /// quadratic and takes noticeably longer than decoding the art does.
    /// </remarks>
    private static List<ArtEntry[]> Chunk(List<ArtEntry> entries, int columns)
    {
        List<ArtEntry[]> rows = new(entries.Count / columns + 1);

        for (int i = 0; i < entries.Count; i += columns)
        {
            int length = Math.Min(columns, entries.Count - i);
            ArtEntry[] row = new ArtEntry[length];

            entries.CopyTo(i, row, 0, length);
            rows.Add(row);
        }

        return rows;
    }

    private static bool Matches(ArtEntry entry, string query)
    {
        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ids are conventionally written in hex, so accept both spellings.
        if (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(query[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            return entry.Id == hex;
        }

        // Formatted into a stack buffer: this runs once per entry per filter,
        // and the browser holds tens of thousands of them.
        Span<char> digits = stackalloc char[12];

        return entry.Id.TryFormat(digits, out int written, provider: CultureInfo.InvariantCulture)
            && digits[..written].Contains(query, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds one row of the list.
    /// </summary>
    /// <remarks>
    /// The item can be null: a template for a reference type is also asked to
    /// build when a container is being cleared, which happens constantly as rows
    /// scroll out of view.
    /// </remarks>
    private StackPanel BuildListRow(ArtEntry? entry)
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = _tileSize + 4,
        };

        if (entry is null)
        {
            return row;
        }

        row.Children.Add(BuildThumbnail(entry, _tileSize));
        row.Children.Add(new TextBlock
        {
            Text = entry.Display,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return row;
    }

    /// <inheritdoc cref="BuildListRow" />
    /// <remarks>
    /// The height is fixed rather than left to the contents. A row built for a
    /// null item would otherwise measure zero, and the virtualizing panel
    /// estimates its extent from the rows it has seen — so one zero-height row
    /// is enough to make it place later rows on top of each other.
    /// </remarks>
    private StackPanel BuildGalleryRow(ArtEntry[]? entries)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Height = CellHeight };

        foreach (ArtEntry entry in entries ?? [])
        {
            row.Children.Add(BuildTile(entry));
        }

        return row;
    }

    private Border BuildTile(ArtEntry entry)
    {
        StackPanel content = new() { Spacing = 2 };

        content.Children.Add(BuildThumbnail(entry, _tileSize));
        content.Children.Add(new TextBlock
        {
            Text = entry.Id.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.Silver,
            FontSize = 11,
        });

        Border tile = new()
        {
            Width = CellWidth - 4,
            Height = CellHeight - 4,
            Margin = new Avalonia.Thickness(2),
            Padding = new Avalonia.Thickness(2),
            BorderThickness = new Avalonia.Thickness(1),
            Child = content,

            // Carrying the entry lets the selection sweep find its tiles without
            // any bookkeeping that could go stale as rows are realised.
            Tag = entry,
        };

        ToolTip.SetTip(tile, entry.Display);
        Paint(tile, ReferenceEquals(entry, _selected));

        tile.PointerPressed += (_, _) => SetSelected(entry);
        tile.DoubleTapped += (_, e) =>
        {
            e.Handled = true;

            SetSelected(entry);
            Accept();
        };

        return tile;
    }

    private Image BuildThumbnail(ArtEntry entry, int size)
    {
        Image thumbnail = new()
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,

            // Records which id this control is waiting for, so a decode that
            // finishes after the control has been reused for another entry can
            // be recognised as stale and dropped.
            Tag = entry.Id,
        };

        // Scrolled out of view: whatever was queued for it is now wasted work.
        // Clearing the tag is what tells the queue to skip it, and it has to be
        // this rather than an attachment test — a freshly built control is not
        // in the tree yet either, and testing attachment up front threw away
        // every thumbnail before it could be shown.
        thumbnail.DetachedFromVisualTree += (_, _) => thumbnail.Tag = null;

        // Pixel art must not be smoothed when scaled into a thumbnail.
        RenderOptions.SetBitmapInterpolationMode(thumbnail, BitmapInterpolationMode.None);

        if (_thumbnails.TryGetValue(entry.Id, out Bitmap? cached))
        {
            // Straight from the cache: no await, so a re-scroll does not flicker
            // through a frame of empty tiles.
            thumbnail.Source = cached;

            Touch(entry.Id);
        }
        else
        {
            LoadThumbnailAsync(entry.Id, thumbnail);
        }

        return thumbnail;
    }

    /// <summary>
    /// Shows whether a tile is the selected one.
    /// </summary>
    /// <remarks>
    /// An unselected tile is painted <see cref="Brushes.Transparent"/> rather
    /// than left with no brush at all. A null background is not hit-tested, so
    /// only the artwork and the caption were clickable and the empty space
    /// around a small piece of art — most of the tile — quietly swallowed the
    /// click.
    /// </remarks>
    private static void Paint(Border tile, bool selected)
    {
        tile.Background = selected
            ? new SolidColorBrush(Color.FromArgb(0x60, 0x33, 0x99, 0xFF))
            : Brushes.Transparent;

        tile.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF))
            : Brushes.Transparent;
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // In gallery mode the list's items are rows, not entries; the tiles
        // report their own selection.
        if (!IsGallery && _results.SelectedItem is ArtEntry entry)
        {
            SetSelected(entry);
        }
    }

    private void SetSelected(ArtEntry entry)
    {
        _selected = entry;

        if (IsGallery)
        {
            RepaintTiles();
        }
        else if (!ReferenceEquals(_results.SelectedItem, entry))
        {
            _results.SelectedItem = entry;
        }

        UpdatePreview();
    }

    /// <summary>
    /// Refreshes the highlight on every tile currently on screen.
    /// </summary>
    /// <remarks>
    /// Sweeping the realised visuals costs one screenful of work and cannot go
    /// stale, which a map of tile controls would as rows scroll in and out.
    /// </remarks>
    private void RepaintTiles()
    {
        foreach (Border tile in _results.GetVisualDescendants().OfType<Border>())
        {
            if (tile.Tag is ArtEntry entry)
            {
                Paint(tile, ReferenceEquals(entry, _selected));
            }
        }
    }

    private void ScrollTo(ArtEntry entry)
    {
        if (!IsGallery)
        {
            _results.SelectedItem = entry;
            _results.ScrollIntoView(entry);

            return;
        }

        int index = _matches.IndexOf(entry);

        if (index >= 0)
        {
            _results.ScrollIntoView(index / _columns);
        }
    }

    private void LoadThumbnailAsync(int id, Image target)
    {
        if (_disposed)
        {
            return;
        }

        _pending.Enqueue((id, target));

        PumpDecodes();
    }

    /// <summary>Starts as many queued decodes as the bound allows.</summary>
    private void PumpDecodes()
    {
        while (!_disposed && _running < ConcurrentDecodes && _pending.Count > 0)
        {
            (int id, Image target) = _pending.Dequeue();

            // Scrolling fast queues far more work than it consumes. By the time
            // a request is served, its tile has often been reused or dropped,
            // and decoding for it would only delay the tiles on screen.
            if (!IsWanted(target, id))
            {
                continue;
            }

            if (_thumbnails.TryGetValue(id, out Bitmap? cached))
            {
                Touch(id);
                target.Source = cached;

                continue;
            }

            _running++;

            _ = ShowWhenDecodedAsync(id, target, _decodeGeneration.Token);
        }
    }

    /// <summary>
    /// Waits for one thumbnail and puts it in its tile.
    /// </summary>
    /// <remarks>
    /// Nothing may escape this method. It is started without being awaited, so
    /// an exception here surfaces on the dispatcher rather than to a caller, and
    /// an unhandled exception in a dispatcher continuation takes the dispatcher
    /// with it — which is how a scrolled gallery once left the whole application
    /// unable to close.
    /// </remarks>
    private async Task ShowWhenDecodedAsync(int id, Image target, CancellationToken token)
    {
        try
        {
            // The one hop back to the UI thread: the cache and the control below
            // are its business alone.
            Bitmap? bitmap = await DecodeSharedAsync(id, token).ConfigureAwait(true);

            if (_disposed || bitmap is null || token.IsCancellationRequested)
            {
                return;
            }

            // Another tile may have finished the same id first; Remember keeps
            // whichever arrived and retires the other rather than disposing it.
            if (!_thumbnails.ContainsKey(id))
            {
                Remember(id, bitmap);
            }

            if (IsWanted(target, id))
            {
                target.Source = _thumbnails.TryGetValue(id, out Bitmap? stored) ? stored : bitmap;
            }
        }
        catch (OperationCanceledException)
        {
            // The visible set moved on.
        }
        catch (Exception ex) when (ex is IOException
            or InvalidDataException
            or ObjectDisposedException)
        {
            // Art that cannot be read leaves its tile empty, which is what the
            // missing-art case has always looked like here.
        }
        finally
        {
            _running--;
            _inFlight.Remove(id);

            PumpDecodes();
        }
    }

    /// <summary>
    /// Decodes one thumbnail, joining a decode already running for that id.
    /// </summary>
    /// <remarks>
    /// Every await inside runs <c>ConfigureAwait(false)</c>, and that is the
    /// whole point of this method rather than a detail of it.
    ///
    /// The first version waited for its permit and released it on the
    /// dispatcher. A screenful of tiles then advanced at roughly one dispatcher
    /// turn each — acquire, hop back, decode, hop back, release, let the next
    /// waiter hop back — and since the dispatcher is simultaneously laying out
    /// the scroll that realised those tiles, filling a screen took seconds for
    /// about thirty milliseconds of actual decoding.
    ///
    /// Nothing here touches a control or the cache, so none of it needs the UI
    /// thread. The single hop back happens in the caller, once, to assign the
    /// image.
    /// </remarks>
    private Task<Bitmap?> DecodeSharedAsync(int id, CancellationToken token)
    {
        // Only joined if it is still going to produce something. A task that
        // has already been cancelled must never be handed to a new caller: the
        // caller would catch the cancellation and give up, leaving the tile
        // blank until its row happened to be realised again — which is what
        // "scroll away and come back and the art appears" looked like.
        if (_inFlight.TryGetValue(id, out Task<Bitmap?>? running) && CanJoinDecode(running))
        {
            return running;
        }

        Task<Bitmap?> started = DecodeAsync();

        // Added on the UI thread, so two tiles cannot both start one.
        _inFlight[id] = started;

        return started;

        // The bound is applied by the caller, so there is nothing to acquire
        // and nothing to release. Removal from the map happens on the UI thread
        // in ShowWhenDecodedAsync.
        Task<Bitmap?> DecodeAsync() => Task.Run(() => DecodeThumbnail(id), token);
    }

    /// <summary>
    /// Whether a decode already under way is worth waiting for.
    /// </summary>
    /// <remarks>
    /// A task that has already been cancelled must never be handed to a new
    /// caller: the caller would catch the cancellation and give up, leaving the
    /// tile blank until its row happened to be realised again.
    /// </remarks>
    internal static bool CanJoinDecode(Task decode)
    {
        ArgumentNullException.ThrowIfNull(decode);

        return !decode.IsCanceled && !decode.IsFaulted;
    }

    /// <summary>True while a control still wants this id and has not been discarded.</summary>
    private static bool IsWanted(Image target, int id) =>
        target.Tag is int wanted && wanted == id;

    /// <summary>
    /// Caches a decoded thumbnail, evicting the least recently used.
    /// </summary>
    /// <remarks>
    /// Least recently used rather than first in: insertion order threw away
    /// exactly what was about to be wanted again, because scrolling down and
    /// back up asks for the earliest entries last.
    ///
    /// An evicted entry is dropped, not disposed, which is the rule stated on
    /// <see cref="_thumbnails"/> and worth restating because breaking it is
    /// invisible in a test: eviction picks the least recently used, and during a
    /// long scroll that bitmap can still be the source of a realised
    /// <see cref="Image"/> the virtualiser is holding. Disposing it tears a hole
    /// in the panel and can throw from inside the render pass. Whether anything
    /// still references it is exactly what this cache cannot know, so the
    /// reference is released and the collector decides.
    ///
    /// The bitmaps that *can* safely be disposed are the ones held when the
    /// window closes, which <see cref="DisposeThumbnails"/> does.
    /// </remarks>
    private void Remember(int id, Bitmap bitmap)
    {
        // Replacing one: its reference is dropped, never disposed, for the same
        // reason an evicted one is. The value is deliberately not taken out of
        // the dictionary into a local, so that nothing here even holds a
        // disposable to be tempted by.
        if (_thumbnails.ContainsKey(id))
        {
            if (_thumbnailNodes.Remove(id, out LinkedListNode<int>? stale))
            {
                _thumbnailOrder.Remove(stale);
            }

            _thumbnails.Remove(id);
        }

        _thumbnails[id] = bitmap;
        _thumbnailNodes[id] = _thumbnailOrder.AddLast(id);

        while (_thumbnailOrder.Count > ThumbnailCacheLimit && _thumbnailOrder.First is { } oldest)
        {
            int evictedId = oldest.Value;

            _thumbnailOrder.RemoveFirst();
            _thumbnailNodes.Remove(evictedId);
            _thumbnails.Remove(evictedId);
        }
    }

    /// <summary>
    /// Empties the thumbnail cache without releasing its bitmaps yet.
    /// </summary>
    /// <remarks>
    /// Used when the tile size changes, which invalidates every cached bitmap
    /// because they were scaled to the old size. They cannot be disposed here:
    /// tiles already on screen still have them as their <c>Image.Source</c> and
    /// are only replaced when the rebind that follows has been laid out. They
    /// are held instead, and released when the window closes.
    /// </remarks>
    private void RetireThumbnails()
    {
        _retired.AddRange(_thumbnails.Values);

        _thumbnails.Clear();
        _thumbnailOrder.Clear();
        _thumbnailNodes.Clear();
    }

    /// <summary>
    /// Releases every thumbnail this browser decoded.
    /// </summary>
    /// <remarks>
    /// Each holds unmanaged pixel memory, and a browser is constructed afresh on
    /// every browse click. Only the preview used to be released, leaving all the
    /// rest to their finalizers.
    /// </remarks>
    private void DisposeThumbnails()
    {
        foreach (Bitmap thumbnail in _thumbnails.Values)
        {
            thumbnail.Dispose();
        }

        foreach (Bitmap thumbnail in _retired)
        {
            thumbnail.Dispose();
        }

        _thumbnails.Clear();
        _thumbnailOrder.Clear();
        _thumbnailNodes.Clear();
        _retired.Clear();
    }

    /// <summary>Abandons in-flight decodes and begins a new generation.</summary>
    /// <remarks>
    /// The map of running decodes is emptied along with the token, or the next
    /// request for one of those ids would join a task that is about to report
    /// cancellation and abandon its tile for good.
    /// </remarks>
    private void CancelDecoding()
    {
        _decodeGeneration.Cancel();
        _decodeGeneration.Dispose();
        _decodeGeneration = new CancellationTokenSource();

        _pending.Clear();
        _inFlight.Clear();
    }

    /// <summary>Marks a cached thumbnail as just used.</summary>
    private void Touch(int id)
    {
        if (_thumbnailNodes.TryGetValue(id, out LinkedListNode<int>? node))
        {
            _thumbnailOrder.Remove(node);
            _thumbnailOrder.AddLast(node);
        }
    }

    /// <summary>
    /// Decodes art already scaled down to tile size.
    /// </summary>
    /// <remarks>
    /// Caching the full-size decode would be far more memory than it is worth: a
    /// single 300x200 gump is a quarter of a megabyte, and the cache holds
    /// hundreds. Nearest-neighbour sampling matches how the tile would have been
    /// drawn anyway, so nothing changes on screen. Art smaller than the tile is
    /// left alone rather than blown up, so its true size stays readable.
    /// </remarks>
    private Bitmap? DecodeThumbnail(int id)
    {
        UoImage? image = Load(id);

        if (image is null)
        {
            return null;
        }

        using SKBitmap decoded = Rendering.UoImageConverter.ToSkBitmap(image);

        int longest = Math.Max(decoded.Width, decoded.Height);

        if (longest <= _tileSize)
        {
            return SkiaBitmap.ToAvalonia(decoded);
        }

        double scale = (double)_tileSize / longest;

        using SKBitmap scaled = decoded.Resize(
            new SKImageInfo(
                Math.Max(1, (int)(decoded.Width * scale)),
                Math.Max(1, (int)(decoded.Height * scale)),
                decoded.ColorType,
                decoded.AlphaType),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

        return scaled is null ? SkiaBitmap.ToAvalonia(decoded) : SkiaBitmap.ToAvalonia(scaled);
    }

    private UoImage? Load(int id) => _kind == ArtBrowserKind.Gump
        ? _data?.GetGump(id)
        : _data?.GetStatic(id);

    /// <summary>
    /// Shows the selected art at full size.
    /// </summary>
    /// <remarks>
    /// The decode runs on the pool. It is a full inflate, Burrows-Wheeler pass
    /// and RLE decode of full-size art, and this is reached from every click and
    /// every arrow-key move through the list, so doing it inline stalled the
    /// window once per keypress and contended with the background thumbnail
    /// decoder for the same container.
    ///
    /// Arrow-keying produces a burst of these, so each carries a generation
    /// number and only the newest result is allowed to land. The title and the
    /// id appear immediately; only the image and its dimensions wait.
    /// </remarks>
    private void UpdatePreview()
    {
        int generation = ++_previewGeneration;

        if (_selected is not { } entry)
        {
            ClearPreview();

            return;
        }

        _previewTitle.Text = entry.Display;
        _previewDetail.Text = string.Create(CultureInfo.InvariantCulture, $"0x{entry.Id:X4}");

        _ = ShowAsync(entry, generation);

        async Task ShowAsync(ArtEntry showing, int forGeneration)
        {
            UoImage? image;

            try
            {
                image = await Task.Run(() => Load(showing.Id)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Superseded while decoding, or the window has gone.
            if (forGeneration != _previewGeneration)
            {
                return;
            }

            Bitmap? decoded = null;

            if (image is not null)
            {
                using SKBitmap bitmap = Rendering.UoImageConverter.ToSkBitmap(image);

                decoded = SkiaBitmap.ToAvalonia(bitmap);
            }

            // Assigned before the previous one is released, so the pane never
            // blanks between two selections and nothing disposes a bitmap that
            // is still on screen.
            Bitmap? previous = _previewBitmap;

            _previewBitmap = decoded;
            _preview.Source = decoded;

            previous?.Dispose();

            _previewDetail.Text = decoded is null
                ? "Could not decode this art."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{decoded.PixelSize.Width} x {decoded.PixelSize.Height}  ·  0x{showing.Id:X4}");
        }
    }

    private void ClearPreview()
    {
        _previewTitle.Text = "Nothing selected";
        _previewDetail.Text = string.Empty;
        _preview.Source = null;

        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private void Accept()
    {
        if (_selected is { } entry)
        {
            SelectedId = entry.Id;
        }

        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        Dispose();
    }

    /// <summary>
    /// Releases the decoded art and the decoding machinery.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="OnClosed"/> rather than left to a caller: a
    /// browser is constructed afresh on every browse click and shown as a
    /// modal, so closing is the only moment it is finished with.
    /// </remarks>
    public void Dispose()
    {
        // Idempotent: OnClosed calls this, and so does anything holding the
        // window in a using. Cancelling an already-disposed token source throws.
        if (_disposed)
        {
            return;
        }

        // Set before anything is released, so a decode that finishes afterwards
        // returns without touching a disposed bitmap or a dead control.
        _disposed = true;

        // Moves the generation past anything in flight, for the preview.
        _previewGeneration++;

        _decodeGeneration.Cancel();

        _pending.Clear();
        _inFlight.Clear();

        _previewBitmap?.Dispose();
        _previewBitmap = null;

        DisposeThumbnails();

        _filterDebounce?.Stop();

        _decodeGeneration.Dispose();
    }

    /// <summary>One entry in the browser.</summary>
    private sealed record ArtEntry(int Id, string Name)
    {
        public string Display => Name.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Id}  ({Name})")
            : Id.ToString(CultureInfo.InvariantCulture);
    }
}
