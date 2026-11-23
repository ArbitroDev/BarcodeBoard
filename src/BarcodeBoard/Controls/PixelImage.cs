using System.Numerics;
using BarcodeBoard.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BarcodeBoard.Controls;

/// <summary>
/// Affiche une image de code pixel pour pixel : un pixel du bitmap = un pixel physique de l'écran.
/// L'élément mesure exactement la taille du bitmap en pixels logiques (pixels / échelle de l'écran), et le
/// bitmap est dessiné par un visuel de composition au plus proche voisin : même un élément qui ne tomberait pas
/// pile sur un pixel physique ne produit aucun gris, jamais de module lissé ni étiré.
/// Le contrôle <see cref="Image"/> de WinUI ne permet pas de choisir l'interpolation : il n'est pas utilisé.
/// </summary>
public sealed class PixelImage : Panel
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(object), typeof(PixelImage), new PropertyMetadata(null, (d, _) => ((PixelImage)d).OnSourceChanged()));

    public static readonly DependencyProperty HidesPointerProperty = DependencyProperty.Register(
        nameof(HidesPointer), typeof(bool), typeof(PixelImage),
        new PropertyMetadata(false, (d, e) => ((PixelImage)d).ProtectedCursor = (bool)e.NewValue ? PointerCursor.Hidden : null));

    private readonly SpriteVisual _sprite;
    private readonly CompositionSurfaceBrush _brush;
    private LoadedImageSurface? _surface;
    private int _version;

    public PixelImage()
    {
        // Fond transparent : le code reçoit le pointeur (HidesPointer).
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _brush = compositor.CreateSurfaceBrush();
        _brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.NearestNeighbor;
        _brush.Stretch = CompositionStretch.Fill;
        _sprite = compositor.CreateSpriteVisual();
        _sprite.Brush = _brush;
        ElementCompositionPreview.SetElementChildVisual(this, _sprite);
        Unloaded += (_, _) =>
        {
            // Un Unloaded tardif peut suivre le Loaded d'un élément déplacé : il est alors ignoré.
            if (IsLoaded) return;
            _brush.Surface = null;
            _surface?.Dispose();
            _surface = null;
        };
        Loaded += (_, _) => { if (_surface is null) OnSourceChanged(); };
    }

    /// <summary>L'image à afficher (<see cref="CodeBitmap"/>), calculée à l'échelle de l'écran.</summary>
    public CodeBitmap? Source
    {
        get => (CodeBitmap?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Pointeur invisible au-dessus de l'image : il ne doit cacher aucune partie d'un code en cours de lecture.</summary>
    public bool HidesPointer
    {
        get => (bool)GetValue(HidesPointerProperty);
        set => SetValue(HidesPointerProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Source is { } s ? new Size(s.WidthDip, s.HeightDip) : new Size(0, 0);

    protected override Size ArrangeOverride(Size finalSize) =>
        Source is { } s ? new Size(s.WidthDip, s.HeightDip) : finalSize;

    private async void OnSourceChanged()
    {
        InvalidateMeasure();
        int version = ++_version;
        var source = Source;
        if (source is null)
        {
            _brush.Surface = null;
            _sprite.Size = Vector2.Zero;
            _surface?.Dispose();
            _surface = null;
            return;
        }

        // PNG sans perte en mémoire, décodé par la composition : ses pixels sont exactement ceux du code.
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(ImageEncoding.Png(source));
            await writer.StoreAsync();
            writer.DetachStream();
        }
        if (version != _version) { stream.Dispose(); return; } // une image plus récente a été demandée entre-temps
        stream.Seek(0);
        var surface = LoadedImageSurface.StartLoadFromStream(stream);
        surface.LoadCompleted += (s, e) =>
        {
            stream.Dispose();
            if (version != _version || e.Status != LoadedImageSourceLoadStatus.Success) { s.Dispose(); return; }
            // L'ancienne image reste affichée jusqu'à ce que la nouvelle soit prête : pas de clignement.
            var previous = _surface;
            _surface = s;
            _brush.Surface = s;
            _sprite.Size = new Vector2((float)source.WidthDip, (float)source.HeightDip);
            previous?.Dispose();
        };
    }
}
