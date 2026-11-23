namespace BarcodeBoard.Core;

/// <summary>
/// Image d'un code prête à afficher : un pixel = un pixel physique de l'écran, niveaux de gris 8 bits
/// (0 noir, 255 blanc), une ligne = <see cref="Width"/> octets. <see cref="Scale"/> est l'échelle de l'écran
/// pour laquelle elle a été calculée (1 = 100 %) : affichée à <see cref="WidthDip"/> × <see cref="HeightDip"/>
/// pixels logiques, elle n'est pas redimensionnée.
/// </summary>
public sealed record CodeBitmap(int Width, int Height, byte[] Pixels, double Scale)
{
    public double WidthDip => Width / Scale;
    public double HeightDip => Height / Scale;
}

/// <summary>Images des codes : taille de la carte, plein écran, presse-papiers, version floutée du mode scan.</summary>
public static class CodeBitmaps
{
    /// <summary>
    /// Image de la carte. <paramref name="maxWidthDip"/> est la largeur disponible : un code plus large est affiné
    /// jusqu'à un pixel physique par module ; s'il ne tient toujours pas, aucune image (un code coupé ne se lit pas).
    /// </summary>
    public static CodeBitmap? ForScreen(CodeMatrix matrix, double sizeDip, double dpiScale, double maxWidthDip = double.PositiveInfinity)
    {
        dpiScale = ValidScale(dpiScale);
        int maxPx = double.IsInfinity(maxWidthDip) ? int.MaxValue : (int)Math.Floor(maxWidthDip * dpiScale);
        return CodeRaster.LayoutWithin(matrix, sizeDip, dpiScale, maxPx) is { } layout ? Create(matrix, layout, dpiScale) : null;
    }

    /// <summary>Même image que la carte à sa taille réglée, pour le presse-papiers.</summary>
    public static CodeBitmap ForClipboard(CodeMatrix matrix, double sizeDip, double dpiScale)
    {
        dpiScale = ValidScale(dpiScale);
        return Create(matrix, CodeRaster.Layout(matrix, sizeDip, dpiScale), dpiScale);
    }

    /// <summary>
    /// Plus grande image qui tient dans la zone donnée (en pixels logiques), pour le plein écran ;
    /// null si le code n'y tient pas même à un pixel par module (jamais un code coupé).
    /// </summary>
    public static CodeBitmap? Fit(CodeMatrix matrix, double maxWidthDip, double maxHeightDip, double dpiScale)
    {
        dpiScale = ValidScale(dpiScale);
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
            if (CodeRaster.LayoutWithin(matrix, size, dpiScale, (int)Math.Floor(maxW)) is not { } narrower) return null;
            layout = narrower;
        }
        return Create(matrix, layout, dpiScale);
    }

    /// <summary>
    /// Version floutée d'une image (mode scan : seul le code visé reste lisible), de même taille.
    /// Trois flous en boîte successifs, proches d'un flou gaussien ; les bords prolongent les pixels du bord.
    /// </summary>
    public static CodeBitmap Blur(CodeBitmap bitmap, double radiusDip)
    {
        int w = bitmap.Width, h = bitmap.Height;
        // Trois boîtes de demi-largeur r donnent un écart-type proche de r ; un rayon de flou correspond à environ 3 écarts-types.
        int r = Math.Max(1, (int)Math.Round(radiusDip * bitmap.Scale / 3));
        var a = new int[w * h];
        for (int i = 0; i < a.Length; i++) a[i] = bitmap.Pixels[i];
        var b = new int[w * h];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxRows(a, b, w, h, r);
            BoxColumns(b, a, w, h, r);
        }
        var result = new byte[w * h];
        for (int i = 0; i < a.Length; i++) result[i] = (byte)Math.Clamp(a[i], 0, 255);
        return bitmap with { Pixels = result };
    }

    private static void BoxRows(int[] src, int[] dst, int w, int h, int r)
    {
        int span = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int sum = 0;
            for (int k = -r; k <= r; k++) sum += src[row + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = (sum + span / 2) / span;
                sum += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
            }
        }
    }

    private static void BoxColumns(int[] src, int[] dst, int w, int h, int r)
    {
        int span = 2 * r + 1;
        for (int x = 0; x < w; x++)
        {
            int sum = 0;
            for (int k = -r; k <= r; k++) sum += src[Math.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (sum + span / 2) / span;
                sum += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
            }
        }
    }

    private static double ValidScale(double dpiScale) => dpiScale > 0 ? dpiScale : 1;

    private static CodeBitmap Create(CodeMatrix matrix, CodeLayout layout, double dpiScale) =>
        new(layout.WidthPx, layout.HeightPx, CodeRaster.Render(matrix, layout), dpiScale);
}
