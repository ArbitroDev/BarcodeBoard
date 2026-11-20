using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BarcodeBoard.Core;
using Microsoft.Win32;

namespace BarcodeBoard;

public partial class MainWindow : Window
{
    private static string FileFilter => Loc.FileFilter;

    private readonly MainViewModel _vm = new();
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IReadOnlyList<string> _startupFiles;
    private readonly bool _isFirst;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private readonly StringBuilder _scanBuffer = new();
    private CodeItemViewModel? _fullItem;
    private CodeItemViewModel? _dragCandidate;
    private Point _dragStart;
    private bool _skipClosePrompts;

    private bool _mica;

    /// <param name="startupFiles">Fichiers .codes à ouvrir une fois la fenêtre affichée.</param>
    /// <param name="adopted">Onglet sorti d'une autre fenêtre : la nouvelle fenêtre ne contient que lui.</param>
    /// <param name="isFirst">Première fenêtre de l'application : elle reprend la position et l'état enregistrés.</param>
    public MainWindow(IReadOnlyList<string>? startupFiles = null, TabViewModel? adopted = null, bool isFirst = false)
    {
        _startupFiles = startupFiles ?? [];
        _isFirst = isFirst;
        if (isFirst) Theme.Apply(Theme.Parse(_settings.Theme));
        InitializeComponent();
        AppWindows.Register(this);
#if DEBUG
        // Captures d'écran : la fenêtre s'affiche sans prendre le clavier de l'utilisateur.
        if (Environment.GetCommandLineArgs().Contains("--topmost")) ShowActivated = false;
#endif
        Theme.Changed += OnThemeChanged;
        Closed += (_, _) => Theme.Changed -= OnThemeChanged;
        UpdateThemeMenu();
        DataContext = _vm;
        _vm.CodeSize = _settings.CodeSize;
        _vm.SideOpen = _settings.SideOpen;
        if (adopted is not null) _vm.Adopt(adopted, 0);
        else _vm.NewTab();
        if (isFirst) ApplySavedBounds();
        else PlaceNextToLastWindow();

        _toastTimer.Tick += (_, _) => HideToast();
        _vm.PropertyChanged += Vm_PropertyChanged;
        Loaded += OnLoaded;
        StateChanged += (_, _) =>
        {
            UpdateMaximizedLayout();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateCaptionMetrics);
        };
        // La zone des boutons système n'est connue de Windows qu'une fois la fenêtre affichée (pas encore à Loaded).
        ContentRendered += (_, _) => UpdateCaptionMetrics();
        SizeChanged += (_, _) =>
        {
            UpdateCaptionMetrics();
            if (FullView.IsVisible) RenderFull();
        };
        Closing += OnClosing;
    }

    private TabViewModel Tab => _vm.SelectedTab;

    // ================= Démarrage, DPI, fenêtre =================

    /// <summary>Icône de la barre de titre : l'image du .ico la plus proche de 16 px à l'échelle de l'écran (dessinée au pixel).</summary>
    private void UpdateTitleIcon()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/BarcodeBoard.ico"),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        int wanted = (int)Math.Round(16 * _vm.DpiScale);
        TitleIcon.Source = decoder.Frames.OrderBy(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth >= wanted)
                           ?? decoder.Frames.MaxBy(f => f.PixelWidth);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        UpdateTitleIcon();
        ApplySideWidth();
        HookTabStrip();
        foreach (var file in _startupFiles) OpenPath(file);
        UpdateMaximizedLayout();
        UpdateCaptionMetrics();
        ComposerBox.Focus();
#if DEBUG
        if (_isFirst) DebugScenario.Run(this, _vm);
#endif
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _mica = !NoMicaRequested() && Backdrop.TryApply(this);
        if (_isFirst) RestorePlacement();
        if (_mica)
        {
            // Vrais boutons système à la place des boutons dessinés, couches translucides sur Mica Alt.
            CaptionButtons.Visibility = Visibility.Collapsed;
            NativeCaptionSpace.Visibility = Visibility.Visible;
        }
        OnThemeChanged();
    }

    /// <summary>Debug : --no-mica garde le fond opaque et les boutons dessinés (captures internes complètes).</summary>
    private static bool NoMicaRequested()
    {
#if DEBUG
        return Environment.GetCommandLineArgs().Contains("--no-mica");
#else
        return false;
#endif
    }

    private static readonly string[] Layers = ["ChromeLayer", "SideLayer", "ContentLayer"];

    /// <summary>Couches de fond : translucides sur Mica, sinon les teintes opaques de la palette.</summary>
    private void OnThemeChanged()
    {
        foreach (var key in Layers)
        {
            if (_mica) Resources[key] = Application.Current.Resources[key + "Mica"];
            else Resources.Remove(key);
        }
        Backdrop.SetDark(this, Theme.IsDark);
        UpdateThemeMenu();
    }

    internal void SetTheme(ThemeChoice choice)
    {
        _settings.Theme = Theme.ToSetting(choice);
        Theme.Apply(choice);
    }

    private void UpdateThemeMenu()
    {
        ThemeSystem.IsChecked = Theme.Choice == ThemeChoice.System;
        ThemeLight.IsChecked = Theme.Choice == ThemeChoice.Light;
        ThemeDark.IsChecked = Theme.Choice == ThemeChoice.Dark;
    }

    private void SettingsPopup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        SettingsPopup.IsOpen = false;
        e.Handled = true;
    }

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeChoice.System);
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeChoice.Light);
    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeChoice.Dark);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _vm.DpiScale = newDpi.DpiScaleX;
        UpdateTitleIcon();
        // Fenêtre agrandie déplacée vers un écran d'une autre échelle : ses marges se recalculent aussi.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { UpdateMaximizedLayout(); UpdateCaptionMetrics(); });
        if (FullView.IsVisible) RenderFull();
    }

    /// <summary>Position enregistrée en pixels physiques, si elle est encore sur un écran branché.</summary>
    private Native.RECT? SavedPlacement()
    {
        if (_settings.Placement is not [int l, int t, int r, int b] || r - l < 200 || b - t < 150) return null;
        var rect = new Native.RECT { Left = l, Top = t, Right = r, Bottom = b };
        return Native.MonitorFromRect(ref rect, Native.MONITOR_DEFAULTTONULL) == IntPtr.Zero ? null : rect;
    }

    /// <summary>
    /// Remet la fenêtre à sa place en pixels physiques : juste même sur un écran secondaire d'une autre échelle,
    /// ou avec des écrans de tailles différentes. Appelé avant le premier affichage.
    /// </summary>
    private void RestorePlacement()
    {
        if (SavedPlacement() is not { } rect) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (!Native.GetWindowPlacement(hwnd, ref placement)) return;
        placement.normalPosition = rect;
        placement.showCmd = 0; // SW_HIDE : la fenêtre n'est pas encore affichée, WPF l'affichera dans son état
        Native.SetWindowPlacement(hwnd, ref placement);
    }

    private void SavePlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (hwnd == IntPtr.Zero || !Native.GetWindowPlacement(hwnd, ref placement)) return;
        var r = placement.normalPosition;
        _settings.Placement = [r.Left, r.Top, r.Right, r.Bottom];
    }

    /// <summary>Nouvelle fenêtre : même taille que la dernière utilisée, décalée pour ne pas la cacher.</summary>
    private void PlaceNextToLastWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (AppWindows.All.FirstOrDefault(w => w != this) is not { } other) return;
        var b = other.WindowState == WindowState.Normal ? new Rect(other.Left, other.Top, other.Width, other.Height) : other.RestoreBounds;
        if (b.IsEmpty) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = b.Left + 32; Top = b.Top + 32; Width = b.Width; Height = b.Height;
    }

    private void ApplySavedBounds()
    {
        if (SavedPlacement() is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual; // position appliquée dans OnSourceInitialized
        }
        else if (_settings is { Left: double l, Top: double t, Width: double w, Height: double h }
            && w >= MinWidth && h >= MinHeight
            && l + w > SystemParameters.VirtualScreenLeft + 50 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
            && t >= SystemParameters.VirtualScreenTop - 10 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l; Top = t; Width = w; Height = h;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Une fenêtre agrandie déborde de l'écran de l'épaisseur de son cadre invisible : on mesure ce que la zone
    /// cliente déborde encore (zone cliente contre zone de travail du moniteur) et on le compense par une marge.
    /// </summary>
    private void UpdateMaximizedLayout()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaxIcon.Data = (Geometry)FindResource(maximized ? "IcoRestore" : "IcoMax");
        // Agrandie, la barre de titre se réduit à la hauteur des onglets : ils touchent le haut de l'écran.
        TitleRow.Height = new GridLength(maximized ? 34 : 42);
        if (!maximized) { Root.Margin = new Thickness(0); return; }

        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (hwnd == IntPtr.Zero || !Native.GetClientScreenRect(hwnd, out var win)
            || !Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref info))
        {
            Root.Margin = new Thickness(8);
            return;
        }
        double s = _vm.DpiScale;
        var work = info.rcWork;
        Root.Margin = new Thickness(
            Math.Max(0, work.Left - win.Left) / s, Math.Max(0, work.Top - win.Top) / s,
            Math.Max(0, win.Right - work.Right) / s, Math.Max(0, win.Bottom - work.Bottom) / s);
    }

    /// <summary>
    /// Avec Mica, les boutons Réduire / Agrandir / Fermer sont dessinés par Windows : on lit leur zone exacte
    /// pour leur réserver la bonne largeur et donner à la roue des paramètres la même hauteur, sans écart.
    /// </summary>
    private void UpdateCaptionMetrics()
    {
        if (!_mica) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var win) || !Native.GetClientScreenRect(hwnd, out var client)
            || Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CAPTION_BUTTON_BOUNDS, out var buttons, Marshal.SizeOf<Native.RECT>()) != 0
            || buttons.Right <= buttons.Left)
            return;

        double s = _vm.DpiScale;
        // Zone des boutons, relative au coin de la fenêtre (bordures invisibles comprises), ramenée à la zone cliente ;
        // Root est décalé de sa marge quand la fenêtre est agrandie.
        // Fenêtre agrandie : les boutons commencent au-dessus de l'écran, la roue ne prend que leur partie visible.
        int left = win.Left + buttons.Left - client.Left;
        double contentTop = client.Top + Root.Margin.Top * s;
        double top = Math.Max(win.Top + buttons.Top, contentTop), bottom = win.Top + buttons.Bottom;
        NativeCaptionSpace.Width = Math.Max(0, (client.Right - client.Left - left) / s - Root.Margin.Right);
        SettingsButton.Height = Math.Max(0, (bottom - top) / s);
        SettingsButton.Margin = new Thickness(0, (top - contentTop) / s, 0, 0);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_skipClosePrompts)
        {
            CommitPendingRenames();
            foreach (var tab in _vm.Tabs.Where(t => t.IsDirty).ToList())
            {
                if (!ConfirmSave(tab)) { e.Cancel = true; return; }
            }
        }

        _settings.CodeSize = _vm.CodeSize;
        _settings.SideOpen = _vm.SideOpen;
        _settings.Maximized = WindowState == WindowState.Maximized;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!b.IsEmpty) { _settings.Left = b.Left; _settings.Top = b.Top; _settings.Width = b.Width; _settings.Height = b.Height; }
        SavePlacement();
        if (!_skipClosePrompts) _settings.Save();
    }

    internal void CloseWithoutPrompts()
    {
        _skipClosePrompts = true;
        Close();
    }

    // ================= Clavier =================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // AltGr arrive comme Ctrl+Alt : ce n'est pas un raccourci, mais un caractère (ś, ł, |…) à laisser passer.
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (FullView.IsVisible)
        {
            switch (key)
            {
                case Key.Escape: CloseFull(); break;
                case Key.Left or Key.Up: StepFull(-1); break;
                case Key.Right or Key.Down: StepFull(1); break;
            }
            e.Handled = true; // rien d'autre ne réagit pendant le plein écran
            return;
        }

        if (_vm.ScanMode && ctrl)
        {
            // Une douchette envoie les caractères de contrôle (GS du GS1-128…) en Ctrl+lettre : ils vont dans
            // la lecture, jamais vers les raccourcis (Ctrl+W fermerait l'onglet). Seul Ctrl+Tab change d'onglet.
            if (key == Key.Tab) SelectTabOffset(shift ? -1 : 1);
            else if (ControlCharacter(key) is char c) _scanBuffer.Append(c);
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            switch (key)
            {
                case Key.T: _vm.NewTab(); FocusComposer(); break;
                case Key.W: CloseTab(Tab); break;
                case Key.Tab: SelectTabOffset(shift ? -1 : 1); break;
                case Key.O: OpenFiles(); break;
                case Key.S when shift: SaveAs(Tab); break;
                case Key.S: Save(Tab); break;
                case Key.L: _vm.SideOpen = !_vm.SideOpen; break;
                case Key.OemComma: SettingsPopup.IsOpen = !SettingsPopup.IsOpen; break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (key == Key.F6)
        {
            _vm.ScanMode = !_vm.ScanMode;
            e.Handled = true;
            return;
        }

        if (_vm.ScanMode)
        {
            // Une douchette branchée sur ce PC « tape » ce qu'elle lit : rien ne doit modifier l'onglet.
            switch (key)
            {
                case Key.Escape: _vm.ScanMode = false; break;
                case Key.Left or Key.Up: Tab.MoveScanFocus(-1); BringScanFocusIntoView(); break;
                case Key.Right or Key.Down: Tab.MoveScanFocus(1); BringScanFocusIntoView(); break;
                case Key.Enter or Key.Tab: FinishScanRead(); break;
                case Key.Space: _scanBuffer.Append(' '); break;
                case Key.Back: if (_scanBuffer.Length > 0) _scanBuffer.Length--; break;
                default: return; // les caractères arrivent par PreviewTextInput
            }
            e.Handled = true;
        }
    }

    /// <summary>Caractère de contrôle ASCII tapé en Ctrl+touche : Ctrl+A à Ctrl+Z (1 à 26), Ctrl+[ \ ] (27 à 29).</summary>
    private static char? ControlCharacter(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)(key - Key.A + 1),
        Key.OemOpenBrackets => '\x1B',
        Key.OemPipe or Key.OemBackslash => '\x1C',
        Key.OemCloseBrackets => '\x1D',
        _ => null,
    };

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_vm.ScanMode || FullView.IsVisible) return;
        _scanBuffer.Append(e.Text);
        e.Handled = true;
    }

    private void FinishScanRead()
    {
        if (_scanBuffer.Length == 0) return;
        _vm.LastRead = _scanBuffer.ToString();
        _scanBuffer.Clear();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab)) { StripPanel?.RevealSelected(); return; }
        if (e.PropertyName == nameof(MainViewModel.SideOpen)) { ApplySideWidth(); return; }
        if (e.PropertyName != nameof(MainViewModel.ScanMode)) return;
        _scanBuffer.Clear();
        if (_vm.ScanMode)
        {
            CommitPendingRenames();
            Keyboard.Focus(CodesScroll);
            BringScanFocusIntoView();
        }
    }

    private void ExitScan_Click(object sender, RoutedEventArgs e) => _vm.ScanMode = false;

    private void BringScanFocusIntoView() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (Tab.ScanFocus is { } f && CodesGrid.ItemContainerGenerator.ContainerFromItem(f) is FrameworkElement c)
                c.BringIntoView();
        });

    // ================= Onglets =================

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        _vm.NewTab();
        FocusComposer();
    }

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TabViewModel tab || tab.IsRenaming) return;
        CommitPendingRenames(); // cliquer un autre onglet ne retire pas le focus : on valide ici
        _vm.SelectedTab = tab;
        // Pas de renommage en mode scan : Entrée et Échap y sont réservés à la douchette.
        if (e.ClickCount == 2 && !_vm.ScanMode) BeginRename(tab, (DependencyObject)sender);
        else if (e.ClickCount == 1)
        {
            _tabDragCandidate = tab;
            _tabDragStart = e.GetPosition(this);
            _tabGrab = e.GetPosition((IInputElement)sender);
            // L'onglet garde la souris : sinon il ne la suit plus dès qu'elle en sort, avant d'être arraché.
            ((UIElement)sender).CaptureMouse();
        }
    }

    // ---- Sortir un onglet dans sa propre fenêtre, ou le fusionner dans une autre (comme l'Explorateur) ----

    private TabViewModel? _tabDragCandidate;
    private Point _tabDragStart, _tabGrab;

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndTabReorder(_tabDragCandidate);
            _tabDragCandidate = null;
            if (sender is UIElement { IsMouseCaptured: true } element) element.ReleaseMouseCapture();
            return;
        }
        if (_tabDragCandidate is not { } tab || tab.IsRenaming || StripPanel is not { } panel) return;
        var p = e.GetPosition(this);
        // L'onglet est arraché quand le pointeur quitte la bande d'onglets vers le haut ou le bas.
        if (Math.Abs(p.Y - _tabDragStart.Y) >= 18)
        {
            _tabDragCandidate = null;
            panel.EndDrag(); // il quitte la bande : sa place dans la bande n'a plus d'importance
            TearOff(tab, _tabGrab);
            return;
        }
        // Sinon il glisse le long de la bande, et les autres onglets lui font de la place.
        if (!panel.IsDragging)
        {
            if (Math.Abs(p.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is not UIElement container) return;
            panel.BeginDrag(container);
        }
        panel.DragTo(e.GetPosition(panel).X - _tabGrab.X);
    }

    /// <summary>Onglet lâché après un glisser le long de la bande : il prend la place où il a été lâché.</summary>
    private void EndTabReorder(TabViewModel? tab)
    {
        if (StripPanel is not { } panel) return;
        int to = panel.EndDrag();
        int from = tab is null ? -1 : _vm.Tabs.IndexOf(tab);
        if (to < 0 || from < 0 || to == from) return;
        _vm.Tabs.Move(from, to);
    }

    /// <summary>
    /// L'onglet part dans une nouvelle fenêtre qui suit la souris jusqu'au relâchement (seul onglet : c'est la fenêtre
    /// elle-même qui suit). Relâché sur la barre d'onglets d'une autre fenêtre, il s'y fusionne.
    /// </summary>
    private void TearOff(TabViewModel tab, Point grab)
    {
        CommitPendingRenames();
        MainWindow moving = this;
        if (_vm.Tabs.Count > 1)
        {
            var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var pointer = PointToScreenDip(Mouse.GetPosition(this));
            _vm.Release(tab);
            moving = new MainWindow(adopted: tab)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = normal.Width,
                Height = normal.Height,
                Left = pointer.X - 60,
                Top = pointer.Y - 20,
            };
            moving.Show();
            moving.UpdateLayout();
            moving.PlaceTabUnderPointer(tab, grab);
        }

        moving.FollowPointerUntilRelease();

        if (!Native.GetCursorPos(out var pt)) return;
        var target = AppWindows.All.FirstOrDefault(w => w != moving && w.IsOverTabBar(pt));
        target?.TakeTab(moving, tab, pt);
    }

    private Point PointToScreenDip(Point p)
    {
        var screen = PointToScreen(p);
        double s = _vm.DpiScale;
        return new Point(screen.X / s, screen.Y / s);
    }

    /// <summary>Place la fenêtre pour que son onglet soit sous le pointeur, au point où il a été saisi.</summary>
    private void PlaceTabUnderPointer(TabViewModel tab, Point grab)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement header
            || !Native.GetCursorPos(out var pt) || !Native.GetWindowRect(hwnd, out var win) || !Native.GetClientScreenRect(hwnd, out var client))
            return;
        var origin = header.TranslatePoint(new Point(grab.X, grab.Y), this);
        double s = _vm.DpiScale;
        int x = pt.X - (client.Left - win.Left) - (int)Math.Round(origin.X * s);
        int y = pt.Y - (client.Top - win.Top) - (int)Math.Round(origin.Y * s);
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
    }

    /// <summary>Déplacement de fenêtre de Windows, comme en tirant la barre de titre ; rend la main au relâchement.</summary>
    private void FollowPointerUntilRelease()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Mouse.Capture(null);
        Native.ReleaseCapture();
        Native.SendMessage(hwnd, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }

    /// <summary>Vrai si ce point de l'écran (pixels physiques) est sur la barre d'onglets de cette fenêtre.</summary>
    private bool IsOverTabBar(Native.POINT pt)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return false;
        var topLeft = TabBar.PointToScreen(new Point(0, 0));
        var bottomRight = TabBar.PointToScreen(new Point(TabBar.ActualWidth, TabBar.ActualHeight));
        return pt.X >= topLeft.X && pt.X < bottomRight.X && pt.Y >= topLeft.Y && pt.Y < bottomRight.Y;
    }

    /// <summary>Accueille l'onglet lâché sur la barre d'onglets, à l'endroit du pointeur ; la fenêtre d'origine vidée se ferme.</summary>
    private void TakeTab(MainWindow source, TabViewModel tab, Native.POINT pt)
    {
        var local = TabStrip.PointFromScreen(new Point(pt.X, pt.Y));
        int index = _vm.Tabs.Count;
        for (int i = 0; i < _vm.Tabs.Count; i++)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            if (local.X < c.TranslatePoint(new Point(c.ActualWidth / 2, 0), TabStrip).X) { index = i; break; }
        }
        MoveTab(source, tab, this, index);
    }

    private static void MoveTab(MainWindow from, TabViewModel tab, MainWindow to, int index)
    {
        from._vm.Release(tab);
        to._vm.Adopt(tab, index);
        to.BringToFront();
        if (from._vm.Tabs.Count == 0)
            from.Dispatcher.BeginInvoke(from.CloseWithoutPrompts);
    }

    private void Tab_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab, ContextMenu: { } menu }) return;
        menu.Items.Clear();

        var newWindow = new MenuItem { Header = Loc.MoveToNewWindow, IsEnabled = _vm.Tabs.Count > 1 };
        newWindow.Click += (_, _) =>
        {
            CommitPendingRenames();
            _vm.Release(tab);
            new MainWindow(adopted: tab).Show();
        };
        menu.Items.Add(newWindow);

        var others = AppWindows.All.Where(w => w != this).ToList();
        var moveTo = new MenuItem { Header = Loc.MoveToWindow, IsEnabled = others.Count > 0 };
        foreach (var other in others)
        {
            var label = Loc.WindowLabel(other._vm.SelectedTab.Name, other._vm.Tabs.Count).Replace("_", "__");
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { CommitPendingRenames(); MoveTab(this, tab, other, other._vm.Tabs.Count); };
            moveTo.Items.Add(item);
        }
        menu.Items.Add(moveTo);

        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = Loc.CloseTab, InputGestureText = "Ctrl+W" };
        close.Click += (_, _) => CloseTab(tab);
        menu.Items.Add(close);
    }

    // ---- Renommer un onglet (double-clic) ----

    private void BeginRename(TabViewModel tab, DependencyObject tabElement)
    {
        tab.BeginRename();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (FindChild<TextBox>(tabElement) is { } box) { box.Focus(); box.SelectAll(); }
        });
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TabViewModel tab) return;
        if (e.Key == Key.Enter) { tab.CommitRename(); e.Handled = true; ComposerBox.Focus(); }
        else if (e.Key == Key.Escape) { tab.CancelRename(); e.Handled = true; ComposerBox.Focus(); }
    }

    private void RenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TabViewModel tab) tab.CommitRename();
    }

    // ---- Bande d'onglets : défilement et liste ----

    private TabStripPanel? StripPanel => FindChild<TabStripPanel>(TabStrip);

    private void TabStrip_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (StripPanel is not { IsOverflowing: true } panel) return;
        panel.ScrollBy(-e.Delta / 2.0);
        e.Handled = true;
    }

    private void TabList_Click(object sender, RoutedEventArgs e)
    {
        var menu = TabListButton.ContextMenu!;
        menu.Items.Clear();
        foreach (var tab in _vm.Tabs)
        {
            // « _ » doublé : sinon le menu le prend pour une touche d'accès et le fait disparaître du nom.
            var item = new MenuItem { Header = tab.Name.Replace("_", "__"), IsCheckable = true, IsChecked = tab == Tab };
            item.Click += (_, _) => _vm.SelectedTab = tab;
            menu.Items.Add(item);
        }
        menu.PlacementTarget = TabListButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void HookTabStrip()
    {
        if (StripPanel is not { } panel) return;
        void Sync() => TabListButton.Visibility = panel.IsOverflowing ? Visibility.Visible : Visibility.Collapsed;
        panel.OverflowChanged += (_, _) => Sync();
        Sync();
    }

    // ---- Liste latérale redimensionnable ----

    private void ApplySideWidth()
    {
        SideColumn.MinWidth = _vm.SideOpen ? 240 : 0;
        SideColumn.Width = new GridLength(_vm.SideOpen ? Math.Clamp(_settings.SideWidth, 240, 720) : 0);
    }

    private void SideSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        _settings.SideWidth = SideColumn.ActualWidth;

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        EndTabReorder(_tabDragCandidate);
        _tabDragCandidate = null;
        if (sender is UIElement { IsMouseCaptured: true } element) element.ReleaseMouseCapture();
        if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).DataContext is TabViewModel tab)
            CloseTab(tab);
    }

    /// <summary>Souris perdue en plein glisser (changement de fenêtre…) : l'onglet reste où il est.</summary>
    private void Tab_LostMouseCapture(object sender, MouseEventArgs e)
    {
        EndTabReorder(_tabDragCandidate);
        _tabDragCandidate = null;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TabViewModel tab) CloseTab(tab);
    }

    private void SelectTabOffset(int delta)
    {
        _vm.SelectedTab = _vm.Tabs[Cyclic.Wrap(_vm.Tabs.IndexOf(Tab) + delta, _vm.Tabs.Count)];
    }

    /// <summary>Valide un renommage resté ouvert (clic ailleurs sans perte de focus, Ctrl+S, fermeture…).</summary>
    private void CommitPendingRenames()
    {
        foreach (var t in _vm.Tabs) t.CommitRename();
    }

    private bool CloseTab(TabViewModel tab)
    {
        CommitPendingRenames();
        if (tab.IsDirty && !ConfirmSave(tab)) return false;
        int index = _vm.Tabs.IndexOf(tab);
        bool wasSelected = tab == Tab;
        if (_vm.Tabs.Count == 1) _vm.NewTab(); // il reste toujours un onglet
        _vm.Tabs.Remove(tab);
        if (wasSelected) _vm.SelectedTab = _vm.Tabs[Math.Min(index, _vm.Tabs.Count - 1)];
        return true;
    }

    /// <summary>Vrai si l'on peut continuer (enregistré ou abandon des modifications).</summary>
    private bool ConfirmSave(TabViewModel tab)
    {
        _vm.SelectedTab = tab;
        return UnsavedDialog.Ask(this, tab.Name) switch
        {
            UnsavedChoice.Save => Save(tab),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    // ================= Fichiers =================

    private string InitialFolder =>
        _settings.LastFolder is { } f && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private void Open_Click(object sender, RoutedEventArgs e) => OpenFiles();
    private void Save_Click(object sender, RoutedEventArgs e) => Save(Tab);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs(Tab);

    private void SaveMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = SaveMenuButton.ContextMenu!;
        menu.PlacementTarget = SaveMenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OpenFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.OpenTitle,
            Filter = FileFilter,
            Multiselect = true,
            InitialDirectory = InitialFolder,
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames) OpenPath(path);
    }

    /// <summary>Onglet de cette fenêtre lié à ce fichier, s'il y en a un.</summary>
    internal TabViewModel? FindTab(string fullPath) => _vm.Tabs.FirstOrDefault(t => t.FilePath is not null
        && string.Equals(Path.GetFullPath(t.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

    internal void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal void OpenPath(string path)
    {
        var full = Path.GetFullPath(path);
        // Déjà ouvert, ici ou dans une autre fenêtre : on y va plutôt que de l'ouvrir deux fois.
        if (AppWindows.FindFile(full) is { } found)
        {
            var (window, existing) = found;
            window._vm.SelectedTab = existing;
            if (window != this) window.BringToFront();
            return;
        }

        CodesDocument doc;
        try { doc = CodesFile.Load(full); }
        catch (InvalidCodesFileException ex)
        {
            MessageBox.Show(this, $"{Path.GetFileName(full)}\n\n{ex.Message}", Loc.OpenFailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tab = Tab.IsPristine ? Tab : _vm.NewTab();
        tab.FilePath = full;
        tab.Load(doc.Codes, doc.Name);
        _vm.SelectedTab = tab;
        _vm.UpdateScanFlags();
        _settings.LastFolder = Path.GetDirectoryName(full);
    }

    private bool Save(TabViewModel tab)
    {
        CommitPendingRenames();
        return tab.FilePath is null ? SaveAs(tab) : WriteTab(tab, tab.FilePath);
    }

    private bool SaveAs(TabViewModel tab)
    {
        CommitPendingRenames();
        var dialog = new SaveFileDialog
        {
            Title = Loc.SaveTitle,
            Filter = FileFilter,
            DefaultExt = CodesFile.Extension,
            AddExtension = true,
            FileName = tab.Name,
            InitialDirectory = tab.FilePath is null ? InitialFolder : Path.GetDirectoryName(tab.FilePath),
        };
        if (dialog.ShowDialog(this) != true) return false;
        return WriteTab(tab, dialog.FileName);
    }

    private bool WriteTab(TabViewModel tab, string path)
    {
        try
        {
            CodesFile.Save(path, tab.ToEntries(), tab.CustomName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"{Loc.SaveFailed(path)}\n\n{ex.Message}",
                Loc.SaveFailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        tab.FilePath = Path.GetFullPath(path);
        tab.IsDirty = false;
        _settings.LastFolder = Path.GetDirectoryName(tab.FilePath);
        ShowToast(Loc.SavedTo(Path.GetFileName(path)));
        return true;
    }

    // ================= Zone de saisie =================

    private void FocusComposer() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (ComposerBox.IsVisible) ComposerBox.Focus(); });

    private void Composer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Tab.IsEditing) Tab.SelectedCode = null;
        else AddFromComposer();
        e.Handled = true;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddFromComposer();

    private void AddFromComposer()
    {
        if (!Tab.AddFromComposer()) ShowToast(Loc.TypeBeforeAdd);
        ComposerBox.Focus();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        Tab.SelectedCode = null;
        ComposerBox.Focus();
    }

    // ================= Liste latérale =================

    private void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Item(sender) is { } item) Tab.SelectedCode = item;
    }

    private void RowText_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Item(sender) is { } item) Tab.SelectedCode = item;
    }

    private void RowText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Item(sender) is not { } item) return;
        FocusRow(Tab.InsertAfter(item));
        e.Handled = true;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e) => FocusRow(Tab.InsertAfter(Tab.Codes.LastOrDefault()));

    private void FocusRow(CodeItemViewModel item) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (SideList.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject c && FindChild<TextBox>(c) is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
            Tab.SelectedCode = item;
        });

    // ================= Cartes =================

    private static CodeItemViewModel? Item(object sender) => (sender as FrameworkElement)?.DataContext as CodeItemViewModel;

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Item(sender) is not { } item) return;
        CommitPendingRenames(); // une carte ne prend pas le focus : le renommage en cours est validé ici
        if (_vm.ScanMode)
        {
            Tab.ScanFocus = item;
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            OpenFull(item);
            e.Handled = true;
            return;
        }
        _dragCandidate = item;
        _dragStart = e.GetPosition(this);
    }

    private void Card_MouseMove(object sender, MouseEventArgs e) => TryStartDrag(sender, e);

    /// <summary>
    /// Largeur dont dispose un code dans sa carte : la grille moins la marge (16), le remplissage (28) et le bord (2)
    /// de la carte. Un code plus large s'affine ou affiche un message, pour ne jamais être coupé.
    /// </summary>
    private void CodesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 || e.ExtentHeightChange != 0)
            _vm.CodeMaxWidth = Math.Max(0, CodesScroll.ViewportWidth - 46);
    }

    private void Card_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.ScanMode && Item(sender) is { } item && _dragCandidate == item) Tab.SelectedCode = item;
        _dragCandidate = null;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { } item) return;
        if (item.Matrix is null)
        {
            ShowToast(Loc.NoImage(item.ErrorMessage));
            return;
        }
        try
        {
            CodeImages.CopyToClipboard(CodeImages.ForClipboard(item.Matrix, _vm.CodeSize, _vm.DpiScale));
            ShowToast(Loc.ImageCopied);
        }
        catch (ExternalException)
        {
            ShowToast(Loc.ClipboardBusy);
        }
    }

    private void CopyText_MouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;      // le clic copie, il ne sélectionne pas la carte
        _dragCandidate = null;
        if (Item(sender) is { } item) CopyText(item.Text);
    }

    private void CopyFullText_MouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_fullItem is not null) CopyText(_fullItem.Text);
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text)) { ShowToast(Loc.NothingToCopy); return; }
        try
        {
            Clipboard.SetDataObject(text, true); // WPF réessaie déjà si le presse-papiers est occupé
            ShowToast(Loc.TextCopied(text.Length > 60 ? text[..57] + "…" : text));
        }
        catch (ExternalException) { ShowToast(Loc.ClipboardBusy); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { } item) return;
        Tab.Remove(item);
        ShowToast(Loc.CodeDeleted);
    }

    private void Full_Click(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item) OpenFull(item);
    }

    private void SwitchToQr_Click(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item) item.Type = CodeType.Qr;
    }

    // ================= Glisser-déposer =================

    private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = Item(sender);
        _dragStart = e.GetPosition(this);
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e) => TryStartDrag(sender, e);

    private void TryStartDrag(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || _vm.ScanMode) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _dragCandidate = null; return; }
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragCandidate;
        _dragCandidate = null;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CodeItemViewModel), item), DragDropEffects.Move);
        }
        finally
        {
            ClearDropMarks();
        }
    }

    private void Card_DragOver(object sender, DragEventArgs e) => MarkDrop(sender, e, vertical: false);
    private void Row_DragOver(object sender, DragEventArgs e) => MarkDrop(sender, e, vertical: true);

    private (CodeItemViewModel source, CodeItemViewModel target, bool after)? DropInfo(object sender, DragEventArgs e, bool vertical)
    {
        if (e.Data.GetData(typeof(CodeItemViewModel)) is not CodeItemViewModel source
            || Item(sender) is not { } target || source.Owner != target.Owner) return null;
        var fe = (FrameworkElement)sender;
        var p = e.GetPosition(fe);
        bool after = vertical ? p.Y > fe.ActualHeight / 2 : p.X > fe.ActualWidth / 2;
        return (source, target, after);
    }

    private void MarkDrop(object sender, DragEventArgs e, bool vertical)
    {
        e.Handled = true;
        if (DropInfo(sender, e, vertical) is not var (_, target, after))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        foreach (var c in Tab.Codes)
            if (c != target) { c.DropBefore = false; c.DropAfter = false; }
        target.DropBefore = !after;
        target.DropAfter = after;
        e.Effects = DragDropEffects.Move;
    }

    private void Item_DragLeave(object sender, DragEventArgs e)
    {
        if (Item(sender) is { } item) { item.DropBefore = false; item.DropAfter = false; }
    }

    private void Item_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DropInfo(sender, e, vertical: IsInSideList((DependencyObject)sender)) is var (source, target, after))
        {
            var codes = target.Owner.Codes;
            target.Owner.Move(codes.IndexOf(source), codes.IndexOf(target) + (after ? 1 : 0));
        }
        ClearDropMarks();
    }

    private bool IsInSideList(DependencyObject d)
    {
        for (var p = d; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p == SideList) return true;
        return false;
    }

    private void ClearDropMarks()
    {
        foreach (var c in Tab.Codes) { c.DropBefore = false; c.DropAfter = false; }
    }

    // ================= Plein écran =================

    private void OpenFull(CodeItemViewModel item)
    {
        if (item.ShowPlaceholder) return; // rien à montrer en grand : une erreur, ou un code trop large
        _fullItem = item;
        FullView.Visibility = Visibility.Visible;
        RenderFull();
        Keyboard.Focus(FullView);
    }

    private void RenderFull()
    {
        if (_fullItem is null) return;
        var codes = _fullItem.Owner.Codes;
        if (!codes.Contains(_fullItem)) { CloseFull(); return; }

        double maxW = Math.Max(100, Root.ActualWidth * 0.92), maxH = Math.Max(100, Root.ActualHeight - 200);
        FullImage.Source = _fullItem.Matrix is null ? null : CodeImages.Fit(_fullItem.Matrix, maxW, maxH, _vm.DpiScale);
        FullText.Text = _fullItem.HasError ? $"{_fullItem.Text}\n{_fullItem.ErrorMessage}" : _fullItem.Text;
        FullCounter.Text = Loc.Counter(codes.IndexOf(_fullItem) + 1, codes.Count);
    }

    private void StepFull(int delta)
    {
        if (_fullItem is null) return;
        var codes = _fullItem.Owner.Codes;
        if (codes.Count == 0) { CloseFull(); return; }
        // Les codes non affichés dans leur carte sont sautés ; s'il n'y en a pas d'autre, on reste sur place.
        int index = codes.IndexOf(_fullItem);
        for (int step = 1; step <= codes.Count; step++)
        {
            var next = codes[Cyclic.Wrap(index + step * delta, codes.Count)];
            if (next.ShowPlaceholder) continue;
            _fullItem = next;
            break;
        }
        RenderFull();
    }

    private void CloseFull()
    {
        _fullItem = null;
        FullView.Visibility = Visibility.Collapsed;
        FullImage.Source = null;
        if (_vm.ScanMode) Keyboard.Focus(CodesScroll);
    }

    private void FullPrev_Click(object sender, RoutedEventArgs e) => StepFull(-1);
    private void FullNext_Click(object sender, RoutedEventArgs e) => StepFull(1);
    private void FullClose_Click(object sender, RoutedEventArgs e) => CloseFull();

    // ================= Message temporaire =================

    internal void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.BeginAnimation(OpacityProperty, Fade(1));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.BeginAnimation(OpacityProperty, Fade(0));
    }

    private static DoubleAnimation Fade(double to) =>
        new(to, SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(160) : TimeSpan.Zero);

    // ================= Outils =================

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Capture de la fenêtre en PNG (pour les vérifications visuelles).</summary>
    internal void SaveSnapshot(string path)
    {
        double s = _vm.DpiScale;
        var rtb = new RenderTargetBitmap((int)Math.Round(Root.ActualWidth * s), (int)Math.Round(Root.ActualHeight * s), 96 * s, 96 * s, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle((Brush)Application.Current.Resources["WindowBg"], null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
        rtb.Render(background);
        rtb.Render(Root);
        File.AppendAllText(Path.ChangeExtension(path, ".txt"),
            $"dpiScale={s} root={Root.ActualWidth}x{Root.ActualHeight} window={ActualWidth}x{ActualHeight}\n");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    internal void DebugOpenFull(CodeItemViewModel item) => OpenFull(item);

#if DEBUG
    /// <summary>Simule une frappe (touche) sur un élément, comme le ferait le clavier ou une douchette.</summary>
    internal void DebugKey(UIElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    internal void DebugText(UIElement target, string text) =>
        target.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, target, text))
        { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });

    internal TextBox DebugComposer => ComposerBox;

    internal void DebugOpenSettings() => SettingsPopup.IsOpen = true;
#endif
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    /// <summary>Zone cliente en coordonnées écran : sans les bordures invisibles laissées au système.</summary>
    public static bool GetClientScreenRect(IntPtr hWnd, out RECT rect)
    {
        rect = default;
        var origin = new POINT();
        if (!GetClientRect(hWnd, out var client) || !ClientToScreen(hWnd, ref origin)) return false;
        rect = new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
        return true;
    }

    public const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    public const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTONEAREST = 2;
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT minPosition, maxPosition; public RECT normalPosition; }
    public const int SW_SHOWNORMAL = 1;
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4;

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public static readonly IntPtr HTCAPTION = 2;
}
