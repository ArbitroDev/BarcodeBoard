using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;
using Windows.Graphics;

namespace BarcodeBoard;

/// <summary>
/// Une fenêtre : onglets dans la barre de titre (TabView natif, glissés d'une fenêtre à l'autre), Mica Alt
/// sous Windows 11, et le contenu de l'onglet actif (voir les autres parties de la classe).
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Taille d'une première fenêtre sans réglages, et taille minimale (pixels logiques).</summary>
    private const double DefaultWidth = 1240, DefaultHeight = 780, MinWidth = 760, MinHeight = 480;

    /// <summary>
    /// L'élément d'onglet de chaque onglet, commun à toutes les fenêtres : un onglet passé dans une autre fenêtre
    /// garde le même élément.
    /// </summary>
    private static readonly Dictionary<TabViewModel, TabViewItem> TabItems = [];

    /// <summary>Un élément d'onglet passe d'un TabView à l'autre : la synchronisation avec le modèle attend la fin.</summary>
    private static bool _movingTab;

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IReadOnlyList<string> _startupFiles;
    private readonly bool _isFirst, _mica;
    private readonly IntPtr _hwnd;
    private bool _syncingTabs, _closeConfirmed, _closePrompting, _skipPrompts, _settingsSaved, _themeTilesSyncing;
    /// <summary>Onglets dont la fermeture est en cours (question affichée) : Ctrl+W répété ne les ferme pas deux fois.</summary>
    private readonly HashSet<TabViewModel> _closingTabs = [];
    private TabViewModel? _titleTab;
    /// <summary>Onglet en cours de glisser, et point saisi (pixels physiques depuis son coin haut gauche).</summary>
    private static (TabViewItem Item, PointInt32 Grab)? _draggedTab;

    public MainViewModel ViewModel { get; } = new();

    private TabViewModel? Tab => ViewModel.SelectedTab;

    /// <param name="startupFiles">Fichiers .codes à ouvrir une fois la fenêtre affichée.</param>
    /// <param name="adopted">Onglet sorti d'une autre fenêtre : la nouvelle fenêtre ne contient que lui.</param>
    /// <param name="isFirst">Première fenêtre de l'application : elle reprend la position et l'état enregistrés.</param>
    public MainWindow(IReadOnlyList<string>? startupFiles = null, TabViewModel? adopted = null, bool isFirst = false)
    {
        _startupFiles = startupFiles ?? [];
        _isFirst = isFirst;
        if (isFirst) Theme.Apply(ThemeSetting.Parse(_settings.Theme));
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindows.Register(this);

        // Onglets dans la barre de titre ; Windows garde ses boutons Réduire / Agrandir / Fermer.
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(IconPath);

        // Mica Alt (Windows 11), le matériau des applications à onglets ; Windows 10 garde un fond opaque.
        _mica = !NoMicaRequested() && MicaController.IsSupported();
        if (_mica) SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };

        ViewModel.CodeSize = _settings.CodeSize;
        ViewModel.SideOpen = _settings.SideOpen;
        ViewModel.Tabs.CollectionChanged += OnTabsChanged;
        ViewModel.PropertyChanged += Vm_PropertyChanged;
        if (adopted is not null) ViewModel.Adopt(adopted, 0);
        else ViewModel.NewTab();

        ApplyTheme();
        Theme.Changed += ApplyTheme;
        Root.ActualThemeChanged += (_, _) => ApplyLayers();

        if (isFirst) RestorePlacement();
        else PlaceNextToLastWindow();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinWidth * WindowScale);
            presenter.PreferredMinimumHeight = (int)(MinHeight * WindowScale);
        }

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(2.4);
        _toastTimer.Tick += (_, _) => HideToast();
        Root.Loaded += OnLoaded;
        AppWindow.Changed += (_, e) => { if (e.DidSizeChange || e.DidPresenterChange) UpdateTabStripPadding(); };
        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Affiche la première fenêtre, agrandie si elle l'était à la fermeture.</summary>
    public void ShowFirst()
    {
        Activate();
        if (_settings.Maximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    // ================= Démarrage, échelle, fenêtre =================

    private static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "BarcodeBoard.ico");

    /// <summary>
    /// Fenêtre agrandie : les onglets touchent le haut de l'écran, comme dans les navigateurs (le pointeur poussé
    /// tout en haut tombe sur un onglet). Sinon, TabView garde sa marge haute (TabViewHeaderPadding).
    /// </summary>
    private void UpdateTabStripPadding()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
            Tabs.Padding = new Thickness(0);
        else
            Tabs.ClearValue(Control.PaddingProperty);
    }

    /// <summary>Debug : --no-mica garde le fond opaque (captures internes complètes, rien de l'écran ne s'y glisse).</summary>
    private static bool NoMicaRequested()
    {
#if DEBUG
        return Environment.GetCommandLineArgs().Contains("--no-mica");
#else
        return false;
#endif
    }

    /// <summary>Échelle de l'écran de la fenêtre, utilisable avant son premier affichage (1 = 100 %).</summary>
    private double WindowScale => GetDpiForWindow(_hwnd) is var dpi and > 0 ? dpi / 96.0 : 1;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Comme l'exemple officiel : le pied de la bande d'onglets déplace la fenêtre, et laisse de la place
        // pour qu'on puisse toujours la saisir à côté des onglets.
        SetTitleBar(CustomDragRegion);
        CustomDragRegion.MinWidth = 188;
        UpdateTabStripPadding();
        ViewModel.DpiScale = Root.XamlRoot.RasterizationScale;
        Root.XamlRoot.Changed += (_, _) =>
        {
            double scale = Root.XamlRoot?.RasterizationScale ?? 1;
            if (scale == ViewModel.DpiScale) return;
            // Fenêtre déplacée vers un écran d'une autre échelle : codes redessinés au pixel près.
            ViewModel.DpiScale = scale;
            if (FullView.Visibility == Visibility.Visible) RenderFull();
        };
        ApplySideWidth();
        foreach (var file in _startupFiles) OpenPath(file);
        FocusComposer();
#if DEBUG
        if (_isFirst) DebugScenario.Run(this, ViewModel);
#endif
    }

    // ================= Thème et couches =================

    private void ApplyTheme()
    {
        Root.RequestedTheme = Theme.Requested;
        AppWindow.TitleBar.PreferredTheme = Theme.TitleBar;
        ApplyLayers();
        UpdateThemeTiles();
    }

    /// <summary>Couches de fond : translucides sur Mica, sinon les teintes opaques de la palette.</summary>
    private void ApplyLayers()
    {
        var theme = Root.ActualTheme;
        string suffix = _mica ? "Mica" : "";
        SideLayer.Background = Theme.Brush("SideLayer" + suffix, theme);
        ContentLayer.Background = Theme.Brush("ContentLayer" + suffix, theme);
        Root.Background = _mica ? new SolidColorBrush(Colors.Transparent) : Theme.Brush("WindowBg", theme);
    }

    internal void SetTheme(ThemeChoice choice)
    {
        _settings.Theme = ThemeSetting.ToSetting(choice);
        Theme.Apply(choice);
    }

    private void UpdateThemeTiles()
    {
        _themeTilesSyncing = true;
        ThemeTiles.SelectedIndex = Theme.Choice switch { ThemeChoice.Light => 1, ThemeChoice.Dark => 2, _ => 0 };
        _themeTilesSyncing = false;
    }

    private void SettingsFlyout_Opening(object? sender, object e) => UpdateThemeTiles();

    private void ThemeTiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_themeTilesSyncing || ThemeTiles.SelectedIndex < 0) return;
        SetTheme(ThemeTiles.SelectedIndex switch { 1 => ThemeChoice.Light, 2 => ThemeChoice.Dark, _ => ThemeChoice.System });
    }

    private void ToggleSettings()
    {
        if (SettingsFlyout.IsOpen) SettingsFlyout.Hide();
        else SettingsFlyout.ShowAt(SettingsButton);
    }

    // ================= Position de la fenêtre =================

    /// <summary>
    /// Remet la première fenêtre à sa place en pixels physiques (juste même sur un écran secondaire d'une autre
    /// échelle), si elle est encore sur un écran branché ; sinon taille par défaut, centrée.
    /// </summary>
    private void RestorePlacement()
    {
        if (_settings.Placement is [int l, int t, int r, int b] && r - l >= 200 && b - t >= 150)
        {
            var rect = new Native.RECT { Left = l, Top = t, Right = r, Bottom = b };
            if (Native.IsOnAScreen(rect))
            {
                Native.SetNormalPosition(_hwnd, rect);
                return;
            }
        }
        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        double s = WindowScale;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(DefaultWidth * s), work.Width), h = Math.Min((int)(DefaultHeight * s), work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    /// <summary>Nouvelle fenêtre : même taille que la dernière utilisée, décalée pour ne pas la cacher.</summary>
    private void PlaceNextToLastWindow()
    {
        if (AppWindows.All.FirstOrDefault(w => w != this && w.ViewModel.HasTabs) is { } other
            && Native.NormalPosition(other._hwnd) is { } rect)
        {
            int offset = (int)(32 * other.WindowScale);
            Native.SetNormalPosition(_hwnd, new Native.RECT
            {
                Left = rect.Left + offset, Top = rect.Top + offset, Right = rect.Right + offset, Bottom = rect.Bottom + offset,
            });
            return;
        }
        CenterOnScreen();
    }

    private void SaveWindowSettings()
    {
        // Une fenêtre vidée par une fusion ou une vérification Debug ne touche pas aux réglages.
        if (_settingsSaved || _skipPrompts || !ViewModel.HasTabs) return;
        _settingsSaved = true;
        _settings.CodeSize = ViewModel.CodeSize;
        _settings.SideOpen = ViewModel.SideOpen;
        _settings.Maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (Native.NormalPosition(_hwnd) is { } r)
        {
            _settings.Placement = [r.Left, r.Top, r.Right, r.Bottom];
            // Position en pixels logiques, lue par la version 1.
            double s = WindowScale;
            _settings.Left = r.Left / s;
            _settings.Top = r.Top / s;
            _settings.Width = (r.Right - r.Left) / s;
            _settings.Height = (r.Bottom - r.Top) / s;
        }
        _settings.Save();
    }

    // ================= Fermeture =================

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (!_closeConfirmed)
        {
            // Fermeture redemandée (bouton Fermer, Alt+F4) pendant les questions : la première série suffit.
            if (_closePrompting) { e.Cancel = true; return; }
            CommitPendingRenames();
            var dirty = ViewModel.Tabs.Where(t => t.IsDirty).ToList();
            if (dirty.Count > 0 && !_skipPrompts)
            {
                e.Cancel = true;
                _closePrompting = true;
                try
                {
                    foreach (var tab in dirty)
                        if (ViewModel.Tabs.Contains(tab) && !await ConfirmSave(tab)) return;
                }
                finally
                {
                    _closePrompting = false;
                }
                _closeConfirmed = true;
                SaveWindowSettings();
                Close();
                return;
            }
            _closeConfirmed = true;
        }
        SaveWindowSettings();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Theme.Changed -= ApplyTheme;
        _toastTimer.Stop();
        // Les onglets de cette fenêtre disparaissent avec elle (un onglet parti ailleurs n'est plus dans sa liste).
        foreach (var tab in ViewModel.Tabs) TabItems.Remove(tab);
    }

    internal void CloseWithoutPrompts()
    {
        _skipPrompts = true;
        _closeConfirmed = true;
        Close();
    }

    internal void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        Native.SetForegroundWindow(_hwnd);
    }

    // ================= Onglets : synchronisation avec TabView =================

    /// <summary>L'élément d'onglet d'un onglet, créé à sa première apparition.</summary>
    private static TabViewItem ItemFor(TabViewModel tab)
    {
        if (TabItems.TryGetValue(tab, out var item)) return item;
        item = new TabViewItem { Header = HeaderText(tab), Tag = tab, IsClosable = true };
        ToolTipService.SetToolTip(item, tab.ToolTip);
        var created = item;
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TabViewModel.Name) or nameof(TabViewModel.IsDirty)) created.Header = HeaderText(tab);
            else if (e.PropertyName == nameof(TabViewModel.ToolTip)) ToolTipService.SetToolTip(created, tab.ToolTip);
        };
        // Menu neuf à chaque ouverture, dans la fenêtre où l'onglet se trouve alors : un menu déjà ouvert dans une
        // fenêtre ne s'ouvre pas forcément dans une autre, et l'onglet passe d'une fenêtre à l'autre.
        item.ContextRequested += (sender, e) =>
        {
            var target = (TabViewItem)sender;
            ShowTabMenu(target, e.TryGetPosition(target, out var point) ? point : null);
            e.Handled = true;
        };
        TabItems[tab] = item;
        return item;
    }

    private static void ShowTabMenu(TabViewItem item, Windows.Foundation.Point? position)
    {
        if (TabOf(item) is not { } tab || AppWindows.For(tab.Main) is not { } window) return;
        var menu = new MenuFlyout();
        window.FillTabMenu(menu, tab);
        if (position is { } point) menu.ShowAt(item, new FlyoutShowOptions { Position = point });
        else menu.ShowAt(item);
    }

    /// <summary>
    /// L'onglet sous un point de l'écran (pixels physiques), d'après sa seule position horizontale. Null hors des
    /// onglets ou plus bas qu'eux.
    /// </summary>
    private (TabViewItem Item, RectInt32 Bounds)? TabAtScreenPoint(PointInt32 point)
    {
        if (Root.XamlRoot is not { } root) return null;
        foreach (var item in Tabs.TabItems.OfType<TabViewItem>())
        {
            var r = root.CoordinateConverter.ConvertLocalToScreen(
                item.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, item.ActualWidth, item.ActualHeight)));
            if (point.X >= r.X && point.X < r.X + r.Width && point.Y < r.Y + r.Height) return (item, r);
        }
        return null;
    }

    /// <summary>Nom de l'onglet, suivi d'un point s'il a des modifications non enregistrées.</summary>
    private static string HeaderText(TabViewModel tab) => tab.IsDirty ? $"{tab.Name} •" : tab.Name;

    private static TabViewModel? TabOf(object? item) => (item as TabViewItem)?.Tag as TabViewModel;

    /// <summary>Les onglets du modèle changent (nouvel onglet, fermeture, onglet venu d'ailleurs) : TabView suit.</summary>
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncingTabs || _movingTab) return;
        _syncingTabs = true;
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    for (int i = 0; i < e.NewItems!.Count; i++)
                        Tabs.TabItems.Insert(e.NewStartingIndex + i, ItemFor((TabViewModel)e.NewItems[i]!));
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (TabViewModel tab in e.OldItems!) Tabs.TabItems.Remove(ItemFor(tab));
                    break;
                case NotifyCollectionChangedAction.Move:
                    var moved = ItemFor((TabViewModel)e.NewItems![0]!);
                    Tabs.TabItems.Remove(moved);
                    Tabs.TabItems.Insert(e.NewStartingIndex, moved);
                    break;
                default:
                    Tabs.TabItems.Clear();
                    foreach (var tab in ViewModel.Tabs) Tabs.TabItems.Add(ItemFor(tab));
                    break;
            }
        }
        finally
        {
            _syncingTabs = false;
        }
        SyncSelectedTab();
    }

    /// <summary>TabView a réordonné ses onglets (glisser le long de la bande) : le modèle suit.</summary>
    private void Tabs_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        // Pendant un glisser, TabView retire puis réinsère l'onglet : on attend que la liste soit complète.
        if (_syncingTabs || _movingTab || Tabs.TabItems.Count != ViewModel.Tabs.Count) return;
        _syncingTabs = true;
        try
        {
            for (int i = 0; i < Tabs.TabItems.Count; i++)
            {
                if (TabOf(Tabs.TabItems[i]) is not { } tab) continue;
                int from = ViewModel.Tabs.IndexOf(tab);
                if (from >= 0 && from != i) ViewModel.Tabs.Move(from, i);
            }
        }
        finally
        {
            _syncingTabs = false;
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs || _movingTab) return;
        if (TabOf(Tabs.SelectedItem) is { } tab && tab != ViewModel.SelectedTab)
        {
            CommitPendingRenames(); // cliquer un autre onglet ne retire pas forcément le focus : on valide ici
            ViewModel.SelectedTab = tab;
        }
    }

    private void SyncSelectedTab()
    {
        object? wanted = ViewModel.SelectedTab is { } tab ? ItemFor(tab) : null;
        if (Tabs.SelectedItem == wanted) return;
        _syncingTabs = true;
        Tabs.SelectedItem = wanted;
        _syncingTabs = false;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedTab):
                SyncSelectedTab();
                WatchTitle();
                break;
            case nameof(MainViewModel.SideOpen):
                ApplySideWidth();
                break;
            case nameof(MainViewModel.ScanMode):
                OnScanModeChanged();
                break;
        }
    }

    /// <summary>Titre de la fenêtre (barre des tâches, Alt+Tab) : « nom de l'onglet - nom de l'application ».</summary>
    private void WatchTitle()
    {
        if (_titleTab is not null) _titleTab.PropertyChanged -= TitleTab_PropertyChanged;
        _titleTab = ViewModel.SelectedTab;
        if (_titleTab is not null) _titleTab.PropertyChanged += TitleTab_PropertyChanged;
        UpdateTitle();
    }

    private void TitleTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Name)) UpdateTitle();
    }

    private void UpdateTitle() => Title = Tab is { } tab ? $"{tab.Name} - {Loc.AppName}" : Loc.AppName;

    // ================= Onglets : actions =================

    private void Tabs_AddTabButtonClick(TabView sender, object args)
    {
        ViewModel.NewTab();
        FocusComposer();
    }

    private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (TabOf(args.Tab) is { } tab) Run(CloseTab(tab));
    }

    private void SelectTabOffset(int delta)
    {
        if (ViewModel.Tabs.Count == 0 || Tab is not { } tab) return;
        ViewModel.SelectedTab = ViewModel.Tabs[Cyclic.Wrap(ViewModel.Tabs.IndexOf(tab) + delta, ViewModel.Tabs.Count)];
    }

    /// <summary>Valide un renommage resté ouvert (clic ailleurs sans perte de focus, Ctrl+S, fermeture…).</summary>
    private void CommitPendingRenames()
    {
        foreach (var t in ViewModel.Tabs) t.CommitRename();
    }

    private async Task<bool> CloseTab(TabViewModel tab)
    {
        CommitPendingRenames();
        if (!ViewModel.Tabs.Contains(tab) || !_closingTabs.Add(tab)) return false;
        try
        {
            if (tab.IsDirty && !await ConfirmSave(tab)) return false;
        }
        finally
        {
            _closingTabs.Remove(tab);
        }
        if (!ViewModel.Tabs.Contains(tab)) return false; // parti dans une autre fenêtre pendant la question
        int index = ViewModel.Tabs.IndexOf(tab);
        bool wasSelected = tab == Tab;
        if (ViewModel.Tabs.Count == 1) ViewModel.NewTab(); // il reste toujours un onglet
        ViewModel.Tabs.Remove(tab);
        TabItems.Remove(tab);
        if (wasSelected) ViewModel.SelectedTab = ViewModel.Tabs[Math.Min(index, ViewModel.Tabs.Count - 1)];
        return true;
    }

    /// <summary>Vrai si l'on peut continuer (enregistré ou abandon des modifications).</summary>
    private async Task<bool> ConfirmSave(TabViewModel tab)
    {
        ViewModel.SelectedTab = tab;
        return await Dialogs.AskUnsaved(Root, tab.Name) switch
        {
            UnsavedChoice.Save => await Save(tab),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>Menu d'un onglet : renommer, nouvelle fenêtre, autre fenêtre, fermer.</summary>
    private void FillTabMenu(MenuFlyout menu, TabViewModel tab)
    {
        menu.Items.Clear();
        var rename = new MenuFlyoutItem { Text = Loc.Rename, IsEnabled = !ViewModel.ScanMode };
        rename.Click += (_, _) => Run(RenameTab(tab));
        menu.Items.Add(rename);
        menu.Items.Add(new MenuFlyoutSeparator());

        var newWindow = new MenuFlyoutItem { Text = Loc.MoveToNewWindow, IsEnabled = ViewModel.Tabs.Count > 1 };
        newWindow.Click += (_, _) =>
        {
            CommitPendingRenames();
            ViewModel.Release(tab);
            new MainWindow(adopted: tab).Activate();
        };
        menu.Items.Add(newWindow);

        var others = AppWindows.All.Where(w => w != this && w.ViewModel.HasTabs).ToList();
        var moveTo = new MenuFlyoutSubItem { Text = Loc.MoveToWindow, IsEnabled = others.Count > 0 };
        foreach (var other in others)
        {
            var item = new MenuFlyoutItem { Text = Loc.WindowLabel(other.ViewModel.SelectedTab?.Name ?? "", other.ViewModel.Tabs.Count) };
            item.Click += (_, _) =>
            {
                CommitPendingRenames();
                MoveTab(this, tab, other, other.ViewModel.Tabs.Count, closeEmptySource: true);
            };
            moveTo.Items.Add(item);
        }
        menu.Items.Add(moveTo);

        menu.Items.Add(new MenuFlyoutSeparator());
        var close = new MenuFlyoutItem { Text = Loc.CloseTab, KeyboardAcceleratorTextOverride = Loc.CloseTabShortcut };
        close.Click += (_, _) => Run(CloseTab(tab));
        menu.Items.Add(close);
    }

    /// <summary>Renommer (menu de l'onglet), jamais en mode scan : Entrée et Échap y sont réservés à la douchette.</summary>
    private async Task RenameTab(TabViewModel tab)
    {
        if (ViewModel.ScanMode) return;
        if (await Dialogs.AskText(Root, Loc.Rename, tab.Name) is not { } name) return;
        tab.BeginRename();
        tab.RenameText = name;
        tab.CommitRename();
    }

    /// <summary>Passe un onglet dans une autre fenêtre (menu de l'onglet) ; la fenêtre d'origine vidée se ferme.</summary>
    private static void MoveTab(MainWindow from, TabViewModel tab, MainWindow to, int index, bool closeEmptySource)
    {
        MoveTabItem(ItemFor(tab), from, to, index);
        if (closeEmptySource)
        {
            to.BringToFront();
            CloseIfEmpty(from);
        }
    }

    /// <summary>
    /// Déplace un élément d'onglet d'un TabView à l'autre (retiré de l'un, inséré dans l'autre : le même objet),
    /// puis met les deux modèles à jour.
    /// </summary>
    private static void MoveTabItem(TabViewItem item, MainWindow? from, MainWindow to, int index)
    {
        if (TabOf(item) is not { } tab) return;
        _movingTab = true;
        try
        {
            (from?.Tabs ?? ParentTabView(item))?.TabItems.Remove(item);
            index = Math.Clamp(index, 0, to.Tabs.TabItems.Count);
            to.Tabs.TabItems.Insert(index, item);
            from?.ViewModel.Release(tab);
            to.ViewModel.Adopt(tab, index);
        }
        finally
        {
            _movingTab = false;
        }
        from?.SyncSelectedTab();
        to.SyncSelectedTab();
    }

    private static void CloseIfEmpty(MainWindow window)
    {
        if (!window.ViewModel.HasTabs) window.DispatcherQueue.TryEnqueue(window.CloseWithoutPrompts);
    }

    /// <summary>TabView qui contient un élément d'onglet, en remontant l'arbre visuel (comme l'exemple officiel).</summary>
    private static TabView? ParentTabView(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is TabView tabView) return tabView;
        return null;
    }

    // ---- Glisser un onglet : sa vignette sous le pointeur, comme dans l'Explorateur de fichiers ----
    // Glisser-déposer de TabView (CanDragTabs) : Windows montre la vignette de l'onglet sous le pointeur. Lâché le
    // long de la bande, l'onglet change de place (TabView) ; sur la bande d'une autre fenêtre, il y entre et sa
    // fenêtre vidée se ferme ; ailleurs, il part dans une nouvelle fenêtre placée pour qu'il tombe sous le pointeur
    // (seul onglet de sa fenêtre : c'est elle qui vient sous le pointeur). Lâché sur sa propre fenêtre : rien.

    /// <summary>Format du glisser-déposer qui désigne un onglet de l'application.</summary>
    private const string TabDragFormat = "BarcodeBoard.Tab";

    private void Tabs_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        CommitPendingRenames();
        args.Data.SetData(TabDragFormat, "tab");
        args.Data.RequestedOperation = DataPackageOperation.Move;
        var grab = new PointInt32(0, 0);
        if (Native.GetCursorPos(out var cursor) && TabAtScreenPoint(new PointInt32(cursor.X, cursor.Y)) is { } hit && hit.Item == args.Tab)
            grab = new PointInt32(cursor.X - hit.Bounds.X, Math.Clamp(cursor.Y - hit.Bounds.Y, 0, Math.Max(0, hit.Bounds.Height - 1)));
        _draggedTab = (args.Tab, grab);
    }

    /// <summary>Glisser fini sur une bande d'onglets (la sienne ou une autre) : plus rien à faire.</summary>
    private void Tabs_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.None) _draggedTab = null;
    }

    /// <summary>Onglet d'une autre fenêtre au-dessus de la bande : il peut y entrer (le réordonnancement reste à TabView).</summary>
    private void Tabs_TabStripDragOver(object sender, DragEventArgs e)
    {
        if (_draggedTab is { } dragged && e.DataView.Contains(TabDragFormat) && ParentTabView(dragged.Item) != Tabs)
            e.AcceptedOperation = DataPackageOperation.Move;
    }

    /// <summary>Onglet d'une autre fenêtre lâché sur la bande : il entre à l'endroit du lâcher.</summary>
    private void Tabs_TabStripDrop(object sender, DragEventArgs e) => Guard(() =>
    {
        if (_draggedTab is not { } dragged || !e.DataView.Contains(TabDragFormat) || TabOf(dragged.Item) is not { } tab
            || AppWindows.For(tab.Main) is not { } source || source == this) return;
        _draggedTab = null;
        int index = Tabs.TabItems.Count;
        for (int i = 0; i < Tabs.TabItems.Count; i++)
        {
            if (Tabs.TabItems[i] is not TabViewItem other) continue;
            if (e.GetPosition(other).X < other.ActualWidth / 2) { index = i; break; }
        }
        MoveTabItem(dragged.Item, source, this, index);
        CloseIfEmpty(source);
        Activate();
    });

    /// <summary>Onglet lâché hors de toute bande d'onglets : nouvelle fenêtre sous le pointeur.</summary>
    private void Tabs_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args) => Guard(() =>
    {
        var grab = _draggedTab?.Grab ?? new PointInt32(0, 0);
        _draggedTab = null;
        if (TabOf(args.Tab) is not { } tab || !Native.GetCursorPos(out var cursor) || Root.XamlRoot is not { } root) return;
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (cursor.X >= position.X && cursor.X < position.X + size.Width && cursor.Y >= position.Y && cursor.Y < position.Y + size.Height)
            return;

        // Décalage d'un onglet par rapport au coin de la fenêtre : l'onglet déplacé, ou le premier, qu'il sera seul
        // à occuper dans la nouvelle fenêtre (même mise en page).
        PointInt32 Offset(UIElement element)
        {
            var screen = root.CoordinateConverter.ConvertLocalToScreen(element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0)));
            return new PointInt32(screen.X - position.X, screen.Y - position.Y);
        }

        if (ViewModel.Tabs.Count == 1)
        {
            var own = Offset(args.Tab);
            AppWindow.Move(new PointInt32(cursor.X - grab.X - own.X, cursor.Y - grab.Y - own.Y));
            return;
        }
        var first = Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault() is { } firstTab ? Offset(firstTab) : new PointInt32(0, 0);
        ViewModel.Release(tab);
        var window = new MainWindow(adopted: tab);
        window.AppWindow.Move(new PointInt32(cursor.X - grab.X - first.X, cursor.Y - grab.Y - first.Y));
        window.Activate();
    });

    /// <summary>
    /// Une erreur pendant un glisser d'onglet ne doit pas passer inaperçue : elle est journalisée et signalée
    /// une fois le geste fini.
    /// </summary>
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            AppSettings.LogError(ex);
            DispatcherQueue.TryEnqueue(() => Run(Dialogs.Show(Root, Loc.AppName, Loc.UnexpectedError(ex.Message))));
        }
    }

    // ================= Outils =================

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
