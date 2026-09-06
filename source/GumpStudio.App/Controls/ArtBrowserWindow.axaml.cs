using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
public sealed partial class ArtBrowserWindow : Window
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

    private readonly TextBox _filter = null!;
    private readonly ListBox _results = null!;
    private readonly ToggleButton _galleryToggle = null!;
    private readonly NumericUpDown _tileSizeBox = null!;
    private readonly TextBlock _count = null!;
    private readonly TextBlock _previewTitle = null!;
    private readonly TextBlock _previewDetail = null!;
    private readonly Image _preview = null!;

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
    private readonly Queue<int> _thumbnailOrder = new();

    /// <summary>
    /// Serialises decoding, one image at a time.
    /// </summary>
    /// <value>
    /// The tail of the queue: each request continues from the previous one.
    /// Only ever read and written on the UI thread, so it needs no lock, and
    /// unlike a semaphore it is not something the window has to dispose.
    /// </value>
    /// <remarks>
    /// A gallery realises a whole screenful of tiles at once — seventy or so —
    /// and firing that many decodes concurrently is worse than useless. The UOP
    /// reader memoises exactly one decompressed entry, and reading a gump takes
    /// two passes over it: one for its dimensions and one for its pixels.
    /// Running them in parallel means every thread evicts every other thread's
    /// memo, so each gump inflates and Burrows-Wheeler-decodes twice instead of
    /// once, on a flooded thread pool.
    /// </remarks>
    private Task _decodeQueue = Task.CompletedTask;

    private List<ArtEntry> _matches = [];
    private ArtEntry? _selected;
    private Bitmap? _previewBitmap;
    private int _columns = 1;
    private int _tileSize = 144;
    private double _chunkedWidth = -1;
    private bool _reflowPending;

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public ArtBrowserWindow()
        : this(null, ArtBrowserKind.Gump, 0)
    {
    }

    public ArtBrowserWindow(UoDataContext? data, ArtBrowserKind kind, int initialId)
    {
        AvaloniaXamlLoader.Load(this);

        _data = data;
        _kind = kind;

        _filter = this.FindControl<TextBox>("FilterBox")!;
        _results = this.FindControl<ListBox>("Results")!;
        _galleryToggle = this.FindControl<ToggleButton>("GalleryToggle")!;
        _tileSizeBox = this.FindControl<NumericUpDown>("TileSizeBox")!;
        _count = this.FindControl<TextBlock>("CountText")!;
        _previewTitle = this.FindControl<TextBlock>("PreviewTitle")!;
        _previewDetail = this.FindControl<TextBlock>("PreviewDetail")!;
        _preview = this.FindControl<Image>("PreviewImage")!;

        Title = kind == ArtBrowserKind.Gump ? "Browse gump art" : "Browse item art";

        _results.SelectionChanged += OnListSelectionChanged;
        _results.DoubleTapped += (_, _) => Accept();

        _filter.TextChanged += (_, _) => ApplyFilter();

        // Re-chunking is what keeps a gallery filling the window instead of
        // leaving a ragged column of empty space. LayoutUpdated as well as
        // SizeChanged, because the first chunking happens in this constructor,
        // when the panel has no width yet and every row would hold one tile.
        _results.SizeChanged += (_, _) => ReflowIfNeeded();
        _results.LayoutUpdated += (_, _) => ReflowIfNeeded();

        _galleryToggle.IsCheckedChanged += (_, _) => ApplyViewMode(remember: true);

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) => Accept();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();

        AppSettings settings = AppSettings.Load();

        _tileSize = Math.Clamp(settings.ArtBrowserTileSize, MinTileSize, MaxTileSize);
        _tileSizeBox.Value = _tileSize;
        _tileSizeBox.ValueChanged += (_, _) => ApplyTileSize();

        _galleryToggle.IsChecked = settings.ArtBrowserGallery;

        ApplyViewMode(remember: false);
        Populate(initialId);
    }

    /// <summary>The chosen id, or null when the dialog was cancelled.</summary>
    public int? SelectedId { get; private set; }

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
            AppSettings settings = AppSettings.Load();

            settings.ArtBrowserGallery = IsGallery;

            AppSettings.Save(settings);
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

        _thumbnails.Clear();
        _thumbnailOrder.Clear();

        Rebind();

        if (_selected is not null)
        {
            ScrollTo(_selected);
        }

        AppSettings settings = AppSettings.Load();

        settings.ArtBrowserTileSize = _tileSize;

        AppSettings.Save(settings);
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

        return entry.Id.ToString(CultureInfo.InvariantCulture)
            .Contains(query, StringComparison.Ordinal);
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
        _decodeQueue = Continue(_decodeQueue);

        async Task Continue(Task previous)
        {
            await previous.ConfigureAwait(true);

            // Scrolling fast queues far more work than it consumes. By the time a
            // request reaches the front, its tile has usually been reused or
            // dropped, and decoding for it would only delay the tiles on screen.
            if (!IsWanted(target, id))
            {
                return;
            }

            if (!_thumbnails.TryGetValue(id, out Bitmap? bitmap))
            {
                bitmap = await Task.Run(() => DecodeThumbnail(id)).ConfigureAwait(true);

                if (bitmap is null)
                {
                    return;
                }

                Remember(id, bitmap);
            }

            if (IsWanted(target, id))
            {
                target.Source = bitmap;
            }
        }
    }

    /// <summary>True while a control still wants this id and has not been discarded.</summary>
    private static bool IsWanted(Image target, int id) =>
        target.Tag is int wanted && wanted == id;

    private void Remember(int id, Bitmap bitmap)
    {
        _thumbnails[id] = bitmap;
        _thumbnailOrder.Enqueue(id);

        while (_thumbnailOrder.Count > ThumbnailCacheLimit)
        {
            _thumbnails.Remove(_thumbnailOrder.Dequeue());
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
            return Encode(decoded);
        }

        double scale = (double)_tileSize / longest;

        using SKBitmap scaled = decoded.Resize(
            new SKImageInfo(
                Math.Max(1, (int)(decoded.Width * scale)),
                Math.Max(1, (int)(decoded.Height * scale)),
                decoded.ColorType,
                decoded.AlphaType),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

        return scaled is null ? Encode(decoded) : Encode(scaled);
    }

    private static Bitmap Encode(SKBitmap bitmap)
    {
        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(encoded.ToArray());

        return new Bitmap(stream);
    }

    private UoImage? Load(int id) => _kind == ArtBrowserKind.Gump
        ? _data?.GetGump(id)
        : _data?.GetStatic(id);

    /// <summary>Decodes art at full size, for the preview panel.</summary>
    private Bitmap? Decode(int id)
    {
        UoImage? image = Load(id);

        return image is null ? null : Encode(Rendering.UoImageConverter.ToSkBitmap(image));
    }

    private void UpdatePreview()
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;

        if (_selected is not { } entry)
        {
            _previewTitle.Text = "Nothing selected";
            _previewDetail.Text = string.Empty;
            _preview.Source = null;

            return;
        }

        _previewTitle.Text = entry.Display;
        _previewBitmap = Decode(entry.Id);
        _preview.Source = _previewBitmap;

        _previewDetail.Text = _previewBitmap is null
            ? "Could not decode this art."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{_previewBitmap.PixelSize.Width} x {_previewBitmap.PixelSize.Height}  ·  0x{entry.Id:X4}");
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

        _previewBitmap?.Dispose();
    }

    /// <summary>One entry in the browser.</summary>
    private sealed record ArtEntry(int Id, string Name)
    {
        public string Display => Name.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Id}  ({Name})")
            : Id.ToString(CultureInfo.InvariantCulture);
    }
}
