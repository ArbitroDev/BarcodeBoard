using System.Collections.Specialized;
using System.ComponentModel;
using BarcodeBoard.Controls;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation.Collections;
using Windows.Graphics;

namespace BarcodeBoard;

/// <summary>
/// Une fenêtre : onglets dans la barre de titre (TabView natif, arrachage et fusion entre fenêtres), Mica Alt
/// sous Windows 11, et le contenu de l'onglet actif (voir les autres parties de la classe).
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Taille d'une première fenêtre sans réglages, et taille minimale (pixels logiques).</summary>
    private const double DefaultWidth = 1240, DefaultHeight = 780, MinWidth = 760, MinHeight = 480;

    /// <summary>
    /// L'élément d'onglet de chaque onglet, commun à toutes les fenêtres : un onglet arraché ou fusionné garde le
    /// même élément (TabView suit cet objet pendant tout le glisser).
    /// </summary>
    private static readonly Dictionary<TabViewModel, TabViewItem> TabItems = [];

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IReadOnlyList<string> _startupFiles;
    private readonly bool _isFirst, _mica;
    private readonly IntPtr _hwnd;
    private bool _syncingTabs, _closeConfirmed, _skipPrompts, _settingsSaved, _themeTilesSyncing;
    private TabViewModel? _titleTab;
    private ScrollViewer? _tabScroller;

    public MainViewModel ViewModel { get; } = new();

    private TabViewModel? Tab => ViewModel.SelectedTab;

    /// <param name="startupFiles">Fichiers .codes à ouvrir une fois la fenêtre affichée.</param>
    /// <param name="adopted">Onglet sorti d'une autre fenêtre : la nouvelle fenêtre ne contient que lui.</param>
    /// <param name="isFirst">Première fenêtre de l'application : elle reprend la position et l'état enregistrés.</param>
    /// <param name="tearOut">Fenêtre demandée par TabView pendant un arrachage : vide, placée et affichée par TabView.</param>
    public MainWindow(IReadOnlyList<string>? startupFiles = null, TabViewModel? adopted = null, bool isFirst = false, bool tearOut = false)
    {
        _startupFiles = startupFiles ?? [];
        _isFirst = isFirst;
        if (isFirst) Theme.Apply(ThemeSetting.Parse(_settings.Theme));
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindows.Register(this);

        // Onglets dans la barre de titre ; Windows garde ses boutons Réduire / Agrandir / Fermer.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.SetIcon(IconPath);

        // Mica Alt (Windows 11), le matériau des applications à onglets ; Windows 10 garde un fond opaque.
        _mica = !NoMicaRequested() && MicaController.IsSupported();
        if (_mica) SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        PrepareTabColors();

        ViewModel.CodeSize = _settings.CodeSize;
        ViewModel.SideOpen = _settings.SideOpen;
        ViewModel.Tabs.CollectionChanged += OnTabsChanged;
        ViewModel.PropertyChanged += Vm_PropertyChanged;
        if (adopted is not null) ViewModel.Adopt(adopted, 0);
        else if (!tearOut) ViewModel.NewTab();

        ApplyTheme();
        Theme.Changed += ApplyTheme;
        Root.ActualThemeChanged += (_, _) => ApplyLayers();

        if (isFirst) RestorePlacement();
        else if (!tearOut) PlaceNextToLastWindow();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinWidth * WindowScale);
            presenter.PreferredMinimumHeight = (int)(MinHeight * WindowScale);
        }

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(2.4);
        _toastTimer.Tick += (_, _) => HideToast();
        Root.Loaded += OnLoaded;
        AppWindow.Changed += OnAppWindowChanged;
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
        ViewModel.DpiScale = Root.XamlRoot.RasterizationScale;
        Root.XamlRoot.Changed += (_, _) =>
        {
            double scale = Root.XamlRoot?.RasterizationScale ?? 1;
            if (scale == ViewModel.DpiScale) return;
            // Fenêtre déplacée vers un écran d'une autre échelle : codes et icône redessinés au pixel près.
            ViewModel.DpiScale = scale;
            UpdateTitleIcon();
            UpdateCaptionMetrics();
            if (FullView.Visibility == Visibility.Visible) RenderFull();
        };
        UpdateTitleIcon();
        UpdateCaptionMetrics();
        ApplySideWidth();
        HookTabScroller();
        foreach (var file in _startupFiles) OpenPath(file);
        FocusComposer();
#if DEBUG
        if (_isFirst) DebugScenario.Run(this, ViewModel);
#endif
    }

    /// <summary>Icône de la barre de titre : l'image du .ico dessinée pour 16 px à l'échelle de l'écran, sans redimensionnement.</summary>
    private async void UpdateTitleIcon()
    {
        try
        {
            if (IconFrames.Png(await File.ReadAllBytesAsync(IconPath), (int)Math.Round(16 * ViewModel.DpiScale)) is not { } png) return;
            var image = new BitmapImage();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            TitleIcon.Source = image;
        }
        catch (IOException) { }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Le plein écran suit la taille de sa zone (FullView_SizeChanged), une fois la mise en page faite.
        if (args.DidPresenterChange || args.DidSizeChange) UpdateCaptionMetrics();
    }

    /// <summary>
    /// Boutons système dessinés par Windows : leur place est réservée à droite des onglets, et la roue des paramètres
    /// prend leur hauteur. Agrandie, la barre de titre se réduit : les onglets touchent le haut de l'écran.
    /// </summary>
    private void UpdateCaptionMetrics()
    {
        if (Root.XamlRoot is null) return;
        bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        var option = maximized ? TitleBarHeightOption.Standard : TitleBarHeightOption.Tall;
        if (AppWindow.TitleBar.PreferredHeightOption != option) AppWindow.TitleBar.PreferredHeightOption = option;
        double scale = Root.XamlRoot.RasterizationScale;
        double captionHeight = AppWindow.TitleBar.Height / scale;
        CaptionColumn.Width = new GridLength(Math.Max(0, AppWindow.TitleBar.RightInset / scale));
        SettingsButton.Height = captionHeight;
        TitleRow.Height = maximized ? GridLength.Auto : new GridLength(Math.Max(captionHeight, 40));
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
        Toolbar.Background = Theme.Brush("ChromeLayer" + suffix, theme);
        SideLayer.Background = Theme.Brush("SideLayer" + suffix, theme);
        ContentLayer.Background = Theme.Brush("ContentLayer" + suffix, theme);
        Root.Background = _mica ? new SolidColorBrush(Colors.Transparent) : Theme.Brush("WindowBg", theme);
    }

    /// <summary>L'onglet actif a la couleur de la barre d'outils, dans laquelle il se fond (comme l'Explorateur).</summary>
    private void PrepareTabColors()
    {
        foreach (var (key, theme) in new[] { ("Light", ElementTheme.Light), ("Dark", ElementTheme.Dark) })
        {
            var colors = new ResourceDictionary
            {
                ["TabViewItemHeaderBackgroundSelected"] = Theme.Brush(_mica ? "ChromeLayerMica" : "ChromeLayer", theme),
                ["TabViewBackground"] = new SolidColorBrush(Colors.Transparent),
            };
            Tabs.Resources.ThemeDictionaries[key] = colors;
        }
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
            CommitPendingRenames();
            var dirty = ViewModel.Tabs.Where(t => t.IsDirty).ToList();
            if (dirty.Count > 0 && !_skipPrompts)
            {
                e.Cancel = true;
                foreach (var tab in dirty)
                    if (!await ConfirmSave(tab)) return;
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
        PointerVisibility.Show();
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
        var header = new TabHeader(tab);
        item = new TabViewItem { Header = header, Tag = tab, IsClosable = true };
        // Menu neuf à chaque ouverture, dans la fenêtre où l'onglet se trouve alors : un menu déjà ouvert dans une
        // fenêtre ne s'ouvre pas forcément dans une autre, et l'onglet passe d'une fenêtre à l'autre.
        item.ContextRequested += (sender, e) =>
        {
            if (AppWindows.For(tab.Main) is not { } window) return;
            var target = (TabViewItem)sender;
            var menu = new MenuFlyout();
            window.FillTabMenu(menu, tab, header);
            if (e.TryGetPosition(target, out var point)) menu.ShowAt(target, new FlyoutShowOptions { Position = point });
            else menu.ShowAt(target);
            e.Handled = true;
        };
        TabItems[tab] = item;
        return item;
    }

    private static TabViewModel? TabOf(object? item) => (item as TabViewItem)?.Tag as TabViewModel;

    /// <summary>Les onglets du modèle changent (nouvel onglet, fermeture, onglet venu d'ailleurs) : TabView suit.</summary>
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncingTabs) return;
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
        if (_syncingTabs || Tabs.TabItems.Count != ViewModel.Tabs.Count) return;
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
        if (_syncingTabs) return;
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
        if (tab.IsDirty && !await ConfirmSave(tab)) return false;
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
    private void FillTabMenu(MenuFlyout menu, TabViewModel tab, TabHeader header)
    {
        menu.Items.Clear();
        var rename = new MenuFlyoutItem { Text = Loc.Rename, IsEnabled = !ViewModel.ScanMode };
        rename.Click += (_, _) => header.BeginRename();
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

    /// <summary>
    /// Passe un onglet d'une fenêtre à une autre, avec son élément d'onglet (le même objet).
    /// La fenêtre d'origine vidée se ferme, sauf pendant un arrachage : TabView la masque puis la ferme lui-même
    /// en fin de geste, et peut encore y remettre l'onglet si l'utilisateur l'arrache à nouveau.
    /// </summary>
    private static void MoveTab(MainWindow from, TabViewModel tab, MainWindow to, int index, bool closeEmptySource)
    {
        from.ViewModel.Release(tab);
        to.ViewModel.Adopt(tab, index);
        if (closeEmptySource)
        {
            to.BringToFront();
            if (from.ViewModel.Tabs.Count == 0) from.DispatcherQueue.TryEnqueue(from.CloseWithoutPrompts);
        }
    }

    // ---- Arrachage et fusion (TabView, Windows App SDK 1.6 et plus) ----

    /// <summary>Un onglet quitte la bande : TabView demande une fenêtre neuve, qu'il affichera et déplacera.</summary>
    private void Tabs_TabTearOutWindowRequested(TabView sender, TabViewTabTearOutWindowRequestedEventArgs args)
    {
        CommitPendingRenames();
        var window = new MainWindow(tearOut: true);
        args.NewWindowId = window.AppWindow.Id;
    }

    /// <summary>
    /// L'onglet passe dans la fenêtre neuve. La fenêtre est retrouvée par son identifiant : l'événement peut venir
    /// d'une autre fenêtre que celle d'origine (onglet fusionné puis arraché à nouveau dans le même geste).
    /// </summary>
    private void Tabs_TabTearOutRequested(TabView sender, TabViewTabTearOutRequestedEventArgs args)
    {
        if (AppWindows.For(args.NewWindowId) is not { } target) return;
        foreach (var item in args.Items)
            if (TabOf(item) is { } tab && AppWindows.For(tab.Main) is { } source && source != target)
                MoveTab(source, tab, target, target.ViewModel.Tabs.Count, closeEmptySource: false);
    }

    private void Tabs_ExternalTornOutTabsDropping(TabView sender, TabViewExternalTornOutTabsDroppingEventArgs args) =>
        args.AllowDrop = true;

    /// <summary>Onglet arraché amené sur la bande de cette fenêtre : il y entre à l'endroit indiqué.</summary>
    private void Tabs_ExternalTornOutTabsDropped(TabView sender, TabViewExternalTornOutTabsDroppedEventArgs args)
    {
        int index = args.DropIndex;
        foreach (var item in args.Items)
            if (TabOf(item) is { } tab && AppWindows.For(tab.Main) is { } source && source != this)
                MoveTab(source, tab, this, index++, closeEmptySource: false);
    }

    // ---- Liste de tous les onglets ----

    /// <summary>Le bouton « Tous les onglets » n'apparaît que si les onglets ne tiennent plus dans la bande.</summary>
    private void HookTabScroller()
    {
        _tabScroller = FindDescendant<ScrollViewer>(Tabs);
        if (_tabScroller is null) return;
        void Sync() => TabListButton.Visibility = _tabScroller.ScrollableWidth > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        _tabScroller.RegisterPropertyChangedCallback(ScrollViewer.ScrollableWidthProperty, (_, _) => Sync());
        Sync();
    }

    private void TabListMenu_Opening(object? sender, object e)
    {
        TabListMenu.Items.Clear();
        foreach (var tab in ViewModel.Tabs)
        {
            var item = new ToggleMenuFlyoutItem { Text = tab.Name, IsChecked = tab == Tab };
            item.Click += (_, _) => ViewModel.SelectedTab = tab;
            TabListMenu.Items.Add(item);
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
