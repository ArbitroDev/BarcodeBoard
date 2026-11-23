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
        var message = Loc.UnexpectedError(e.Exception.Message);
        if (AppWindows.LastActive is { } window && window.Content is FrameworkElement root && root.XamlRoot is not null)
        {
            ShowError(root, message);
            return;
        }
        // Aucune fenêtre prête (erreur au démarrage…) : message de Windows, qui n'a besoin d'aucune fenêtre.
        ShowNativeError(message);
        // Sans aucune fenêtre, rien à préserver : l'application s'arrête au lieu de rester ouverte, invisible.
        if (AppWindows.All.Count == 0) Exit();
    }

    private static async void ShowError(FrameworkElement root, string message)
    {
        try { await Dialogs.Show(root, Loc.AppName, message); }
        catch (Exception ex)
        {
            AppSettings.LogError(ex);
            ShowNativeError(message);
        }
    }

    private static void ShowNativeError(string message) =>
        Native.MessageBox(IntPtr.Zero, message, Loc.AppName, Native.MB_OK | Native.MB_ICONERROR);
}
