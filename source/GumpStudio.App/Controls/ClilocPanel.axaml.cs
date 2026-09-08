using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

using GumpStudio.Uo.Data;

namespace GumpStudio.App.Controls;

/// <summary>
/// The client's localised strings, searchable by text or by id.
/// </summary>
/// <remarks>
/// <para>
/// The 1.8 editor had a cliloc browser, but it had no search box: it listed all
/// ~124,000 strings and left you to scroll. Finding one by its text is the whole
/// point of this, since an id says nothing about what a player will read.
/// </para>
/// <para>
/// A panel rather than a dialog, so it can stay open beside the canvas while a
/// gump is being laid out — and so the language selector has somewhere to live
/// that is not a modal window.
/// </para>
/// <para>
/// It takes strings rather than a <c>UoDataContext</c>: the panel has nothing to
/// do with how a client is read, and that is the seam which lets the filter, the
/// count and the hand-off be tested with no Ultima installation present.
/// </para>
/// </remarks>
public sealed partial class ClilocPanel : UserControl
{
    /// <summary>Width of the id column, matching the original's 100px gutter.</summary>
    private const string RowColumns = "68,*";

    private readonly TextBox _filter;
    private readonly ComboBox _languages;
    private readonly Button _apply;
    private readonly TextBlock _count;
    private readonly ListBox _list;
    private readonly DispatcherTimer _debounce;

    private IReadOnlyList<ClilocEntry> _all = [];
    private IReadOnlyList<ClilocEntry> _matches = [];

    /// <summary>
    /// Set while the language list is being filled from a client.
    /// </summary>
    /// <remarks>
    /// Assigning <c>SelectedItem</c> raises the same event a click does, and
    /// reporting the client's own language back as a user choice would save it
    /// to settings and start a redundant re-read.
    /// </remarks>
    private bool _fillingLanguages;

    public ClilocPanel()
    {
        AvaloniaXamlLoader.Load(this);

        _filter = this.FindControl<TextBox>("FilterBox")!;
        _languages = this.FindControl<ComboBox>("LanguageBox")!;
        _apply = this.FindControl<Button>("ApplyButton")!;
        _count = this.FindControl<TextBlock>("CountLabel")!;
        _list = this.FindControl<ListBox>("ResultList")!;

        _list.ItemTemplate = RowTemplate();

        // Created once and subscribed once. The art browser re-subscribes on
        // every keystroke because it builds its timer lazily; there is nothing
        // to guard against here.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += OnDebounceTick;

        _filter.TextChanged += (_, _) => ScheduleFilter();
        _apply.Click += (_, _) => Take();
        _list.DoubleTapped += (_, _) => Take();
        _list.KeyDown += OnListKeyDown;
        _languages.SelectionChanged += OnLanguageChanged;
    }

    /// <summary>The highlighted id, or null when nothing is selected.</summary>
    public int? SelectedId => _list.SelectedItem is ClilocEntry entry ? entry.Id : null;

    /// <summary>Raised when a row is taken: Apply, a double-tap, or Enter.</summary>
    public event EventHandler<int>? EntryChosen;

    /// <summary>Raised only for a language the user picked.</summary>
    public event EventHandler<string>? LanguageChosen;

    /// <summary>
    /// Fills the browser from a client's strings.
    /// </summary>
    /// <param name="entries">The strings, already ordered by id.</param>
    /// <param name="languages">The cliloc files the client ships.</param>
    /// <param name="language">Which of them is being read.</param>
    public void Load(
        IReadOnlyList<ClilocEntry> entries,
        IReadOnlyList<string> languages,
        string? language)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(languages);

        _all = entries;

        FillLanguages(languages, language);
        ApplyFilter();
    }

    /// <summary>Reports why the list is empty, in place of the count.</summary>
    public void ShowStatus(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _count.Text = message;
    }

    /// <summary>Names what Apply would write to, or disables it.</summary>
    public void ShowTarget(string? label)
    {
        _apply.IsEnabled = label is not null;

        ToolTip.SetTip(
            _apply,
            label is null
                ? "Select a cliloc field, or use its … button, to choose what this writes to."
                : $"Write the selected id to '{label}'.");
    }

    /// <summary>Filters to one id and selects it, as the browse button does.</summary>
    public void SeedFilter(int clilocId)
    {
        _filter.Text = clilocId == 0
            ? string.Empty
            : clilocId.ToString(CultureInfo.InvariantCulture);

        // Straight through, not debounced: the author clicked a button and is
        // waiting to see the row.
        ApplyFilter();

        _list.SelectedItem = _matches.FirstOrDefault(e => e.Id == clilocId);
    }

    public void FocusFilter()
    {
        _filter.Focus();
        _filter.SelectAll();
    }

    /// <summary>Filters now, skipping the debounce.</summary>
    internal void ApplyQuery(string query)
    {
        _filter.Text = query;

        ApplyFilter();
    }

    internal TextBox Filter => _filter;

    internal ComboBox Languages => _languages;

    internal Button Apply => _apply;

    internal ListBox List => _list;

    internal string CountText => _count.Text ?? string.Empty;

    internal int MatchCount => _matches.Count;

    private static FuncDataTemplate<ClilocEntry> RowTemplate() =>
        // Recycling off. A recycled presenter hands back the existing child and
        // only swaps its DataContext, which would leave the literal text of the
        // row it was built for.
        new(
            static (_, _) =>
            {
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions(RowColumns) };

                TextBlock id = new()
                {
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Avalonia.Thickness(0, 0, 8, 0),
                };

                // Trimmed rather than wrapped. A wrapped string gives rows
                // different heights, and a virtualised list of 124,000 of those
                // makes the scrollbar jump as it scrolls.
                TextBlock text = new()
                {
                    Foreground = Brushes.Silver,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                id.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(ClilocEntry.Id)));
                text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(ClilocEntry.Text)));

                // The row trims, so the full string has to be reachable.
                text.Bind(ToolTip.TipProperty, new Avalonia.Data.Binding(nameof(ClilocEntry.Text)));

                Grid.SetColumn(id, 0);
                Grid.SetColumn(text, 1);

                row.Children.Add(id);
                row.Children.Add(text);

                return row;
            },
            supportsRecycling: false);

    private void FillLanguages(IReadOnlyList<string> languages, string? language)
    {
        _fillingLanguages = true;

        try
        {
            // Upper-cased for the list only. The value is a file extension, and
            // ENU reads as a language tag where enu reads as a typo.
            _languages.ItemsSource = languages
                .Select(static code => code.ToUpperInvariant())
                .ToList();

            _languages.SelectedItem = language?.ToUpperInvariant();
            _languages.IsEnabled = languages.Count > 1;
        }
        finally
        {
            _fillingLanguages = false;
        }
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_fillingLanguages && _languages.SelectedItem is string code)
        {
            LanguageChosen?.Invoke(this, code);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter)
        {
            Take();

            e.Handled = true;
        }
    }

    /// <summary>Reports the highlighted row, when there is somewhere to put it.</summary>
    private void Take()
    {
        if (_apply.IsEnabled && SelectedId is { } id)
        {
            EntryChosen?.Invoke(this, id);
        }
    }

    /// <summary>
    /// Waits for typing to settle before filtering.
    /// </summary>
    /// <remarks>
    /// Filtering walks every one of ~124,000 strings and rebuilds the match
    /// list. Doing that per keystroke turns typing a seven-digit id into seven
    /// separate stalls.
    /// </remarks>
    private void ScheduleFilter()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = (_filter.Text ?? string.Empty).Trim();

        // The empty query reuses the same list rather than copying 124,000
        // entries to say "all of them".
        _matches = query.Length == 0
            ? _all
            : [.. _all.Where(e => ClilocFilter.Matches(e.Id, e.Text, query))];

        Rebind();

        _count.Text = string.Create(CultureInfo.InvariantCulture, $"{_matches.Count} of {_all.Count}");
    }

    /// <summary>
    /// Hands the list its new rows.
    /// </summary>
    /// <remarks>
    /// The source is cleared first. Handing the panel a replacement directly
    /// leaves it reconciling one set of rows against another, which has stranded
    /// realised containers on screen before.
    /// </remarks>
    private void Rebind()
    {
        ClilocEntry? selected = _list.SelectedItem as ClilocEntry?;

        _list.ItemsSource = null;
        _list.ItemsSource = _matches;

        // Keep the highlight if the row survived the filter.
        if (selected is { } kept && _matches.Contains(kept))
        {
            _list.SelectedItem = kept;
        }
    }
}
