using System.Windows;
using System.Windows.Controls;

namespace BarcodeBoard;

/// <summary>
/// Bande d'onglets façon navigateur : les onglets rétrécissent quand il y en a beaucoup,
/// puis la bande défile (molette) en gardant toujours l'onglet actif visible.
/// La place des boutons « + » et « liste des onglets » est réservée à droite.
/// Comme dans l'Explorateur de Windows 11, un onglet se glisse le long de la bande pour changer de place.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public const double MinTab = 110, MaxTab = 220, Gap = 2;

    private double _offset, _tabWidth;
    private bool _revealSelected = true;

    private UIElement? _dragged;
    private double _dragX;

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

        if (_revealSelected && _dragged is null)
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

        // Pendant un glisser, l'onglet tiré suit le pointeur et les autres lui laissent la place où il tomberait.
        int from = _dragged is null ? -1 : InternalChildren.IndexOf(_dragged);
        int target = from < 0 ? -1 : DropIndex();

        for (int i = 0, slot = 0; i < n; i++)
        {
            double x;
            if (i == from) x = DraggedX(n, step);
            else
            {
                if (slot == target) slot++;
                x = slot++ * step - _offset;
            }
            InternalChildren[i].Arrange(new Rect(x, 0, _tabWidth, final.Height));
        }
        return final;
    }

    private double DraggedX(int n, double step) => Math.Clamp(_dragX, -_offset, (n - 1) * step - _offset);

    /// <summary>Place que prendrait l'onglet tiré s'il était lâché maintenant : celle dont il couvre le plus.</summary>
    private int DropIndex()
    {
        int n = InternalChildren.Count;
        double step = _tabWidth + Gap;
        return step <= 0 ? 0 : Math.Clamp((int)Math.Round((DraggedX(n, step) + _offset) / step), 0, n - 1);
    }

    // ---- Glisser un onglet le long de la bande ----

    /// <summary>Commence à faire suivre le pointeur à cet onglet.</summary>
    public void BeginDrag(UIElement child)
    {
        _dragged = child;
        SetZIndex(child, 1);
    }

    /// <summary>Bord gauche voulu pour l'onglet tiré, dans le repère de la bande.</summary>
    public void DragTo(double left)
    {
        if (_dragged is null) return;
        _dragX = left;
        InvalidateArrange();
    }

    /// <summary>Fin du glisser ; rend la place où l'onglet a été lâché, ou -1 s'il n'y avait pas de glisser.</summary>
    public int EndDrag()
    {
        if (_dragged is null) return -1;
        int index = DropIndex();
        SetZIndex(_dragged, 0);
        _dragged = null;
        _revealSelected = true;
        InvalidateArrange();
        return index;
    }

    public bool IsDragging => _dragged is not null;

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
