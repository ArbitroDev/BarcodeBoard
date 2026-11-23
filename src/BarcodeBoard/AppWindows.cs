using Microsoft.UI;

namespace BarcodeBoard;

/// <summary>Les fenêtres ouvertes, de la plus récemment utilisée à la plus ancienne.</summary>
internal static class AppWindows
{
    private static readonly List<MainWindow> Windows = [];

    public static IReadOnlyList<MainWindow> All => Windows;

    /// <summary>Dernière fenêtre utilisée qui montre des onglets (une fenêtre vidée par une fusion va se fermer).</summary>
    public static MainWindow? LastActive => Windows.FirstOrDefault(w => w.ViewModel.HasTabs);

    public static void Register(MainWindow window)
    {
        Windows.Insert(0, window);
        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated) return;
            Windows.Remove(window);
            Windows.Insert(0, window);
        };
        window.Closed += (_, _) => Windows.Remove(window);
    }

    /// <summary>La fenêtre qui affiche ces onglets.</summary>
    public static MainWindow? For(MainViewModel vm) => Windows.FirstOrDefault(w => w.ViewModel == vm);

    public static MainWindow? For(WindowId id) => Windows.FirstOrDefault(w => w.AppWindow.Id.Value == id.Value);

    /// <summary>Fichiers .codes reçus d'un autre lancement : dans la dernière fenêtre utilisée, ou une nouvelle fenêtre.</summary>
    public static void HandleRequest(IReadOnlyList<string> files)
    {
        if (files.Count == 0 || LastActive is not { } target)
        {
            new MainWindow(startupFiles: files).Activate();
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
