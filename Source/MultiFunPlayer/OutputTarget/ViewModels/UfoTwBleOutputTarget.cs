using MultiFunPlayer.Common;
using MultiFunPlayer.Property;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("UFO-TW BLE (PauseStop v2)")]
internal sealed class UfoTwBleOutputTarget : AsyncAbstractOutputTarget, IHandle<MediaPlayingChangedMessage>
{
    internal static readonly Guid ServiceUuid = Guid.Parse("40ee0200-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid OriginalServiceUuid = Guid.Parse("40ee2222-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid LegacyOriginalServiceUuid = Guid.Parse("40ee1111-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid CharacteristicUuid = Guid.Parse("40ee0202-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid LegacyOriginalCharacteristicUuid = Guid.Parse("40ee2222-63ec-4b7f-8ce7-712efd55b90e");

    private const string DefaultDeviceName = "UFO-TW";
    private const byte CommandPrefix = 0;
    private const byte OriginalCommandPrefix = 5;
    private static readonly TimeSpan BleOperationTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _packetWriteGate = new(1, 1);
    private GattCharacteristic _activeCharacteristic;
    private int _activeProtocol = (int)UfoTwProtocol.Unknown;
    private int _isMediaPlaying = 1;
    private int _forceNextPacket;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public string SelectedDeviceId { get; private set; }
    public UfoTwBleDevice SelectedDevice { get; set; }
    public ObservableCollection<UfoTwBleDevice> AvailableDevices { get; } = [];
    [JsonProperty] public bool IsGenuineUfoTw { get; set; } = true;

    public IReadOnlyList<UfoTwAxisControl> UfoAxisControls { get; }

    public UfoTwBleOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        foreach (var axis in DeviceAxis.All.Where(IsUfoAxis))
            AxisSettings[axis].Enabled = true;

        UfoAxisControls = DeviceAxis.All
            .Where(IsUfoAxis)
            .Select(axis => new UfoTwAxisControl(axis, AxisSettings[axis]))
            .ToArray();
    }

    public void Handle(MediaPlayingChangedMessage message)
    {
        var wasPlaying = Interlocked.Exchange(ref _isMediaPlaying, message.IsPlaying ? 1 : 0);

        if (!message.IsPlaying && wasPlaying != 0)
        {
            // The pause packet is sent once per playing -> paused transition.
            // The next playing update must be sent even when its value happens
            // to equal the last packet from before the pause.
            Interlocked.Exchange(ref _forceNextPacket, 1);
            Logger.Info("Media source reported pause; sending one UFO-TW stop command");
            _ = SendCenterOnPauseAsync();
        }
        else if (message.IsPlaying && wasPlaying == 0)
        {
            Interlocked.Exchange(ref _forceNextPacket, 1);
            Logger.Info("Media source reported playback resume; the current Lnip/Rnip command will be restored");
        }
    }

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new AsyncFixedUpdateContext()
        {
            UpdateInterval = 100,
            MinimumUpdateInterval = 50,
            MaximumUpdateInterval = 250,
        },
        _ => null,
    };

    public async Task OnRefreshDevices()
    {
        if (Status is ConnectionStatus.Connected or ConnectionStatus.Disconnecting)
            return;

        try
        {
            var matching = await ScanBluetoothLeDevicesAsync();
            var ordered = matching
                .OrderByDescending(IsUfoTwDevice)
                .ThenByDescending(x => x.SignalStrength)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ReplaceAvailableDevices(ordered);

            var selectedId = SelectedDevice?.Id ?? SelectedDeviceId;
            SelectedDevice = AvailableDevices.FirstOrDefault(x => string.Equals(x.Id, selectedId, StringComparison.Ordinal))
                ?? AvailableDevices.FirstOrDefault();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to enumerate UFO-TW BLE devices");
            _ = DialogHelper.ShowErrorAsync(e, "Failed to scan for UFO-TW BLE devices", "RootDialog");
        }
    }

    private static async Task<IReadOnlyList<UfoTwBleDevice>> ScanBluetoothLeDevicesAsync()
    {
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        if (watcher == null)
            throw new OutputTargetException("Windows could not create a BLE device watcher; check that Bluetooth is enabled");

        var devices = new ConcurrentDictionary<ulong, UfoTwBleDevice>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> onReceived = (_, args) =>
        {
            if (args == null || args.BluetoothAddress == 0)
                return;

            var name = args.Advertisement?.LocalName;
            var advertisedProtocol = GetAdvertisedProtocol(args.Advertisement);
            if (!IsLikelyUfoName(name) && advertisedProtocol == UfoTwProtocol.Unknown)
                return;

            if (string.IsNullOrWhiteSpace(name))
                name = $"BLE {args.BluetoothAddress:X12}";

            devices.AddOrUpdate(
                args.BluetoothAddress,
                _ => new UfoTwBleDevice(args.BluetoothAddress, name)
                {
                    Protocol = advertisedProtocol,
                    SignalStrength = args.RawSignalStrengthInDBm,
                },
                (_, existing) =>
                {
                    if (!name.StartsWith("BLE ", StringComparison.OrdinalIgnoreCase))
                        existing.Name = name;
                    if (advertisedProtocol != UfoTwProtocol.Unknown)
                        existing.Protocol = advertisedProtocol;
                    existing.SignalStrength = args.RawSignalStrengthInDBm;
                    return existing;
                });
        };
        TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementWatcherStoppedEventArgs> onStopped = (_, _) => completed.TrySetResult(true);

        watcher.Received += onReceived;
        watcher.Stopped += onStopped;

        try
        {
            watcher.Start();
            await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            watcher.Stop();

            // Protocol selection is explicit because genuine devices and the
            // compatibility firmware may advertise the same UUIDs. Avoid
            // opening every nearby BLE device here: that caused long scans,
            // false positives and unstable connections on Windows.
            return devices.Values.ToList();
        }
        finally
        {
            watcher.Received -= onReceived;
            watcher.Stopped -= onStopped;
        }
    }

    private static UfoTwProtocol GetAdvertisedProtocol(BluetoothLEAdvertisement advertisement)
    {
        if (advertisement?.ServiceUuids == null)
            return UfoTwProtocol.Unknown;

        if (advertisement.ServiceUuids.Contains(LegacyOriginalServiceUuid))
            return UfoTwProtocol.LegacyOriginalUfoTw;
        if (advertisement.ServiceUuids.Contains(OriginalServiceUuid))
            return UfoTwProtocol.OriginalUfoTw;
        if (advertisement.ServiceUuids.Contains(ServiceUuid))
            return UfoTwProtocol.CompatibilityFirmware;

        return UfoTwProtocol.Unknown;
    }

    private static async Task AddWindowsBluetoothDevicesAsync(ConcurrentDictionary<ulong, UfoTwBleDevice> devices)
    {
        try
        {
            // Buttplug/Intiface keeps a Windows BLE device manager in the server.
            // This is the direct WinRT equivalent of consulting Windows' BLE
            // device enumeration in addition to listening for advertisements.
            var selector = BluetoothLEDevice.GetDeviceSelector();
            var deviceInformation = await AwaitWinRtAsync(
                DeviceInformation.FindAllAsync(selector, ["System.Devices.Aep.DeviceAddress"]),
                "enumerate Windows BLE devices",
                BleOperationTimeout,
                CancellationToken.None);
            foreach (var information in deviceInformation)
            {
                if (information == null || !IsLikelyUfoName(information.Name))
                    continue;

                BluetoothLEDevice device = null;
                try
                {
                    try
                    {
                        device = await AwaitWinRtAsync(
                            BluetoothLEDevice.FromIdAsync(information.Id),
                            "open cached Windows BLE device",
                            BleOperationTimeout,
                            CancellationToken.None);
                    }
                    catch
                    {
                        // The cached entry can still expose its address even
                        // when Windows refuses to open it at scan time.
                    }

                    var address = device?.BluetoothAddress ?? 0;
                    if (address == 0)
                        address = GetAddressFromDeviceInformation(information);
                    if (address == 0)
                        continue;

                    if (devices.TryGetValue(address, out var existing))
                    {
                        if (existing.Name.StartsWith("BLE ", StringComparison.OrdinalIgnoreCase))
                            existing.Name = information.Name;
                    }
                    else
                    {
                        devices.TryAdd(address, new UfoTwBleDevice(address, information.Name));
                    }
                }
                catch
                {
                    // A stale Windows cache entry is harmless; advertisement
                    // scanning and manual address selection remain available.
                }
                finally
                {
                    device?.Dispose();
                }
            }
        }
        catch
        {
            // Some Windows versions reject this selector when Bluetooth is off.
            // The active watcher above is still the primary discovery path.
        }
    }

    private static ulong GetAddressFromDeviceInformation(DeviceInformation information)
    {
        if (!information.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value) || value == null)
            return 0;

        if (value is ulong address)
            return address;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    public void OnSelectedDeviceChanged()
        => SelectedDeviceId = SelectedDevice?.Id;

    protected override async ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} over native BLE [Device: {1}, Type: {2}]", Identifier, SelectedDevice?.Name, connectionType);

        if (DeviceAxis.Parse("Lnip") == null || DeviceAxis.Parse("Rnip") == null)
            throw new OutputTargetException("MFP could not initialize the Lnip/Rnip axes; restart MFP once after installing this version");

        if (SelectedDevice == null || string.IsNullOrWhiteSpace(SelectedDevice.Id))
            await OnRefreshDevices();

        if (SelectedDevice == null)
            throw new OutputTargetException("No UFO-TW BLE device selected. Turn on the device and press Refresh");

        return true;
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        BluetoothLEDevice device = null;
        GattDeviceService service = null;
        GattCharacteristic characteristic = null;
        var protocol = UfoTwProtocol.Unknown;

        try
        {
            var detectedProtocol = UfoTwProtocol.Unknown;
            (device, service, detectedProtocol) = await ConnectToUfoTwAsync(SelectedDevice.BluetoothAddress, token);
            protocol = ResolveProtocol(detectedProtocol, IsGenuineUfoTw);
            Logger.Info("UFO-TW GATT service found [UUID: {0}]", service.Uuid);
            characteristic = await FindUfoTwCharacteristicAsync(service, protocol, token);
            SelectedDevice.Protocol = protocol;
            Logger.Info("UFO-TW control characteristic found [UUID: {0}, Properties: {1}, DetectedProtocol: {2}, GenuineProtocol: {3}, Protocol: {4}]",
                characteristic.Uuid, characteristic.CharacteristicProperties, detectedProtocol, IsGenuineUfoTw, protocol);
            Volatile.Write(ref _activeProtocol, (int)protocol);
            Volatile.Write(ref _activeCharacteristic, characteristic);
            await SendPacketAsync(characteristic, EncodePacket(0.5, 0.5, protocol), protocol, token);
            var lastPacket = EncodePacket(0.5, 0.5, protocol);

            Status = ConnectionStatus.Connected;
            Logger.Info("Connected to UFO-TW BLE device [Address: {0:X12}, Protocol: {1}]", SelectedDevice.BluetoothAddress, protocol);
            EventAggregator.Publish(new SyncRequestMessage());

            await FixedUpdateAsync(() => !token.IsCancellationRequested, async (_, elapsed) =>
            {
                // Paused means stopped for every media source. Do not let a
                // script update or a manual test overwrite the one-shot stop
                // command until the active media source reports playing again.
                if (Volatile.Read(ref _isMediaPlaying) == 0)
                    return;

                var leftAxis = DeviceAxis.Parse("Lnip");
                var rightAxis = DeviceAxis.Parse("Rnip");
                var packet = EncodePacket(
                    GetOutputValue(leftAxis),
                    GetOutputValue(rightAxis),
                    protocol);

                var forceWrite = Interlocked.Exchange(ref _forceNextPacket, 0) != 0;
                if (!forceWrite && packet.AsSpan().SequenceEqual(lastPacket))
                    return;

                Logger.Trace("Sending UFO-TW BLE packet [{0}, {1}, {2}] [Elapsed: {3}]", packet[0], packet[1], packet[2], elapsed);
                await SendPacketAsync(characteristic, packet, protocol, token);
                lastPacket = packet;
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to UFO-TW BLE device");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to UFO-TW BLE device", "RootDialog");
        }
        catch (Exception e)
        {
            Logger.Error(e, "{0} failed with exception", Identifier);
        }
        finally
        {
            if (characteristic != null)
            {
                try { await SendPacketAsync(characteristic, EncodePacket(0.5, 0.5, protocol), protocol, CancellationToken.None); }
                catch (Exception e) { Logger.Warn(e, "Failed to send UFO-TW stop packet"); }
            }

            Volatile.Write(ref _activeCharacteristic, null);
            Volatile.Write(ref _activeProtocol, (int)UfoTwProtocol.Unknown);
            characteristic = null;
            service = null;
            device?.Dispose();
        }
    }

    private async Task SendCenterOnPauseAsync()
    {
        var characteristic = Volatile.Read(ref _activeCharacteristic);
        if (characteristic == null)
            return;

        var protocol = (UfoTwProtocol)Volatile.Read(ref _activeProtocol);
        try
        {
            var packet = EncodePacket(0.5, 0.5, protocol);
            Logger.Info("Video paused; resetting UFO-TW to center [Packet: {0}, {1}, {2}]",
                packet[0], packet[1], packet[2]);
            await SendPacketAsync(characteristic, packet, protocol, CancellationToken.None);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Failed to reset UFO-TW after video pause");
        }
    }

    private async Task SendPacketAsync(
        GattCharacteristic characteristic,
        byte[] packet,
        UfoTwProtocol protocol,
        CancellationToken token)
    {
        await _packetWriteGate.WaitAsync(token);
        try
        {
            Exception lastError = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await WritePacketAsync(characteristic, packet, protocol);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    lastError = e;
                    if (attempt < 2)
                        await Task.Delay(40, token);
                }
            }

            throw new OutputTargetException("UFO-TW BLE packet failed after 2 attempts", lastError);
        }
        finally
        {
            _packetWriteGate.Release();
        }
    }

    private static async Task<(BluetoothLEDevice Device, GattDeviceService Service, UfoTwProtocol Protocol)> ConnectToUfoTwAsync(ulong bluetoothAddress, CancellationToken token)
    {
        Exception lastError = null;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            BluetoothLEDevice device = null;
            try
            {
                if (attempt > 1)
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), token);

                device = await AwaitWinRtAsync(
                    BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress),
                    "open BLE device",
                    BleOperationTimeout,
                    token);
                if (device == null)
                    throw new OutputTargetException("Windows could not open the selected UFO-TW BLE device");

                var cached = await AwaitWinRtAsync(
                    device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Cached),
                    "read cached UFO-TW service",
                    BleOperationTimeout,
                    token);
                if (cached.Status == GattCommunicationStatus.Success && cached.Services.Count > 0)
                    return (device, cached.Services[0], UfoTwProtocol.CompatibilityFirmware);

                var uncached = await AwaitWinRtAsync(
                    device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached),
                    "read UFO-TW service",
                    BleOperationTimeout,
                    token);
                if (uncached.Status == GattCommunicationStatus.Success && uncached.Services.Count > 0)
                    return (device, uncached.Services[0], UfoTwProtocol.CompatibilityFirmware);

                var originalCached = await AwaitWinRtAsync(
                    device.GetGattServicesForUuidAsync(OriginalServiceUuid, BluetoothCacheMode.Cached),
                    "read cached original UFO-TW service",
                    BleOperationTimeout,
                    token);
                if (originalCached.Status == GattCommunicationStatus.Success && originalCached.Services.Count > 0)
                    return (device, originalCached.Services[0], UfoTwProtocol.OriginalUfoTw);

                var originalUncached = await AwaitWinRtAsync(
                    device.GetGattServicesForUuidAsync(OriginalServiceUuid, BluetoothCacheMode.Uncached),
                    "read original UFO-TW service",
                    BleOperationTimeout,
                    token);
                if (originalUncached.Status == GattCommunicationStatus.Success && originalUncached.Services.Count > 0)
                    return (device, originalUncached.Services[0], UfoTwProtocol.OriginalUfoTw);

                var legacyOriginalCached = await AwaitWinRtAsync(
                    device.GetGattServicesForUuidAsync(LegacyOriginalServiceUuid, BluetoothCacheMode.Cached),
                    "read legacy original UFO-TW service",
                    BleOperationTimeout,
                    token);
                if (legacyOriginalCached.Status == GattCommunicationStatus.Success && legacyOriginalCached.Services.Count > 0)
                    return (device, legacyOriginalCached.Services[0], UfoTwProtocol.LegacyOriginalUfoTw);

                var legacyOriginalUncached = await AwaitWinRtAsync(
                    device.GetGattServicesForUuidAsync(LegacyOriginalServiceUuid, BluetoothCacheMode.Uncached),
                    "read legacy original UFO-TW service",
                    BleOperationTimeout,
                    token);
                if (legacyOriginalUncached.Status == GattCommunicationStatus.Success && legacyOriginalUncached.Services.Count > 0)
                    return (device, legacyOriginalUncached.Services[0], UfoTwProtocol.LegacyOriginalUfoTw);

                // The ESP32 compatibility firmware uses 40ee0200/40ee0202.
                // Genuine units encountered in the wild use either
                // 40ee2222/40ee0202 or 40ee1111/40ee2222. Search the complete
                // GATT table as Intiface does when a fixed service lookup is
                // unavailable.
                foreach (var cacheMode in new[] { BluetoothCacheMode.Cached, BluetoothCacheMode.Uncached })
                {
                    var allServices = await AwaitWinRtAsync(
                        device.GetGattServicesAsync(cacheMode),
                        "enumerate GATT services",
                        BleOperationTimeout,
                        token);
                    if (allServices.Status != GattCommunicationStatus.Success)
                        continue;

                    foreach (var candidateService in allServices.Services)
                    {
                        if (candidateService.Uuid == OriginalServiceUuid
                            && await ContainsUfoTwCharacteristicAsync(
                                candidateService, CharacteristicUuid, cacheMode, token))
                            return (device, candidateService, UfoTwProtocol.OriginalUfoTw);

                        if (candidateService.Uuid == LegacyOriginalServiceUuid
                            && await ContainsUfoTwCharacteristicAsync(
                                candidateService, LegacyOriginalCharacteristicUuid, cacheMode, token))
                            return (device, candidateService, UfoTwProtocol.LegacyOriginalUfoTw);
                    }
                }

                throw new OutputTargetException($"UFO-TW service lookup failed (BLE status: {uncached.Status})");
            }
            catch (OperationCanceledException)
            {
                device?.Dispose();
                throw;
            }
            catch (Exception e)
            {
                lastError = e;
                device?.Dispose();
            }
        }

        throw new OutputTargetException("Unable to reach the UFO-TW GATT service after 4 attempts", lastError);
    }

    private static async Task<GattCharacteristic> FindUfoTwCharacteristicAsync(
        GattDeviceService service,
        UfoTwProtocol protocol,
        CancellationToken token)
    {
        Exception lastError = null;
        var lastStatus = GattCommunicationStatus.Unreachable;
        var characteristicUuid = protocol == UfoTwProtocol.LegacyOriginalUfoTw
            ? LegacyOriginalCharacteristicUuid
            : CharacteristicUuid;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                if (attempt > 1)
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), token);

                foreach (var cacheMode in new[] { BluetoothCacheMode.Cached, BluetoothCacheMode.Uncached })
                {
                    var result = await AwaitWinRtAsync(
                        service.GetCharacteristicsAsync(cacheMode),
                        "enumerate GATT characteristics",
                        BleOperationTimeout,
                        token);
                    lastStatus = result.Status;
                    if (result.Status != GattCommunicationStatus.Success)
                        continue;

                    var characteristic = result.Characteristics.FirstOrDefault(x =>
                        IsUfoTwControlCharacteristic(x, characteristicUuid));
                    if (characteristic != null)
                        return characteristic;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                lastError = e;
            }
        }

        throw new OutputTargetException($"UFO-TW control characteristic lookup failed (BLE status: {lastStatus})", lastError);
    }

    private double GetOutputValue(DeviceAxis axis)
    {
        if (axis == null || !AxisSettings[axis].Enabled)
            return 0.5;

        var control = UfoAxisControls.FirstOrDefault(x => x.Axis == axis);
        if (control?.TestEnabled == true)
            return control.TestValue;

        // UFO-TW has no position/physical travel limit. Its funscript value maps
        // directly to the firmware's signed speed value (0..49 reverse,
        // 50 stop, 51..100 forward).
        return GetValue(axis);
    }

    public void ResetTestValues()
    {
        foreach (var control in UfoAxisControls)
            control.Reset();
    }

    internal static byte[] EncodePacket(double leftValue, double rightValue)
        => EncodePacket(leftValue, rightValue, UfoTwProtocol.CompatibilityFirmware);

    internal static byte[] EncodeOriginalPacket(double leftValue, double rightValue)
        => EncodePacket(leftValue, rightValue, UfoTwProtocol.OriginalUfoTw);

    internal static byte[] EncodeLegacyOriginalPacket(double leftValue, double rightValue)
        => EncodePacket(leftValue, rightValue, UfoTwProtocol.LegacyOriginalUfoTw);

    internal static UfoTwProtocol ResolveProtocol(UfoTwProtocol detectedProtocol, bool isGenuineUfoTw)
        => isGenuineUfoTw
            ? detectedProtocol == UfoTwProtocol.LegacyOriginalUfoTw
                ? UfoTwProtocol.LegacyOriginalUfoTw
                : UfoTwProtocol.OriginalUfoTw
            : UfoTwProtocol.CompatibilityFirmware;

    private static byte[] EncodePacket(double leftValue, double rightValue, UfoTwProtocol protocol)
        => protocol is UfoTwProtocol.OriginalUfoTw or UfoTwProtocol.LegacyOriginalUfoTw
            ? [OriginalCommandPrefix, EncodeOriginalMotorValue(leftValue), EncodeOriginalMotorValue(rightValue)]
            : [CommandPrefix, EncodeMotorValue(leftValue), EncodeMotorValue(rightValue)];

    internal static byte EncodeMotorValue(double value)
    {
        if (!double.IsFinite(value))
            return 0;

        var centered = Math.Clamp(value, 0, 1) * 2 - 1;
        var magnitude = (byte)Math.Clamp((int)Math.Round(Math.Abs(centered) * 127, MidpointRounding.AwayFromZero), 0, 127);
        if (magnitude == 0)
            return 0;

        return centered < 0 ? (byte)(magnitude | 0x80) : magnitude;
    }

    private static byte EncodeOriginalMotorValue(double value)
    {
        if (!double.IsFinite(value))
            return 0x80;

        var centered = Math.Clamp(value, 0, 1) * 2 - 1;
        var magnitude = (byte)Math.Clamp((int)Math.Round(Math.Abs(centered) * 99, MidpointRounding.AwayFromZero), 0, 99);
        // The genuine UFO-TW is the Vorze SA dual-rotator protocol:
        // bit 7 is direction and must be set even for zero speed. This means
        // the stop packet is 05 80 80, not 05 00 00.
        return centered >= 0 ? (byte)(0x80 | magnitude) : magnitude;
    }

    private static Task WritePacketAsync(
        GattCharacteristic characteristic,
        byte[] packet,
        UfoTwProtocol protocol)
    {
        var buffer = CryptographicBuffer.CreateFromByteArray(packet);
        return WriteBufferAsync(characteristic, buffer, protocol);
    }

    private static async Task WriteBufferAsync(
        GattCharacteristic characteristic,
        Windows.Storage.Streams.IBuffer buffer,
        UfoTwProtocol protocol)
    {
        var supportsWrite = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write);
        var supportsWriteWithoutResponse = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse);
        if (protocol is UfoTwProtocol.OriginalUfoTw or UfoTwProtocol.LegacyOriginalUfoTw)
        {
            if (!supportsWrite)
                throw new OutputTargetException("The genuine UFO-TW control characteristic does not support Write With Response");

            // The official Vorze UFO-TW implementation writes every packet
            // with response. Some units advertise both properties, so choosing
            // WriteWithoutResponse here makes a successful connection appear
            // to work while the device ignores or rejects commands.
            var originalStatus = await AwaitWinRtAsync(
                characteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithResponse),
                "write genuine UFO-TW control packet",
                BleOperationTimeout,
                CancellationToken.None);
            if (originalStatus != GattCommunicationStatus.Success)
                throw new OutputTargetException($"Genuine UFO-TW BLE write failed (BLE status: {originalStatus})");

            return;
        }

        var writeOption = supportsWriteWithoutResponse
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;
        var status = await AwaitWinRtAsync(
            characteristic.WriteValueAsync(buffer, writeOption),
            "write UFO-TW control packet",
            BleOperationTimeout,
            CancellationToken.None);
        if (status != GattCommunicationStatus.Success)
            throw new OutputTargetException($"UFO-TW BLE write failed (BLE status: {status})");
    }

    private static bool IsUfoTwDevice(UfoTwBleDevice device)
        => IsLikelyUfoName(device.Name) || device.Protocol != UfoTwProtocol.Unknown;

    private static bool IsLikelyUfoName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        return trimmed.Equals("UFO", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(DefaultDeviceName, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("UFO-", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Vorze", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUfoTwControlCharacteristic(GattCharacteristic characteristic, Guid characteristicUuid)
        => characteristic != null
        && characteristic.Uuid == characteristicUuid
        && (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
            || characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse));

    private static async Task<bool> ContainsUfoTwCharacteristicAsync(
        GattDeviceService service,
        Guid characteristicUuid,
        BluetoothCacheMode cacheMode,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await AwaitWinRtAsync(
            service.GetCharacteristicsAsync(cacheMode),
            "inspect UFO-TW control characteristic",
            BleOperationTimeout,
            token);
        return result.Status == GattCommunicationStatus.Success
            && result.Characteristics.Any(x => IsUfoTwControlCharacteristic(x, characteristicUuid));
    }

    private static async Task<UfoTwProtocol> DetectUfoTwProtocolAsync(UfoTwBleDevice candidate)
    {
        BluetoothLEDevice device = null;
        try
        {
            device = await AwaitWinRtAsync(
                BluetoothLEDevice.FromBluetoothAddressAsync(candidate.BluetoothAddress),
                "open BLE device while scanning",
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            if (device == null)
                return UfoTwProtocol.Unknown;

            var result = await AwaitWinRtAsync(
                device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached),
                "read compatibility UFO-TW service while scanning",
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
                return UfoTwProtocol.CompatibilityFirmware;

            var original = await AwaitWinRtAsync(
                device.GetGattServicesForUuidAsync(OriginalServiceUuid, BluetoothCacheMode.Uncached),
                "read original UFO-TW service while scanning",
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            if (original.Status == GattCommunicationStatus.Success && original.Services.Count > 0)
                return UfoTwProtocol.OriginalUfoTw;

            var legacyOriginal = await AwaitWinRtAsync(
                device.GetGattServicesForUuidAsync(LegacyOriginalServiceUuid, BluetoothCacheMode.Uncached),
                "read legacy original UFO-TW service while scanning",
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            if (legacyOriginal.Status == GattCommunicationStatus.Success && legacyOriginal.Services.Count > 0)
                return UfoTwProtocol.LegacyOriginalUfoTw;

            if (!IsLikelyUfoName(candidate.Name))
                return UfoTwProtocol.Unknown;

            var allServices = await AwaitWinRtAsync(
                device.GetGattServicesAsync(BluetoothCacheMode.Uncached),
                "enumerate GATT services while scanning",
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            if (allServices.Status != GattCommunicationStatus.Success)
                return UfoTwProtocol.Unknown;

            foreach (var service in allServices.Services)
            {
                if (service.Uuid == OriginalServiceUuid
                    && await ContainsUfoTwCharacteristicAsync(
                        service, CharacteristicUuid, BluetoothCacheMode.Uncached, CancellationToken.None))
                    return UfoTwProtocol.OriginalUfoTw;

                if (service.Uuid == LegacyOriginalServiceUuid
                    && await ContainsUfoTwCharacteristicAsync(
                        service, LegacyOriginalCharacteristicUuid, BluetoothCacheMode.Uncached, CancellationToken.None))
                    return UfoTwProtocol.LegacyOriginalUfoTw;
            }

            return UfoTwProtocol.Unknown;
        }
        catch
        {
            return UfoTwProtocol.Unknown;
        }
        finally
        {
            device?.Dispose();
        }
    }

    private void ReplaceAvailableDevices(IReadOnlyList<UfoTwBleDevice> devices)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            AvailableDevices.Clear();
            foreach (var device in devices)
                AvailableDevices.Add(device);
            return;
        }

        dispatcher.Invoke(() =>
        {
            AvailableDevices.Clear();
            foreach (var device in devices)
                AvailableDevices.Add(device);
        });
    }

    private static Task<T> AwaitWinRtAsync<T>(IAsyncOperation<T> operation)
    {
        if (operation == null)
            return Task.FromException<T>(new OutputTargetException("Windows returned an empty BLE operation"));

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Completed = (completedOperation, status) =>
        {
            try
            {
                if (status == AsyncStatus.Completed)
                    completion.TrySetResult(completedOperation.GetResults());
                else
                    completion.TrySetException(new OutputTargetException($"Windows BLE operation failed (status: {status})"));
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        };

        return completion.Task;
    }

    private static async Task<T> AwaitWinRtAsync<T>(
        IAsyncOperation<T> operation,
        string operationName,
        TimeSpan timeout,
        CancellationToken token)
    {
        var operationTask = AwaitWinRtAsync(operation);
        var timeoutTask = Task.Delay(timeout, token);
        if (await Task.WhenAny(operationTask, timeoutTask) != operationTask)
        {
            token.ThrowIfCancellationRequested();
            throw new OutputTargetException($"Windows BLE {operationName} timed out after {timeout.TotalSeconds:0.#} seconds");
        }

        return await operationTask;
    }

    private static bool IsUfoAxis(DeviceAxis axis)
        => string.Equals(axis.Name, "Lnip", StringComparison.OrdinalIgnoreCase)
        || string.Equals(axis.Name, "Rnip", StringComparison.OrdinalIgnoreCase);

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
            settings[nameof(SelectedDeviceId)] = SelectedDeviceId;
        else if (action == SettingsAction.Loading
              && settings.TryGetValue<string>(nameof(SelectedDeviceId), out var selectedDeviceId))
            SelectedDeviceId = selectedDeviceId;
    }

    public override void RegisterProperties(IPropertyManager p)
    {
        base.RegisterProperties(p);
        p.RegisterProperty($"{Identifier}::Device", () => SelectedDevice?.Name ?? SelectedDeviceId);
    }

    public override void UnregisterProperties(IPropertyManager p)
    {
        base.UnregisterProperties(p);
        p.UnregisterProperty($"{Identifier}::Device");
    }
}

internal enum UfoTwProtocol
{
    Unknown,
    CompatibilityFirmware,
    OriginalUfoTw,
    LegacyOriginalUfoTw,
}

internal sealed class UfoTwBleDevice : PropertyChangedBase
{
    public ulong BluetoothAddress { get; }
    public string Name { get; set; }
    public string DisplayName => $"{Name} [{BluetoothAddress:X12}]";
    public string Id { get; }
    public UfoTwProtocol Protocol { get; set; }
    public short SignalStrength { get; set; } = short.MinValue;

    public UfoTwBleDevice(ulong bluetoothAddress, string name)
    {
        BluetoothAddress = bluetoothAddress;
        Name = name;
        Id = $"BLE:{bluetoothAddress:X12}";
    }
}

internal sealed class UfoTwAxisControl : PropertyChangedBase
{
    public DeviceAxis Axis { get; }
    public DeviceAxisSettings Settings { get; }
    public string Name => Axis.Name;

    // Manual test output is deliberately opt-in so a saved test value cannot
    // override Lnip/Rnip funscript playback after reconnecting.
    public bool TestEnabled { get; set; }
    public double TestValue { get; set; } = 0.5;

    public UfoTwAxisControl(DeviceAxis axis, DeviceAxisSettings settings)
    {
        Axis = axis;
        Settings = settings;
    }

    public void Reset()
        => TestValue = 0.5;
}
