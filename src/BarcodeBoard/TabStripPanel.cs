using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BarcodeBoard;

/// <summary>
/// Bande d'onglets façon navigateur : les onglets rétrécissent quand il y en a beaucoup,
/// puis la bande défile (molette) en gardant toujours l'onglet actif visible.
/// La place des boutons « + » et « liste des onglets » est réservée à droite.
/// Comme dans l'Explorateur de Windows 11, un onglet se glisse le long de la bande pour changer de place, et les
/// onglets glissent jusqu'à leur nouvelle place (déplacement, ouverture, fermeture, arrivée d'une autre fenêtre).
/// </summary>
public sealed class TabStripPanel : Panel
{
    public const double MinTab = 110, MaxTab = 220, Gap = 2;
    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(180);

    private double _offset, _tabWidth;
    private bool _revealSelected = true, _animateNext;

    /// <summary>Dernière place de chaque onglet (par onglet et non par élément : un élément peut être recréé).</summary>
    private readonly Dictionary<object, double> _lastX = [];

    private UIElement? _dragged;
    private double _dragX;

    public TabStripPanel() => ClipToBounds = true;

    /// <summary>Largeur laissée libre à droite de la bande (boutons).</summary>
    public double Reserve { get; set; } = 80;

    public bool IsOverflowing { get; private set; }
    public event EventHandler? OverflowChanged;

    /// <summary>Nombre de places : les onglets, plus celle réservée à un onglet venu d'une autre fenêtre.</summary>
    private int Slots => InternalChildren.Count + (_incoming >= 0 ? 1 : 0);

    protected override Size MeasureOverride(Size available)
    {
        int n = Slots;
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
        double maxOffset = Math.Max(0, Slots * step - final.Width);

        if (_revealSelected && _dragged is null)
        {
            for (int i = 0; i < n; i++)
            {
                if (InternalChildren[i] is FrameworkElement { DataContext: TabViewModel { IsActive: true } })
                {
                    // Sa place, décalée d'un cran s'il est après la place ouverte pour un onglet d'une autre fenêtre.
                    double left = (_incoming >= 0 && i >= _incoming ? i + 1 : i) * step, right = left + _tabWidth;
                    if (left < _offset) _offset = left;
                    else if (right > _offset + final.Width) _offset = right - final.Width;
                    break;
                }
            }
        }
        _offset = Math.Clamp(_offset, 0, maxOffset);

        // Pendant un glisser, l'onglet tiré suit le pointeur et les autres lui laissent la place où il tomberait ;
        // de même pour un onglet d'une autre fenêtre tenu au-dessus de la bande.
        int from = _dragged is null ? -1 : InternalChildren.IndexOf(_dragged);
        int target = from >= 0 ? DropIndex() : _incoming;

        bool animate = _animateNext && SystemParameters.ClientAreaAnimation;
        _animateNext = false;
        var seen = new HashSet<object>();
        for (int i = 0, slot = 0; i < n; i++)
        {
            var child = InternalChildren[i];
            double x;
            if (i == from) x = DraggedX(n, step);
            else
            {
                if (slot == target) slot++;
                x = slot++ * step - _offset;
            }
            child.Arrange(new Rect(x, 0, _tabWidth, final.Height));

            var key = Key(child);
            seen.Add(key);
            if (i == from) StopSlide(child);
            else if (animate && _lastX.TryGetValue(key, out var old)) Slide(child, old - x);
            _lastX[key] = x;
        }
        foreach (var gone in _lastX.Keys.Where(k => !seen.Contains(k)).ToList()) _lastX.Remove(gone);
        return final;
    }

    private static object Key(UIElement child) => (child as FrameworkElement)?.DataContext ?? child;

    /// <summary>Fait partir l'onglet de sa place affichée (décalée de <paramref name="shift"/>) jusqu'à sa nouvelle place.</summary>
    private static void Slide(UIElement child, double shift)
    {
        if (Math.Abs(shift) < 0.5) return; // même place : l'animation en cours, s'il y en a une, se termine
        if (child.RenderTransform is not TranslateTransform t)
            child.RenderTransform = t = new TranslateTransform();
        double from = shift + t.X; // un onglet encore en mouvement repart d'où il est vraiment
        if (Math.Abs(from) < 0.5) return;
        var slide = new DoubleAnimation(from, 0, SlideDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        t.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private static void StopSlide(UIElement child)
    {
        if (child.RenderTransform is not TranslateTransform t) return;
        t.BeginAnimation(TranslateTransform.XProperty, null);
        t.X = 0;
    }

    /// <summary>
    /// Bord gauche de l'onglet tiré : il reste dans la partie visible de la bande (qui défile toute seule quand on
    /// pousse contre un bord, voir <see cref="AutoScroll"/>) et ne dépasse pas le premier ni le dernier onglet.
    /// </summary>
    private double DraggedX(int n, double step)
    {
        double min = Math.Max(-_offset, 0);
        double max = Math.Min((n - 1) * step - _offset, ActualWidth - _tabWidth);
        return max < min ? min : Math.Clamp(_dragX, min, max);
    }

    /// <summary>Place que prendrait l'onglet tiré s'il était lâché maintenant : celle dont il couvre le plus.</summary>
    private int DropIndex()
    {
        int n = InternalChildren.Count;
        double step = _tabWidth + Gap;
        if (n == 0) return -1;
        return step <= 0 ? 0 : Math.Clamp((int)Math.Round((DraggedX(n, step) + _offset) / step), 0, n - 1);
    }

    // Défilement automatique : l'onglet tiré poussé contre un bord fait défiler la bande, tant qu'il y reste.
    private System.Windows.Threading.DispatcherTimer? _scrollTimer;

    private void AutoScroll()
    {
        double over = _dragX < 0 ? _dragX : Math.Max(0, _dragX - (ActualWidth - _tabWidth));
        if (_dragged is null || !IsOverflowing || Math.Abs(over) < 1)
        {
            _scrollTimer?.Stop();
            return;
        }
        if (_scrollTimer is null)
        {
            _scrollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += (_, _) => AutoScrollStep();
        }
        _scrollTimer.Start();
    }

    private void AutoScrollStep()
    {
        double over = _dragX < 0 ? _dragX : Math.Max(0, _dragX - (ActualWidth - _tabWidth));
        if (_dragged is null || Math.Abs(over) < 1) { _scrollTimer?.Stop(); return; }
        // Plus le pointeur dépasse, plus la bande défile vite (avec une limite).
        _offset += Math.Clamp(over / 4, -24, 24);
        InvalidateArrange();
    }

    protected override void OnVisualChildrenChanged(DependencyObject added, DependencyObject removed)
    {
        base.OnVisualChildrenChanged(added, removed);
        _animateNext = true;
    }

    // ---- Onglet d'une autre fenêtre tenu au-dessus de la bande ----

    private int _incoming = -1;

    /// <summary>Place où entrerait un onglet lâché à cette abscisse (repère de la bande).</summary>
    public int InsertionIndex(double x)
    {
        double step = _tabWidth + Gap;
        int n = InternalChildren.Count;
        // Par place et non par onglet : la place ouverte reste sous le pointeur, sans aller-retour.
        return step <= 0 ? n : Math.Clamp((int)Math.Floor((x + _offset) / step), 0, n);
    }

    /// <summary>Ouvre une place à l'endroit du pointeur : les onglets s'écartent pour accueillir l'onglet tenu.</summary>
    public void ShowIncoming(double x)
    {
        int index = InsertionIndex(x);
        if (index == _incoming) return;
        _incoming = index;
        _animateNext = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Referme la place ouverte (l'onglet tenu est reparti, ou il vient d'être accueilli).</summary>
    public void HideIncoming()
    {
        if (_incoming < 0) return;
        _incoming = -1;
        _animateNext = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public int IncomingIndex => _incoming;

    /// <summary>La place ouverte est dessinée comme un onglet vide, pour montrer où l'onglet va entrer.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_incoming < 0 || TryFindResource("Hover") is not Brush fill) return;
        double x = _incoming * (_tabWidth + Gap) - _offset;
        var shape = new RectangleGeometry(new Rect(x, 4, _tabWidth, Math.Max(0, ActualHeight - 4)), 7, 7);
        var pen = TryFindResource("Accent") is Brush accent ? new Pen(accent, 1.5) { DashStyle = new DashStyle([3, 2], 0) } : null;
        dc.DrawGeometry(fill, pen, shape);
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
        _animateNext = true;
        InvalidateArrange();
        AutoScroll();
    }

    /// <summary>Fin du glisser ; rend la place où l'onglet a été lâché, ou -1 s'il n'y avait pas de glisser.</summary>
    public int EndDrag()
    {
        _scrollTimer?.Stop();
        if (_dragged is null) return -1;
        int index = InternalChildren.Contains(_dragged) ? DropIndex() : -1;
        SetZIndex(_dragged, 0);
        _dragged = null;
        _revealSelected = true;
        _animateNext = true;
        InvalidateArrange();
        return index;
    }

    public bool IsDragging => _dragged is not null;

    /// <summary>Défilement à la molette.</summary>
    public void ScrollBy(double delta)
    {
        _offset += delta;
        _revealSelected = false;
        _animateNext = true;
        InvalidateArrange();
    }

    /// <summary>Ramène l'onglet actif dans la partie visible (après un changement d'onglet).</summary>
    public void RevealSelected()
    {
        _revealSelected = true;
        _animateNext = true;
        InvalidateMeasure();
        InvalidateArrange();
    }
}
