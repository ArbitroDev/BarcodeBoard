using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace BarcodeBoard;

public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>« Enregistrer les modifications de X ? » avec Enregistrer / Ne pas enregistrer / Annuler.</summary>
public sealed class UnsavedDialog : Window
{
    private UnsavedChoice _choice = UnsavedChoice.Cancel;

    private UnsavedDialog(string tabName)
    {
        Title = Loc.AppName;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        // Une fenêtre à part ne reprend pas les couleurs du thème : on les lie explicitement,
        // et la barre de titre système passe en sombre avec le thème.
        SetResourceReference(BackgroundProperty, "BodyBg");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => Backdrop.SetDark(this, Theme.IsDark);

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MaxWidth = 460 };
        root.Children.Add(new TextBlock
        {
            Text = Loc.UnsavedQuestion(tabName),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock
        {
            Text = Loc.UnsavedExplanation,
            Margin = new Thickness(0, 8, 0, 20),
            TextWrapping = TextWrapping.Wrap,
        });
        ((TextBlock)root.Children[1]).SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(MakeButton(Loc.Save, UnsavedChoice.Save, primary: true, isDefault: true));
        buttons.Children.Add(MakeButton(Loc.DontSave, UnsavedChoice.Discard, primary: false));
        var cancel = MakeButton(Loc.Cancel, UnsavedChoice.Cancel, primary: false);
        cancel.IsCancel = true;
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
    }

    private Button MakeButton(string label, UnsavedChoice choice, bool primary, bool isDefault = false)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2), VerticalAlignment = VerticalAlignment.Center },
            Style = (Style)Application.Current.Resources[primary ? "PrimaryButton" : "SecondaryButton"],
            FontSize = 13,
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
        };
        b.Click += (_, _) => { _choice = choice; DialogResult = choice != UnsavedChoice.Cancel; };
        return b;
    }

    public static UnsavedChoice Ask(Window owner, string tabName)
    {
        var dialog = new UnsavedDialog(tabName) { Owner = owner };
        dialog.ShowDialog();
        return dialog._choice;
    }
}
