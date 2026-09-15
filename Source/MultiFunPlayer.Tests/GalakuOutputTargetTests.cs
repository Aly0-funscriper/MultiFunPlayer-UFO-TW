using MultiFunPlayer.OutputTarget.ViewModels;

namespace MultiFunPlayer.Tests;

public sealed class GalakuOutputTargetTests
{
    // Expected bytes taken from buttplug-rs tests (test_galaku.yaml).
    [Fact]
    public void SpeedZeroMatchesButtplugVector()
        => Assert.Equal(new byte[] { 0x23, 0x81, 0xBB, 0xAB, 0xD2, 0xEC, 0x3B, 0x23, 0xBB, 0xA3, 0x3B, 0x90 }, GalakuProtocol.SpeedCommand(0));

    [Fact]
    public void SpeedFullMatchesButtplugVector()
        => Assert.Equal(new byte[] { 0x23, 0x81, 0xBB, 0xAB, 0xD2, 0xEC, 0x57, 0x23, 0xBB, 0xA3, 0x3B, 0x44 }, GalakuProtocol.SpeedCommand(100));

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 50)]
    [InlineData(1.0, 100)]
    [InlineData(1.7, 100)]
    [InlineData(-1.0, 0)]
    [InlineData(double.NaN, 0)]
    public void ValueToSpeedIsClamped(double value, int expected)
        => Assert.Equal(expected, GalakuProtocol.ToSpeed(value));

    [Theory]
    [InlineData("K118", true)]
    [InlineData(" k118 ", true)]
    [InlineData("GX21", true)]
    [InlineData("UFO-TW", false)]
    [InlineData("", false)]
    public void ScanFilterAcceptsGalakuNames(string name, bool expected)
        => Assert.Equal(expected, GalakuProtocol.IsKnownName(name));

    [Fact]
    public void BallVibratorHasFriendlyName()
        => Assert.Contains("Ball vibrator", GalakuProtocol.FriendlyName("K118"));
}

public sealed class GalakuMigrationTests
{
    [Fact]
    public void MigrationEnablesExistingV0AndAddsMissingV0()
    {
        var settings = Newtonsoft.Json.Linq.JObject.Parse("""
            {"Devices":[
              {"Name":"TCode-0.3","Axes":[{"Name":"L0","Enabled":true},{"Name":"V0","Enabled":false}]},
              {"Name":"Custom","Axes":[{"Name":"L0","Enabled":true}]}
            ]}
            """);
        new MultiFunPlayer.Settings.Migrations.Migration0046().Migrate(settings);

        foreach (var device in settings["Devices"]!)
        {
            var v0 = device["Axes"]!.Single(axis => (string)axis["Name"]! == "V0");
            Assert.True((bool)v0["Enabled"]!);
        }
        Assert.Equal(46, (int)settings["ConfigVersion"]!);
    }
}
