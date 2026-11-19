using System.Windows;
using Microsoft.Win32;

namespace BarcodeBoard;

public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// Thème clair ou sombre. Par défaut on suit Windows (Paramètres &gt; Personnalisation &gt; Couleurs),
/// y compris quand l'utilisateur le change pendant que l'application est ouverte.
/// </summary>
internal static class Theme
{
    private static bool _listening;

    public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;
    public static bool IsDark { get; private set; }

    /// <summary>Déclenché après chaque changement effectif de palette.</summary>
    public static event Action? Changed;

    public static ThemeChoice Parse(string? value) => value switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    public static string ToSetting(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        _ => "system",
    };

    public static void Apply(ThemeChoice choice)
    {
        Choice = choice;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }

        bool dark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && WindowsUsesDarkApps());
        if (!_applied || dark != IsDark)
        {
            // App.xaml fusionne la palette claire en premier dictionnaire : on la remplace à chaud.
            var merged = Application.Current.Resources.MergedDictionaries;
            var palette = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/BarcodeBoard;component/Themes/{(dark ? "Dark" : "Light")}.xaml"),
            };
            if (merged.Count == 0) merged.Add(palette); else merged[0] = palette;
            _applied = true;
        }
        IsDark = dark;
        Changed?.Invoke();
    }

    private static bool _applied;

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Choice != ThemeChoice.System) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
    }
}
