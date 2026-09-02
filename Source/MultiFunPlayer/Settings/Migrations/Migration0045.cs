using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0045 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        if (settings["Devices"] is not JArray devices) return;

        foreach (var device in devices.OfType<JObject>())
        {
            if (device["Axes"] is not JArray axes) continue;
            AddAxisIfMissing(axes, "Lnip", "UFO Left");
            AddAxisIfMissing(axes, "Rnip", "UFO Right");
        }
    }

    private static void AddAxisIfMissing(JArray axes, string name, string friendlyName)
    {
        if (axes.OfType<JObject>().Any(axis => string.Equals((string)axis["Name"], name, StringComparison.OrdinalIgnoreCase))) return;
        axes.Add(JObject.FromObject(new DeviceAxisSettings
        {
            Name = name,
            FriendlyName = friendlyName,
            FunscriptNames = new([name]),
            Enabled = true,
            DefaultValue = 0.5
        }));
    }
}
