using System.Windows;

namespace BarcodeBoard;

/// <summary>
/// Arrondi des boutons de la barre d'outils, réglable bouton par bouton. Sert au bouton divisé
/// Enregistrer : partie gauche arrondie à gauche, flèche arrondie à droite, les deux jointives.
/// </summary>
public static class ButtonShape
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(ButtonShape), new FrameworkPropertyMetadata(new CornerRadius(5)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
}
