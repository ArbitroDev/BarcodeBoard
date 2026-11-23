using System.Buffers.Binary;

namespace BarcodeBoard;

/// <summary>
/// Images d'un fichier .ico. L'icône de la barre de titre prend l'image dessinée pour la taille exacte à
/// l'échelle de l'écran (16 px à 100 %, 20 px à 125 %…) : affichée sans redimensionnement, elle reste nette.
/// </summary>
public static class IconFrames
{
    /// <summary>
    /// Plus petite image d'au moins <paramref name="sizePx"/> pixels (sinon la plus grande), en PNG ;
    /// null si le fichier n'est pas une icône aux images PNG.
    /// </summary>
    public static byte[]? Png(byte[] ico, int sizePx)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) != 1) return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        var frames = new List<(int Size, int Offset, int Length)>();
        for (int i = 0; i < count && 6 + 16 * (i + 1) <= ico.Length; i++)
        {
            var e = ico.AsSpan(6 + 16 * i);
            int size = e[0] == 0 ? 256 : e[0];
            int length = BinaryPrimitives.ReadInt32LittleEndian(e[8..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(e[12..]);
            if (offset < 0 || length < 8 || offset + length > ico.Length) continue;
            if (!ico.AsSpan(offset, 8).SequenceEqual(PngSignature)) continue;
            frames.Add((size, offset, length));
        }
        if (frames.Count == 0) return null;
        var best = frames.Where(f => f.Size >= sizePx).OrderBy(f => f.Size).FirstOrDefault();
        if (best.Length == 0) best = frames.MaxBy(f => f.Size);
        return ico.AsSpan(best.Offset, best.Length).ToArray();
    }

    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
}
