using System.Text.Json;

namespace BarcodeBoard;

/// <summary>Réglages de l'application, dans %LOCALAPPDATA%\BarcodeBoard\settings.json (même format qu'en version 1).</summary>
public sealed class AppSettings
{
    public double CodeSize { get; set; } = MainViewModel.DefaultSize;
    public bool SideOpen { get; set; } = true;
    public double SideWidth { get; set; } = 360;
    /// <summary>"system" (comme Windows), "light" ou "dark".</summary>
    public string Theme { get; set; } = "system";
    public string? LastFolder { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    /// <summary>Position normale de la fenêtre en pixels physiques (gauche, haut, droite, bas), telle que Windows la donne.</summary>
    public int[]? Placement { get; set; }
    public bool Maximized { get; set; }

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarcodeBoard");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Emplacement des réglages avant le changement de nom de l'application.</summary>
    private static string LegacyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodesEcran", "settings.json");

    /// <summary>Journal des erreurs imprévues.</summary>
    public static string ErrorLogPath => Path.Combine(Folder, "errors.log");

    /// <summary>Réglages communs à toutes les fenêtres (thème, taille des codes…) : la dernière fermée enregistre.</summary>
    public static AppSettings Current { get; } = Load();

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path)) return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    /// <summary>Réglages lus d'un texte JSON ; un texte illisible donne les réglages par défaut.</summary>
    public static AppSettings Parse(string json)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Fichier temporaire puis remplacement : jamais de réglages à moitié écrits.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, ToJson());
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Ajoute une erreur imprévue au journal ; un journal impossible à écrire est ignoré.</summary>
    public static void LogError(Exception error)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(ErrorLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public enum ThemeChoice { System, Light, Dark }

/// <summary>Valeurs du thème dans les réglages.</summary>
public static class ThemeSetting
{
    public static ThemeChoice Parse(string? value) => value switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    public static string ToSetting(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        _ => "system",
    };
}
