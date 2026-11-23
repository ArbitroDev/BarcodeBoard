using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BarcodeBoard.Core;

/// <summary>
/// Encodages d'une image de code : PNG (affichage et presse-papiers) et DIB (format bitmap de Windows,
/// celui que Word, Outlook, Teams et Paint lisent dans le presse-papiers). Les deux sont déclarés à 96 DPI :
/// collée, l'image garde la taille qu'elle a dans l'application.
/// </summary>
public static class ImageEncoding
{
    /// <summary>96 DPI en pixels par mètre, l'unité des en-têtes PNG et BMP.</summary>
    private const int PixelsPerMeter96Dpi = 3780;

    /// <summary>PNG en niveaux de gris 8 bits, sans perte : les pixels relus sont exactement ceux de l'image.</summary>
    public static byte[] Png(CodeBitmap bitmap)
    {
        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, bitmap.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), bitmap.Height);
        header[8] = 8; // bits par pixel
        header[9] = 0; // niveaux de gris ; compression, filtres et entrelacement : 0
        WriteChunk(png, "IHDR", header);

        var physical = new byte[9];
        BinaryPrimitives.WriteInt32BigEndian(physical, PixelsPerMeter96Dpi);
        BinaryPrimitives.WriteInt32BigEndian(physical.AsSpan(4), PixelsPerMeter96Dpi);
        physical[8] = 1; // unité : le mètre
        WriteChunk(png, "pHYs", physical);

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                z.WriteByte(0); // ligne sans filtre
                z.Write(bitmap.Pixels, y * bitmap.Width, bitmap.Width);
            }
        }
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>DIB 24 bits (en-tête BITMAPINFOHEADER puis lignes de bas en haut, chacune arrondie à 4 octets).</summary>
    public static byte[] Dib(CodeBitmap bitmap)
    {
        int stride = (bitmap.Width * 3 + 3) & ~3;
        var dib = new byte[40 + stride * bitmap.Height];
        var h = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(h, 40);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], bitmap.Width);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], bitmap.Height);
        BinaryPrimitives.WriteInt16LittleEndian(h[12..], 1);   // plans
        BinaryPrimitives.WriteInt16LittleEndian(h[14..], 24);  // bits par pixel
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], 0);   // BI_RGB, sans compression
        BinaryPrimitives.WriteInt32LittleEndian(h[20..], stride * bitmap.Height);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], PixelsPerMeter96Dpi);
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], PixelsPerMeter96Dpi);
        for (int y = 0; y < bitmap.Height; y++)
        {
            int src = (bitmap.Height - 1 - y) * bitmap.Width, dst = 40 + y * stride;
            for (int x = 0; x < bitmap.Width; x++)
            {
                byte v = bitmap.Pixels[src + x];
                dib[dst + 3 * x] = v;
                dib[dst + 3 * x + 1] = v;
                dib[dst + 3 * x + 2] = v;
            }
        }
        return dib;
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        png.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        png.Write(buffer);
    }

    /// <summary>CRC-32 des blocs PNG (polynôme 0xEDB88320).</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
