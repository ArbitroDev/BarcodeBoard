using BarcodeBoard.Core;
using ZXing;

namespace BarcodeBoard.Core.Tests;

public class GeneratorTests
{
    /// <summary>Dessine le code comme à l'écran puis le relit avec ZXing.</summary>
    private static string? Decode(string text, CodeType type, double size = 160, double scale = 1)
    {
        var r = CodeGenerator.Generate(text, type);
        Assert.True(r.Ok, r.Error?.Message);
        var layout = CodeRaster.Layout(r.Matrix!, size, scale);
        var px = CodeRaster.Render(r.Matrix!, layout);
        var source = new RGBLuminanceSource(px, layout.WidthPx, layout.HeightPx, RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new BarcodeReaderGeneric();
        reader.Options.PossibleFormats = [type == CodeType.Qr ? BarcodeFormat.QR_CODE : BarcodeFormat.CODE_128];
        reader.Options.PureBarcode = type == CodeType.Qr;
        return reader.Decode(source)?.Text;
    }

    [Theory]
    [InlineData("PAL-000482-A")]
    [InlineData("https://exemple.fr/suivi/88412")]
    [InlineData("Éléments accentués & symboles #42")]
    [InlineData("QTE:24;UNITE:CARTON")]
    public void Qr_roundtrip(string text) => Assert.Equal(text, Decode(text, CodeType.Qr));

    [Theory]
    [InlineData("PAL-000482-A")]
    [InlineData("12345678")]
    [InlineData("lot 2026/11 x")]
    public void Code128_roundtrip(string text) => Assert.Equal(text, Decode(text, CodeType.Code128));

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Roundtrip_at_every_windows_scale(double scale)
    {
        Assert.Equal("LOT-2026-11-0042", Decode("LOT-2026-11-0042", CodeType.Code128, 160, scale));
        Assert.Equal("LOC-A12-03-B", Decode("LOC-A12-03-B", CodeType.Qr, 160, scale));
    }

    [Fact]
    public void Ascii_qr_has_no_eci_segment()
    {
        // Avec ECI, la matrice d'un même texte ASCII serait différente de celle produite sans charset.
        var r = CodeGenerator.Generate("ABC-123", CodeType.Qr);
        Assert.True(r.Ok);
        Assert.Equal(21 + 2 * CodeGenerator.QrQuietZone, r.Matrix!.Width); // version 1 suffit
    }

    [Fact]
    public void Empty_text_is_an_error()
    {
        Assert.Equal(CodeErrorKind.Empty, CodeGenerator.Generate("", CodeType.Qr).Error!.Kind);
        Assert.Equal(CodeErrorKind.Empty, CodeGenerator.Generate(null, CodeType.Code128).Error!.Kind);
    }

    [Fact]
    public void Code128_rejects_accents_and_names_the_character()
    {
        var e = CodeGenerator.Generate("Réception", CodeType.Code128).Error!;
        Assert.Equal(CodeErrorKind.NotAscii, e.Kind);
        Assert.Equal('é', e.BadChar);
        Assert.Contains("é", e.Message);
    }

    [Fact]
    public void Too_long_texts_are_errors()
    {
        Assert.Equal(CodeErrorKind.TooLong, CodeGenerator.Generate(new string('a', 3000), CodeType.Qr).Error!.Kind);
        Assert.Equal(CodeErrorKind.TooLong, CodeGenerator.Generate(new string('a', 81), CodeType.Code128).Error!.Kind);
        Assert.True(CodeGenerator.Generate(new string('a', 80), CodeType.Code128).Ok);
    }

    [Theory]
    [InlineData("A\tB")]
    [InlineData("01095011010209171719050810ABCD1234\u001d2112345678")] // GS1-128 : séparateur GS
    [InlineData("ABC123456789012DEF")]                               // bascule jeux B et C
    [InlineData("x9")]
    public void Code128_special_and_mixed_texts_roundtrip(string text) =>
        Assert.Equal(text, Decode(text, CodeType.Code128));

    [Fact]
    public void Longest_code128_is_read_back()
    {
        var text = string.Concat(Enumerable.Range(0, CodeGenerator.Code128MaxLength).Select(i => (char)('A' + i % 26)));
        Assert.Equal(text, Decode(text, CodeType.Code128));
    }

    [Fact]
    public void Qr_capacity_limit_is_an_error_not_a_crash()
    {
        // Niveau M, octets : 2331 au plus pour la version 40.
        Assert.True(CodeGenerator.Generate(new string('a', 2331), CodeType.Qr).Ok);
        Assert.Equal(CodeErrorKind.TooLong, CodeGenerator.Generate(new string('a', 2332), CodeType.Qr).Error!.Kind);
        // UTF-8 : « é » prend deux octets.
        Assert.Equal(CodeErrorKind.TooLong, CodeGenerator.Generate(new string('é', 1200), CodeType.Qr).Error!.Kind);
        Assert.Equal("ééé", Decode("ééé", CodeType.Qr));
    }

    [Fact]
    public void Quiet_zones_are_white()
    {
        var qr = CodeGenerator.Generate("X", CodeType.Qr).Matrix!;
        for (int i = 0; i < qr.Width; i++)
            for (int q = 0; q < CodeGenerator.QrQuietZone; q++)
            {
                Assert.False(qr.IsDark(i, q));
                Assert.False(qr.IsDark(q, i));
            }

        var bar = CodeGenerator.Generate("X", CodeType.Code128).Matrix!;
        for (int q = 0; q < CodeGenerator.Code128QuietZone; q++)
        {
            Assert.False(bar.IsDark(q, 0));
            Assert.False(bar.IsDark(bar.Width - 1 - q, 0));
        }
    }
}

public class LayoutTests
{
    [Fact]
    public void Qr_module_is_whole_pixels_close_to_requested_size()
    {
        var m = CodeGenerator.Generate("LOC-A12-03-B", CodeType.Qr).Matrix!; // 29 modules
        var l = CodeRaster.Layout(m, 160, 1);
        Assert.Equal(l.ModulePx * m.Width, l.WidthPx);
        Assert.InRange(l.WidthPx, 160 - m.Width / 2, 160 + m.Width / 2);
    }

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.25, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.0, 2)]
    public void Module_is_at_least_one_logical_pixel(double scale, int minPx)
    {
        var m = CodeGenerator.Generate(new string('A', 40), CodeType.Code128).Matrix!;
        Assert.True(CodeRaster.Layout(m, 110, scale).ModulePx >= minPx);
    }

    [Fact]
    public void Barcode_is_smaller_than_a_wide_strip()
    {
        var m = CodeGenerator.Generate("PAL-000482-A", CodeType.Code128).Matrix!;
        var l = CodeRaster.Layout(m, 160, 1);
        Assert.InRange(l.WidthPx, 150, 260);
        Assert.Equal(67, l.HeightPx);
    }

    [Theory]
    [InlineData(CodeType.Qr, 1.0)]
    [InlineData(CodeType.Qr, 1.5)]
    [InlineData(CodeType.Code128, 1.25)]
    public void Render_is_pure_black_and_white(CodeType type, double scale)
    {
        var m = CodeGenerator.Generate("LOT-2026-11-0042", type).Matrix!;
        var px = CodeRaster.Render(m, CodeRaster.Layout(m, 160, scale));
        Assert.All(px, p => Assert.True(p is 0 or 255));
    }

    [Fact]
    public void Each_qr_module_is_a_uniform_square_block()
    {
        var m = CodeGenerator.Generate("LOC-A12-03-B", CodeType.Qr).Matrix!;
        var l = CodeRaster.Layout(m, 160, 1.5);
        var px = CodeRaster.Render(m, l);
        for (int my = 0; my < m.Height; my++)
            for (int mx = 0; mx < m.Width; mx++)
            {
                byte expected = m.IsDark(mx, my) ? (byte)0 : (byte)255;
                for (int dy = 0; dy < l.ModulePx; dy++)
                    for (int dx = 0; dx < l.ModulePx; dx++)
                        Assert.Equal(expected, px[(my * l.ModulePx + dy) * l.WidthPx + mx * l.ModulePx + dx]);
            }
    }

    [Fact]
    public void Code_too_wide_shrinks_to_whole_pixels_and_stays_readable()
    {
        var text = string.Concat(Enumerable.Range(0, CodeGenerator.Code128MaxLength).Select(i => (char)('A' + i % 26)));
        var m = CodeGenerator.Generate(text, CodeType.Code128).Matrix!;
        // À 150 %, un module d'un pixel logique fait 2 pixels physiques : le code dépasse 1 200 px.
        Assert.True(CodeRaster.Layout(m, 160, 1.5).WidthPx > 1200);
        var l = CodeRaster.LayoutWithin(m, 160, 1.5, 1200)!.Value;
        Assert.Equal(1, l.ModulePx);
        Assert.True(l.WidthPx <= 1200);
        var px = CodeRaster.Render(m, l);
        var source = new RGBLuminanceSource(px, l.WidthPx, l.HeightPx, RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new BarcodeReaderGeneric();
        reader.Options.PossibleFormats = [BarcodeFormat.CODE_128];
        Assert.Equal(text, reader.Decode(source)?.Text);
    }

    [Fact]
    public void Code_that_cannot_fit_even_at_one_pixel_is_refused()
    {
        var m = CodeGenerator.Generate("LOT-2026-11-0042", CodeType.Code128).Matrix!;
        Assert.Null(CodeRaster.LayoutWithin(m, 160, 1, m.Width - 1));
        Assert.Equal(CodeRaster.Layout(m, 160, 1), CodeRaster.LayoutWithin(m, 160, 1, 10_000));
    }

    [Fact]
    public void Invalid_scale_falls_back_to_100_percent()
    {
        var m = CodeGenerator.Generate("A", CodeType.Qr).Matrix!;
        Assert.Equal(CodeRaster.Layout(m, 160, 1), CodeRaster.Layout(m, 160, 0));
        Assert.Equal(CodeRaster.Layout(m, 160, 1), CodeRaster.Layout(m, 160, -2));
    }

    [Fact]
    public void Render_draws_full_height_bars()
    {
        var m = CodeGenerator.Generate("A", CodeType.Code128).Matrix!;
        var l = CodeRaster.Layout(m, 160, 1);
        var px = CodeRaster.Render(m, l);
        int firstBar = CodeGenerator.Code128QuietZone * l.ModulePx;
        for (int y = 0; y < l.HeightPx; y++)
        {
            Assert.Equal(0, px[y * l.WidthPx + firstBar]);
            Assert.Equal(255, px[y * l.WidthPx]);
        }
    }
}

public class FileTests
{
    [Fact]
    public void Save_then_load_keeps_order_text_and_type()
    {
        var codes = new[]
        {
            new CodeEntry("PAL-000482-A", CodeType.Code128),
            new CodeEntry("Éléments accentués & \"guillemets\"", CodeType.Qr),
            new CodeEntry("", CodeType.Qr),
        };
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.codes");
        try
        {
            CodesFile.Save(path, codes);
            Assert.Equal(codes, CodesFile.Load(path).Codes);
            Assert.Null(CodesFile.Load(path).Name);
            Assert.Contains("\"format\": \"barcode-board\"", File.ReadAllText(path));
            Assert.Contains("Éléments", File.ReadAllText(path)); // lisible à la main, pas d'échappement É
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Unknown_type_loads_as_qr()
    {
        var codes = CodesFile.Deserialize("""{"format":"barcode-board","version":1,"codes":[{"text":"x","type":"datamatrix"}]}""");
        Assert.Equal(CodeType.Qr, Assert.Single(codes.Codes).Type);
    }

    [Fact]
    public void Files_from_the_first_version_still_open()
    {
        var doc = CodesFile.Deserialize("""{"format":"codes-ecran","version":1,"codes":[{"text":"PAL-1","type":"code128"}]}""");
        Assert.Equal(new CodeEntry("PAL-1", CodeType.Code128), Assert.Single(doc.Codes));
    }

    [Fact]
    public void Tab_name_is_saved_and_reloaded()
    {
        var json = CodesFile.Serialize([new CodeEntry("A", CodeType.Qr)], "Réception quai 3");
        Assert.Contains("\"name\": \"Réception quai 3\"", json);
        Assert.Equal("Réception quai 3", CodesFile.Deserialize(json).Name);
        Assert.Null(CodesFile.Deserialize(CodesFile.Serialize([new CodeEntry("A", CodeType.Qr)], "  ")).Name);
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{"format":"autre","version":1,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":2,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":1}""")]
    [InlineData("""{"format":"barcode-board","version":1,"codes":[{"type":"qr"}]}""")]
    // Valeurs ou structures inattendues : refusées proprement, sans autre exception (qui ferait planter l'application).
    [InlineData("""{"format":"barcode-board","version":1.5,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":99999999999,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":0,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":-1,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":"1","codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":1,"version":1,"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":1,"codes":[],"codes":[]}""")]
    [InlineData("""{"format":"barcode-board","version":1,"codes":[{"text":"a","text":"b"}]}""")]
    [InlineData("""{"format":"barcode-board","version":1,"codes":[null]}""")]
    [InlineData("""{"format":"barcode-board","version":1,"codes":[42]}""")]
    [InlineData("""{"format":"barcode-board","version":1,"codes":[{"text":5}]}""")]
    [InlineData("""[]""")]
    [InlineData("")]
    public void Invalid_files_are_rejected(string json) =>
        Assert.Throws<InvalidCodesFileException>(() => CodesFile.Deserialize(json));

    [Fact]
    public void Name_of_another_type_is_ignored()
    {
        var doc = CodesFile.Deserialize("""{"format":"barcode-board","version":1,"name":12,"codes":[]}""");
        Assert.Null(doc.Name);
    }

    [Fact]
    public void Save_replaces_an_existing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.codes");
        try
        {
            CodesFile.Save(path, [new CodeEntry("ANCIEN", CodeType.Qr)]);
            CodesFile.Save(path, [new CodeEntry("NOUVEAU", CodeType.Code128)]);
            Assert.Equal(new CodeEntry("NOUVEAU", CodeType.Code128), Assert.Single(CodesFile.Load(path).Codes));
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_to_a_read_only_file_fails_and_leaves_no_temp_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.codes");
        try
        {
            CodesFile.Save(path, [new CodeEntry("A", CodeType.Qr)]);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Assert.ThrowsAny<Exception>(() => CodesFile.Save(path, [new CodeEntry("B", CodeType.Qr)]));
            Assert.Equal("A", Assert.Single(CodesFile.Load(path).Codes).Text);
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_to_a_missing_folder_fails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}", "x.codes");
        Assert.Throws<DirectoryNotFoundException>(() => CodesFile.Save(path, [new CodeEntry("A", CodeType.Qr)]));
    }
}

/// <summary>Messages en anglais. La langue est un réglage global : ces tests ne tournent pas en parallèle des autres.</summary>
[CollectionDefinition("Langue", DisableParallelization = true)]
public class LanguageCollection;

[Collection("Langue")]
public class LanguageTests
{
    [Fact]
    public void Messages_follow_the_language()
    {
        bool saved = Lang.French;
        try
        {
            Lang.French = false;
            Assert.Equal("Type some text", CodeGenerator.Generate("", CodeType.Qr).Error!.Message);
            Assert.Contains("Code 128: é", CodeGenerator.Generate("é", CodeType.Code128).Error!.Message);
            Assert.Contains("not a valid", Assert.Throws<InvalidCodesFileException>(() => CodesFile.Deserialize("x")).Message);

            Lang.French = true;
            Assert.Equal("Tapez un texte", CodeGenerator.Generate("", CodeType.Qr).Error!.Message);
            Assert.Contains("Code 128 : é", CodeGenerator.Generate("é", CodeType.Code128).Error!.Message);
        }
        finally { Lang.French = saved; }
    }
}
