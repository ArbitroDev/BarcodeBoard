using System.Runtime.InteropServices;
using BarcodeBoard.Core;

namespace BarcodeBoard;

/// <summary>
/// Presse-papiers par l'API Win32 : l'image en DIB (format que Word, Outlook, Teams et Paint collent) et en PNG,
/// déclarées à 96 DPI pour garder leur taille au collage ; le texte en Unicode.
/// </summary>
internal static class Win32Clipboard
{
    private const uint CF_DIB = 8, CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;

    /// <summary>Faux si le presse-papiers reste occupé par une autre application.</summary>
    public static bool SetImage(IntPtr owner, CodeBitmap bitmap) =>
        Set(owner, [(CF_DIB, ImageEncoding.Dib(bitmap)), (RegisterClipboardFormat("PNG"), ImageEncoding.Png(bitmap))]);

    public static bool SetText(IntPtr owner, string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        System.Text.Encoding.Unicode.GetBytes(text, bytes);
        return Set(owner, [(CF_UNICODETEXT, bytes)]);
    }

    private static bool Set(IntPtr owner, (uint Format, byte[] Data)[] items)
    {
        // Une autre application peut tenir le presse-papiers un court instant : quelques essais, sans figer l'interface.
        bool opened = false;
        for (int attempt = 0; attempt < 5 && !(opened = OpenClipboard(owner)); attempt++) Thread.Sleep(15);
        if (!opened) return false;
        try
        {
            if (!EmptyClipboard()) return false;
            foreach (var (format, data) in items)
            {
                var memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (memory == IntPtr.Zero) return false;
                var target = GlobalLock(memory);
                if (target == IntPtr.Zero) { GlobalFree(memory); return false; }
                Marshal.Copy(data, 0, target, data.Length);
                GlobalUnlock(memory);
                // Accepté, le bloc appartient au système ; refusé, il reste à libérer.
                if (SetClipboardData(format, memory) == IntPtr.Zero) { GlobalFree(memory); return false; }
            }
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}

/// <summary>Position des fenêtres en pixels physiques, et mise au premier plan.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT minPosition, maxPosition; public RECT normalPosition; }

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const int SW_HIDE = 0;

    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

    public const uint MB_OK = 0, MB_ICONERROR = 0x10;
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, SM_SWAPBUTTON = 23;

    /// <summary>Vrai si le bouton principal de la souris est enfoncé (le droit pour un gaucher qui les a inversés).</summary>
    public static bool IsPrimaryButtonDown() =>
        (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON) & 0x8000) != 0;

    /// <summary>Position normale (non agrandie) de la fenêtre, en pixels physiques.</summary>
    public static RECT? NormalPosition(IntPtr hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        return hwnd != IntPtr.Zero && GetWindowPlacement(hwnd, ref placement) ? placement.normalPosition : null;
    }

    /// <summary>Place une fenêtre pas encore affichée à cette position normale (elle reste cachée).</summary>
    public static void SetNormalPosition(IntPtr hwnd, RECT rect)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return;
        placement.normalPosition = rect;
        placement.showCmd = SW_HIDE;
        SetWindowPlacement(hwnd, ref placement);
    }

    /// <summary>Vrai si ce rectangle touche encore un écran branché.</summary>
    public static bool IsOnAScreen(RECT rect) => MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
}
