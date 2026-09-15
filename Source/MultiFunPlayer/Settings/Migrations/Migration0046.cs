using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

/// <summary>Enables the V0 (Vibrate) axis on every device profile so the Galaku output can use it.</summary>
internal sealed class Migration0046 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        if (settings["Devices"] is not JArray devices) return;

        foreach (var device in devices.OfType<JObject>())
        {
            if (device["Axes"] is not JArray axes) continue;
            var v0 = axes.OfType<JObject>().FirstOrDefault(axis => string.Equals((string)axis["Name"], "V0", StringComparison.OrdinalIgnoreCase));
            if (v0 != null)
            {
                v0["Enabled"] = true;
                continue;
            }

            axes.Add(JObject.FromObject(new DeviceAxisSettings
            {
                Name = "V0",
                FriendlyName = "Vibrate",
                FunscriptNames = new(["vib", "V0"]),
                Enabled = true,
                DefaultValue = 0
            }));
        }
    }
}
