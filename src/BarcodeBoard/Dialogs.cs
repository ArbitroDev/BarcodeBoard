using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarcodeBoard;

public enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>
/// Boîtes de dialogue de WinUI (ContentDialog), dans le thème de la fenêtre. WinUI n'en ouvre qu'une à la fois
/// par fenêtre : les suivantes attendent leur tour (plusieurs fichiers invalides, erreur pendant une question…).
/// </summary>
internal static class Dialogs
{
    private static readonly ConditionalWeakTable<XamlRoot, SemaphoreSlim> Turns = [];

    private static async Task<ContentDialogResult> ShowInTurn(ContentDialog dialog)
    {
        var turn = Turns.GetValue(dialog.XamlRoot, _ => new SemaphoreSlim(1, 1));
        await turn.WaitAsync();
        try { return await dialog.ShowAsync(); }
        finally { turn.Release(); }
    }

    /// <summary>« Enregistrer les modifications de X ? » avec Enregistrer / Ne pas enregistrer / Annuler.</summary>
    public static async Task<UnsavedChoice> AskUnsaved(FrameworkElement root, string tabName)
    {
        var dialog = Create(root, Loc.UnsavedQuestion(tabName), Loc.UnsavedExplanation);
        dialog.PrimaryButtonText = Loc.Save;
        dialog.SecondaryButtonText = Loc.DontSave;
        dialog.CloseButtonText = Loc.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await ShowInTurn(dialog) switch
        {
            ContentDialogResult.Primary => UnsavedChoice.Save,
            ContentDialogResult.Secondary => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    /// <summary>Demande un texte (nom d'un onglet…) ; null si l'utilisateur annule.</summary>
    public static async Task<string?> AskText(FrameworkElement root, string title, string text)
    {
        var box = new TextBox { Text = text, MinWidth = 320 };
        var dialog = Create(root, title, "");
        dialog.Content = box;
        dialog.PrimaryButtonText = Loc.Ok;
        dialog.CloseButtonText = Loc.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        return await ShowInTurn(dialog) == ContentDialogResult.Primary ? box.Text : null;
    }

    /// <summary>Message d'erreur avec un seul bouton.</summary>
    public static async Task Show(FrameworkElement root, string title, string message)
    {
        var dialog = Create(root, title, message);
        dialog.CloseButtonText = Loc.Ok;
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowInTurn(dialog);
    }

    private static ContentDialog Create(FrameworkElement root, string title, string message) => new()
    {
        XamlRoot = root.XamlRoot,
        // Une boîte de dialogue s'affiche hors de l'arbre de la fenêtre : elle ne reprend pas son thème d'elle-même.
        RequestedTheme = root.ActualTheme,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        Title = title,
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
    };
}
