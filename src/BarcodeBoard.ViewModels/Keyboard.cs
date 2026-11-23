using System.Text;

namespace BarcodeBoard;

/// <summary>Ce que produit une touche, selon le mode de la fenêtre.</summary>
public enum KeyAction
{
    /// <summary>La touche n'est pas pour nous : elle suit son cours (saisie, navigation).</summary>
    None,
    /// <summary>La touche est absorbée sans effet.</summary>
    Swallow,
    CloseFull, FullPrevious, FullNext,
    NewTab, CloseTab, NextTab, PreviousTab,
    Open, Save, SaveAs, ToggleList, ToggleSettings,
    ToggleScan, ExitScan, ScanPrevious, ScanNext,
    /// <summary>Fin d'une lecture de la douchette (Entrée ou Tab).</summary>
    FinishRead,
    Backspace, Space,
    /// <summary>Caractère de contrôle tapé par la douchette en Ctrl+touche (GS du GS1-128…).</summary>
    ControlCharacter,
}

public readonly record struct KeyResult(KeyAction Action, char Character = '\0');

/// <summary>
/// Raccourcis clavier de la fenêtre. Les touches sont des codes de touche virtuelle Windows (VK_*).
/// En mode scan, une douchette USB « tape » ce qu'elle lit : aucune touche ne modifie l'onglet.
/// </summary>
public static class KeyRouting
{
    private const int Back = 0x08, Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Space = 0x20,
        Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, F6 = 0x75,
        OemComma = 0xBC, Oem4 = 0xDB, Oem5 = 0xDC, Oem6 = 0xDD, Oem102 = 0xE2;

    /// <param name="ctrl">Ctrl enfoncé. AltGr arrive comme Ctrl+Alt : ce n'est pas un raccourci, mais un caractère.</param>
    public static KeyResult Route(int key, bool ctrl, bool alt, bool shift, bool scanMode, bool fullScreen)
    {
        bool shortcut = ctrl && !alt;

        if (fullScreen)
        {
            // Rien d'autre ne réagit pendant le plein écran.
            return new(key switch
            {
                Escape => KeyAction.CloseFull,
                Left or Up => KeyAction.FullPrevious,
                Right or Down => KeyAction.FullNext,
                _ => KeyAction.Swallow,
            });
        }

        if (scanMode && shortcut)
        {
            // Les caractères de contrôle vont dans la lecture, jamais vers les raccourcis (Ctrl+W fermerait l'onglet).
            // Seul Ctrl+Tab change d'onglet.
            if (key == Tab) return new(shift ? KeyAction.PreviousTab : KeyAction.NextTab);
            return ControlCharacter(key) is char c ? new(KeyAction.ControlCharacter, c) : new(KeyAction.Swallow);
        }

        if (shortcut)
        {
            return new(key switch
            {
                'T' => KeyAction.NewTab,
                'W' => KeyAction.CloseTab,
                Tab => shift ? KeyAction.PreviousTab : KeyAction.NextTab,
                'O' => KeyAction.Open,
                'S' => shift ? KeyAction.SaveAs : KeyAction.Save,
                'L' => KeyAction.ToggleList,
                OemComma => KeyAction.ToggleSettings,
                _ => KeyAction.None,
            });
        }

        if (key == F6) return new(KeyAction.ToggleScan);

        if (scanMode)
        {
            return new(key switch
            {
                Escape => KeyAction.ExitScan,
                Left or Up => KeyAction.ScanPrevious,
                Right or Down => KeyAction.ScanNext,
                Enter or Tab => KeyAction.FinishRead,
                Space => KeyAction.Space,
                Back => KeyAction.Backspace,
                _ => KeyAction.None, // les caractères arrivent par la saisie de texte
            });
        }
        return new(KeyAction.None);
    }

    /// <summary>Caractère de contrôle ASCII tapé en Ctrl+touche : Ctrl+A à Ctrl+Z (1 à 26), Ctrl+[ \ ] (27 à 29).</summary>
    public static char? ControlCharacter(int key) => key switch
    {
        >= 'A' and <= 'Z' => (char)(key - 'A' + 1),
        Oem4 => '\x1B',
        Oem5 or Oem102 => '\x1C',
        Oem6 => '\x1D',
        _ => null,
    };
}

/// <summary>
/// Lecture en cours de la douchette, en mode scan. Les caractères imprimables arrivent par la saisie de texte ;
/// l'espace, Entrée, Tab, Retour arrière et les caractères de contrôle arrivent par les touches
/// (<see cref="KeyRouting"/>) : ignorés ici quand ils arrivent aussi en texte, ils ne comptent jamais deux fois.
/// </summary>
public sealed class ScanReader
{
    private readonly StringBuilder _buffer = new();

    /// <summary>Texte reçu de la saisie (caractère tapé, y compris avec AltGr).</summary>
    public void Type(string text)
    {
        foreach (var c in text)
            if (c > ' ' && c != '\x7F') _buffer.Append(c);
    }

    public void AddControl(char c) => _buffer.Append(c);
    public void AddSpace() => _buffer.Append(' ');

    public void Backspace()
    {
        if (_buffer.Length > 0) _buffer.Length--;
    }

    public void Clear() => _buffer.Clear();

    /// <summary>Texte lu depuis la dernière fin de lecture ; null s'il n'y a rien.</summary>
    public string? Finish()
    {
        if (_buffer.Length == 0) return null;
        var text = _buffer.ToString();
        _buffer.Clear();
        return text;
    }
}
