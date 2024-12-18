using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Converters;

[GlobalJsonConverter]
internal sealed class ShortcutActionConfigurationConverter(IShortcutManager manager) : JsonConverter<IShortcutActionConfiguration>
{
    public override IShortcutActionConfiguration ReadJson(JsonReader reader, Type objectType, IShortcutActionConfiguration existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader) as JObject;
        var actionName = o[nameof(IShortcutActionConfiguration.Name)].ToString();
        var settings = o[nameof(IShortcutActionConfiguration.Settings)].ToObject<List<TypedValue>>();

        return manager.CreateShortcutActionConfigurationInstance(actionName, settings);
    }

    public override void WriteJson(JsonWriter writer, IShortcutActionConfiguration value, JsonSerializer serializer)
    {
        var o = new JObject
        {
            [nameof(IShortcutActionConfiguration.Name)] = value.Name,
            [nameof(IShortcutActionConfiguration.Settings)] = JArray.FromObject(value.Settings.Select(s => new TypedValue(s.Type, s.Value)))
        };

        serializer.Serialize(writer, o);
    }
}
