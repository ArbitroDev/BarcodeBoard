using System.Text.RegularExpressions;

namespace BarcodeBoard.Core.Tests;

/// <summary>
/// Garde-fous sur les ressources XAML des thèmes, vérifiés sur le texte des fichiers.
/// Un nom de couleur de palette identique à une clé d'App.xaml fait planter l'application :
/// la clé d'App.xaml l'emporte, et un Style se retrouve à la place d'une couleur
/// (bug « 4 QR codes agrandis » : la barre de défilement recevait le style ScrollThumb).
/// </summary>
public class ThemeResourceTests
{
    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "BarcodeBoard");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/BarcodeBoard introuvable");
    }

    private static HashSet<string> Keys(string file) =>
        Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Palette_keys_never_reuse_an_App_xaml_key()
    {
        var palette = Keys(Path.Combine(AppDir(), "Themes", "Light.xaml"));
        var app = Keys(Path.Combine(AppDir(), "App.xaml"));
        Assert.Empty(palette.Intersect(app));
    }

    [Fact]
    public void Light_and_dark_palettes_define_the_same_keys()
    {
        var light = Keys(Path.Combine(AppDir(), "Themes", "Light.xaml"));
        var dark = Keys(Path.Combine(AppDir(), "Themes", "Dark.xaml"));
        Assert.Equal(light.Order(), dark.Order());
    }

    /// <summary>Toute ressource demandée par l'interface existe : une clé absente laisse une couleur vide sans erreur.</summary>
    [Fact]
    public void Every_requested_resource_is_defined()
    {
        var dir = AppDir();
        var defined = Directory.GetFiles(dir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(Keys).ToHashSet();

        var requested = new HashSet<string>();
        foreach (var f in Directory.GetFiles(dir, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\")))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{DynamicResource ([^}\s]+)\}"))
                requested.Add(m.Groups[1].Value);
        foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\")))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"SetResourceReference\([^,]+,\s*""([^""]+)""\)"))
                requested.Add(m.Groups[1].Value);

        Assert.Empty(requested.Except(defined));
    }
}
