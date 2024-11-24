using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.Plugin;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public abstract class PluginSettingsBase : PropertyChangedBase
{
    public virtual UIElement CreateView() => null;

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
            settings.MergeAll(JObject.FromObject(this));
        else if (action == SettingsAction.Loading)
            settings.Populate(this);
    }
}