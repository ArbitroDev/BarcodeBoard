using System.Globalization;

namespace BarcodeBoard.Core;

/// <summary>
/// Langue de l'interface : français si Windows est en français, anglais sinon. Choisie une fois au démarrage.
/// </summary>
public static class Lang
{
    public static bool French { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    /// <summary>Le texte dans la langue de l'interface.</summary>
    public static string T(string french, string english) => French ? french : english;
}
