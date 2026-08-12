using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0044 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertiesByPath(settings, "$.Script.AxisSettings.*.InvertValue", "InvertScript");

        EditPropertiesByPath(settings, "$.Shortcut.Shortcuts[*].Actions[?(@.Descriptor =~ /Axis::InvertValue::.*/i)].Descriptor",
            v => Regex.Replace(v.ToString(), "^Axis::InvertValue::", "Axis::InvertScript::"));
    }
}