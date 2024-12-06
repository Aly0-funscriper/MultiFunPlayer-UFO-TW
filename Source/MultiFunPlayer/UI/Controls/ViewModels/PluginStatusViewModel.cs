using MultiFunPlayer.Plugin;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class PluginStatusViewModel(IPluginManager pluginManager) : Screen
{
    public IReadOnlyObservableCollection<PluginContainer> Containers => pluginManager.Containers;
}
