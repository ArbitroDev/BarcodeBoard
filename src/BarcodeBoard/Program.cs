using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using BarcodeBoard.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BarcodeBoard;

public static class Program
{
    /// <summary>Fichiers .codes passés au lancement (double-clic dans l'Explorateur, glisser sur l'icône).</summary>
    public static IReadOnlyList<string> StartupFiles { get; private set; } = [];

    [STAThread]
    private static int Main(string[] args)
    {
#if DEBUG
        // Vérifications : --lang en ou --lang fr force la langue au lieu de suivre Windows.
        int lang = Array.IndexOf(args, "--lang");
        if (lang >= 0 && lang + 1 < args.Length) Lang.French = args[lang + 1] == "fr";
#endif
        StartupFiles = args
            .Where(a => !a.StartsWith("--") && File.Exists(a)
                        && string.Equals(Path.GetExtension(a), CodesFile.Extension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .ToList();

        // Les options de vérification (--demo, --capture…) lancent une instance à part, sans déranger celle ouverte.
        bool checkRun = args.Any(a => a.StartsWith("--"));
        if (!checkRun && !SingleInstance.TryBecomePrimary() && SingleInstance.SendToPrimary(StartupFiles))
            return 0;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }
}

/// <summary>
/// Une seule instance de l'application par session Windows. Un deuxième lancement (double-clic sur un
/// fichier .codes dans l'Explorateur, raccourci…) transmet ses fichiers à l'instance déjà ouverte puis s'arrête.
/// </summary>
internal static class SingleInstance
{
    private static readonly string Name = $"BarcodeBoard-{Environment.UserName}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    private static Mutex? _mutex;

    /// <summary>Appelé sur le fil de l'interface quand un autre lancement envoie ses fichiers.</summary>
    public static event Action<IReadOnlyList<string>>? Requested;

    /// <summary>Vrai si cette instance est la première ; elle écoutera les demandes des suivantes (<see cref="Listen"/>).</summary>
    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Name}", out bool created);
        if (created) return true;
        // Instance secondaire : elle n'écoute jamais (le tube appartient à la première).
        _mutex.Dispose();
        _mutex = null;
        return false;
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

    /// <summary>Écoute les lancements suivants ; à appeler une fois l'interface prête (instance principale seulement).</summary>
    public static void Listen(DispatcherQueue ui)
    {
        if (_mutex is null) return;
        Task.Run(async () =>
        {
            bool logged = false;
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
                    ui.TryEnqueue(() => Requested?.Invoke(files));
                }
                catch (IOException)
                {
                    // Tube momentanément indisponible : on réessaie sans occuper le processeur.
                    await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    // Toute autre erreur (nom du tube refusé…) ne doit pas arrêter l'écoute en silence : sans elle, les
                    // lancements suivants ouvriraient une deuxième instance. Notée une fois, puis nouvel essai.
                    if (!logged) AppSettings.LogError(ex);
                    logged = true;
                    await Task.Delay(2000);
                }
            }
        });
    }

    private const int ASFW_ANY = -1;
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
