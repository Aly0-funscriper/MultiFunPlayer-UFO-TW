using MultiFunPlayer.OutputTarget.ViewModels;

namespace MultiFunPlayer.Tests;

public sealed class UfoTwBleOutputTargetTests
{
    [Theory]
    [InlineData(0, 255)]
    [InlineData(0.49, 131)]
    [InlineData(0.5, 0)]
    [InlineData(0.51, 3)]
    [InlineData(1, 127)]
    public void EncodeMotorValueUsesDirectionBitAndMagnitude(double value, byte expected)
        => Assert.Equal(expected, UfoTwBleOutputTarget.EncodeMotorValue(value));

    [Fact]
    public void EncodePacketUsesFirmwareByteOrder()
        => Assert.Equal([0, 127, 255], UfoTwBleOutputTarget.EncodePacket(1, 0));

    [Fact]
    public void EncodeOriginalPacketUsesUfoTwProtocol()
        => Assert.Equal([5, 0xe3, 0x63], UfoTwBleOutputTarget.EncodeOriginalPacket(1, 0));

    [Fact]
    public void EncodeOriginalPacketUsesCenteredStopByte()
        => Assert.Equal([5, 0x80, 0x80], UfoTwBleOutputTarget.EncodeOriginalPacket(0.5, 0.5));

    [Fact]
    public void LegacyOriginalProtocolUsesTheSameUfoTwPacket()
        => Assert.Equal([5, 0xe3, 0x63], UfoTwBleOutputTarget.EncodeLegacyOriginalPacket(1, 0));

    [Fact]
    public void GenuineModeOverridesSharedCompatibilityUuid()
        => Assert.Equal(
            UfoTwProtocol.OriginalUfoTw,
            UfoTwBleOutputTarget.ResolveProtocol(
                UfoTwProtocol.CompatibilityFirmware,
                true));

    [Fact]
    public void CompatibilityModeKeepsFirmwarePacketFormat()
        => Assert.Equal(
            UfoTwProtocol.CompatibilityFirmware,
            UfoTwBleOutputTarget.ResolveProtocol(
                UfoTwProtocol.CompatibilityFirmware,
                false));

    [Theory]
    [InlineData(0.5, 0.5, "UFO,0,0\n")]
    [InlineData(1, 0, "UFO,127,255\n")]
    [InlineData(0.49, 0.51, "UFO,131,3\n")]
    public void UsbSerialCommandUsesSafeAsciiFraming(double left, double right, string expected)
        => Assert.Equal(expected, UfoTwUsbSerialOutputTarget.EncodeSerialCommand(left, right));
}
