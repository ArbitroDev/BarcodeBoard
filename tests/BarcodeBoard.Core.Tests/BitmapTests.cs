using System.Buffers.Binary;
using System.IO.Compression;
using BarcodeBoard.Core;
using ZXing;

namespace BarcodeBoard.Core.Tests;

/// <summary>Images des codes sans WPF : taille à l'écran, plein écran, flou, PNG et DIB du presse-papiers.</summary>
public class BitmapTests
{
    private static CodeMatrix Matrix(string text, CodeType type)
    {
        var r = CodeGenerator.Generate(text, type);
        Assert.True(r.Ok, r.Error?.Message);
        return r.Matrix!;
    }

    private static string? Read(CodeBitmap bmp, CodeType type)
    {
        var source = new RGBLuminanceSource(bmp.Pixels, bmp.Width, bmp.Height, RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new BarcodeReaderGeneric();
        reader.Options.PossibleFormats = [type == CodeType.Qr ? BarcodeFormat.QR_CODE : BarcodeFormat.CODE_128];
        reader.Options.PureBarcode = type == CodeType.Qr;
        return reader.Decode(source)?.Text;
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Screen_bitmap_is_the_raster_at_the_screen_scale(double scale)
    {
        var m = Matrix("LOC-A12-03-B", CodeType.Qr);
        var bmp = CodeBitmaps.ForScreen(m, 160, scale)!;
        var layout = CodeRaster.Layout(m, 160, scale);
        Assert.Equal(layout.WidthPx, bmp.Width);
        Assert.Equal(layout.HeightPx, bmp.Height);
        Assert.Equal(CodeRaster.Render(m, layout), bmp.Pixels);
        Assert.Equal(scale, bmp.Scale);
        Assert.Equal(bmp.Width / scale, bmp.WidthDip, 6);
        Assert.Equal("LOC-A12-03-B", Read(bmp, CodeType.Qr));
    }

    [Fact]
    public void Screen_bitmap_too_wide_for_its_card_narrows_then_disappears()
    {
        var m = Matrix(new string('7', 80), CodeType.Code128);
        var full = CodeBitmaps.ForScreen(m, 160, 1.5)!;
        var narrow = CodeBitmaps.ForScreen(m, 160, 1.5, maxWidthDip: full.WidthDip - 1)!;
        Assert.True(narrow.Width < full.Width);
        Assert.True(narrow.WidthDip <= full.WidthDip - 1);
        Assert.Equal(new string('7', 80), Read(narrow, CodeType.Code128));
        Assert.Null(CodeBitmaps.ForScreen(m, 160, 1.5, maxWidthDip: (m.Width - 1) / 1.5));
    }

    [Theory]
    [InlineData(CodeType.Qr, 1.0)]
    [InlineData(CodeType.Qr, 1.25)]
    [InlineData(CodeType.Code128, 1.5)]
    [InlineData(CodeType.Code128, 2.0)]
    public void Full_screen_bitmap_fits_the_area_and_stays_readable(CodeType type, double scale)
    {
        var m = Matrix("PAL-000482-A", type);
        var bmp = CodeBitmaps.Fit(m, 900, 520, scale)!;
        Assert.True(bmp.WidthDip <= 900 && bmp.HeightDip <= 520);
        Assert.True(bmp.Width >= m.Width * 4, "le plein écran agrandit le code");
        Assert.Equal("PAL-000482-A", Read(bmp, type));
    }

    [Fact]
    public void Full_screen_refuses_a_code_that_cannot_fit_even_at_one_pixel()
    {
        var m = Matrix(new string('7', 80), CodeType.Code128);
        Assert.Null(CodeBitmaps.Fit(m, (m.Width - 1) / 1.5, 400, 1.5));
        Assert.NotNull(CodeBitmaps.Fit(m, m.Width / 1.5, 400, 1.5));
    }

    [Fact]
    public void Clipboard_bitmap_is_the_full_size_code()
    {
        var m = Matrix("LOT-2026-11-0042", CodeType.Code128);
        var bmp = CodeBitmaps.ForClipboard(m, 160, 1.25);
        Assert.Equal(CodeRaster.Layout(m, 160, 1.25).WidthPx, bmp.Width);
        Assert.Equal("LOT-2026-11-0042", Read(bmp, CodeType.Code128));
    }

    [Fact]
    public void Blur_keeps_the_size_and_leaves_no_readable_code()
    {
        var m = Matrix("LOC-A12-03-B", CodeType.Qr);
        var bmp = CodeBitmaps.ForScreen(m, 160, 1.5)!;
        var blurred = CodeBitmaps.Blur(bmp, radiusDip: 22);
        Assert.Equal(bmp.Width, blurred.Width);
        Assert.Equal(bmp.Height, blurred.Height);
        Assert.Equal(bmp.Scale, blurred.Scale);
        Assert.Contains(blurred.Pixels, p => p is > 0 and < 255);
        Assert.Null(Read(blurred, CodeType.Qr));
        // Le flou ne touche pas à l'original
        Assert.All(bmp.Pixels, p => Assert.True(p is 0 or 255));
    }

    [Fact]
    public void Blur_of_a_uniform_image_is_unchanged()
    {
        var white = new CodeBitmap(40, 30, Enumerable.Repeat((byte)255, 40 * 30).ToArray(), 1);
        Assert.All(CodeBitmaps.Blur(white, 22).Pixels, p => Assert.Equal(255, p));
    }

    // ---- PNG ----

    private static (int w, int h, byte[] gray, Dictionary<string, byte[]> chunks) DecodePng(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        var chunks = new Dictionary<string, byte[]>();
        var idat = new MemoryStream();
        int pos = 8;
        while (pos < png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len).ToArray();
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            Assert.Equal(Crc32(png.AsSpan(pos + 4, len + 4)), crc);
            if (type == "IDAT") idat.Write(data); else chunks[type] = data;
            pos += 12 + len;
        }
        var ihdr = chunks["IHDR"];
        int w = BinaryPrimitives.ReadInt32BigEndian(ihdr), h = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4));
        Assert.Equal(8, ihdr[8]);  // 8 bits
        Assert.Equal(0, ihdr[9]);  // niveaux de gris
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        var bytes = raw.ToArray();
        var gray = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            Assert.Equal(0, bytes[y * (w + 1)]); // filtre « aucun »
            Array.Copy(bytes, y * (w + 1) + 1, gray, y * w, w);
        }
        Assert.True(chunks.ContainsKey("IEND"));
        return (w, h, gray, chunks);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }

    [Fact]
    public void Png_holds_the_exact_pixels_at_96_dpi()
    {
        var bmp = CodeBitmaps.ForScreen(Matrix("ART-77810-BLEU-M", CodeType.Code128), 160, 1.25)!;
        var (w, h, gray, chunks) = DecodePng(ImageEncoding.Png(bmp));
        Assert.Equal(bmp.Width, w);
        Assert.Equal(bmp.Height, h);
        Assert.Equal(bmp.Pixels, gray);
        // pHYs : 3780 pixels par mètre (96 DPI), unité le mètre
        var phys = chunks["pHYs"];
        Assert.Equal(3780, BinaryPrimitives.ReadInt32BigEndian(phys));
        Assert.Equal(3780, BinaryPrimitives.ReadInt32BigEndian(phys.AsSpan(4)));
        Assert.Equal(1, phys[8]);
    }

    // ---- DIB (CF_DIB) ----

    [Fact]
    public void Dib_is_a_bottom_up_24_bit_bitmap_at_96_dpi()
    {
        var bmp = new CodeBitmap(3, 2, [0, 255, 0, 255, 0, 255], 1);
        var dib = ImageEncoding.Dib(bmp);
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(dib));            // BITMAPINFOHEADER
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));   // largeur
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)));   // hauteur positive : de bas en haut
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(12)));
        Assert.Equal(24, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16)));  // BI_RGB
        int stride = 12;                                                          // 3 × 3 octets arrondis à 4
        Assert.Equal(stride * 2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(20)));
        Assert.Equal(3780, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(24)));
        Assert.Equal(3780, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(28)));
        Assert.Equal(40 + stride * 2, dib.Length);
        // Première ligne du fichier = dernière ligne de l'image : 255, 0, 255
        Assert.Equal(new byte[] { 255, 255, 255, 0, 0, 0, 255, 255, 255 }, dib[40..49]);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 0, 0, 0 }, dib[(40 + stride)..(40 + stride + 9)]);
    }
}
