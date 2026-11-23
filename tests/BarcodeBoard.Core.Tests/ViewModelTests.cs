using BarcodeBoard.Core;

namespace BarcodeBoard.Core.Tests;

/// <summary>Modèles de vue de l'application, indépendants de l'interface.</summary>
public class ViewModelTests
{
    private static MainViewModel Window(params string[] texts)
    {
        var vm = new MainViewModel { DpiScale = 1.5 };
        var tab = vm.NewTab();
        tab.Load(texts.Select(t => new CodeEntry(t, CodeType.Qr)));
        return vm;
    }

    [Fact]
    public void Codes_get_an_image_at_the_window_scale()
    {
        var vm = Window("A-1");
        var code = vm.SelectedTab!.Codes[0];
        Assert.NotNull(code.Image);
        Assert.Equal(1.5, code.Image!.Scale);
        vm.DpiScale = 2;
        Assert.Equal(2, code.Image!.Scale);
        Assert.True(code.ShowImage);
        Assert.False(code.ShowPlaceholder);
    }

    [Fact]
    public void Code_too_wide_for_its_card_shows_a_message_and_can_switch_to_qr()
    {
        var vm = Window();
        var tab = vm.SelectedTab!;
        tab.Load([new CodeEntry(new string('7', 80), CodeType.Code128)]);
        vm.CodeMaxWidth = 100;
        var code = tab.Codes[0];
        Assert.True(code.TooWide);
        Assert.True(code.ShowPlaceholder);
        Assert.False(code.ShowImage);
        Assert.Equal(Loc.TooWide, code.PlaceholderMessage);
        Assert.True(code.CanSwitchToQr);
        Assert.True(code.CanCopy, "un code trop large pour sa carte reste copiable");
        code.Type = CodeType.Qr;
        Assert.False(code.CanSwitchToQr);
    }

    [Fact]
    public void Code_in_error_cannot_be_copied_and_offers_qr_for_accents()
    {
        var vm = Window();
        vm.SelectedTab!.Load([new CodeEntry("Café", CodeType.Code128)]);
        var code = vm.SelectedTab.Codes[0];
        Assert.True(code.HasError);
        Assert.False(code.CanCopy);
        Assert.True(code.CanSwitchToQr);
        Assert.Null(code.Image);
    }

    [Fact]
    public void Scan_mode_blurs_every_code_but_the_focused_one()
    {
        var vm = Window("A", "B", "C");
        var tab = vm.SelectedTab!;
        vm.ScanMode = true;
        Assert.Same(tab.Codes[0], tab.ScanFocus);
        Assert.False(tab.Codes[0].IsBlurred);
        Assert.True(tab.Codes[1].IsBlurred);
        Assert.NotSame(tab.Codes[1].Image, tab.Codes[1].ShownImage);
        Assert.Same(tab.Codes[0].Image, tab.Codes[0].ShownImage);
        Assert.True(tab.Codes[0].HidesPointer);
        Assert.False(tab.Codes[1].HidesPointer);
        tab.MoveScanFocus(-1);
        Assert.Same(tab.Codes[2], tab.ScanFocus);
        vm.ScanMode = false;
        Assert.All(tab.Codes, c => Assert.False(c.IsBlurred));
        Assert.All(tab.Codes, c => Assert.Same(c.Image, c.ShownImage));
    }

    [Fact]
    public void Scanner_read_is_compared_with_the_focused_code()
    {
        var vm = Window("PAL-1", "PAL-2");
        vm.ScanMode = true;
        vm.LastRead = "PAL-1";
        Assert.True(vm.LastReadMatches);
        Assert.Equal(Loc.ReadMatches, vm.LastReadStatus);
        vm.SelectedTab!.MoveScanFocus(1);
        Assert.False(vm.LastReadMatches);
    }

    [Fact]
    public void Composer_adds_codes_and_edits_the_selected_one()
    {
        var vm = Window();
        var tab = vm.SelectedTab!;
        Assert.False(tab.AddFromComposer());
        tab.ComposerText = "LOT-1";
        tab.ComposerIsCode128 = true;
        Assert.True(tab.AddFromComposer());
        Assert.Equal(CodeType.Code128, tab.Codes[0].Type);
        Assert.Equal("", tab.ComposerText);
        Assert.True(tab.IsDirty);
        tab.SelectedCode = tab.Codes[0];
        Assert.True(tab.IsEditing);
        Assert.False(tab.NotEditing);
        tab.ComposerText = "LOT-2";
        Assert.Equal("LOT-2", tab.Codes[0].Text);
        Assert.True(tab.Codes[0].IsSelected);
    }

    [Fact]
    public void Move_puts_the_code_before_the_target_position()
    {
        var vm = Window("A", "B", "C", "D");
        var tab = vm.SelectedTab!;
        tab.Move(0, 3);
        Assert.Equal(["B", "C", "A", "D"], tab.Codes.Select(c => c.Text));
        tab.Move(3, 0);
        Assert.Equal(["D", "B", "C", "A"], tab.Codes.Select(c => c.Text));
    }

    [Fact]
    public void Rename_is_kept_only_when_different_from_the_default_name()
    {
        var vm = Window();
        var tab = vm.SelectedTab!;
        string original = tab.Name;
        tab.BeginRename();
        tab.RenameText = "  Quai 3  ";
        tab.CommitRename();
        Assert.Equal("Quai 3", tab.Name);
        Assert.Equal("Quai 3", tab.CustomName);
        tab.BeginRename();
        tab.RenameText = original;
        tab.CommitRename();
        Assert.Null(tab.CustomName);
    }

    [Fact]
    public void Tab_moves_to_another_window_and_is_redrawn_at_its_scale()
    {
        var a = Window("A");
        var b = new MainViewModel { DpiScale = 1 };
        b.NewTab();
        var tab = a.SelectedTab!;
        a.NewTab();
        a.Release(tab);
        b.Adopt(tab, 0);
        Assert.DoesNotContain(tab, a.Tabs);
        Assert.Same(tab, b.Tabs[0]);
        Assert.Same(tab, b.SelectedTab);
        Assert.Same(b, tab.Main);
        Assert.Equal(1, tab.Codes[0].Image!.Scale);
    }

    [Fact]
    public void Window_emptied_by_a_tear_out_has_no_selected_tab()
    {
        var a = Window("A");
        var tab = a.SelectedTab!;
        a.Release(tab);
        Assert.Empty(a.Tabs);
        Assert.Null(a.SelectedTab);
        Assert.False(a.HasTabs);
    }

    [Fact]
    public void Untitled_numbers_are_never_reused_across_windows()
    {
        var a = new MainViewModel();
        var b = new MainViewModel();
        Assert.NotEqual(a.NewTab().Name, b.NewTab().Name);
    }
}

/// <summary>Raccourcis clavier et lecture de la douchette : en mode scan, le clavier ne modifie rien.</summary>
public class KeyboardTests
{
    private const int Tab = 0x09, Enter = 0x0D, Esc = 0x1B, Space = 0x20, Back = 0x08, Left = 0x25, Right = 0x27,
        F6 = 0x75, W = 0x57, S = 0x53, G = 0x47, Comma = 0xBC, OpenBracket = 0xDB, CloseBracket = 0xDD;

    private static KeyResult Route(int key, bool ctrl = false, bool alt = false, bool shift = false, bool scan = false, bool full = false) =>
        KeyRouting.Route(key, ctrl, alt, shift, scan, full);

    [Fact]
    public void Shortcuts_outside_scan_mode()
    {
        Assert.Equal(KeyAction.CloseTab, Route(W, ctrl: true).Action);
        Assert.Equal(KeyAction.Save, Route(S, ctrl: true).Action);
        Assert.Equal(KeyAction.SaveAs, Route(S, ctrl: true, shift: true).Action);
        Assert.Equal(KeyAction.PreviousTab, Route(Tab, ctrl: true, shift: true).Action);
        Assert.Equal(KeyAction.ToggleSettings, Route(Comma, ctrl: true).Action);
        Assert.Equal(KeyAction.ToggleScan, Route(F6).Action);
        Assert.Equal(KeyAction.None, Route(Enter).Action);
    }

    [Fact]
    public void AltGr_is_not_a_shortcut()
    {
        // AltGr arrive comme Ctrl+Alt : c'est un caractère à laisser passer.
        Assert.Equal(KeyAction.None, Route(W, ctrl: true, alt: true).Action);
        Assert.Equal(KeyAction.None, Route(W, ctrl: true, alt: true, scan: true).Action);
    }

    [Fact]
    public void Scan_mode_never_triggers_editing_shortcuts()
    {
        Assert.Equal(new KeyResult(KeyAction.ControlCharacter, '\x17'), Route(W, ctrl: true, scan: true));
        Assert.Equal(new KeyResult(KeyAction.ControlCharacter, '\x1D'), Route(CloseBracket, ctrl: true, scan: true)); // GS du GS1-128
        Assert.Equal(new KeyResult(KeyAction.ControlCharacter, '\x1B'), Route(OpenBracket, ctrl: true, scan: true));
        Assert.Equal(KeyAction.Swallow, Route(Comma, ctrl: true, scan: true).Action);
        Assert.Equal(KeyAction.NextTab, Route(Tab, ctrl: true, scan: true).Action);
        Assert.Equal(KeyAction.FinishRead, Route(Enter, scan: true).Action);
        Assert.Equal(KeyAction.FinishRead, Route(Tab, scan: true).Action);
        Assert.Equal(KeyAction.Backspace, Route(Back, scan: true).Action);
        Assert.Equal(KeyAction.Space, Route(Space, scan: true).Action);
        Assert.Equal(KeyAction.ScanNext, Route(Right, scan: true).Action);
        Assert.Equal(KeyAction.ExitScan, Route(Esc, scan: true).Action);
        Assert.Equal(KeyAction.ToggleScan, Route(F6, scan: true).Action);
        Assert.Equal(KeyAction.None, Route(G, scan: true).Action); // le caractère arrive par la saisie de texte
    }

    [Fact]
    public void Full_screen_takes_every_key()
    {
        Assert.Equal(KeyAction.CloseFull, Route(Esc, full: true).Action);
        Assert.Equal(KeyAction.FullPrevious, Route(Left, full: true, scan: true).Action);
        Assert.Equal(KeyAction.FullNext, Route(Right, full: true).Action);
        Assert.Equal(KeyAction.Swallow, Route(W, ctrl: true, full: true).Action);
    }

    [Fact]
    public void Scan_reader_collects_a_read_until_enter()
    {
        var reader = new ScanReader();
        reader.Type("PAL-0");
        reader.Type(" ");   // l'espace arrive par la touche, pas par la saisie de texte
        reader.Type("\r");  // Entrée et Tab arrivent par les touches
        reader.Type("\u0001");
        reader.AddControl('\x1D');
        reader.AddSpace();
        reader.Type("X");
        reader.Backspace();
        reader.Type("é");
        Assert.Equal("PAL-0\x1D é", reader.Finish());
        Assert.Null(reader.Finish());
    }
}

/// <summary>Réglages : ceux de la version 1 sont relus tels quels.</summary>
public class SettingsTests
{
    [Fact]
    public void Version_1_settings_are_read_back()
    {
        var s = AppSettings.Parse("""
            { "CodeSize": 200, "SideOpen": false, "SideWidth": 420, "Theme": "dark", "LastFolder": "C:\\Codes",
              "Left": 10, "Top": 20, "Width": 1240, "Height": 780, "Placement": [15, 25, 1875, 1195], "Maximized": true }
            """);
        Assert.Equal(200, s.CodeSize);
        Assert.False(s.SideOpen);
        Assert.Equal(420, s.SideWidth);
        Assert.Equal(ThemeChoice.Dark, ThemeSetting.Parse(s.Theme));
        Assert.Equal([15, 25, 1875, 1195], s.Placement!);
        Assert.True(s.Maximized);
        Assert.Equal(s.Placement, AppSettings.Parse(s.ToJson()).Placement!);
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_defaults()
    {
        var s = AppSettings.Parse("{ not json");
        Assert.Equal(MainViewModel.DefaultSize, s.CodeSize);
        Assert.Equal(ThemeChoice.System, ThemeSetting.Parse(s.Theme));
    }
}

/// <summary>
/// Icône de l'exe et des fenêtres : Windows prend dans le .ico l'image de la taille exacte de chaque échelle
/// (16 px à 100 %, 20 px à 125 %…) ; dessinée pour cette taille, elle reste nette.
/// </summary>
public class IconTests
{
    private static byte[] Ico()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "BarcodeBoard", "Assets", "BarcodeBoard.ico");
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        throw new FileNotFoundException("BarcodeBoard.ico introuvable");
    }

    /// <summary>Tailles des images du .ico (en-tête ICONDIR puis une entrée de 16 octets par image ; 0 = 256).</summary>
    private static IEnumerable<int> FrameSizes(byte[] ico)
    {
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (int i = 0; i < count; i++) yield return ico[6 + 16 * i] is 0 ? 256 : ico[6 + 16 * i];
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(256)]
    public void Has_a_frame_drawn_for_the_size(int size) => Assert.Contains(size, FrameSizes(Ico()));
}
