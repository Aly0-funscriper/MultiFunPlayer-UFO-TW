using MultiFunPlayer.OutputTarget.ViewModels;
using MultiFunPlayer.Settings.Migrations;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Tests;

public sealed class UfoTwOutputTargetTests
{
    [Theory]
    [InlineData("UFO-TW", true)]
    [InlineData("ufo.tw", true)]
    [InlineData("UFO TW", true)]
    [InlineData("BLE 3C8427B5F6EA", false)]
    [InlineData("LYWSD03MMC", false)]
    [InlineData("UFO speaker", false)]
    public void BleNameFilterOnlyAcceptsExactUfoTwNames(string name, bool expected)
        => Assert.Equal(expected, UfoBleConnection.IsExactUfoName(name));

    [Fact]
    public void CompatibilityEncodingUsesCenterAsStopAndOppositeDirectionBits()
    {
        Assert.Equal(0, UfoEncoding.Compatibility(0.5));
        Assert.True((UfoEncoding.Compatibility(0.0) & 0x80) != 0);
        Assert.True((UfoEncoding.Compatibility(1.0) & 0x80) == 0);
        Assert.Equal(127, UfoEncoding.Compatibility(1.0));
    }

    [Fact]
    public void GenuineEncodingUsesExpectedCenterAndDirectionRanges()
    {
        Assert.Equal(128, UfoEncoding.Genuine(0.5));
        Assert.InRange(UfoEncoding.Genuine(0.0), 0, 99);
        Assert.InRange(UfoEncoding.Genuine(1.0), 128, 227);
    }

    [Fact]
    public void GenuineProtocolButtonOverridesAmbiguousCompatibilityService()
        => Assert.Equal(UfoBleProtocol.Genuine,
            UfoBleConnection.ResolveProtocol(UfoBleProtocol.Compatibility, useGenuineProtocol: true));

    [Fact]
    public void MigrationAddsUfoAxesToExistingCustomProfiles()
    {
        var settings = JObject.Parse("""{"Devices":[{"Name":"Custom","Axes":[{"Name":"L0"}]}]}""");
        new Migration0045().Migrate(settings);

        var names = settings["Devices"]![0]!["Axes"]!.Select(axis => (string)axis["Name"]!).ToArray();
        Assert.Contains("Lnip", names);
        Assert.Contains("Rnip", names);
        Assert.Equal(45, (int)settings["ConfigVersion"]!);
    }
}
