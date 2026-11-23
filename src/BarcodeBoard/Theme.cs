using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BarcodeBoard;

/// <summary>
/// Thème clair ou sombre de toutes les fenêtres. « Comme Windows » laisse chaque fenêtre suivre le mode des
/// applications de Windows (Paramètres &gt; Personnalisation &gt; Couleurs), y compris quand il change.
/// </summary>
internal static class Theme
{
    public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;

    /// <summary>Déclenché après chaque changement de choix : chaque fenêtre applique le nouveau thème.</summary>
    public static event Action? Changed;

    public static void Apply(ThemeChoice choice)
    {
        Choice = choice;
        Changed?.Invoke();
    }

    public static ElementTheme Requested => Choice switch
    {
        ThemeChoice.Light => ElementTheme.Light,
        ThemeChoice.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Boutons Réduire / Agrandir / Fermer, dessinés par Windows, dans le thème de la fenêtre.</summary>
    public static TitleBarTheme TitleBar => Choice switch
    {
        ThemeChoice.Light => TitleBarTheme.Light,
        ThemeChoice.Dark => TitleBarTheme.Dark,
        _ => TitleBarTheme.UseDefaultAppMode,
    };

    /// <summary>Pinceau de la palette d'un thème (App.xaml), pour les fonds choisis en code (couches sur Mica ou non).</summary>
    public static Brush Brush(string key, ElementTheme actual)
    {
        var palette = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[actual == ElementTheme.Dark ? "Dark" : "Light"];
        return (Brush)palette[key];
    }
}
