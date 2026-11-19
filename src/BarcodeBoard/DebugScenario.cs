#if DEBUG
using System.IO;
using System.Windows.Threading;
using BarcodeBoard.Core;

namespace BarcodeBoard;

/// <summary>
/// Scénarios de vérification visuelle, compilés en Debug uniquement.
/// Exemple : BarcodeBoard.exe --demo --scan --capture C:\temp\scan.png
/// </summary>
internal static class DebugScenario
{
    public static void Run(MainWindow window, MainViewModel vm)
    {
        var args = Environment.GetCommandLineArgs();
        bool Has(string flag) => args.Contains(flag);
        string? Value(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        if (Value("--open") is { } file) window.OpenPath(file);
        if (Has("--topmost")) window.Topmost = true;
        if (Value("--theme") is { } theme) Theme.Apply(Theme.Parse(theme));
        if (Value("--compose") is { } typed) vm.SelectedTab.ComposerText = typed;
        if (Has("--settings"))
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => window.DebugOpenSettings());
        if (Has("--ask-save"))
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => UnsavedDialog.Ask(window, vm.SelectedTab.Name));
        if (Value("--close-after") is { } ms)
            window.Dispatcher.BeginInvoke(async () => { await Task.Delay(int.Parse(ms)); window.CloseWithoutPrompts(); });

        if (Has("--demo"))
        {
            var tab = vm.SelectedTab;
            tab.Load([
                new("PAL-000482-A", CodeType.Code128),
                new(Lang.T("https://exemple.fr/suivi/88412", "https://example.com/track/88412"), CodeType.Qr),
                new("LOT-2026-11-0042", CodeType.Code128),
                new("LOC-A12-03-B", CodeType.Qr),
                new(Lang.T("ART-77810-BLEU-M", "ART-77810-BLUE-M"), CodeType.Code128),
                new(Lang.T("Réf. ÉTÉ-12", "Café-12"), CodeType.Code128),
            ], Lang.T("Réception quai 3", "Dock 3 receiving"));
            var second = vm.NewTab();
            second.Load([new("B-01-01", CodeType.Code128), new("B-01-02", CodeType.Code128)], Lang.T("Inventaire allée B", "Aisle B inventory"));
            second.IsDirty = true;
            vm.SelectedTab = tab;
        }
        if (Value("--size") is { } size) vm.CodeSize = double.Parse(size);
        if (Value("--tabs") is { } tabs)
        {
            var first = vm.SelectedTab;
            for (int i = 0; i < int.Parse(tabs); i++) vm.NewTab();
            if (Has("--first-tab")) vm.SelectedTab = first;
        }
        if (Has("--rename")) { vm.SelectedTab.BeginRename(); vm.SelectedTab.RenameText = "Quai 3 matin"; vm.SelectedTab.CommitRename(); }
        if (Has("--side-closed")) vm.SideOpen = false;
        if (Has("--edit") && vm.SelectedTab.Codes.Count > 0) vm.SelectedTab.SelectedCode = vm.SelectedTab.Codes[0];
        if (Has("--scan"))
        {
            vm.ScanMode = true;
            if (vm.SelectedTab.Codes.Count > 1) vm.SelectedTab.ScanFocus = vm.SelectedTab.Codes[1];
            vm.LastRead = vm.SelectedTab.ScanFocus?.Text;
        }
        if (Has("--full") && vm.SelectedTab.Codes.Count > 0) window.DebugOpenFull(vm.SelectedTab.Codes[0]);

        if (Has("--simulate"))
        {
            // Saisie + Entrée dans la zone de saisie, puis F6, puis une « douchette » qui tape un texte et Entrée.
            var log = new List<string>();
            var tab = vm.SelectedTab;
            int before = tab.Codes.Count;
            tab.ComposerText = "TEST-123";
            window.DebugKey(window.DebugComposer, System.Windows.Input.Key.Enter);
            log.Add($"ajout par Entrée : {before} -> {tab.Codes.Count} codes, dernier = {tab.Codes[^1].Text}, zone vidée = {tab.ComposerText.Length == 0}");

            window.DebugKey(window, System.Windows.Input.Key.F6);
            log.Add($"F6 : mode scan = {vm.ScanMode}, code net = {tab.ScanFocus?.Text}");

            var texts = tab.Codes.Select(c => c.Text).ToList();
            foreach (var ch in "PAL-000482-A") window.DebugText(window.DebugComposer, ch.ToString());
            window.DebugKey(window.DebugComposer, System.Windows.Input.Key.Enter);
            log.Add($"lecture 1 : « {vm.LastRead} », correspond = {vm.LastReadMatches}");
            window.DebugKey(window, System.Windows.Input.Key.Right);
            log.Add($"flèche droite : code net = {tab.ScanFocus?.Text}");
            foreach (var ch in "XYZ") window.DebugText(window, ch.ToString());
            window.DebugKey(window, System.Windows.Input.Key.Space);
            window.DebugKey(window, System.Windows.Input.Key.Enter);
            log.Add($"lecture 2 : « {vm.LastRead} », correspond = {vm.LastReadMatches}");
            log.Add($"onglet inchangé par la douchette : {texts.SequenceEqual(tab.Codes.Select(c => c.Text))}, codes = {tab.Codes.Count}");

            tab.Move(0, 3);
            log.Add($"déplacement 0 -> 3 : {string.Join(", ", tab.Codes.Take(4).Select(c => c.Text))}");
            if (Value("--capture") is { } p) File.WriteAllLines(Path.ChangeExtension(p, ".sim.txt"), log);
        }

        if (Value("--capture") is { } path)
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
            {
                await Task.Delay(int.Parse(Value("--capture-delay") ?? "500"));
                window.SaveSnapshot(path);
                window.CloseWithoutPrompts();
            });
        }
    }
}
#endif
