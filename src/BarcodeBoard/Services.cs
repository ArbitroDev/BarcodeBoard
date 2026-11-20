using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BarcodeBoard.Core;

namespace BarcodeBoard;

public static class CodeImages
{
    /// <summary>
    /// Image à afficher : un pixel du bitmap = un pixel physique de l'écran. Le DPI du bitmap est celui
    /// de l'écran, donc WPF l'affiche sans redimensionnement (Stretch=None, mise à l'échelle au plus proche).
    /// </summary>
    /// <param name="maxWidthDip">Largeur disponible : un code plus large est affiné jusqu'à un pixel physique par
    /// module ; s'il ne tient toujours pas, aucune image (un code coupé ne se lit pas).</param>
    public static BitmapSource? ForScreen(CodeMatrix matrix, double sizeDip, double dpiScale, double maxWidthDip = double.PositiveInfinity)
    {
        int maxPx = double.IsInfinity(maxWidthDip) ? int.MaxValue : (int)Math.Floor(maxWidthDip * dpiScale);
        return CodeRaster.LayoutWithin(matrix, sizeDip, dpiScale, maxPx) is { } layout ? Create(matrix, layout, 96 * dpiScale) : null;
    }

    /// <summary>Même image que <see cref="ForScreen"/>, mais déclarée à 96 DPI pour le presse-papiers.</summary>
    public static BitmapSource ForClipboard(CodeMatrix matrix, double sizeDip, double dpiScale)
    {
        var layout = CodeRaster.Layout(matrix, sizeDip, dpiScale);
        return Create(matrix, layout, 96);
    }

    /// <summary>
    /// Plus grande image qui tient dans la zone donnée (en pixels logiques), pour le plein écran ;
    /// null si le code n'y tient pas même à un pixel par module (jamais un code coupé).
    /// </summary>
    public static BitmapSource? Fit(CodeMatrix matrix, double maxWidthDip, double maxHeightDip, double dpiScale)
    {
        bool qr = matrix.Height > 1;
        double size = qr
            ? Math.Min(maxWidthDip, maxHeightDip)
            : Math.Min(maxWidthDip / CodeRaster.BarcodeWidthRatio, maxHeightDip / CodeRaster.BarcodeHeightRatio);
        double maxW = maxWidthDip * dpiScale, maxH = maxHeightDip * dpiScale;
        var layout = CodeRaster.Layout(matrix, size, dpiScale);
        while (size > 20 && (layout.WidthPx > maxW || layout.HeightPx > maxH))
        {
            size -= 4;
            layout = CodeRaster.Layout(matrix, size, dpiScale);
        }
        // Code très long sur un écran mis à l'échelle : un module d'un pixel logique ne tient pas. On descend
        // alors jusqu'à un pixel physique (toujours entier, donc toujours net) plutôt que de rogner le code.
        if (layout.WidthPx > maxW)
        {
            if (CodeRaster.LayoutWithin(matrix, size, dpiScale, (int)maxW) is not { } narrower) return null;
            layout = narrower;
        }
        return Create(matrix, layout, 96 * dpiScale);
    }

    private static BitmapSource Create(CodeMatrix matrix, CodeLayout layout, double dpi)
    {
        var pixels = CodeRaster.Render(matrix, layout);
        var bmp = BitmapSource.Create(layout.WidthPx, layout.HeightPx, dpi, dpi, PixelFormats.Gray8, null, pixels, layout.WidthPx);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Copie en PNG et en bitmap classique, pour Word, Outlook, Teams et Paint.</summary>
    public static void CopyToClipboard(BitmapSource image)
    {
        var bgr = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        bgr.Freeze();
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);

        var data = new DataObject();
        data.SetImage(bgr);
        data.SetData("PNG", png);
        // WPF réessaie déjà plusieurs fois si une autre application tient le presse-papiers :
        // une boucle de plus ne ferait que figer l'interface. L'appelant signale l'échec.
        Clipboard.SetDataObject(data, true);
    }
}

/// <summary>Réglages de l'application, dans %LOCALAPPDATA%\BarcodeBoard\settings.json.</summary>
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

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarcodeBoard", "settings.json");

    /// <summary>Emplacement des réglages avant le changement de nom de l'application.</summary>
    private static string LegacyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodesEcran", "settings.json");

    /// <summary>Réglages communs à toutes les fenêtres (thème, taille des codes…) : la dernière fermée enregistre.</summary>
    public static AppSettings Current { get; } = Load();

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Fichier temporaire puis remplacement : jamais de réglages à moitié écrits.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
