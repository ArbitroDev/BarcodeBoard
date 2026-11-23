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

/// <summary>
/// Mêmes garde-fous pour l'application WinUI : palettes claire et sombre (ThemeDictionaries d'App.xaml) aux clés
/// identiques, sans collision avec les autres clés d'App.xaml, et toute ressource demandée définie.
/// </summary>
public class WinUiThemeResourceTests
{
    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "BarcodeBoard.WinUI");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/BarcodeBoard.WinUI introuvable");
    }

    private static string AppXaml => File.ReadAllText(Path.Combine(AppDir(), "App.xaml"));

    private static HashSet<string> Keys(string xaml) =>
        Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

    private static string Palette(string theme) =>
        Regex.Match(AppXaml, $"<ResourceDictionary x:Key=\"{theme}\">(.*?)</ResourceDictionary>", RegexOptions.Singleline).Groups[1].Value;

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.GetFiles(AppDir(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Light_and_dark_palettes_define_the_same_keys()
    {
        var light = Keys(Palette("Light"));
        Assert.NotEmpty(light);
        Assert.Equal(light.Order(), Keys(Palette("Dark")).Order());
    }

    [Fact]
    public void Palette_keys_never_reuse_another_App_xaml_key()
    {
        var palette = Keys(Palette("Light"));
        var outside = Regex.Replace(AppXaml, "<ResourceDictionary.ThemeDictionaries>.*?</ResourceDictionary.ThemeDictionaries>", "", RegexOptions.Singleline);
        Assert.Empty(palette.Intersect(Keys(outside)));
    }

    /// <summary>
    /// Une clé absente laisse une couleur vide ou fait planter le chargement. Les clés propres à l'application
    /// doivent être définies ; les autres sont des ressources de WinUI (…Brush, …Style…).
    /// </summary>
    [Fact]
    public void Every_requested_resource_is_defined()
    {
        var defined = SourceFiles("*.xaml").SelectMany(f => Keys(File.ReadAllText(f))).ToHashSet();
        var requested = new HashSet<string>();
        foreach (var f in SourceFiles("*.xaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{(?:ThemeResource|StaticResource) ([^}\s]+)\}"))
                requested.Add(m.Groups[1].Value);
        var fromCode = new HashSet<string>();
        foreach (var f in SourceFiles("*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Theme\.Brush\(""([^""]+)"""))
                fromCode.Add(m.Groups[1].Value);

        string[] systemSuffixes = ["Brush", "Style", "FontFamily", "FontSize", "Thickness", "CornerRadius", "Margin", "Padding"];
        var missing = requested.Where(k => !defined.Contains(k) && !systemSuffixes.Any(k.EndsWith)).ToList();
        Assert.Empty(missing);
        Assert.Empty(fromCode.Except(Keys(Palette("Light"))));
    }
}
