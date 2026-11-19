using System.IO;
using System.Windows;
using System.Windows.Threading;
using BarcodeBoard.Core;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Fichiers .codes passés au lancement : double-clic dans l'Explorateur, glisser sur l'icône.
        var files = e.Args
            .Where(a => !a.StartsWith("--") && File.Exists(a)
                        && string.Equals(Path.GetExtension(a), CodesFile.Extension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .ToList();

        // Les options de vérification (--demo, --capture…) lancent une instance à part, sans déranger celle ouverte.
        bool checkRun = e.Args.Any(a => a.StartsWith("--"));
        if (!checkRun && !SingleInstance.TryBecomePrimary(AppWindows.HandleRequest) && SingleInstance.SendToPrimary(files))
        {
            Shutdown();
            return;
        }

        new MainWindow(startupFiles: files, isFirst: true).Show();
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
        var owner = AppWindows.LastActive is { IsLoaded: true } w ? w : null;
        var message = Loc.UnexpectedError(e.Exception.Message);
        if (owner is null) MessageBox.Show(message, Loc.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(owner, message, Loc.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
