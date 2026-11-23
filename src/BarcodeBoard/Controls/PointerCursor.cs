using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace BarcodeBoard.Controls;

/// <summary>
/// Curseurs des éléments. WinUI pose lui-même le curseur à chaque mouvement du pointeur, d'après le
/// <c>ProtectedCursor</c> de l'élément survolé : le compteur d'affichage de Win32 (<c>ShowCursor</c>) n'y a pas
/// d'effet, et WinUI n'a pas de forme « aucun ». Le curseur masqué est donc un curseur Win32 transparent,
/// confié à WinUI par son interface d'interopérabilité.
/// </summary>
public static class PointerCursor
{
    /// <summary>
    /// Forme du curseur au-dessus d'un élément (XAML : <c>controls:PointerCursor.Shape="SizeWestEast"</c>).
    /// <c>ProtectedCursor</c> n'est accessible qu'aux classes dérivées : il est posé par réflexion.
    /// </summary>
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.RegisterAttached(
        "Shape", typeof(InputSystemCursorShape), typeof(PointerCursor),
        new PropertyMetadata(InputSystemCursorShape.Arrow, (d, e) =>
            ProtectedCursorProperty.SetValue(d, InputSystemCursor.Create((InputSystemCursorShape)e.NewValue))));

    public static InputSystemCursorShape GetShape(UIElement element) => (InputSystemCursorShape)element.GetValue(ShapeProperty);
    public static void SetShape(UIElement element, InputSystemCursorShape value) => element.SetValue(ShapeProperty, value);

    private static readonly PropertyInfo ProtectedCursorProperty =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static InputCursor? _hidden;
    private static bool _hiddenFailed;

    /// <summary>Curseur invisible ; null s'il n'a pas pu être créé (le pointeur reste alors visible).</summary>
    internal static InputCursor? Hidden
    {
        get
        {
            if (_hidden is null && !_hiddenFailed)
            {
                try { _hidden = CreateHidden(); }
                catch (Exception ex)
                {
                    _hiddenFailed = true;
                    AppSettings.LogError(ex);
                }
            }
            return _hidden;
        }
    }

    private static InputCursor CreateHidden()
    {
        // Masque ET à 1 (l'écran reste tel quel) et masque OU exclusif à 0 : aucun pixel dessiné.
        const int size = 32;
        var and = Enumerable.Repeat((byte)0xFF, size * size / 8).ToArray();
        var xor = new byte[size * size / 8];
        IntPtr cursor = CreateCursor(GetModuleHandle(null), 0, 0, size, size, and, xor);
        if (cursor == IntPtr.Zero) throw new InvalidOperationException($"CreateCursor : erreur {Marshal.GetLastWin32Error()}");

        // IInputCursorStaticsInterop (Microsoft.UI.Input.InputCursor.Interop.h) : CreateFromHCursor est la
        // première méthode après celles d'IInspectable (emplacement 6 de la table virtuelle).
        using var factory = WinRT.ActivationFactory.Get("Microsoft.UI.Input.InputCursor", InputCursorStaticsInteropIid);
        IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory.ThisPtr), 6 * IntPtr.Size);
        var createFromHCursor = Marshal.GetDelegateForFunctionPointer<CreateFromHCursorMethod>(method);
        Marshal.ThrowExceptionForHR(createFromHCursor(factory.ThisPtr, cursor, out IntPtr result));
        try { return InputCursor.FromAbi(result); }
        finally { Marshal.Release(result); }
    }

    private static readonly Guid InputCursorStaticsInteropIid = new("ac6f5065-90c4-46ce-beb7-05e138e54117");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateFromHCursorMethod(IntPtr self, IntPtr cursor, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateCursor(IntPtr instance, int hotSpotX, int hotSpotY, int width, int height, byte[] andPlane, byte[] xorPlane);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
