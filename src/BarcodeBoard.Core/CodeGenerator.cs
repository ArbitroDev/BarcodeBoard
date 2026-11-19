using ZXing;
using ZXing.Common;
using ZXing.OneD;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace BarcodeBoard.Core;

public enum CodeType
{
    Qr,
    Code128,
}

public enum CodeErrorKind
{
    Empty,
    NotAscii,
    TooLong,
}

/// <summary>Raison pour laquelle un texte ne peut pas devenir un code.</summary>
public sealed record CodeError(CodeErrorKind Kind, string Message, char? BadChar = null);

/// <summary>
/// Matrice de modules, marges blanches comprises. Pour un code-barres, <see cref="Height"/> vaut 1 :
/// chaque colonne sombre est une barre sur toute la hauteur.
/// </summary>
public sealed class CodeMatrix
{
    private readonly bool[] _dark;

    public CodeMatrix(int width, int height, bool[] dark)
    {
        if (dark.Length != width * height) throw new ArgumentException("Taille de matrice incohérente.", nameof(dark));
        Width = width;
        Height = height;
        _dark = dark;
    }

    public int Width { get; }
    public int Height { get; }
    public bool IsDark(int x, int y) => _dark[y * Width + x];
}

public sealed record CodeResult(CodeMatrix? Matrix, CodeError? Error)
{
    public bool Ok => Matrix is not null;
}

public static class CodeGenerator
{
    public const int QrQuietZone = 4;
    public const int Code128QuietZone = 10;
    public const int Code128MaxLength = 80;

    public static CodeResult Generate(string? text, CodeType type)
    {
        if (string.IsNullOrEmpty(text))
            return new(null, new CodeError(CodeErrorKind.Empty, Lang.T("Tapez un texte", "Type some text")));

        return type == CodeType.Qr ? GenerateQr(text) : GenerateCode128(text);
    }

    /// <summary>Premier caractère que le Code 128 ne sait pas encoder (hors ASCII 0-127), ou null.</summary>
    public static char? FirstNonAscii(string text)
    {
        foreach (var c in text)
            if (c > 127) return c;
        return null;
    }

    private static CodeResult GenerateQr(string text)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            [EncodeHintType.MARGIN] = QrQuietZone,
        };
        // Texte ASCII : encodage par défaut, sans segment ECI (certaines douchettes recopient l'ECI
        // dans le texte lu). Texte avec accents : UTF-8 déclaré par ECI, comme le prévoit la norme.
        if (FirstNonAscii(text) is not null)
            hints[EncodeHintType.CHARACTER_SET] = "UTF-8";

        try
        {
            var bits = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
            return new(ToMatrix(bits, oneRow: false), null);
        }
        catch (WriterException)
        {
            return new(null, new CodeError(CodeErrorKind.TooLong, Lang.T("Texte trop long pour un QR code", "Text too long for a QR code")));
        }
    }

    private static CodeResult GenerateCode128(string text)
    {
        if (FirstNonAscii(text) is char bad)
            return new(null, new CodeError(CodeErrorKind.NotAscii,
                Lang.T($"Caractère non accepté par le Code 128 : {bad}", $"Character not supported by Code 128: {bad}"), bad));
        if (text.Length > Code128MaxLength)
            return new(null, new CodeError(CodeErrorKind.TooLong, Lang.T(
                $"Texte trop long pour un Code 128 ({Code128MaxLength} caractères maximum)",
                $"Text too long for Code 128 ({Code128MaxLength} characters maximum)")));

        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = Code128QuietZone };
        try
        {
            var bits = new Code128Writer().encode(text, BarcodeFormat.CODE_128, 0, 0, hints);
            return new(ToMatrix(bits, oneRow: true), null);
        }
        catch (ArgumentException ex)
        {
            return new(null, new CodeError(CodeErrorKind.NotAscii,
                Lang.T($"Texte non accepté par le Code 128 : {ex.Message}", $"Text not supported by Code 128: {ex.Message}")));
        }
    }

    private static CodeMatrix ToMatrix(BitMatrix bits, bool oneRow)
    {
        int w = bits.Width, h = oneRow ? 1 : bits.Height;
        var dark = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dark[y * w + x] = bits[x, y];
        return new CodeMatrix(w, h, dark);
    }
}
