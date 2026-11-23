using Microsoft.UI.Xaml;

namespace BarcodeBoard;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        new MainWindow(startupFiles: Program.StartupFiles, isFirst: true).ShowFirst();
        // Lancements suivants (double-clic sur un .codes…) : leurs fichiers arrivent ici.
        SingleInstance.Requested += AppWindows.HandleRequest;
        SingleInstance.Listen(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    }

    /// <summary>
    /// Filet de sécurité : une erreur imprévue est signalée et notée dans un journal, mais l'application reste
    /// ouverte, pour ne pas perdre les onglets non enregistrés.
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        AppSettings.LogError(e.Exception);
        if (AppWindows.LastActive is { } window && window.Content is FrameworkElement root && root.XamlRoot is not null)
            _ = Dialogs.Show(root, Loc.AppName, Loc.UnexpectedError(e.Exception.Message));
    }
}
