using System.Windows.Controls;

namespace MultiFunPlayer.Plugin;

/// <summary>
/// Interaction logic for PluginViewBase.xaml
/// </summary>
public partial class PluginViewBase : UserControl
{
    public object ToolBarContent { get; set; }

    public PluginViewBase()
    {
        InitializeComponent();
    }
}
