using PropertyChanged;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.Plugin;

/// <summary>
/// Interaction logic for PluginViewBase.xaml
/// </summary>
[DoNotNotify]
public partial class PluginViewBase : UserControl
{
    public static readonly DependencyProperty ToolBarContentProperty =
        DependencyProperty.Register(nameof(ToolBarContent), typeof(object),
                typeof(PluginViewBase), new FrameworkPropertyMetadata(null));

    public object ToolBarContent
    {
        get => GetValue(ToolBarContentProperty);
        set => SetValue(ToolBarContentProperty, value);
    }

    public static readonly DependencyProperty StatusForegroundProperty =
        DependencyProperty.Register(nameof(StatusForeground), typeof(Brush),
            typeof(PluginViewBase), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush));

    public Brush StatusForeground
    {
        get => (Brush)GetValue(StatusForegroundProperty);
        set => SetValue(StatusForegroundProperty, value);
    }

    public static readonly DependencyProperty StatusContentProperty =
        DependencyProperty.Register(nameof(StatusContent), typeof(object),
            typeof(PluginViewBase), new FrameworkPropertyMetadata(null));

    public object StatusContent
    {
        get => GetValue(StatusContentProperty);
        set => SetValue(StatusContentProperty, value);
    }

    public PluginViewBase()
    {
        InitializeComponent();
    }
}
