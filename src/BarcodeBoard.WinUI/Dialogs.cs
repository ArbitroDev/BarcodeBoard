using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarcodeBoard;

public enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>Boîtes de dialogue de WinUI (ContentDialog), dans le thème de la fenêtre.</summary>
internal static class Dialogs
{
    /// <summary>« Enregistrer les modifications de X ? » avec Enregistrer / Ne pas enregistrer / Annuler.</summary>
    public static async Task<UnsavedChoice> AskUnsaved(FrameworkElement root, string tabName)
    {
        var dialog = Create(root, Loc.UnsavedQuestion(tabName), Loc.UnsavedExplanation);
        dialog.PrimaryButtonText = Loc.Save;
        dialog.SecondaryButtonText = Loc.DontSave;
        dialog.CloseButtonText = Loc.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChoice.Save,
            ContentDialogResult.Secondary => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    /// <summary>Message d'erreur avec un seul bouton.</summary>
    public static async Task Show(FrameworkElement root, string title, string message)
    {
        var dialog = Create(root, title, message);
        dialog.CloseButtonText = "OK";
        dialog.DefaultButton = ContentDialogButton.Close;
        await dialog.ShowAsync();
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
