using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace BarcodeBoard;

/// <summary>
/// Clavier : raccourcis, et en mode scan la lecture de la douchette. Une douchette USB « tape » ce qu'elle lit :
/// en mode scan, aucune touche ne modifie l'onglet (règles dans <see cref="KeyRouting"/>).
/// </summary>
public sealed partial class MainWindow
{
    private readonly ScanReader _scan = new();

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HandleKey(e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift))) e.Handled = true;
    }

    /// <summary>Caractères tapés (douchette en mode scan), y compris ceux d'AltGr.</summary>
    private void Root_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (!ViewModel.ScanMode || FullScreenOpen) return;
        _scan.Type(e.Character.ToString());
        e.Handled = true;
    }

    /// <summary>Vrai si la touche a été prise en charge (elle ne va alors pas plus loin).</summary>
    internal bool HandleKey(VirtualKey key, bool ctrl, bool alt, bool shift)
    {
        var result = KeyRouting.Route((int)key, ctrl, alt, shift, ViewModel.ScanMode, FullScreenOpen);
        switch (result.Action)
        {
            case KeyAction.None: return false;
            case KeyAction.Swallow: break;
            case KeyAction.CloseFull: CloseFull(); break;
            case KeyAction.FullPrevious: StepFull(-1); break;
            case KeyAction.FullNext: StepFull(1); break;
            case KeyAction.NewTab: ViewModel.NewTab(); FocusComposer(); break;
            case KeyAction.CloseTab: if (Tab is { } tab) _ = CloseTab(tab); break;
            case KeyAction.NextTab: SelectTabOffset(1); break;
            case KeyAction.PreviousTab: SelectTabOffset(-1); break;
            case KeyAction.Open: OpenFiles(); break;
            case KeyAction.Save: if (Tab is { } saved) _ = Save(saved); break;
            case KeyAction.SaveAs: if (Tab is { } savedAs) _ = SaveAs(savedAs); break;
            case KeyAction.ToggleList: ViewModel.SideOpen = !ViewModel.SideOpen; break;
            case KeyAction.ToggleSettings: ToggleSettings(); break;
            case KeyAction.ToggleScan: ViewModel.ScanMode = !ViewModel.ScanMode; break;
            case KeyAction.ExitScan: ViewModel.ScanMode = false; break;
            case KeyAction.ScanPrevious: Tab?.MoveScanFocus(-1); PointerVisibility.Show(); BringScanFocusIntoView(); break;
            case KeyAction.ScanNext: Tab?.MoveScanFocus(1); PointerVisibility.Show(); BringScanFocusIntoView(); break;
            case KeyAction.FinishRead: if (_scan.Finish() is { } read) ViewModel.LastRead = read; break;
            case KeyAction.Backspace: _scan.Backspace(); break;
            case KeyAction.Space: _scan.AddSpace(); break;
            case KeyAction.ControlCharacter: _scan.AddControl(result.Character); break;
        }
        return true;
    }

    /// <summary>Caractère reçu en mode scan (aussi utilisé par la vérification Debug).</summary>
    internal void HandleCharacter(char c)
    {
        if (ViewModel.ScanMode && !FullScreenOpen) _scan.Type(c.ToString());
    }

    private void OnScanModeChanged()
    {
        _scan.Clear();
        PointerVisibility.Show();
        if (ViewModel.ScanMode)
        {
            CommitPendingRenames();
            // Le clavier va à la grille : rien de ce que tape la douchette n'arrive dans une zone de saisie.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => CodesScroll.Focus(FocusState.Programmatic));
            BringScanFocusIntoView();
        }
        else FocusComposer();
    }

    private void ExitScan_Click(object sender, RoutedEventArgs e) => ViewModel.ScanMode = false;

    private void BringScanFocusIntoView() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Tab?.ScanFocus is { } f && CodesGrid.ContainerFromItem(f) is FrameworkElement c)
                c.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        });
}
