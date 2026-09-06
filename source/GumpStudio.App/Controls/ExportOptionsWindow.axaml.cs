using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using GumpStudio.Core.Export;

namespace GumpStudio.App.Controls;

/// <summary>
/// Asks how to export, once a file has been chosen.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed the export menu had one entry per dialect and no options at
/// all: <c>Namespace</c> and <c>IncludeComments</c> always took their defaults in
/// the editor, reachable only from the CLI and the API.
/// </para>
/// <para>
/// It opens <em>after</em> the file picker, not before, because the gump name
/// defaults to the chosen file name. Asking first would leave nothing to derive
/// it from and would quietly change the default name of every export.
/// </para>
/// </remarks>
public sealed partial class ExportOptionsWindow : Window
{
    private readonly ComboBox _dialect = null!;
    private readonly TextBox _name = null!;
    private readonly TextBox _namespace = null!;
    private readonly CheckBox _comments = null!;
    private readonly IReadOnlyList<ConverterDialect> _dialects = [];

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public ExportOptionsWindow()
        : this("Export", [], new GumpExportOptions())
    {
    }

    public ExportOptionsWindow(
        string title, IReadOnlyList<ConverterDialect> dialects, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(dialects);
        ArgumentNullException.ThrowIfNull(options);

        AvaloniaXamlLoader.Load(this);

        _dialects = dialects;

        _dialect = this.FindControl<ComboBox>("DialectBox")!;
        _name = this.FindControl<TextBox>("NameBox")!;
        _namespace = this.FindControl<TextBox>("NamespaceBox")!;
        _comments = this.FindControl<CheckBox>("CommentsBox")!;

        Title = title;

        _name.Text = options.GumpName;
        _namespace.Text = options.Namespace;
        _comments.IsChecked = options.IncludeComments;

        // A converter with one form has nothing to ask about, so the row goes
        // rather than showing a picker with a single entry.
        bool hasDialects = dialects.Count > 0;

        _dialect.IsVisible = hasDialects;
        this.FindControl<TextBlock>("DialectLabel")!.IsVisible = hasDialects;

        if (hasDialects)
        {
            _dialect.ItemsSource = dialects.Select(d => d.DisplayName).ToList();
            _dialect.SelectedIndex = Math.Max(0, IndexOf(dialects, options.Dialect));
        }

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) =>
        {
            Result = options with
            {
                GumpName = string.IsNullOrWhiteSpace(_name.Text) ? options.GumpName : _name.Text,
                Namespace = string.IsNullOrWhiteSpace(_namespace.Text)
                    ? options.Namespace
                    : _namespace.Text,
                IncludeComments = _comments.IsChecked ?? true,
                Dialect = SelectedDialect(),
            };

            Close();
        };

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    /// <summary>The chosen settings, or null when cancelled.</summary>
    public GumpExportOptions? Result { get; private set; }

    private string? SelectedDialect() =>
        _dialects.Count > 0 && _dialect.SelectedIndex >= 0
            ? _dialects[_dialect.SelectedIndex].Id
            : null;

    private static int IndexOf(IReadOnlyList<ConverterDialect> dialects, string? id)
    {
        for (int i = 0; i < dialects.Count; i++)
        {
            if (string.Equals(dialects[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }
}
