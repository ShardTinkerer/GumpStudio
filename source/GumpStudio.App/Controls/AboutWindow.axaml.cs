using System.Globalization;
using System.Reflection;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>
/// Who wrote this, and which build it is.
/// </summary>
/// <remarks>
/// Carries the 1.8 credits verbatim as well as the version, because the artwork
/// and most of the ideas in the editor are still Bradley Uffner's and Melanius's
/// — the rewrite changed the code, not the authorship of what it reproduces.
/// </remarks>
public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<Image>("Graphic")!.Source = Artwork.Splash();

        this.FindControl<TextBlock>("VersionText")!.Text = Describe();
        this.FindControl<TextBlock>("RuntimeText")!.Text = string.Create(
            CultureInfo.InvariantCulture,
            $".NET {Environment.Version}  ·  {Environment.OSVersion.VersionString}");

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
    }

    /// <summary>
    /// The product and version, as the assembly declares them.
    /// </summary>
    /// <remarks>
    /// From the informational version rather than
    /// <see cref="AssemblyName.Version"/>, which is padded to four parts and
    /// would report the 2.0.0 in the build properties as "2.0.0.0". The original
    /// showed exactly that, labelled "Core Version".
    /// </remarks>
    internal static string Describe()
    {
        Assembly assembly = typeof(AboutWindow).Assembly;

        string? version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();

        // A build from a git checkout gets "+<commit>" appended, which is noise
        // in a title line.
        if (version is { } stamped && stamped.IndexOf('+', StringComparison.Ordinal) is > 0 and { } plus)
        {
            version = stamped[..plus];
        }

        return string.Create(CultureInfo.InvariantCulture, $"GumpStudio {version ?? "2.0.0"}");
    }
}
