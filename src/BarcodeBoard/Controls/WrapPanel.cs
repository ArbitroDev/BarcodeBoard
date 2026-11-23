using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace BarcodeBoard.Controls;

/// <summary>
/// Place les cartes de gauche à droite puis passe à la ligne, comme le WrapPanel de WPF (absent de WinUI).
/// Les cartes ont des largeurs différentes (QR code, code-barres) : une grille uniforme ne convient pas.
/// Chaque ligne prend la hauteur de sa plus haute carte ; les cartes sont alignées en haut de leur ligne.
/// </summary>
public sealed class WrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + size.Width > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight;
                lineWidth = lineHeight = 0;
            }
            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(Math.Max(width, lineWidth), height + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight;
                lineHeight = 0;
            }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return finalSize;
    }
}
