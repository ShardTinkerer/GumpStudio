using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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
/// A picker for gump and item art.
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
/// plus Burrows-Wheeler pass. Thumbnails are decoded only for rows the list
/// actually realises.
/// </para>
/// </remarks>
public sealed partial class ArtBrowserWindow : Window
{
    private readonly List<ArtEntry> _all = [];
    private readonly UoDataContext? _data;
    private readonly ArtBrowserKind _kind;

    private readonly TextBox _filter = null!;
    private readonly ListBox _results = null!;
    private readonly TextBlock _count = null!;
    private readonly TextBlock _previewTitle = null!;
    private readonly TextBlock _previewDetail = null!;
    private readonly Image _preview = null!;

    private Bitmap? _previewBitmap;

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
        _count = this.FindControl<TextBlock>("CountText")!;
        _previewTitle = this.FindControl<TextBlock>("PreviewTitle")!;
        _previewDetail = this.FindControl<TextBlock>("PreviewDetail")!;
        _preview = this.FindControl<Image>("PreviewImage")!;

        Title = kind == ArtBrowserKind.Gump ? "Browse gump art" : "Browse item art";

        _results.ItemTemplate = new FuncDataTemplate<ArtEntry>((entry, _) => BuildRow(entry), supportsRecycling: true);
        _results.SelectionChanged += (_, _) => UpdatePreview();

        _filter.TextChanged += (_, _) => ApplyFilter();

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) => Accept();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();

        _results.DoubleTapped += (_, _) => Accept();

        Populate(initialId);
    }

    /// <summary>The chosen id, or null when the dialog was cancelled.</summary>
    public int? SelectedId { get; private set; }

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

        ArtEntry? initial = _all.Find(e => e.Id == initialId);

        if (initial is not null)
        {
            _results.SelectedItem = initial;
            _results.ScrollIntoView(initial);
        }
    }

    private void ApplyFilter()
    {
        string query = (_filter.Text ?? string.Empty).Trim();

        List<ArtEntry> matches = query.Length == 0
            ? _all
            : [.. _all.Where(e => Matches(e, query))];

        _results.ItemsSource = matches;
        _count.Text = string.Create(CultureInfo.InvariantCulture, $"{matches.Count} of {_all.Count}");
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

    private StackPanel BuildRow(ArtEntry entry)
    {
        StackPanel row = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Height = 44,
        };

        Image thumbnail = new()
        {
            Width = 40,
            Height = 40,
            Stretch = Avalonia.Media.Stretch.Uniform,
            StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
        };

        // Pixel art must not be smoothed when scaled into a thumbnail.
        Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(
            thumbnail, Avalonia.Media.Imaging.BitmapInterpolationMode.None);

        // Decoding happens off the UI thread: a realised row must not block
        // scrolling while a gump inflates.
        _ = LoadThumbnailAsync(entry, thumbnail);

        row.Children.Add(thumbnail);
        row.Children.Add(new TextBlock
        {
            Text = entry.Display,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });

        return row;
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

        if (_results.SelectedItem is not ArtEntry entry)
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
        if (_results.SelectedItem is ArtEntry entry)
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

    /// <summary>One row in the browser.</summary>
    private sealed record ArtEntry(int Id, string Name)
    {
        public string Display => Name.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Id}  ({Name})")
            : Id.ToString(CultureInfo.InvariantCulture);
    }
}
