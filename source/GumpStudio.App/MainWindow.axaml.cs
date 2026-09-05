using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
