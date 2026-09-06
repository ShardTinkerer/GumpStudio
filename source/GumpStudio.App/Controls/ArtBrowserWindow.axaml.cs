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
    /// <summary>Side of a gallery tile's image area, in pixels.</summary>
    private const int TileSize = 72;

    /// <summary>Footprint of a gallery tile, image plus caption plus margins.</summary>
    private const int CellWidth = 84;
    private const int CellHeight = 100;

    private readonly List<ArtEntry> _all = [];
    private readonly UoDataContext? _data;
    private readonly ArtBrowserKind _kind;

    private readonly TextBox _filter = null!;
    private readonly ListBox _results = null!;
    private readonly ToggleButton _galleryToggle = null!;
    private readonly TextBlock _count = null!;
    private readonly TextBlock _previewTitle = null!;
    private readonly TextBlock _previewDetail = null!;
    private readonly Image _preview = null!;

    private List<ArtEntry> _matches = [];
    private ArtEntry? _selected;
    private Bitmap? _previewBitmap;
    private int _columns = 1;

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
        _count = this.FindControl<TextBlock>("CountText")!;
        _previewTitle = this.FindControl<TextBlock>("PreviewTitle")!;
        _previewDetail = this.FindControl<TextBlock>("PreviewDetail")!;
        _preview = this.FindControl<Image>("PreviewImage")!;

        Title = kind == ArtBrowserKind.Gump ? "Browse gump art" : "Browse item art";

        _results.SelectionChanged += OnListSelectionChanged;
        _results.DoubleTapped += (_, _) => Accept();

        _filter.TextChanged += (_, _) => ApplyFilter();

        // Re-chunking on resize is what keeps a gallery filling the window
        // instead of leaving a ragged column of empty space.
        _results.SizeChanged += (_, _) => ReflowIfNeeded();

        _galleryToggle.IsCheckedChanged += (_, _) => ApplyViewMode(remember: true);

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) => Accept();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();

        _galleryToggle.IsChecked = AppSettings.Load().ArtBrowserGallery;

        ApplyViewMode(remember: false);
        Populate(initialId);
    }

    /// <summary>The chosen id, or null when the dialog was cancelled.</summary>
    public int? SelectedId { get; private set; }

    private bool IsGallery => _galleryToggle.IsChecked == true;

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
    private void Rebind()
    {
        if (!IsGallery)
        {
            _results.ItemsSource = _matches;
            _results.SelectedItem = _selected;

            return;
        }

        _columns = ColumnCount();
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

    private void ReflowIfNeeded()
    {
        if (!IsGallery || ColumnCount() == _columns)
        {
            return;
        }

        Rebind();

        if (_selected is not null)
        {
            ScrollTo(_selected);
        }
    }

    private static List<ArtEntry[]> Chunk(List<ArtEntry> entries, int columns)
    {
        List<ArtEntry[]> rows = new(entries.Count / columns + 1);

        for (int i = 0; i < entries.Count; i += columns)
        {
            rows.Add([.. entries.Skip(i).Take(columns)]);
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
            Height = 44,
        };

        if (entry is null)
        {
            return row;
        }

        row.Children.Add(BuildThumbnail(entry, 40));
        row.Children.Add(new TextBlock
        {
            Text = entry.Display,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return row;
    }

    /// <inheritdoc cref="BuildListRow" />
    private StackPanel BuildGalleryRow(ArtEntry[]? entries)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal };

        foreach (ArtEntry entry in entries ?? [])
        {
            row.Children.Add(BuildTile(entry));
        }

        return row;
    }

    private Border BuildTile(ArtEntry entry)
    {
        StackPanel content = new() { Spacing = 2 };

        content.Children.Add(BuildThumbnail(entry, TileSize));
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
        };

        // Pixel art must not be smoothed when scaled into a thumbnail.
        RenderOptions.SetBitmapInterpolationMode(thumbnail, BitmapInterpolationMode.None);

        // Decoding happens off the UI thread: a realised tile must not block
        // scrolling while a gump inflates.
        _ = LoadThumbnailAsync(entry, thumbnail);

        return thumbnail;
    }

    private static void Paint(Border tile, bool selected)
    {
        tile.Background = selected ? new SolidColorBrush(Color.FromArgb(0x60, 0x33, 0x99, 0xFF)) : null;
        tile.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)) : Brushes.Transparent;
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

    private async Task LoadThumbnailAsync(ArtEntry entry, Image target)
    {
        Bitmap? bitmap = await Task.Run(() => Decode(entry.Id)).ConfigureAwait(true);

        if (bitmap is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => target.Source = bitmap);
        }
    }

    private Bitmap? Decode(int id)
    {
        UoImage? image = _kind == ArtBrowserKind.Gump
            ? _data?.GetGump(id)
            : _data?.GetStatic(id);

        if (image is null)
        {
            return null;
        }

        using SKBitmap skia = Rendering.UoImageConverter.ToSkBitmap(image);
        using SKData encoded = skia.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(encoded.ToArray());

        return new Bitmap(stream);
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
