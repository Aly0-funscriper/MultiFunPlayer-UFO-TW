using System.Windows;

namespace MultiFunPlayer.UI.Behaviours;

internal static class ExpanderAssist
{
    public static readonly DependencyProperty ExpanderButtonIsEnabledProperty =
        DependencyProperty.RegisterAttached("ExpanderButtonIsEnabled",
            typeof(bool), typeof(ExpanderAssist),
                new PropertyMetadata(true));

    public static bool GetExpanderButtonIsEnabled(DependencyObject dp)
        => (bool)dp.GetValue(ExpanderButtonIsEnabledProperty);

    public static void SetExpanderButtonIsEnabled(DependencyObject dp, bool value)
        => dp.SetValue(ExpanderButtonIsEnabledProperty, value);

    public static readonly DependencyProperty ExpanderButtonVisibilityProperty =
        DependencyProperty.RegisterAttached("ExpanderButtonVisibility",
            typeof(Visibility), typeof(ExpanderAssist),
                new PropertyMetadata(Visibility.Visible));

    public static Visibility GetExpanderButtonVisibility(DependencyObject dp)
        => (Visibility)dp.GetValue(ExpanderButtonIsEnabledProperty);

    public static void SetExpanderButtonVisibility(DependencyObject dp, Visibility value)
        => dp.SetValue(ExpanderButtonIsEnabledProperty, value);
}
