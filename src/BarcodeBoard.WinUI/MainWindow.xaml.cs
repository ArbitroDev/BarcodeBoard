using Microsoft.UI.Xaml;

namespace BarcodeBoard;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = Loc.AppName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "BarcodeBoard.ico"));
    }
}
