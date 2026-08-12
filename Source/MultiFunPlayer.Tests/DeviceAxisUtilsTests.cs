using MultiFunPlayer.Common;

namespace MultiFunPlayer.Tests;

public class DeviceAxisUtilsTests
{
    static DeviceAxisUtilsTests()
    {
        DeviceAxis.InitializeFromDevice(new()
        {
            IsDefault = false,
            OutputPrecision = 4,
            Name = "test-device",
            Axes = [
                new() { Name = "L0", FriendlyName = "Up/Down", FunscriptNames = ["raw", "*", "stroke", "L0", "up"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "L1", FriendlyName = "Forward/Backward", FunscriptNames = ["surge", "L1", "forward"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "L2", FriendlyName = "Left/Right", FunscriptNames = ["sway", "L2", "left"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R0", FriendlyName = "Twist", FunscriptNames = ["twist", "R0", "yaw"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R1", FriendlyName = "Roll", FunscriptNames = ["roll", "R1"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R2", FriendlyName = "Pitch", FunscriptNames = ["pitch", "R2"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "Lnip", FriendlyName = "Left nipple", FunscriptNames = ["Lnip"], Enabled = true, DefaultValue = 0.5, },
                new() { Name = "Rnip", FriendlyName = "Right nipple", FunscriptNames = ["Rnip"], Enabled = true, DefaultValue = 0.5, },
            ]
        });
    }

    public static IEnumerable<object[]> ScriptNameAndExpectedAxes => [
        ["name.funscript", new[] { DeviceAxis.Parse("L0") }],
        ["name.pitch.funscript", new[] { DeviceAxis.Parse("R2") }],
        ["name.Lnip.funscript", new[] { DeviceAxis.Parse("Lnip") }],
        ["name.Rnip.funscript", new[] { DeviceAxis.Parse("Rnip") }],
        ["name.unknown.funscript", new[] { DeviceAxis.Parse("L0") }],
    ];

    [Theory]
    [MemberData(nameof(ScriptNameAndExpectedAxes))]
    public void FindAxesMatchingOnlyScriptNameHasExpectedOutput(string scriptName, DeviceAxis[] expectedAxes)
    {
        var foundAxes = DeviceAxisUtils.FindAxesMatchingName(scriptName);
        Assert.Equal(expectedAxes, foundAxes);
    }

    public static IEnumerable<object[]> ScriptNameMediaNameAndExpectedAxes => [
        ["name.funscript", "name.mp4", new[] { DeviceAxis.Parse("L0") }],
        ["name.funscript", "other.mp4", Array.Empty<DeviceAxis>()],
        ["name.pitch.funscript", "name.mp4", new[] { DeviceAxis.Parse("R2") }],
        ["name.pitch.funscript", "other.mp4", Array.Empty<DeviceAxis>()],
        ["name.unknown.funscript", "name.mp4", Array.Empty<DeviceAxis>()],
    ];

    [Theory]
    [MemberData(nameof(ScriptNameMediaNameAndExpectedAxes))]
    public void FindAxesMatchingScriptNameAndMediaNameHasExpectedOutput(string scriptName, string mediaName, DeviceAxis[] expectedAxes)
    {
        var foundAxes = DeviceAxisUtils.FindAxesMatchingName(scriptName, mediaName);
        Assert.Equal(expectedAxes, foundAxes);
    }

    public static IEnumerable<object[]> FileNameAndExpectedBaseName => [
        ["name.funscript", "name.funscript"],
        ["name.pitch.funscript", "name.funscript"],
        ["name.unknown.funscript", "name.unknown.funscript"]
    ];

    [Theory]
    [MemberData(nameof(FileNameAndExpectedBaseName))]
    public void GetBaseNameWithExtensionHasExpectedOutput(string fileName, string expectedBaseName)
    {
        var baseName = DeviceAxisUtils.GetBaseNameWithExtension(fileName);
        Assert.Equal(expectedBaseName, baseName);
    }

    [Fact]
    public void CustomNippleAxesAreAcceptedButNotSerializedAsTCode()
    {
        var left = DeviceAxis.Parse("lnip");
        var right = DeviceAxis.Parse("Rnip");

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.False(left.IsTCodeAxis);
        Assert.False(right.IsTCodeAxis);
        Assert.Equal(string.Empty, DeviceAxis.ToString(left, 0.5));
        Assert.Equal(string.Empty, DeviceAxis.ToString(left, 0.5, 50));

        var output = DeviceAxis.ToString([
            new KeyValuePair<DeviceAxis, double>(left, 0.5),
            new KeyValuePair<DeviceAxis, double>(DeviceAxis.Parse("L0"), 0.5)
        ]);

        Assert.DoesNotContain("Lnip", output);
        Assert.Contains("L0", output);
    }

    public static IEnumerable<object[]> DeviceAxisScriptNamesMediaNameAndExpectedNames => [
        [DeviceAxis.Parse("L0"), new[] { "name.funscript", "name.raw.funscript", "name.unknown.funscript" }, "name.mp4", new[] { "name.raw.funscript", "name.funscript" }],
        [DeviceAxis.Parse("L0"), new[] { "name.funscript", }, "other.mp4", Array.Empty<string>()],
        [DeviceAxis.Parse("R2"), new[] { "name.pitch.funscript", "other.pitch.funscript" }, "name.mp4", new[] { "name.pitch.funscript" }],
        [DeviceAxis.Parse("L0"), new[] { "name.unknown.funscript" }, "name.mp4", Array.Empty<string>()],
    ];

    [Theory]
    [MemberData(nameof(DeviceAxisScriptNamesMediaNameAndExpectedNames))]
    public void FindNamesMatchingAxisHasExpectedOutput(DeviceAxis axis, IEnumerable<string> scriptNames, string mediaName, IEnumerable<string> expectedNames)
    {
        var matchingNames = DeviceAxisUtils.FindNamesMatchingAxis(axis, scriptNames, mediaName);
        Assert.Equal(expectedNames, matchingNames);

        var items = scriptNames.Select(n => new FindNamesMatchingAxisItem(n));
        var matchingItems = DeviceAxisUtils.FindNamesMatchingAxis(axis, items, x => x.Name, mediaName);
        Assert.Equal(expectedNames.Select(n => new FindNamesMatchingAxisItem(n)), matchingItems);
    }

    private record FindNamesMatchingAxisItem(string Name);
}
