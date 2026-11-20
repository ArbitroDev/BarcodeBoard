using BarcodeBoard.Core;

namespace BarcodeBoard;

/// <summary>
/// Textes de l'interface, en français et en anglais. La langue suit celle de Windows (<see cref="Lang"/>).
/// Dans le XAML : <c>{x:Static local:Loc.Save}</c>.
/// </summary>
public static class Loc
{
    private static string T(string french, string english) => Lang.T(french, english);

    // ---- Application ----
    public static string AppName => T("Tableau de code-barres", "Barcode Board");

    // ---- Barre de titre et onglets ----
    public static string Minimize => T("Réduire", "Minimize");
    public static string Maximize => T("Agrandir", "Maximize");
    public static string Close => T("Fermer", "Close");
    public static string SettingsTip => T("Paramètres (Ctrl+,)", "Settings (Ctrl+,)");
    public static string Settings => T("Paramètres", "Settings");
    public static string AppTheme => T("Thème de l'application", "App theme");
    public static string ThemeSystem => T("Comme Windows", "Use Windows setting");
    public static string ThemeLight => T("Clair", "Light");
    public static string ThemeDark => T("Sombre", "Dark");
    public static string CloseTabTip => T("Fermer l'onglet (Ctrl+W)", "Close tab (Ctrl+W)");
    public static string NewTabTip => T("Nouvel onglet (Ctrl+T)", "New tab (Ctrl+T)");
    public static string AllTabs => T("Tous les onglets", "All tabs");
    public static string MoveToNewWindow => T("Déplacer vers une nouvelle fenêtre", "Move to new window");
    public static string MoveToWindow => T("Déplacer vers la fenêtre", "Move to window");
    public static string WindowLabel(string selectedTab, int tabCount) =>
        tabCount == 1 ? selectedTab : T($"{selectedTab} et {tabCount - 1} autre(s)", $"{selectedTab} and {tabCount - 1} more");
    public static string CloseTab => T("Fermer l'onglet", "Close tab");
    public static string Untitled(int n) => T($"Sans titre {n}", $"Untitled {n}");

    // ---- Barre d'outils ----
    public static string List => T("Liste", "List");
    public static string ListTip => T("Afficher ou masquer la liste (Ctrl+L)", "Show or hide the list (Ctrl+L)");
    public static string Open => T("Ouvrir", "Open");
    public static string OpenTip => T("Ouvrir un onglet enregistré (Ctrl+O)", "Open a saved tab (Ctrl+O)");
    public static string Save => T("Enregistrer", "Save");
    public static string SaveTip => T("Enregistrer l'onglet (Ctrl+S)", "Save tab (Ctrl+S)");
    public static string SaveMoreTip => T("Autres options d'enregistrement", "More save options");
    public static string SaveAs => T("Enregistrer sous…", "Save as…");
    public static string SaveAsShortcut => T("Ctrl+Maj+S", "Ctrl+Shift+S");
    public static string CodeSize => T("Taille des codes", "Code size");
    public static string CodeSizeTip => T("Taille de tous les codes", "Size of all codes");
    public static string ScanMode => T("Mode scan", "Scan mode");
    public static string ScanModeTip => T("Mode scan (F6)", "Scan mode (F6)");
    public static string NotSavedYet => T("Pas encore enregistré", "Not saved yet");
    public static string UnsavedChanges(string file) => T($"{file}, modifications non enregistrées", $"{file}, unsaved changes");
    public static string SavedIn(string file) => T($"Enregistré dans {file}", $"Saved to {file}");

    // ---- Liste latérale ----
    public static string ResizeListTip => T("Glisser pour redimensionner la liste", "Drag to resize the list");
    public static string DragToMove => T("Glisser pour déplacer", "Drag to move");
    public static string QrCode => "QR code";
    public static string Code128 => "Code 128";
    public static string Code128Tip => T("Code-barres (Code 128)", "Barcode (Code 128)");
    public static string TextToEncode => T("Texte à encoder", "Text to encode");
    public static string CopyImage => T("Copier l'image", "Copy image");
    public static string Delete => T("Supprimer", "Delete");
    public static string EnterKey => T("Entrée", "Enter");
    public static string AddRow => T("Ajouter une ligne", "Add a row");
    public static string Count(int n) => n switch
    {
        0 => T("Aucun code", "No codes"),
        1 => "1 code",
        _ => $"{n} codes",
    };

    // ---- Zone de saisie et grille ----
    public static string EditingSelected => T("Modification du code sélectionné", "Editing the selected code");
    public static string Done => T("Terminer", "Done");
    public static string Add => T("Ajouter", "Add");
    public static string TooWide => T("Trop large pour être lu en entier ici : agrandissez la fenêtre ou ouvrez-le en plein écran.",
                                      "Too wide to be read in full here: widen the window or open it in full screen.");
    public static string TooWideFull => T("Trop large pour être lu en entier, même en plein écran : agrandissez la fenêtre ou passez-le en QR code.",
                                          "Too wide to be read in full, even in full screen: widen the window or switch it to a QR code.");
    public static string SwitchToQr => T("Passer en QR code", "Switch to QR code");
    public static string FullScreen => T("Plein écran", "Full screen");
    public static string CopyTextTip => T("Cliquer pour copier le texte", "Click to copy the text");
    public static string EmptyTab => T("Cet onglet est vide. Tapez un texte au-dessus, puis Ajouter.",
                                       "This tab is empty. Type some text above, then Add.");

    // ---- Mode scan ----
    public static string ExitScan => T("Quitter (Échap)", "Exit (Esc)");
    public static string ScanBanner => T("Mode scan : seul le code cliqué reste net. Flèches pour passer au code suivant ou précédent.",
                                         "Scan mode: only the clicked code stays sharp. Use the arrow keys to move to the next or previous code.");
    public static string ReadByScanner => T("Lu par la douchette : ", "Read by the scanner: ");
    public static string ReadMatches => T("✓ identique au code affiché", "✓ matches the displayed code");
    public static string ReadDiffers => T("✗ différent du code affiché", "✗ differs from the displayed code");

    // ---- Plein écran ----
    public static string Previous => T("Précédent", "Previous");
    public static string Next => T("Suivant", "Next");
    public static string CloseEsc => T("Fermer (Échap)", "Close (Esc)");
    public static string Counter(int index, int total) => T($"{index} sur {total}", $"{index} of {total}");

    // ---- Fichiers ----
    public static string FileFilter => T("Onglets de codes (*.codes)|*.codes", "Barcode Board tabs (*.codes)|*.codes");
    public static string OpenTitle => T("Ouvrir un onglet", "Open a tab");
    public static string OpenFailedTitle => T("Ouverture impossible", "Unable to open");
    public static string SaveTitle => T("Enregistrer l'onglet", "Save tab");
    public static string SaveFailedTitle => T("Enregistrement impossible", "Unable to save");
    public static string SaveFailed(string path) => T($"L'onglet n'a pas pu être enregistré dans {path}.", $"The tab could not be saved to {path}.");
    public static string SavedTo(string file) => T($"Onglet enregistré dans {file}", $"Tab saved to {file}");

    // ---- Messages temporaires ----
    public static string TypeBeforeAdd => T("Tapez un texte avant d'ajouter un code", "Type some text before adding a code");
    public static string ImageCopied => T("Image du code copiée", "Code image copied");
    public static string ClipboardBusy => T("Le presse-papiers est occupé par une autre application. Réessayez.",
                                            "The clipboard is in use by another application. Try again.");
    public static string NothingToCopy => T("Rien à copier : le texte est vide", "Nothing to copy: the text is empty");
    public static string TextCopied(string text) => T($"Texte copié : {text}", $"Text copied: {text}");
    public static string CodeDeleted => T("Code supprimé", "Code deleted");

    // ---- Erreur imprévue ----
    public static string UnexpectedError(string detail) => T(
        $"Une erreur inattendue s'est produite. L'application reste ouverte : pensez à enregistrer vos onglets.\n\n{detail}",
        $"An unexpected error occurred. The app stays open: remember to save your tabs.\n\n{detail}");

    // ---- Modifications non enregistrées ----
    public static string UnsavedQuestion(string tab) => T($"Enregistrer les modifications de « {tab} » ?", $"Save changes to \"{tab}\"?");
    public static string UnsavedExplanation => T("Sans enregistrement, les modifications de cet onglet seront perdues.",
                                                 "If you don't save, changes to this tab will be lost.");
    public static string DontSave => T("Ne pas enregistrer", "Don't save");
    public static string Cancel => T("Annuler", "Cancel");
}
