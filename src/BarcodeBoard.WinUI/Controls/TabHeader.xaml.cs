using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BarcodeBoard.Controls;

public sealed partial class TabHeader : UserControl
{
    public TabHeader(TabViewModel tab)
    {
        Tab = tab;
        InitializeComponent();
    }

    public TabViewModel Tab { get; }

    /// <summary>Renommage par double-clic, jamais en mode scan (Entrée et Échap y sont réservés à la douchette).</summary>
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Tab.Main.ScanMode || Tab.IsRenaming) return;
        e.Handled = true;
        BeginRename();
    }

    public void BeginRename()
    {
        Tab.BeginRename();
        // La zone de saisie n'est visible qu'au prochain passage de mise en page.
        DispatcherQueue.TryEnqueue(() =>
        {
            RenameBox.Focus(FocusState.Programmatic);
            RenameBox.SelectAll();
        });
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Tab.CommitRename();
        else if (e.Key == VirtualKey.Escape) Tab.CancelRename();
        else return;
        e.Handled = true;
        AppWindows.For(Tab.Main)?.FocusComposer();
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e) => Tab.CommitRename();
}
