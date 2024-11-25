using MultiFunPlayer.Plugin;
using System.Windows;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal class PluginDialog(PluginBase pluginInstance)
{
    public PluginBase PluginInstance { get; } = pluginInstance;
    public UIElement View => PluginInstance.View;

    public override bool Equals(object obj) => obj switch
    {
        PluginBase pluginInstance => EqualityComparer<PluginBase>.Default.Equals(PluginInstance, pluginInstance),
        PluginDialog dialog => EqualityComparer<PluginBase>.Default.Equals(PluginInstance, dialog.PluginInstance),
        _ => false
    };

    public override int GetHashCode() => PluginInstance.GetHashCode();
}
