using BarcodeBoard.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace BarcodeBoard;

/// <summary>Contenu de l'onglet actif : zone de saisie, liste latérale, cartes, plein écran, messages.</summary>
public sealed partial class MainWindow
{
    private readonly DispatcherQueueTimer _toastTimer;
    private CodeItemViewModel? _fullItem;

    // ================= Zone de saisie =================

    internal void FocusComposer() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!ViewModel.ScanMode && FullView.Visibility == Visibility.Collapsed) ComposerBox.Focus(FocusState.Programmatic);
        });

    private void Composer_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || Tab is not { } tab) return;
        if (tab.IsEditing) tab.SelectedCode = null;
        else AddFromComposer();
        e.Handled = true;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddFromComposer();

    private void AddFromComposer()
    {
        if (Tab is { } tab && !tab.AddFromComposer()) ShowToast(Loc.TypeBeforeAdd);
        ComposerBox.Focus(FocusState.Programmatic);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is { } tab) tab.SelectedCode = null;
        ComposerBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Choix du type : la bascule cliquée reste cochée même si elle l'était déjà.</summary>
    private void ComposerQr_Click(object sender, RoutedEventArgs e) => SetComposerType(sender, qr: true);
    private void ComposerCode128_Click(object sender, RoutedEventArgs e) => SetComposerType(sender, qr: false);

    private void SetComposerType(object sender, bool qr)
    {
        if (Tab is not { } tab) return;
        if (qr) tab.ComposerIsQr = true; else tab.ComposerIsCode128 = true;
        ((ToggleButton)sender).IsChecked = true;
    }

    // ================= Liste latérale =================

    private static CodeItemViewModel? Item(object sender) => (sender as FrameworkElement)?.DataContext as CodeItemViewModel;

    private void Row_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Tab is { } tab && Item(sender) is { } item) tab.SelectedCode = item;
    }

    private void RowText_GotFocus(object sender, RoutedEventArgs e)
    {
        if (Tab is { } tab && Item(sender) is { } item) tab.SelectedCode = item;
    }

    /// <summary>Entrée dans une ligne : nouvelle ligne juste après, prête à être tapée.</summary>
    private void RowText_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || Tab is not { } tab || Item(sender) is not { } item) return;
        FocusRow(tab.InsertAfter(item));
        e.Handled = true;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is { } tab) FocusRow(tab.InsertAfter(tab.Codes.LastOrDefault()));
    }

    private void RowQr_Click(object sender, RoutedEventArgs e) => SetRowType(sender, CodeType.Qr);
    private void RowCode128_Click(object sender, RoutedEventArgs e) => SetRowType(sender, CodeType.Code128);

    private static void SetRowType(object sender, CodeType type)
    {
        if (Item(sender) is { } item) item.Type = type;
        ((ToggleButton)sender).IsChecked = true;
    }

    private void FocusRow(CodeItemViewModel item) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Tab is not { } tab) return;
            if (SideList.ContainerFromItem(item) is DependencyObject row && FindDescendant<TextBox>(row) is { } box)
            {
                box.Focus(FocusState.Programmatic);
                box.SelectAll();
            }
            tab.SelectedCode = item;
        });

    // ---- Largeur de la liste (WinUI n'a pas de GridSplitter) ----

    private double _splitterStartX, _splitterStartWidth;
    private bool _splitterDragging;

    private void ApplySideWidth() =>
        SideColumn.Width = new GridLength(ViewModel.SideOpen ? Math.Clamp(_settings.SideWidth, 240, 720) : 0);

    private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e) => SplitterLine.Opacity = 1;

    private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitterDragging) SplitterLine.Opacity = 0;
    }

    private void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        _splitterDragging = SideSplitter.CapturePointer(e.Pointer);
        _splitterStartX = e.GetCurrentPoint(Root).Position.X;
        _splitterStartWidth = SideColumn.ActualWidth;
        e.Handled = true;
    }

    private void Splitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitterDragging) return;
        double width = Math.Clamp(_splitterStartWidth + e.GetCurrentPoint(Root).Position.X - _splitterStartX, 240, 720);
        SideColumn.Width = new GridLength(width);
    }

    private void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitterDragging) return;
        SideSplitter.ReleasePointerCapture(e.Pointer);
        EndSplitterDrag();
    }

    private void Splitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndSplitterDrag();

    private void EndSplitterDrag()
    {
        if (!_splitterDragging) return;
        _splitterDragging = false;
        SplitterLine.Opacity = 0;
        _settings.SideWidth = SideColumn.ActualWidth;
    }

    // ================= Cartes =================

    /// <summary>
    /// Largeur dont dispose un code dans sa carte : la grille moins sa marge (16), le remplissage (28) et le bord (2)
    /// de la carte, et le remplissage de la grille. Un code plus large s'affine ou affiche un message, jamais coupé.
    /// </summary>
    private void CodesScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ViewModel.CodeMaxWidth = Math.Max(0, CodesScroll.ViewportWidth - CodesScroll.Padding.Left - CodesScroll.Padding.Right - 46);

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Tab is not { } tab || Item(sender) is not { } item || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        CommitPendingRenames(); // une carte ne prend pas le focus : le renommage en cours est validé ici
        if (ViewModel.ScanMode)
        {
            tab.ScanFocus = item;
            CodesScroll.Focus(FocusState.Programmatic);
            e.Handled = true;
            return;
        }
        BeginDragCandidate((UIElement)sender, item, e, vertical: false);
    }

    private void Card_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.ScanMode || Item(sender) is not { } item) return;
        _drag = null;
        OpenFull(item);
        e.Handled = true;
    }

    /// <summary>Le texte se limite à la taille du code flouté : un long texte n'agrandit pas la carte.</summary>
    private void CodeVisual_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (VisualTreeHelperParent(sender) is Grid { Children.Count: > 1 } host && host.Children[1] is FrameworkElement overlay)
        {
            overlay.MaxWidth = e.NewSize.Width;
            overlay.MaxHeight = e.NewSize.Height;
        }
    }

    private static DependencyObject? VisualTreeHelperParent(object element) =>
        Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent((DependencyObject)element);

    /// <summary>
    /// Le pointeur ne doit pas masquer le code net pendant la lecture (mode scan) ; sans code affiché, il reste visible
    /// pour cliquer sur « Passer en QR code ». Réévalué à chaque mouvement : le code sous le pointeur peut devenir net.
    /// </summary>
    private void Code_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (Item(sender) is { HidesPointer: true }) PointerVisibility.Hide();
        else PointerVisibility.Show();
    }

    private void Code_PointerExited(object sender, PointerRoutedEventArgs e) => PointerVisibility.Show();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        // Le bouton est désactivé pour un code en erreur : il n'a pas d'image.
        if (Item(sender) is not { Matrix: { } matrix }) return;
        var bitmap = CodeBitmaps.ForClipboard(matrix, ViewModel.CodeSize, ViewModel.DpiScale);
        ShowToast(Win32Clipboard.SetImage(_hwnd, bitmap) ? Loc.ImageCopied : Loc.ClipboardBusy);
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        _drag = null; // le clic copie, il ne sélectionne pas la carte
        if (Item(sender) is { } item) CopyText(item.Text);
    }

    private void CopyFullText_Click(object sender, RoutedEventArgs e)
    {
        if (_fullItem is not null) CopyText(_fullItem.Text);
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text)) { ShowToast(Loc.NothingToCopy); return; }
        ShowToast(Win32Clipboard.SetText(_hwnd, text) ? Loc.TextCopied(text.Length > 60 ? text[..57] + "…" : text) : Loc.ClipboardBusy);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not { } tab || Item(sender) is not { } item) return;
        tab.Remove(item);
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

    // ================= Glisser pour réordonner (cartes et lignes) =================

    /// <summary>Glisser en cours ou possible : élément saisi, point de départ, sens de la liste.</summary>
    private sealed record DragState(CodeItemViewModel Item, UIElement Source, Point Start, bool Vertical)
    {
        public bool Started { get; set; }
    }

    private DragState? _drag;

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Item(sender) is not { } item || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        BeginDragCandidate((UIElement)sender, item, e, vertical: true);
        e.Handled = true;
    }

    private void BeginDragCandidate(UIElement source, CodeItemViewModel item, PointerRoutedEventArgs e, bool vertical)
    {
        _drag = new DragState(item, source, e.GetCurrentPoint(Root).Position, vertical);
        source.CapturePointer(e.Pointer);
    }

    private void Drag_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || ViewModel.ScanMode || Tab is not { } tab) return;
        var p = e.GetCurrentPoint(Root).Position;
        if (!drag.Started)
        {
            // Seuil de glisser de Windows (4 px à 100 %)
            if (Math.Abs(p.X - drag.Start.X) < 4 && Math.Abs(p.Y - drag.Start.Y) < 4) return;
            drag.Started = true;
        }
        var list = drag.Vertical ? (ItemsControl)SideList : CodesGrid;
        foreach (var c in tab.Codes) { c.DropBefore = false; c.DropAfter = false; }
        if (DropTarget(list, tab, e, drag.Vertical) is var (target, after) && target != drag.Item)
        {
            target.DropBefore = !after;
            target.DropAfter = after;
        }
    }

    /// <summary>Élément sous le pointeur et côté où l'élément glissé irait (après : à droite ou en dessous).</summary>
    private static (CodeItemViewModel Target, bool After)? DropTarget(ItemsControl list, TabViewModel tab, PointerRoutedEventArgs e, bool vertical)
    {
        for (int i = 0; i < tab.Codes.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not FrameworkElement container) continue;
            var p = e.GetCurrentPoint(container).Position;
            if (p.X < 0 || p.Y < 0 || p.X >= container.ActualWidth || p.Y >= container.ActualHeight) continue;
            bool after = vertical ? p.Y > container.ActualHeight / 2 : p.X > container.ActualWidth / 2;
            return (tab.Codes[i], after);
        }
        return null;
    }

    private void Drag_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || Tab is not { } tab) return;
        _drag = null;
        drag.Source.ReleasePointerCapture(e.Pointer);
        ClearDropMarks();
        if (!drag.Started)
        {
            // Simple clic sur une carte : elle passe dans la zone de saisie pour être modifiée.
            if (!drag.Vertical && !ViewModel.ScanMode) tab.SelectedCode = drag.Item;
            return;
        }
        var list = drag.Vertical ? (ItemsControl)SideList : CodesGrid;
        if (DropTarget(list, tab, e, drag.Vertical) is var (target, after) && target != drag.Item)
            tab.Move(tab.Codes.IndexOf(drag.Item), tab.Codes.IndexOf(target) + (after ? 1 : 0));
    }

    private void Drag_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is null) return;
        _drag = null;
        ClearDropMarks();
    }

    private void ClearDropMarks()
    {
        if (Tab is not { } tab) return;
        foreach (var c in tab.Codes) { c.DropBefore = false; c.DropAfter = false; }
    }

    // ================= Plein écran =================

    private void OpenFull(CodeItemViewModel item)
    {
        if (item.HasError) return; // pas de code à montrer en grand (un code trop large pour sa carte, lui, peut l'être)
        _fullItem = item;
        FullView.Visibility = Visibility.Visible;
        // Zone pas encore mesurée (elle était masquée) : le rendu attend sa taille (FullView_SizeChanged).
        if (FullView.ActualHeight > 0) RenderFull();
        FullView.DispatcherQueue.TryEnqueue(() => FullPrevButton.Focus(FocusState.Programmatic));
    }

    private void RenderFull()
    {
        if (_fullItem is null) return;
        var codes = _fullItem.Owner.Codes;
        if (!codes.Contains(_fullItem)) { CloseFull(); return; }

        double maxW = Math.Max(100, FullView.ActualWidth * 0.92), maxH = Math.Max(100, FullView.ActualHeight - 200);
        // Le plein écran ne reçoit jamais un code en erreur (OpenFull, StepFull) : il a toujours une matrice.
        FullImage.Source = CodeBitmaps.Fit(_fullItem.Matrix!, maxW, maxH, ViewModel.DpiScale);
        FullText.Text = FullImage.Source is null ? $"{_fullItem.Text}\n{Loc.TooWideFull}" : _fullItem.Text;
        // Seuls les codes sans erreur comptent : ce sont les seuls que les flèches font défiler.
        var shown = codes.Where(c => !c.HasError).ToList();
        FullCounter.Text = Loc.Counter(shown.IndexOf(_fullItem) + 1, shown.Count);
        FullPrevButton.IsEnabled = FullNextButton.IsEnabled = shown.Count > 1;
    }

    private void StepFull(int delta)
    {
        if (_fullItem is null) return;
        var codes = _fullItem.Owner.Codes;
        if (codes.Count == 0) { CloseFull(); return; }
        // Les codes en erreur sont sautés ; s'il n'y en a pas d'autre, on reste sur place.
        int index = codes.IndexOf(_fullItem);
        for (int step = 1; step <= codes.Count; step++)
        {
            var next = codes[Cyclic.Wrap(index + step * delta, codes.Count)];
            if (next.HasError) continue;
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
        PointerVisibility.Show();
        if (ViewModel.ScanMode) CodesScroll.Focus(FocusState.Programmatic);
        else FocusComposer();
    }

    private bool FullScreenOpen => FullView.Visibility == Visibility.Visible;

    /// <summary>Ouverture du plein écran ou fenêtre redimensionnée : le code est recalculé pour la place réelle.</summary>
    private void FullView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FullScreenOpen && e.NewSize.Height > 0) RenderFull();
    }

    private void FullPrev_Click(object sender, RoutedEventArgs e) => StepFull(-1);
    private void FullNext_Click(object sender, RoutedEventArgs e) => StepFull(1);
    private void FullClose_Click(object sender, RoutedEventArgs e) => CloseFull();

    private void FullImage_PointerEntered(object sender, PointerRoutedEventArgs e) => PointerVisibility.Hide();
    private void FullImage_PointerExited(object sender, PointerRoutedEventArgs e) => PointerVisibility.Show();

    // ================= Message temporaire =================

    internal void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.Opacity = 0;
    }

#if DEBUG
    internal void DebugOpenFull(CodeItemViewModel item) => OpenFull(item);
#endif
}
