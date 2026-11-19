using System.Windows;
using System.Windows.Controls;

namespace BarcodeBoard;

/// <summary>
/// Bande d'onglets façon navigateur : les onglets rétrécissent quand il y en a beaucoup,
/// puis la bande défile (molette) en gardant toujours l'onglet actif visible.
/// La place des boutons « + » et « liste des onglets » est réservée à droite.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public const double MinTab = 110, MaxTab = 220, Gap = 2;

    private double _offset, _tabWidth;
    private bool _revealSelected = true;

    public TabStripPanel() => ClipToBounds = true;

    /// <summary>Largeur laissée libre à droite de la bande (boutons).</summary>
    public double Reserve { get; set; } = 80;

    public bool IsOverflowing { get; private set; }
    public event EventHandler? OverflowChanged;

    protected override Size MeasureOverride(Size available)
    {
        int n = InternalChildren.Count;
        double room = double.IsInfinity(available.Width) ? n * (MaxTab + Gap) : Math.Max(0, available.Width - Reserve);
        _tabWidth = n == 0 ? 0 : Math.Clamp(room / n - Gap, MinTab, MaxTab);

        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_tabWidth, available.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double total = n * (_tabWidth + Gap);
        bool overflow = total > room + 0.5;
        if (overflow != IsOverflowing)
        {
            IsOverflowing = overflow;
            Dispatcher.BeginInvoke(() => OverflowChanged?.Invoke(this, EventArgs.Empty));
        }
        return new Size(Math.Min(total, room), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int n = InternalChildren.Count;
        double step = _tabWidth + Gap;
        double maxOffset = Math.Max(0, n * step - final.Width);

        if (_revealSelected)
        {
            for (int i = 0; i < n; i++)
            {
                if (InternalChildren[i] is FrameworkElement { DataContext: TabViewModel { IsActive: true } })
                {
                    double left = i * step, right = left + _tabWidth;
                    if (left < _offset) _offset = left;
                    else if (right > _offset + final.Width) _offset = right - final.Width;
                    break;
                }
            }
        }
        _offset = Math.Clamp(_offset, 0, maxOffset);

        for (int i = 0; i < n; i++)
            InternalChildren[i].Arrange(new Rect(i * step - _offset, 0, _tabWidth, final.Height));
        return final;
    }

    /// <summary>Défilement à la molette.</summary>
    public void ScrollBy(double delta)
    {
        _offset += delta;
        _revealSelected = false;
        InvalidateArrange();
    }

    /// <summary>Ramène l'onglet actif dans la partie visible (après un changement d'onglet).</summary>
    public void RevealSelected()
    {
        _revealSelected = true;
        InvalidateMeasure();
        InvalidateArrange();
    }
}
