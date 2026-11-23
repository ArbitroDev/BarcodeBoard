#if DEBUG
using System.Runtime.InteropServices;
using BarcodeBoard.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace BarcodeBoard;

/// <summary>
/// Scénarios de vérification visuelle, compilés en Debug uniquement.
/// Exemple : BarcodeBoard.exe --demo --scan --no-mica --capture C:\temp\scan.png
/// </summary>
internal static class DebugScenario
{
    public static void Run(MainWindow window, MainViewModel vm)
    {
        var args = Environment.GetCommandLineArgs();
        bool Has(string flag) => args.Contains(flag);
        string? Value(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        if (Value("--open") is { } file) window.OpenPath(file);
        if (Has("--topmost") && window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        if (Value("--theme") is { } theme) Theme.Apply(ThemeSetting.Parse(theme));
        if (Value("--compose") is { } typed && vm.SelectedTab is { } composing) composing.ComposerText = typed;
        if (Value("--close-after") is { } ms)
            window.DispatcherQueue.TryEnqueue(async () => { await Task.Delay(int.Parse(ms)); window.CloseWithoutPrompts(); });

        if (Has("--demo") && vm.SelectedTab is { } tab)
        {
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
        if (Has("--rename") && vm.SelectedTab is { } renamed) { renamed.BeginRename(); renamed.RenameText = "Quai 3 matin"; renamed.CommitRename(); }
        if (Has("--side-closed")) vm.SideOpen = false;
        if (Has("--edit") && vm.SelectedTab is { Codes.Count: > 0 } edited) edited.SelectedCode = edited.Codes[0];
        if (Has("--scan") && vm.SelectedTab is { } scanned)
        {
            vm.ScanMode = true;
            if (scanned.Codes.Count > 1) scanned.ScanFocus = scanned.Codes[1];
            vm.LastRead = scanned.ScanFocus?.Text;
        }
        if (Has("--full") && vm.SelectedTab is { Codes.Count: > 0 } full) window.DebugOpenFull(full.Codes[0]);

        if (Has("--simulate") && vm.SelectedTab is { } sim)
        {
            // Saisie + Entrée, puis F6, puis une « douchette » qui tape un texte et Entrée : sans clic ni frappe réelle.
            var log = new List<string>();
            int before = sim.Codes.Count;
            sim.ComposerText = "TEST-123";
            sim.AddFromComposer();
            log.Add($"ajout : {before} -> {sim.Codes.Count} codes, dernier = {sim.Codes[^1].Text}, zone vidée = {sim.ComposerText.Length == 0}");

            window.HandleKey(VirtualKey.F6, false, false, false);
            log.Add($"F6 : mode scan = {vm.ScanMode}, code net = {sim.ScanFocus?.Text}");

            var texts = sim.Codes.Select(c => c.Text).ToList();
            foreach (var ch in "PAL-000482-A") window.HandleCharacter(ch);
            window.HandleKey(VirtualKey.Enter, false, false, false);
            log.Add($"lecture 1 : « {vm.LastRead} », correspond = {vm.LastReadMatches}");
            window.HandleKey(VirtualKey.Right, false, false, false);
            log.Add($"flèche droite : code net = {sim.ScanFocus?.Text}");
            foreach (var ch in "XYZ") window.HandleCharacter(ch);
            window.HandleKey(VirtualKey.W, true, false, false); // Ctrl+W : caractère de contrôle, pas de fermeture
            window.HandleKey(VirtualKey.Space, false, false, false);
            window.HandleKey(VirtualKey.Enter, false, false, false);
            log.Add($"lecture 2 : « {vm.LastRead?.Replace("\x17", "<0x17>")} », correspond = {vm.LastReadMatches}");
            log.Add($"onglet inchangé par la douchette : {texts.SequenceEqual(sim.Codes.Select(c => c.Text))}, codes = {sim.Codes.Count}, onglets = {vm.Tabs.Count}");

            sim.Move(0, 3);
            log.Add($"déplacement 0 -> 3 : {string.Join(", ", sim.Codes.Take(4).Select(c => c.Text))}");
            if (Value("--capture") is { } p) File.WriteAllLines(Path.ChangeExtension(p, ".sim.txt"), log);
        }

        if (Value("--capture") is { } path)
        {
            window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                await Task.Delay(int.Parse(Value("--capture-delay") ?? "800"));
                await Capture(window, vm, path);
                window.CloseWithoutPrompts();
            });
        }
    }

    /// <summary>
    /// Capture de la fenêtre telle que Windows la compose (PrintWindow, contenu complet) : les codes dessinés par la
    /// composition y sont, au pixel près. Avec --no-mica, rien de l'écran ne se glisse sous la fenêtre.
    /// </summary>
    private static async Task Capture(MainWindow window, MainViewModel vm, string path)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(dc, bitmap);
        PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT);
        SelectObject(dc, old); // GetDIBits exige un bitmap qui n'est sélectionné dans aucun contexte
        var info = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var pixels = new byte[w * h * 4];
        GetDIBits(dc, bitmap, 0, (uint)h, pixels, ref info, 0);
        DeleteObject(bitmap);
        DeleteDC(dc);
        ReleaseDC(IntPtr.Zero, screen);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        double dpi = 96 * vm.DpiScale;
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)w, (uint)h, dpi, dpi, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using (var file = File.Create(path))
            await stream.AsStreamForRead().CopyToAsync(file);
        File.AppendAllText(Path.ChangeExtension(path, ".txt"), $"dpiScale={vm.DpiScale} window={w}x{h}\n");
    }

    private const uint PW_RENDERFULLCONTENT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
}
#endif
