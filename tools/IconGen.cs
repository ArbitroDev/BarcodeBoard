#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishAot=false
#:property PublishTrimmed=false

// Icône de « Barcode Board ».
//   dotnet run tools/IconGen.cs -- preview <planche.png>          : compare toutes les propositions
//   dotnet run tools/IconGen.cs -- ico <A…L> <dossier>             : écrit BarcodeBoard.ico (16 à 256 px) + PNG
//   Icône retenue : K (dotnet run tools/IconGen.cs -- ico K src/BarcodeBoard/Assets)
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var thread = new Thread(() =>
{
    if (args.Length >= 2 && args[0] == "preview") IconGen.WritePreview(args[1]);
    else if (args.Length >= 3 && args[0] == "ico") IconGen.WriteIcon(args[1], args[2]);
    else Console.WriteLine("Usage : preview <png> | ico <A…L> <dossier>  (icône retenue : K)");
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

record Variant(string Id, string Name, Action<DrawingContext> Large, Action<DrawingContext> Small);

static class IconGen
{
    static readonly Color Ink = Color.FromRgb(0x1B, 0x1F, 0x24);
    static readonly Color BlueTop = Color.FromRgb(0x5C, 0xB4, 0xFF);
    static readonly Color BlueBottom = Color.FromRgb(0x0B, 0x5C, 0xAD);
    static readonly Color Laser = Color.FromRgb(0xFF, 0x2E, 0x48);
    // Toutes les tailles que Windows demande selon l'échelle d'affichage (barre des tâches : 24 à 100 %, 30 à 125 %, 36 à 150 %…).
    static readonly int[] Sizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256];
    /// <summary>En dessous, la version calée au pixel ; au-dessus, le dessin complet (plus de détail dans la barre des tâches).</summary>
    const int SmallMax = 20;

    // Première série (A à D) gardée pour mémoire ; la planche montre la série moderne (E à H).
    static readonly Variant[] AllVariants =
    [
        new("A", "A. Étiquette laser", LabelLarge, LabelSmall),
        new("B", "B. Viseur", FinderLarge, FinderSmall),
        new("C", "C. Écran", ScreenLarge, ScreenSmall),
        new("D", "D. Hybride", HybridLarge, HybridSmall),
        new("E", "E. Glyphe seul", GlyphLarge, GlyphSmall),
        new("F", "F. Tuile", TileLarge, TileSmall),
        new("G", "G. Calques", LayersLarge, LayersSmall),
        new("H", "H. Viseur", ViewfinderLarge, ViewfinderSmall),
        new("I", "I. Tuile pleine", FullTileLarge, FullTileSmall),
        new("J", "J. Débord", BleedLarge, BleedSmall),
        new("K", "K. Squircle", SquircleLarge, SquircleSmall),
        new("L", "L. Halo", HaloLarge, HaloSmall),
    ];
    // La planche montre la série en cours (I à L).
    static readonly Variant[] Variants = AllVariants[8..];

    // ======================= Troisième série : le signe prend toute la place =======================

    static readonly Color DeepTop = Color.FromRgb(0x2F, 0x9B, 0xFF);
    static readonly Color DeepBottom = Color.FromRgb(0x0A, 0x4F, 0xC2);
    static readonly Color BarsLight = Color.FromRgb(0xA8, 0xE6, 0xFF);

    /// <summary>Glyphe en deux tons : repères dans un pinceau, barres dans un autre.</summary>
    static void Glyph2(DrawingContext dc, Brush rings, Brush bars, double x, double y, double c, double gap)
    {
        // Les repères via Glyph, les barres masquées puis redessinées : plus simple de dessiner tout puis surcharger.
        Glyph(dc, rings, x, y, c, gap);
        double bx = x + c + gap, by = y + c + gap;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(bx, by, c, c));
        foreach (var (off, w) in new[] { (0.0, 0.16), (0.28, 0.08), (0.48, 0.24), (0.84, 0.16) })
            dc.DrawRoundedRectangle(bars, null, new Rect(bx + off * c, by, w * c, c), c * 0.04, c * 0.04);
    }

    // I. Tuile pleine : la tuile remplit le cadre, le signe est grand.
    static void FullTileLarge(DrawingContext dc)
    {
        var tile = new Rect(6, 6, 244, 244);
        dc.DrawRoundedRectangle(Diagonal(DeepTop, DeepBottom), null, tile, 60, 60);
        dc.DrawRoundedRectangle(null, new Pen(Solid(0x2E, 0xFF, 0xFF, 0xFF), 2), Inflate(tile, -1), 59, 59);
        Glyph(dc, Brushes.White, 38, 38, 82, 16);
    }
    static void FullTileSmall(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Diagonal(DeepTop, DeepBottom), null, new Rect(0, 0, 16, 16), 3.5, 3.5);
        GlyphPx(dc, Brushes.White, 2, 2, 5, 2);
    }

    // J. Débord : le signe dépasse et la tuile le coupe.
    static void BleedLarge(DrawingContext dc)
    {
        var tile = new Rect(6, 6, 244, 244);
        var clip = new RectangleGeometry(tile, 60, 60);
        dc.DrawGeometry(Diagonal(DeepTop, DeepBottom), null, clip);
        dc.PushClip(clip);
        Glyph(dc, Brushes.White, 44, 44, 112, 18);
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(Solid(0x2E, 0xFF, 0xFF, 0xFF), 2), Inflate(tile, -1), 59, 59);
    }
    static void BleedSmall(DrawingContext dc)
    {
        var clip = new RectangleGeometry(new Rect(0, 0, 16, 16), 3.5, 3.5);
        dc.DrawGeometry(Diagonal(DeepTop, DeepBottom), null, clip);
        dc.PushClip(clip);
        GlyphPx(dc, Brushes.White, 3, 3, 6, 2);
        dc.Pop();
    }

    // K. Squircle : forme super-ellipse, signe en deux tons.
    static Geometry Squircle(Rect r, double n = 5)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, a = r.Width / 2, b = r.Height / 2;
            for (int i = 0; i <= 360; i++)
            {
                double t = i * Math.PI / 180, ct = Math.Cos(t), st = Math.Sin(t);
                var p = new Point(cx + a * Math.Sign(ct) * Math.Pow(Math.Abs(ct), 2 / n), cy + b * Math.Sign(st) * Math.Pow(Math.Abs(st), 2 / n));
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, false);
            }
        }
        g.Freeze();
        return g;
    }
    static void SquircleLarge(DrawingContext dc)
    {
        // La tuile occupe tout le cadre et le signe les trois quarts : même présence que les autres icônes de la barre des tâches.
        var shape = Squircle(new Rect(1, 1, 254, 254));
        dc.DrawGeometry(Diagonal(DeepTop, DeepBottom), new Pen(Solid(0x2E, 0xFF, 0xFF, 0xFF), 2), shape);
        Glyph2(dc, Brushes.White, Solid(BarsLight), 32, 32, 86, 20);
    }
    static void SquircleSmall(DrawingContext dc)
    {
        dc.DrawGeometry(Diagonal(DeepTop, DeepBottom), null, Squircle(new Rect(0, 0, 16, 16), 4));
        GlyphPx(dc, Brushes.White, 2, 2, 5, 2);
        foreach (var (o, w) in new[] { (0, 1), (2, 2) }) Px(dc, Solid(BarsLight), 9 + o, 9, w, 5);
    }

    // L. Halo : pas de boîte, le signe sur un halo lumineux.
    static void HaloLarge(DrawingContext dc)
    {
        var halo = new RadialGradientBrush(
            [new GradientStop(Color.FromArgb(0x90, 0x7C, 0xD3, 0xFF), 0), new GradientStop(Color.FromArgb(0x50, 0x4C, 0xC2, 0xFF), 0.55),
             new GradientStop(Color.FromArgb(0x00, 0x4C, 0xC2, 0xFF), 1)]);
        dc.DrawEllipse(halo, null, new Point(128, 128), 128, 128);
        Glyph(dc, DiagonalAbs(DeepTop, DeepBottom, new Rect(24, 24, 208, 208)), 24, 24, 96, 16);
    }
    static void HaloSmall(DrawingContext dc) => GlyphPx(dc, DiagonalAbs(DeepTop, DeepBottom, new Rect(1, 1, 14, 14)), 1, 1, 6, 2);

    // ======================= Série moderne : le glyphe 2 × 2 =======================
    // Trois cases sont des repères de QR code, la quatrième des barres de code-barres.

    static readonly Color SkyTop = Color.FromRgb(0x4C, 0xC2, 0xFF);     // bleu ciel Fluent
    static readonly Color SkyBottom = Color.FromRgb(0x10, 0x5E, 0xD8);
    static readonly Color AquaTop = Color.FromRgb(0x5E, 0xE7, 0xD0);
    static readonly Color AquaBottom = Color.FromRgb(0x12, 0x9E, 0xD6);

    static Brush Diagonal(Color a, Color b) => new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
    static Brush DiagonalAbs(Color a, Color b, Rect r) =>
        new LinearGradientBrush(a, b, r.TopLeft, r.BottomRight) { MappingMode = BrushMappingMode.Absolute };

    /// <summary>Le glyphe vectoriel : 4 cases de côté <paramref name="c"/> séparées de <paramref name="gap"/>.</summary>
    static void Glyph(DrawingContext dc, Brush b, double x, double y, double c, double gap)
    {
        void Ring(double cx, double cy)
        {
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new RectangleGeometry(new Rect(cx, cy, c, c), c * 0.26, c * 0.26));
            double t = c * 0.17;
            g.Children.Add(new RectangleGeometry(new Rect(cx + t, cy + t, c - 2 * t, c - 2 * t), c * 0.13, c * 0.13));
            dc.DrawGeometry(b, null, g);
            double k = c * 0.34;
            dc.DrawRoundedRectangle(b, null, new Rect(cx + (c - k) / 2, cy + (c - k) / 2, k, k), c * 0.08, c * 0.08);
        }
        Ring(x, y);
        Ring(x + c + gap, y);
        Ring(x, y + c + gap);
        double bx = x + c + gap, by = y + c + gap;
        foreach (var (off, w) in new[] { (0.0, 0.16), (0.28, 0.08), (0.48, 0.24), (0.84, 0.16) })
            dc.DrawRoundedRectangle(b, null, new Rect(bx + off * c, by, w * c, c), c * 0.04, c * 0.04);
    }

    /// <summary>Le glyphe calé au pixel : cases de <paramref name="n"/> px.</summary>
    static void GlyphPx(DrawingContext dc, Brush b, int x, int y, int n, int gap)
    {
        void Ring(int cx, int cy)
        {
            if (n >= 5) { PixelRing(dc, b, cx, cy, n, n - 2); Px(dc, b, cx + 2, cy + 2, n - 4, n - 4); }
            else PixelRing(dc, b, cx, cy, n, n - 2);
        }
        Ring(x, y);
        Ring(x + n + gap, y);
        Ring(x, y + n + gap);
        int bx = x + n + gap, by = y + n + gap;
        var bars = n >= 6 ? new[] { (0, 1), (2, 2), (5, 1) } : n >= 4 ? new[] { (0, 1), (2, 2) } : new[] { (0, 1), (2, 1) };
        foreach (var (o, w) in bars) Px(dc, b, bx + o, by, w, n);
    }

    // E. Glyphe seul
    static void GlyphLarge(DrawingContext dc) => Glyph(dc, DiagonalAbs(SkyTop, SkyBottom, new Rect(20, 20, 216, 216)), 20, 20, 100, 16);
    static void GlyphSmall(DrawingContext dc) => GlyphPx(dc, DiagonalAbs(SkyTop, SkyBottom, new Rect(1, 1, 14, 14)), 1, 1, 6, 2);

    // F. Tuile
    static void TileLarge(DrawingContext dc)
    {
        var tile = new Rect(16, 16, 224, 224);
        dc.DrawRoundedRectangle(Solid(0x1F, 0, 0, 0), null, new Rect(16, 20, 224, 224), 56, 56);   // ombre très légère
        dc.DrawRoundedRectangle(Diagonal(SkyTop, SkyBottom), null, tile, 56, 56);
        dc.DrawRoundedRectangle(null, new Pen(Solid(0x33, 0xFF, 0xFF, 0xFF), 2), Inflate(tile, -1), 55, 55);
        Glyph(dc, Brushes.White, 60, 60, 62, 12);
    }
    static void TileSmall(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Diagonal(SkyTop, SkyBottom), null, new Rect(1, 1, 14, 14), 3.5, 3.5);
        GlyphPx(dc, Brushes.White, 3, 3, 4, 2);
    }

    // G. Calques
    static void LayersLarge(DrawingContext dc)
    {
        var back = new Rect(72, 20, 164, 164);
        var front = new Rect(20, 72, 164, 164);
        dc.PushOpacity(0.9);
        dc.DrawRoundedRectangle(Diagonal(AquaTop, AquaBottom), null, back, 40, 40);
        dc.Pop();
        dc.PushOpacity(0.93);
        dc.DrawRoundedRectangle(Diagonal(SkyTop, SkyBottom), null, front, 40, 40);
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(Solid(0x33, 0xFF, 0xFF, 0xFF), 2), Inflate(front, -1), 39, 39);
        Glyph(dc, Brushes.White, 50, 102, 48, 8);
    }
    static void LayersSmall(DrawingContext dc)
    {
        dc.PushOpacity(0.9);
        dc.DrawRoundedRectangle(Diagonal(AquaTop, AquaBottom), null, new Rect(5, 1, 10, 10), 2.5, 2.5);
        dc.Pop();
        dc.DrawRoundedRectangle(Diagonal(SkyTop, SkyBottom), null, new Rect(1, 5, 10, 10), 2.5, 2.5);
        GlyphPx(dc, Brushes.White, 2, 6, 3, 2);
    }

    // H. Viseur
    static void ViewfinderLarge(DrawingContext dc)
    {
        var area = new Rect(16, 16, 224, 224);
        var pen = new Pen(DiagonalAbs(AquaTop, AquaBottom, area), 20) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        double a = 26, b = 230, l = 54;
        foreach (var (x, y, dx, dy) in new[] { (a, a, 1, 1), (b, a, -1, 1), (a, b, 1, -1), (b, b, -1, -1) })
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, y + dy * l), false, false);
                c.ArcTo(new Point(x + dx * 0, y + dy * 0), new Size(0, 0), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(new Point(x + dx * l, y), true, true);
            }
            dc.DrawGeometry(null, pen, g);
        }
        Glyph(dc, DiagonalAbs(SkyTop, SkyBottom, new Rect(68, 68, 120, 120)), 68, 68, 54, 12);
    }
    static void ViewfinderSmall(DrawingContext dc)
    {
        var b = DiagonalAbs(AquaTop, AquaBottom, new Rect(0, 0, 16, 16));
        Px(dc, b, 0, 0, 4, 2); Px(dc, b, 0, 0, 2, 4);
        Px(dc, b, 12, 0, 4, 2); Px(dc, b, 14, 0, 2, 4);
        Px(dc, b, 0, 14, 4, 2); Px(dc, b, 0, 12, 2, 4);
        Px(dc, b, 12, 14, 4, 2); Px(dc, b, 14, 12, 2, 4);
        GlyphPx(dc, DiagonalAbs(SkyTop, SkyBottom, new Rect(4, 4, 8, 8)), 4, 4, 3, 2);
    }

    // ======================= Sorties =======================

    public static void WriteIcon(string id, string outDir)
    {
        var v = AllVariants.Single(x => x.Id == id);
        Directory.CreateDirectory(outDir);
        var pngs = Sizes.Select(s => (s, Png(Render(v, s)))).ToList();
        foreach (var (s, data) in pngs)
            if (s is 16 or 32 or 48 or 256) File.WriteAllBytes(Path.Combine(outDir, $"BarcodeBoard-{s}.png"), data);
        WriteIco(Path.Combine(outDir, "BarcodeBoard.ico"), pngs);
        Console.WriteLine($"Icône {v.Name} écrite dans {outDir}");
    }

    public static void WritePreview(string path)
    {
        int[] shown = [128, 64, 48, 32, 24, 16];
        const int labelW = 200, gap = 18, rowH = 170, panelPad = 20;
        int panelW = panelPad * 2 + shown.Sum() + gap * (shown.Length - 1);
        int w = labelW + panelW * 2, h = rowH * Variants.Length;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(labelW, 0, panelW, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(labelW + panelW, 0, panelW, h));
            for (int r = 0; r < Variants.Length; r++)
            {
                var v = Variants[r];
                int top = r * rowH;
                var text = new FormattedText(v.Name, CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Variable Display Semibold"), 18, new SolidColorBrush(Ink), 1);
                dc.DrawText(text, new Point(20, top + rowH / 2 - 12));
                if (r > 0) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)), null, new Rect(0, top, w, 1));
                for (int p = 0; p < 2; p++)
                {
                    int x = labelW + p * panelW + panelPad;
                    foreach (var s in shown)
                    {
                        var img = Render(v, s == 128 ? 256 : s);
                        int y = top + (rowH - s) / 2;
                        dc.DrawImage(img, new Rect(x, y, s, s));
                        x += s + gap;
                    }
                }
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        File.WriteAllBytes(path, Png(rtb));
        Console.WriteLine($"Planche écrite : {path}");
    }

    static BitmapSource Render(Variant v, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            bool small = size <= SmallMax;
            dc.PushTransform(new ScaleTransform(size / (small ? 16.0 : 256.0), size / (small ? 16.0 : 256.0)));
            (small ? v.Small : v.Large)(dc);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ======================= Outils de dessin =======================

    static Brush Vertical(Color top, Color bottom) => new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
    static Brush Solid(Color c) => new SolidColorBrush(c);
    static Brush Solid(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
    static Rect Inflate(Rect r, double d) { r.Inflate(d, d); return r; }

    static void Shadow(DrawingContext dc, Rect r, double radius, double strength)
    {
        for (int i = 4; i >= 1; i--)
        {
            var s = Inflate(r, i * 1.5);
            s.Offset(0, 3 + i * 1.5);
            dc.DrawRoundedRectangle(Solid((byte)(255 * strength / (i + 1)), 0, 0, 0), null, s, radius + i, radius + i);
        }
    }

    /// <summary>Repère QR : anneau de 7 modules, blanc d'un module, cœur de 3 modules.</summary>
    static void QrFinder(DrawingContext dc, Brush ink, double x, double y, double m)
    {
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new RectangleGeometry(new Rect(x, y, 7 * m, 7 * m), m * 0.6, m * 0.6));
        ring.Children.Add(new RectangleGeometry(new Rect(x + m, y + m, 5 * m, 5 * m), m * 0.3, m * 0.3));
        dc.DrawGeometry(ink, null, ring);
        dc.DrawRoundedRectangle(ink, null, new Rect(x + 2 * m, y + 2 * m, 3 * m, 3 * m), m * 0.3, m * 0.3);
    }

    /// <summary>Anneau carré pixel (petites tailles).</summary>
    static void PixelRing(DrawingContext dc, Brush ink, double x, double y, double size, double hole)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new RectangleGeometry(new Rect(x, y, size, size)));
        double o = (size - hole) / 2;
        g.Children.Add(new RectangleGeometry(new Rect(x + o, y + o, hole, hole)));
        dc.DrawGeometry(ink, null, g);
    }

    static void Px(DrawingContext dc, Brush b, double x, double y, double w = 1, double h = 1) => dc.DrawRectangle(b, null, new Rect(x, y, w, h));

    // ======================= A. Étiquette laser =======================

    static void LabelLarge(DrawingContext dc)
    {
        var back = new Rect(64, 18, 176, 160);
        Shadow(dc, back, 30, 0.10);
        dc.DrawRoundedRectangle(Vertical(BlueTop, BlueBottom), null, back, 30, 30);
        dc.DrawRoundedRectangle(null, new Pen(Solid(0x40, 0xFF, 0xFF, 0xFF), 2), Inflate(back, -1), 29, 29);
        var bar = Solid(0xF0, 0xFF, 0xFF, 0xFF);
        double x = 200;
        foreach (var (w, gap) in new[] { (4.0, 3.0), (2.0, 3.0), (6.0, 3.0), (2.0, 4.0), (4.0, 2.0), (2.0, 0.0) })
        {
            dc.DrawRectangle(bar, null, new Rect(x, 40, w, 112));
            x += w + gap;
        }

        var card = new Rect(16, 66, 178, 174);
        Shadow(dc, card, 30, 0.16);
        dc.DrawRoundedRectangle(Vertical(Colors.White, Color.FromRgb(0xE6, 0xEC, 0xF3)), new Pen(Solid(0x26, 0, 0, 0), 2), card, 30, 30);
        var ink = Solid(Ink);
        const double m = 9;
        double ox = 37, oy = 86;
        QrFinder(dc, ink, ox, oy, m);
        QrFinder(dc, ink, ox + 9 * m, oy, m);
        QrFinder(dc, ink, ox, oy + 9 * m, m);
        int[,] data = { { 1, 0, 1, 1, 0, 1, 0 }, { 0, 1, 1, 0, 1, 0, 1 }, { 1, 1, 0, 1, 0, 0, 1 }, { 0, 0, 1, 0, 1, 1, 0 },
                        { 1, 0, 1, 1, 0, 1, 1 }, { 0, 1, 0, 0, 1, 0, 1 }, { 1, 1, 0, 1, 1, 0, 0 } };
        for (int r = 0; r < 7; r++)
            for (int c = 0; c < 7; c++)
                if (data[r, c] == 1) dc.DrawRectangle(ink, null, new Rect(ox + (9 + c) * m, oy + (9 + r) * m, m, m));

        double y = oy + 8 * m;
        var fade = new LinearGradientBrush(
            [new GradientStop(Color.FromArgb(0, Laser.R, Laser.G, Laser.B), 0),
             new GradientStop(Color.FromArgb(0x70, Laser.R, Laser.G, Laser.B), 0.15),
             new GradientStop(Color.FromArgb(0x70, Laser.R, Laser.G, Laser.B), 0.85),
             new GradientStop(Color.FromArgb(0, Laser.R, Laser.G, Laser.B), 1)], new Point(0, 0), new Point(1, 0));
        for (int i = 0; i < 4; i++)
        {
            double h = 22 - i * 5;
            var g = fade.Clone();
            g.Opacity = 0.25 + i * 0.12;
            dc.DrawRoundedRectangle(g, null, new Rect(2, y - h / 2, 222, h), h / 2, h / 2);
        }
        var core = new LinearGradientBrush(
            [new GradientStop(Color.FromArgb(0, Laser.R, Laser.G, Laser.B), 0), new GradientStop(Laser, 0.1),
             new GradientStop(Laser, 0.9), new GradientStop(Color.FromArgb(0, Laser.R, Laser.G, Laser.B), 1)], new Point(0, 0), new Point(1, 0));
        dc.DrawRoundedRectangle(core, null, new Rect(4, y - 3.5, 218, 7), 3.5, 3.5);
        dc.DrawRectangle(Solid(0xB0, 0xFF, 0xFF, 0xFF), null, new Rect(30, y - 1, 166, 2));
    }

    static void LabelSmall(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Vertical(BlueTop, BlueBottom), null, new Rect(4, 1, 11, 10), 2, 2);
        Px(dc, Solid(0xE6, 0xFF, 0xFF, 0xFF), 13, 3, 1, 5);
        Px(dc, Solid(0x55, 0, 0, 0), 1, 5, 11, 11);
        dc.DrawRoundedRectangle(Solid(Color.FromRgb(0x5E, 0x66, 0x70)), null, new Rect(1, 4, 11, 11), 2, 2);
        dc.DrawRoundedRectangle(Vertical(Colors.White, Color.FromRgb(0xE6, 0xEC, 0xF3)), null, new Rect(1.6, 4.6, 9.8, 9.8), 1.5, 1.5);
        var ink = Solid(Ink);
        PixelRing(dc, ink, 3, 6, 3, 1);
        PixelRing(dc, ink, 7, 6, 3, 1);
        PixelRing(dc, ink, 3, 10, 3, 1);
        Px(dc, ink, 7, 10); Px(dc, ink, 9, 11); Px(dc, ink, 8, 12);
        Px(dc, Solid(Laser), 0, 9, 13, 1);
    }

    // ======================= B. Viseur =======================

    static readonly Color TealTop = Color.FromRgb(0x3C, 0xD9, 0xB0);
    static readonly Color TealBottom = Color.FromRgb(0x07, 0x76, 0x62);

    static void FinderLarge(DrawingContext dc)
    {
        var tile = new Rect(20, 20, 216, 216);
        Shadow(dc, tile, 52, 0.14);
        dc.DrawRoundedRectangle(Vertical(TealTop, TealBottom), null, tile, 52, 52);
        dc.DrawRoundedRectangle(null, new Pen(Solid(0x4D, 0xFF, 0xFF, 0xFF), 2), Inflate(tile, -1), 51, 51);

        // Coins du viseur
        var pen = new Pen(Brushes.White, 15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        double a = 56, b = 200, l = 40;
        foreach (var (x, y, dx, dy) in new[] { (a, a, 1, 1), (b, a, -1, 1), (a, b, 1, -1), (b, b, -1, -1) })
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, y + dy * l), false, false);
                c.LineTo(new Point(x, y), true, true);
                c.LineTo(new Point(x + dx * l, y), true, true);
            }
            dc.DrawGeometry(null, pen, g);
        }

        // Le code visé, en blanc : trois repères et quelques modules
        var white = Brushes.White;
        const double m = 6;
        double ox = 128 - 10.5 * m, oy = 128 - 10.5 * m;   // QR de 21 modules centré
        QrFinder(dc, white, ox, oy, m);
        QrFinder(dc, white, ox + 14 * m, oy, m);
        QrFinder(dc, white, ox, oy + 14 * m, m);
        var rnd = new Random(7);
        for (int r = 0; r < 21; r++)
            for (int c = 0; c < 21; c++)
            {
                bool inFinder = (r < 8 && c < 8) || (r < 8 && c > 12) || (r > 12 && c < 8);
                if (!inFinder && rnd.NextDouble() < 0.28) dc.DrawRectangle(white, null, new Rect(ox + c * m, oy + r * m, m, m));
            }
    }

    static void FinderSmall(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Vertical(TealTop, TealBottom), null, new Rect(1, 1, 14, 14), 3, 3);
        var w = Brushes.White;
        // coins (3 px de bras)
        Px(dc, w, 3, 3, 3, 1); Px(dc, w, 3, 3, 1, 3);
        Px(dc, w, 10, 3, 3, 1); Px(dc, w, 12, 3, 1, 3);
        Px(dc, w, 3, 12, 3, 1); Px(dc, w, 3, 10, 1, 3);
        Px(dc, w, 10, 12, 3, 1); Px(dc, w, 12, 10, 1, 3);
        // repère central
        PixelRing(dc, w, 6, 6, 4, 2);
    }

    // ======================= C. Écran =======================

    static readonly Color GraphiteTop = Color.FromRgb(0x4A, 0x50, 0x59);
    static readonly Color GraphiteBottom = Color.FromRgb(0x1F, 0x23, 0x28);

    static void ScreenLarge(DrawingContext dc)
    {
        // Pied
        dc.DrawRectangle(Vertical(GraphiteTop, GraphiteBottom), null, new Rect(112, 186, 32, 30));
        var foot = new Rect(70, 208, 116, 16);
        Shadow(dc, foot, 8, 0.12);
        dc.DrawRoundedRectangle(Vertical(GraphiteTop, GraphiteBottom), null, foot, 8, 8);

        var bezel = new Rect(12, 30, 232, 164);
        Shadow(dc, bezel, 22, 0.16);
        dc.DrawRoundedRectangle(Vertical(GraphiteTop, GraphiteBottom), null, bezel, 22, 22);
        var screen = new Rect(24, 42, 208, 140);
        dc.DrawRoundedRectangle(Vertical(BlueTop, BlueBottom), null, screen, 10, 10);
        // reflet discret
        dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0.6, 1)), null, screen, 10, 10);

        // Étiquette code-barres affichée
        var label = new Rect(54, 62, 148, 100);
        Shadow(dc, label, 8, 0.12);
        dc.DrawRoundedRectangle(Vertical(Colors.White, Color.FromRgb(0xEC, 0xF0, 0xF5)), null, label, 8, 8);
        var ink = Solid(Ink);
        double x = 70;
        int[] pattern = [6, 3, 3, 3, 9, 3, 3, 6, 3, 3, 6, 3, 3, 3, 9, 3, 3, 6, 6, 3, 3, 3, 6];
        bool dark = true;
        foreach (var wd in pattern)
        {
            if (dark) dc.DrawRectangle(ink, null, new Rect(x, 76, wd, 54));
            x += wd;
            dark = !dark;
        }
        dc.DrawRoundedRectangle(Solid(0x55, 0x1B, 0x1F, 0x24), null, new Rect(92, 140, 72, 6), 3, 3);
    }

    static void ScreenSmall(DrawingContext dc)
    {
        var graphite = Vertical(GraphiteTop, GraphiteBottom);
        dc.DrawRoundedRectangle(graphite, null, new Rect(0, 2, 16, 11), 1.5, 1.5);
        dc.DrawRectangle(Vertical(BlueTop, BlueBottom), null, new Rect(1, 3, 14, 9));
        dc.DrawRectangle(Brushes.White, null, new Rect(3, 4, 10, 7));
        var ink = Solid(Ink);
        foreach (var bx in new[] { 4, 6, 7, 9, 11 }) Px(dc, ink, bx, 5, 1, 5);
        Px(dc, graphite, 7, 13, 2, 1);
        Px(dc, graphite, 5, 14, 6, 1);
    }

    // ======================= D. Hybride =======================

    static readonly Color HybridBlue = Color.FromRgb(0x0F, 0x6C, 0xBD);

    static void HybridLarge(DrawingContext dc)
    {
        var tile = new Rect(20, 20, 216, 216);
        Shadow(dc, tile, 48, 0.14);
        dc.DrawRoundedRectangle(Vertical(Colors.White, Color.FromRgb(0xE9, 0xED, 0xF2)), new Pen(Solid(0x24, 0, 0, 0), 2), tile, 48, 48);

        var ink = Solid(Ink);
        var blue = Solid(HybridBlue);
        const double m = 19, ox = 52, oy = 50;
        // Haut : un QR code (repère + modules) sur 4 rangées
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new RectangleGeometry(new Rect(ox, oy, 3 * m, 3 * m), 6, 6));
        ring.Children.Add(new RectangleGeometry(new Rect(ox + m * 0.75, oy + m * 0.75, 1.5 * m, 1.5 * m), 2, 2));
        dc.DrawGeometry(ink, null, ring);
        int[,] top =
        {
            { 0, 0, 0, 1, 0, 1, 1, 0 },
            { 0, 0, 0, 0, 1, 1, 0, 1 },
            { 0, 0, 0, 1, 1, 0, 1, 1 },
            { 1, 0, 1, 1, 0, 1, 1, 0 },
        };
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                if (top[r, c] == 1) dc.DrawRoundedRectangle(c == 6 ? blue : ink, null, new Rect(ox + c * m, oy + r * m, m, m), 2, 2);

        // Bas : chaque colonne se prolonge en barres de code-barres
        (double off, double w)[][] bars =
        [
            [(0, 7), (11, 4)], [(3, 13)], [(0, 4), (8, 9)], [(0, 19)],
            [(5, 5), (13, 4)], [(0, 10)], [(2, 15)], [(4, 4), (11, 8)],
        ];
        double yEnd = 208;
        for (int c = 0; c < 8; c++)
        {
            bool joined = top[3, c] == 1;
            double y0 = joined ? oy + 3 * m : oy + 4 * m + 6;
            foreach (var (off, w) in bars[c])
                dc.DrawRectangle(c == 6 ? blue : ink, null, new Rect(ox + c * m + off, y0, w, yEnd - y0));
        }
    }

    static void HybridSmall(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Solid(Color.FromRgb(0xA9, 0xB1, 0xBB)), null, new Rect(1, 1, 14, 14), 3, 3);
        dc.DrawRoundedRectangle(Vertical(Colors.White, Color.FromRgb(0xEC, 0xF0, 0xF4)), null, new Rect(2, 2, 12, 12), 2, 2);
        var ink = Solid(Ink);
        var blue = Solid(HybridBlue);
        PixelRing(dc, ink, 3, 3, 3, 1);
        Px(dc, ink, 7, 3); Px(dc, ink, 9, 3); Px(dc, ink, 12, 3);
        Px(dc, ink, 8, 4); Px(dc, blue, 11, 4);
        Px(dc, ink, 7, 5); Px(dc, ink, 10, 5);
        foreach (var (bx, bw) in new[] { (3, 1), (5, 2), (8, 1), (12, 1) }) Px(dc, ink, bx, 7, bw, 6);
        Px(dc, blue, 10, 6, 1, 7);
    }

    // ======================= Fichiers =======================

    static byte[] Png(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Fichier .ico dont chaque image est un PNG (format accepté depuis Windows Vista).</summary>
    static void WriteIco(string path, List<(int size, byte[] data)> images)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
    }
}
