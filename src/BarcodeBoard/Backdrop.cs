using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace BarcodeBoard;

/// <summary>
/// Matériau Mica Alt de Windows 11 (22H2 et plus), celui des applications à onglets (Explorateur, Bloc-notes),
/// avec les vrais boutons système de la barre de titre. Sur Windows 10, rien ne change : la fenêtre garde
/// son fond opaque et ses boutons dessinés.
/// </summary>
internal static class Backdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TABBEDWINDOW = 4; // Mica Alt

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    private const int WM_NCCALCSIZE = 0x0083;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    /// <summary>
    /// Laisse Windows calculer les bords gauche, droit et bas de la fenêtre (ses bordures invisibles de
    /// redimensionnement) et ne retire que la barre de titre. Windows place ses boutons Réduire, Agrandir et Fermer
    /// en comptant sur ces bordures : sans elles, un écart apparaît entre le bouton Fermer et le bord de la fenêtre.
    /// </summary>
    private static IntPtr KeepSystemBorders(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCCALCSIZE || wParam == IntPtr.Zero) return IntPtr.Zero;
        // Le premier rectangle de NCCALCSIZE_PARAMS : la fenêtre en entrée, la zone cliente en sortie.
        int top = Marshal.PtrToStructure<Native.RECT>(lParam).Top;
        Native.DefWindowProc(hwnd, msg, wParam, lParam);
        var client = Marshal.PtrToStructure<Native.RECT>(lParam);
        client.Top = top;
        Marshal.StructureToPtr(client, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>À appeler depuis OnSourceInitialized. Vrai si Mica Alt est actif.</summary>
    public static bool TryApply(Window window)
    {
        if (!IsSupported) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        int type = DWMSBT_TABBEDWINDOW;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) != 0) return false;

        // Cadre étendu à toute la fenêtre : le système dessine Mica et ses boutons de titre derrière le contenu WPF.
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 42,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = true,
        });
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // Ajouté après WindowChrome, ce crochet passe avant le sien ; le cadre est ensuite recalculé.
        HwndSource.FromHwnd(hwnd)?.AddHook(KeepSystemBorders);
        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        SetDark(window, Theme.IsDark);
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
        return true;
    }

    /// <summary>Mica, cadre et boutons système en version sombre ou claire.</summary>
    public static void SetDark(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
