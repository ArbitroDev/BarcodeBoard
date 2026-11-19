using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BarcodeBoard;

public partial class App : Application
{
    public App()
    {
#if DEBUG
        // Vérifications : --lang en ou --lang fr force la langue au lieu de suivre Windows.
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--lang");
        if (i >= 0 && i + 1 < args.Length) Core.Lang.French = args[i + 1] == "fr";
#endif
        DispatcherUnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Filet de sécurité : une erreur imprévue est signalée et notée dans un journal, mais l'application reste
    /// ouverte, pour ne pas perdre les onglets non enregistrés.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarcodeBoard", "errors.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        e.Handled = true;
        var owner = MainWindow is { IsLoaded: true } w ? w : null;
        var message = Loc.UnexpectedError(e.Exception.Message);
        if (owner is null) MessageBox.Show(message, Loc.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(owner, message, Loc.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
