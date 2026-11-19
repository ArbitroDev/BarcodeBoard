using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace BarcodeBoard;

/// <summary>
/// Une seule instance de l'application par session Windows. Un deuxième lancement (double-clic sur un
/// fichier .codes dans l'Explorateur, raccourci…) transmet ses fichiers à l'instance déjà ouverte puis s'arrête.
/// </summary>
internal static class SingleInstance
{
    private static readonly string Name = $"BarcodeBoard-{Environment.UserName}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    private static Mutex? _mutex;

    /// <summary>Vrai si cette instance est la première ; elle reçoit alors les demandes des suivantes.</summary>
    public static bool TryBecomePrimary(Action<IReadOnlyList<string>> onRequest)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Name}", out bool created);
        if (!created) return false;
        Task.Run(() => Listen(onRequest));
        return true;
    }

    /// <summary>Confie les fichiers à l'instance déjà ouverte (aucun fichier : elle ouvre une nouvelle fenêtre).</summary>
    public static bool SendToPrimary(IReadOnlyList<string> files)
    {
        try
        {
            // L'instance qui reçoit pourra passer au premier plan : c'est l'utilisateur qui vient de lancer celle-ci.
            AllowSetForegroundWindow(ASFW_ANY);
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.Write(string.Join('\n', files));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task Listen(Action<IReadOnlyList<string>> onRequest)
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var text = await reader.ReadToEndAsync();
                var files = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Application.Current?.Dispatcher.BeginInvoke(() => onRequest(files));
            }
            catch (IOException) { }
        }
    }

    private const int ASFW_ANY = -1;
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}

/// <summary>Les fenêtres ouvertes, de la plus récemment utilisée à la plus ancienne.</summary>
internal static class AppWindows
{
    private static readonly List<MainWindow> Windows = [];

    public static IReadOnlyList<MainWindow> All => Windows;

    public static MainWindow? LastActive => Windows.FirstOrDefault();

    public static void Register(MainWindow window)
    {
        Windows.Insert(0, window);
        window.Activated += (_, _) => { Windows.Remove(window); Windows.Insert(0, window); };
        window.Closed += (_, _) => Windows.Remove(window);
    }

    /// <summary>Fichiers .codes reçus d'un autre lancement : dans la dernière fenêtre utilisée, ou une nouvelle fenêtre.</summary>
    public static void HandleRequest(IReadOnlyList<string> files)
    {
        if (files.Count == 0 || LastActive is not { } target)
        {
            var window = new MainWindow(startupFiles: files);
            window.Show();
            return;
        }
        foreach (var file in files) target.OpenPath(file);
        target.BringToFront();
    }

    /// <summary>Fenêtre et onglet où ce fichier est déjà ouvert, s'il l'est.</summary>
    public static (MainWindow Window, TabViewModel Tab)? FindFile(string fullPath)
    {
        foreach (var w in Windows)
            if (w.FindTab(fullPath) is { } tab) return (w, tab);
        return null;
    }
}
