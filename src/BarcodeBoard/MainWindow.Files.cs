using BarcodeBoard.Core;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;

namespace BarcodeBoard;

/// <summary>Fichiers .codes : ouvrir, enregistrer, enregistrer sous (sélecteurs du Windows App SDK, liés à la fenêtre).</summary>
public sealed partial class MainWindow
{
    private string InitialFolder =>
        _settings.LastFolder is { } f && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private void Open_Click(object sender, RoutedEventArgs e) => Run(OpenFiles());

    private void Save_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs e)
    {
        if (Tab is { } tab) Run(Save(tab));
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is { } tab) Run(SaveAs(tab));
    }

    private async Task OpenFiles()
    {
        var picker = new FileOpenPicker(AppWindow.Id) { Title = Loc.OpenTitle, SuggestedFolder = InitialFolder };
        picker.FileTypeFilter.Add(CodesFile.Extension);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null) return;
        foreach (var file in files) OpenPath(file.Path);
    }

    /// <summary>Onglet de cette fenêtre lié à ce fichier, s'il y en a un.</summary>
    internal TabViewModel? FindTab(string fullPath) => ViewModel.Tabs.FirstOrDefault(t => t.FilePath is not null
        && string.Equals(Path.GetFullPath(t.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

    internal void OpenPath(string path)
    {
        var full = Path.GetFullPath(path);
        // Déjà ouvert, ici ou dans une autre fenêtre : on y va plutôt que de l'ouvrir deux fois.
        if (AppWindows.FindFile(full) is { } found)
        {
            var (window, existing) = found;
            window.ViewModel.SelectedTab = existing;
            if (window != this) window.BringToFront();
            return;
        }

        CodesDocument doc;
        try { doc = CodesFile.Load(full); }
        catch (InvalidCodesFileException ex)
        {
            Run(Dialogs.Show(Root, Loc.OpenFailedTitle, $"{Path.GetFileName(full)}\n\n{ex.Message}"));
            return;
        }

        var tab = Tab is { IsPristine: true } pristine ? pristine : ViewModel.NewTab();
        tab.FilePath = full;
        tab.Load(doc.Codes, doc.Name);
        ViewModel.SelectedTab = tab;
        ViewModel.UpdateScanFlags();
        _settings.LastFolder = Path.GetDirectoryName(full);
    }

    private async Task<bool> Save(TabViewModel tab)
    {
        CommitPendingRenames();
        return tab.FilePath is null ? await SaveAs(tab) : await WriteTab(tab, tab.FilePath);
    }

    private async Task<bool> SaveAs(TabViewModel tab)
    {
        CommitPendingRenames();
        var picker = new FileSavePicker(AppWindow.Id)
        {
            Title = Loc.SaveTitle,
            SuggestedFileName = tab.Name,
            DefaultFileExtension = CodesFile.Extension,
            SuggestedFolder = tab.FilePath is null ? InitialFolder : Path.GetDirectoryName(tab.FilePath) ?? InitialFolder,
        };
        picker.FileTypeChoices.Add(Loc.FileTypeName, [CodesFile.Extension]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;
        return await WriteTab(tab, file.Path);
    }

    private async Task<bool> WriteTab(TabViewModel tab, string path)
    {
        try
        {
            CodesFile.Save(path, tab.ToEntries(), tab.CustomName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Show(Root, Loc.SaveFailedTitle, $"{Loc.SaveFailed(path)}\n\n{ex.Message}");
            return false;
        }
        tab.FilePath = Path.GetFullPath(path);
        tab.IsDirty = false;
        _settings.LastFolder = Path.GetDirectoryName(tab.FilePath);
        ShowToast(Loc.SavedTo(Path.GetFileName(path)));
        return true;
    }
}
