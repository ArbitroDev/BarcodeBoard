namespace BarcodeBoard.Core;

/// <summary>Dimensions en pixels physiques d'un code dessiné à l'écran.</summary>
public readonly record struct CodeLayout(int ModulePx, int WidthPx, int HeightPx);

public static class CodeRaster
{
    /// <summary>Largeur visée d'un code-barres, relative à la taille réglée (côté d'un QR code).</summary>
    public const double BarcodeWidthRatio = 1.2;
    /// <summary>Hauteur d'un code-barres, relative à la taille réglée.</summary>
    public const double BarcodeHeightRatio = 0.42;

    /// <summary>
    /// Calcule la taille d'un module en pixels physiques entiers.
    /// <paramref name="sizeDip"/> est la taille réglée en pixels logiques, <paramref name="dpiScale"/>
    /// le facteur d'échelle de Windows (1 = 100 %, 1,5 = 150 %).
    /// Un module fait toujours au moins un pixel logique, arrondi au pixel physique.
    /// </summary>
    public static CodeLayout Layout(CodeMatrix matrix, double sizeDip, double dpiScale)
    {
        if (dpiScale <= 0) dpiScale = 1;
        int minPx = Math.Max(1, (int)Math.Round(dpiScale, MidpointRounding.AwayFromZero));

        if (matrix.Height > 1)
        {
            int px = Math.Max(minPx, (int)Math.Round(sizeDip * dpiScale / matrix.Width, MidpointRounding.AwayFromZero));
            return new CodeLayout(px, px * matrix.Width, px * matrix.Height);
        }

        int barPx = Math.Max(minPx, (int)Math.Round(sizeDip * BarcodeWidthRatio * dpiScale / matrix.Width, MidpointRounding.AwayFromZero));
        int height = Math.Max(barPx * 8, (int)Math.Round(sizeDip * BarcodeHeightRatio * dpiScale, MidpointRounding.AwayFromZero));
        return new CodeLayout(barPx, barPx * matrix.Width, height);
    }

    /// <summary>Pixels en niveaux de gris 8 bits (0 noir, 255 blanc), une ligne = <c>WidthPx</c> octets.</summary>
    public static byte[] Render(CodeMatrix matrix, CodeLayout layout)
    {
        var pixels = new byte[layout.WidthPx * layout.HeightPx];
        Array.Fill(pixels, (byte)255);
        int m = layout.ModulePx;

        for (int y = 0; y < layout.HeightPx; y++)
        {
            int my = matrix.Height == 1 ? 0 : y / m;
            int row = y * layout.WidthPx;
            for (int mx = 0; mx < matrix.Width; mx++)
            {
                if (!matrix.IsDark(mx, my)) continue;
                Array.Fill(pixels, (byte)0, row + mx * m, m);
            }
        }
        return pixels;
    }
}
