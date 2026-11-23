using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BarcodeBoard.Core;

namespace BarcodeBoard;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>Un code de l'onglet : texte, type et image prête à afficher.</summary>
public sealed class CodeItemViewModel : Observable
{
    /// <summary>Rayon du flou des codes non visés en mode scan, en pixels logiques.</summary>
    public const double BlurRadius = 22;

    private string _text;
    private CodeType _type;
    private CodeResult _result;
    private CodeBitmap? _image, _blurred;
    private bool _isSelected, _isScanFocus, _isBlurred, _dropBefore, _dropAfter;

    public CodeItemViewModel(TabViewModel owner, string text, CodeType type)
    {
        Owner = owner;
        _text = text;
        _type = type;
        _result = CodeGenerator.Generate(text, type);
        Rerender();
    }

    public TabViewModel Owner { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value ?? "")) return;
            Regenerate();
            Owner.OnCodeChanged(this);
        }
    }

    public CodeType Type
    {
        get => _type;
        set
        {
            if (!Set(ref _type, value)) return;
            Raise(nameof(IsQr));
            Raise(nameof(IsCode128));
            Raise(nameof(TypeLabel));
            Regenerate();
            Owner.OnCodeChanged(this);
        }
    }

    public bool IsQr { get => Type == CodeType.Qr; set { if (value) Type = CodeType.Qr; } }
    public bool IsCode128 { get => Type == CodeType.Code128; set { if (value) Type = CodeType.Code128; } }
    public string TypeLabel => Type == CodeType.Qr ? Loc.QrCode : Loc.Code128;

    /// <summary>Image nette du code (null : erreur ou code trop large pour sa carte).</summary>
    public CodeBitmap? Image
    {
        get => _image;
        private set
        {
            if (!Set(ref _image, value)) return;
            _blurred = null;
            Raise(nameof(ShownImage));
        }
    }

    /// <summary>Image affichée : floutée en mode scan si ce n'est pas le code visé (calculée une fois).</summary>
    public CodeBitmap? ShownImage => IsBlurred && _image is not null ? _blurred ??= CodeBitmaps.Blur(_image, BlurRadius) : _image;

    public CodeMatrix? Matrix => _result.Matrix;
    public bool HasError => !_result.Ok;
    /// <summary>Copie de l'image et plein écran : impossibles seulement pour un code en erreur (un code trop large le peut).</summary>
    public bool CanCopy => _result.Ok;
    public string? ErrorMessage => _result.Error?.Message;
    /// <summary>Code valide, mais trop large pour la place disponible même à un pixel par module.</summary>
    public bool TooWide => _result.Ok && Image is null;
    /// <summary>À la place du code : une erreur, ou un code trop large (jamais un code coupé, illisible).</summary>
    public bool ShowPlaceholder => HasError || TooWide;
    public bool ShowImage => !ShowPlaceholder;
    public string? PlaceholderMessage => HasError ? ErrorMessage : TooWide ? Loc.TooWide : null;
    /// <summary>Code 128 impossible à afficher (caractère refusé, texte trop long ou trop large) : un QR code le peut.</summary>
    public bool CanSwitchToQr => Type == CodeType.Code128
        && (_result.Error?.Kind is CodeErrorKind.NotAscii or CodeErrorKind.TooLong || TooWide);

    /// <summary>Taille du cadre d'erreur, pour qu'il occupe la place du code.</summary>
    public double PlaceholderWidth => Owner.Main.CodeSize;
    public double PlaceholderHeight => Type == CodeType.Qr ? Owner.Main.CodeSize : Math.Round(Owner.Main.CodeSize * CodeRaster.BarcodeHeightRatio);

    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) Raise(nameof(ShowRing)); }
    }

    public bool IsScanFocus
    {
        get => _isScanFocus;
        set
        {
            if (!Set(ref _isScanFocus, value)) return;
            Raise(nameof(ShowRing));
            Raise(nameof(HidesPointer));
        }
    }

    public bool IsBlurred
    {
        get => _isBlurred;
        set
        {
            if (!Set(ref _isBlurred, value)) return;
            Raise(nameof(ShownImage));
            Raise(nameof(CodeOpacity));
            Raise(nameof(DetailsOpacity));
        }
    }

    /// <summary>Anneau de sélection : code en cours de modification, ou code visé en mode scan.</summary>
    public bool ShowRing => IsSelected || IsScanFocus;
    /// <summary>Le pointeur ne doit pas masquer le code pendant la lecture ; sans code affiché, il reste visible.</summary>
    public bool HidesPointer => IsScanFocus && !ShowPlaceholder;
    /// <summary>Code flouté en mode scan : estompé.</summary>
    public double CodeOpacity => IsBlurred ? 0.25 : 1;
    /// <summary>Texte et boutons de la carte : masqués sur un code flouté, sans changer la taille de la carte.</summary>
    public double DetailsOpacity => IsBlurred ? 0 : 1;

    public bool DropBefore { get => _dropBefore; set => Set(ref _dropBefore, value); }
    public bool DropAfter { get => _dropAfter; set => Set(ref _dropAfter, value); }

    private void Regenerate()
    {
        _result = CodeGenerator.Generate(_text, _type);
        Raise(nameof(HasError));
        Raise(nameof(CanCopy));
        Raise(nameof(ErrorMessage));
        Raise(nameof(CanSwitchToQr));
        Raise(nameof(Matrix));
        Rerender();
    }

    public void Rerender()
    {
        var main = Owner.Main;
        Image = _result.Ok ? CodeBitmaps.ForScreen(_result.Matrix!, main.CodeSize, main.DpiScale, main.CodeMaxWidth) : null;
        Raise(nameof(TooWide));
        Raise(nameof(ShowPlaceholder));
        Raise(nameof(ShowImage));
        Raise(nameof(PlaceholderMessage));
        Raise(nameof(CanSwitchToQr));
        Raise(nameof(PlaceholderWidth));
        Raise(nameof(PlaceholderHeight));
        Raise(nameof(HidesPointer));
    }

    public CodeEntry ToEntry() => new(Text, Type);
}

/// <summary>Un onglet : une série de codes, éventuellement liée à un fichier .codes.</summary>
public sealed class TabViewModel : Observable
{
    private string? _filePath;
    private bool _isDirty, _isActive;
    private CodeItemViewModel? _selected, _scanFocus;
    private string _draft = "";
    private CodeType _draftType = CodeType.Qr;

    public TabViewModel(MainViewModel main, int untitledNumber)
    {
        Main = main;
        UntitledNumber = untitledNumber;
        Codes.CollectionChanged += OnCodesChanged;
    }

    /// <summary>La fenêtre qui affiche l'onglet. Change quand l'onglet passe dans une autre fenêtre.</summary>
    public MainViewModel Main { get; private set; }
    public int UntitledNumber { get; }

    /// <summary>Rattache l'onglet à une autre fenêtre : ses codes sont redessinés à sa taille et à son échelle.</summary>
    internal void AttachTo(MainViewModel main)
    {
        Main = main;
        ScanFocus = null;
        foreach (var c in Codes)
        {
            c.IsScanFocus = false;
            c.IsBlurred = false;
            c.Rerender();
        }
    }

    public ObservableCollection<CodeItemViewModel> Codes { get; } = [];

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (!Set(ref _filePath, value)) return;
            Raise(nameof(Name));
            Raise(nameof(FileStatus));
            Raise(nameof(ToolTip));
        }
    }

    private string? _customName;
    private bool _isRenaming;
    private string _renameText = "";

    /// <summary>Nom choisi au double-clic (enregistré dans le fichier) ; sinon le nom du fichier ; sinon « Sans titre n ».</summary>
    public string Name => _customName ?? DefaultName;
    private string DefaultName => FilePath is null ? Loc.Untitled(UntitledNumber) : Path.GetFileNameWithoutExtension(FilePath);
    public string? CustomName => _customName;
    public string ToolTip => FilePath is null ? Name : $"{Name}\n{FilePath}";

    public bool IsRenaming
    {
        get => _isRenaming;
        private set { if (Set(ref _isRenaming, value)) Raise(nameof(NotRenaming)); }
    }

    public bool NotRenaming => !IsRenaming;
    public string RenameText { get => _renameText; set => Set(ref _renameText, value ?? ""); }

    public void BeginRename()
    {
        RenameText = Name;
        IsRenaming = true;
    }

    public void CommitRename()
    {
        if (!IsRenaming) return;
        IsRenaming = false;
        var text = RenameText.Trim();
        var custom = text.Length == 0 || text == DefaultName ? null : text;
        if (custom == _customName) return;
        _customName = custom;
        IsDirty = true;
        Raise(nameof(Name));
        Raise(nameof(ToolTip));
    }

    public void CancelRename() => IsRenaming = false;

    public bool IsDirty
    {
        get => _isDirty;
        set { if (Set(ref _isDirty, value)) Raise(nameof(FileStatus)); }
    }

    public string FileStatus => FilePath is null
        ? Loc.NotSavedYet
        : IsDirty ? Loc.UnsavedChanges(Path.GetFileName(FilePath)) : Loc.SavedIn(Path.GetFileName(FilePath));

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>Onglet neuf jamais touché : on peut le remplacer à l'ouverture d'un fichier.</summary>
    public bool IsPristine => FilePath is null && !IsDirty && Codes.Count == 0 && _draft.Length == 0 && _customName is null;

    public bool HasCodes => Codes.Count > 0;
    public bool IsEmpty => Codes.Count == 0;
    public string CountLabel => Loc.Count(Codes.Count);

    // ---- Sélection et zone de saisie ----

    public CodeItemViewModel? SelectedCode
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (_selected is not null) _selected.IsSelected = true;
            Raise();
            Raise(nameof(IsEditing));
            Raise(nameof(NotEditing));
            Raise(nameof(ComposerText));
            Raise(nameof(ComposerIsQr));
            Raise(nameof(ComposerIsCode128));
        }
    }

    public bool IsEditing => SelectedCode is not null;
    public bool NotEditing => SelectedCode is null;

    public string ComposerText
    {
        get => SelectedCode?.Text ?? _draft;
        set
        {
            if (SelectedCode is not null) SelectedCode.Text = value;
            else if (_draft != value) { _draft = value ?? ""; Raise(); }
        }
    }

    private CodeType ComposerType => SelectedCode?.Type ?? _draftType;

    public bool ComposerIsQr { get => ComposerType == CodeType.Qr; set { if (value) SetComposerType(CodeType.Qr); } }
    public bool ComposerIsCode128 { get => ComposerType == CodeType.Code128; set { if (value) SetComposerType(CodeType.Code128); } }

    private void SetComposerType(CodeType type)
    {
        if (SelectedCode is not null) SelectedCode.Type = type;
        else _draftType = type;
        Raise(nameof(ComposerIsQr));
        Raise(nameof(ComposerIsCode128));
    }

    /// <summary>Ajoute le texte de la zone de saisie. Faux si la zone est vide.</summary>
    public bool AddFromComposer()
    {
        if (string.IsNullOrWhiteSpace(_draft)) return false;
        Codes.Add(new CodeItemViewModel(this, _draft, _draftType));
        _draft = "";
        Raise(nameof(ComposerText));
        IsDirty = true;
        return true;
    }

    public CodeItemViewModel InsertAfter(CodeItemViewModel? item)
    {
        int index = item is null ? Codes.Count : Codes.IndexOf(item) + 1;
        var type = item?.Type ?? (Codes.Count > 0 ? Codes[^1].Type : CodeType.Qr);
        var created = new CodeItemViewModel(this, "", type);
        Codes.Insert(index, created);
        IsDirty = true;
        return created;
    }

    public void Remove(CodeItemViewModel item)
    {
        if (SelectedCode == item) SelectedCode = null;
        int index = Codes.IndexOf(item);
        Codes.Remove(item);
        if (ScanFocus == item) ScanFocus = Codes.Count == 0 ? null : Codes[Math.Min(index, Codes.Count - 1)];
        IsDirty = true;
    }

    /// <summary>Déplace un code pour qu'il se retrouve devant l'élément qui occupait <paramref name="to"/>.</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= Codes.Count || to == from || to == from + 1) return;
        if (to > from) to--;
        Codes.Move(from, Math.Clamp(to, 0, Codes.Count - 1));
        IsDirty = true;
    }

    public void OnCodeChanged(CodeItemViewModel item)
    {
        IsDirty = true;
        if (item == SelectedCode)
        {
            Raise(nameof(ComposerText));
            Raise(nameof(ComposerIsQr));
            Raise(nameof(ComposerIsCode128));
        }
        if (item == ScanFocus) Main.RefreshScanStatus();
    }

    // ---- Mode scan ----

    public CodeItemViewModel? ScanFocus
    {
        get => _scanFocus;
        set
        {
            if (!Set(ref _scanFocus, value)) return;
            Main.UpdateScanFlags();
        }
    }

    public void MoveScanFocus(int delta)
    {
        if (Codes.Count == 0) return;
        // Aucun code net : la première flèche rend net le premier code, sans en sauter.
        ScanFocus = ScanFocus is null ? Codes[0] : Codes[Cyclic.Wrap(Codes.IndexOf(ScanFocus) + delta, Codes.Count)];
    }

    // ---- Fichier ----

    public void Load(IEnumerable<CodeEntry> entries, string? name = null)
    {
        _customName = string.IsNullOrWhiteSpace(name) ? null : name;
        Raise(nameof(Name));
        Raise(nameof(ToolTip));
        Codes.Clear();
        foreach (var e in entries) Codes.Add(new CodeItemViewModel(this, e.Text, e.Type));
        IsDirty = false;
        // Fichier ouvert en mode scan : le premier code est net d'emblée (l'ancien code net n'existe plus).
        ScanFocus = Main.ScanMode ? Codes.FirstOrDefault() : null;
    }

    public IReadOnlyList<CodeEntry> ToEntries() => Codes.Select(c => c.ToEntry()).ToList();

    private void OnCodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Raise(nameof(HasCodes));
        Raise(nameof(IsEmpty));
        Raise(nameof(CountLabel));
        if (Main.ScanMode) Main.UpdateScanFlags();
    }
}

/// <summary>Index circulaire : après le dernier élément vient le premier, et inversement.</summary>
public static class Cyclic
{
    public static int Wrap(int index, int count) => (index % count + count) % count;
}

/// <summary>Une fenêtre : ses onglets, la taille des codes, le mode scan.</summary>
public sealed class MainViewModel : Observable
{
    public const double MinSize = 110, MaxSize = 280, DefaultSize = 160;

    private TabViewModel? _selectedTab;
    private double _codeSize = DefaultSize;
    private double _dpiScale = 1;
    private bool _sideOpen = true, _scanMode;
    private string? _lastRead;
    /// <summary>Numéro des onglets « Sans titre », commun à toutes les fenêtres : jamais deux « Sans titre 1 ».</summary>
    private static int _untitled;

    public MainViewModel() => Tabs.CollectionChanged += (_, _) => Raise(nameof(HasTabs));

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>Faux seulement pendant un instant : fenêtre neuve qui attend un onglet arraché, ou vidée par une fusion.</summary>
    public bool HasTabs => Tabs.Count > 0;

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;
            if (_selectedTab is not null) _selectedTab.IsActive = false;
            _selectedTab = value;
            if (_selectedTab is not null)
            {
                _selectedTab.IsActive = true;
                if (ScanMode) _selectedTab.ScanFocus ??= _selectedTab.Codes.FirstOrDefault();
            }
            Raise();
            LastRead = null;
            UpdateScanFlags();
        }
    }

    public double CodeSize
    {
        get => _codeSize;
        set
        {
            if (!Set(ref _codeSize, Math.Round(Math.Clamp(value, MinSize, MaxSize)))) return;
            RerenderAll();
        }
    }

    public double DpiScale
    {
        get => _dpiScale;
        set { if (value > 0 && Set(ref _dpiScale, value)) RerenderAll(); }
    }

    public bool SideOpen { get => _sideOpen; set => Set(ref _sideOpen, value); }

    private double _codeMaxWidth = double.PositiveInfinity;

    /// <summary>Largeur disponible pour un code dans sa carte (pixels logiques), d'après la largeur de la grille.</summary>
    public double CodeMaxWidth
    {
        get => _codeMaxWidth;
        set
        {
            // Au pixel près : un redimensionnement de fenêtre envoie beaucoup de petites variations.
            if (Math.Abs(value - _codeMaxWidth) < 1) return;
            _codeMaxWidth = value;
            RerenderAll();
        }
    }

    public bool ScanMode
    {
        get => _scanMode;
        set
        {
            if (!Set(ref _scanMode, value)) return;
            Raise(nameof(NotScanMode));
            LastRead = null;
            if (value && SelectedTab is { } tab)
            {
                tab.ScanFocus = tab.SelectedCode ?? tab.ScanFocus ?? tab.Codes.FirstOrDefault();
                tab.SelectedCode = null;
            }
            UpdateScanFlags();
        }
    }

    public bool NotScanMode => !ScanMode;

    /// <summary>Dernier texte reçu du clavier (douchette) pendant le mode scan.</summary>
    public string? LastRead
    {
        get => _lastRead;
        set { if (Set(ref _lastRead, value)) RefreshScanStatus(); }
    }

    public bool HasLastRead => LastRead is not null;
    public bool LastReadMatches => LastRead is not null && SelectedTab?.ScanFocus is { } f && f.Text == LastRead;
    public string LastReadStatus => LastReadMatches ? Loc.ReadMatches : Loc.ReadDiffers;

    public void RefreshScanStatus()
    {
        Raise(nameof(HasLastRead));
        Raise(nameof(LastReadMatches));
        Raise(nameof(LastReadStatus));
    }

    public TabViewModel NewTab()
    {
        var tab = new TabViewModel(this, ++_untitled);
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Accueille un onglet venu d'une autre fenêtre, à la position donnée, et l'affiche.</summary>
    public void Adopt(TabViewModel tab, int index)
    {
        tab.AttachTo(this);
        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        SelectedTab = tab;
        UpdateScanFlags();
    }

    /// <summary>Retire un onglet qui part dans une autre fenêtre ; l'onglet voisin prend sa place.</summary>
    public void Release(TabViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index < 0) return;
        if (tab == _selectedTab)
            SelectedTab = Tabs.Count > 1 ? Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1] : null;
        Tabs.Remove(tab);
        tab.IsActive = false;
    }

    public void UpdateScanFlags()
    {
        foreach (var tab in Tabs)
            foreach (var c in tab.Codes)
            {
                bool focus = ScanMode && tab == _selectedTab && c == tab.ScanFocus;
                c.IsScanFocus = focus;
                c.IsBlurred = ScanMode && !focus;
            }
        RefreshScanStatus();
    }

    private void RerenderAll()
    {
        foreach (var tab in Tabs)
            foreach (var c in tab.Codes)
                c.Rerender();
    }
}
