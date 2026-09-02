using MultiFunPlayer.Common;
using MultiFunPlayer.Property;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("UFO-TW")]
internal sealed class UfoTwOutputTarget : AsyncAbstractOutputTarget, IHandle<MediaPlayingChangedMessage>
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private IUfoConnection _connection;
    private int _isMediaPlaying;
    private int _forceSend = 1;
    private ConnectionStatus _status;
    private UfoConnectionMethod _selectedMethod = UfoConnectionMethod.BluetoothLe;
    private UfoDevice _selectedDevice;

    public override ConnectionStatus Status
    {
        get => _status;
        protected set
        {
            if (_status == value) return;
            _status = value;
            NotifyOfPropertyChange(nameof(Status));
            NotifyOfPropertyChange(nameof(IsDisconnected));
            NotifyOfPropertyChange(nameof(CanToggleConnect));
        }
    }

    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool CanToggleConnect => Status is not ConnectionStatus.Connecting and not ConnectionStatus.Disconnecting;
    public IReadOnlyList<UfoConnectionMethod> ConnectionMethods { get; } = UfoConnectionMethod.All;
    public ObservableCollection<UfoDevice> AvailableDevices { get; } = [];
    public IReadOnlyList<UfoAxisControl> AxisControls { get; }
    public string ScanStatus { get; set; } = "Press Scan. Only verified UFO-TW advertisements are shown.";
    public string SelectedDeviceId { get; set; }

    public UfoConnectionMethod SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (_selectedMethod == value) return;
            _selectedMethod = value;
            SelectedDevice = null;
            AvailableDevices.Clear();
            ScanStatus = "Press Scan to find a device for this connection type.";
            NotifyOfPropertyChange(nameof(SelectedMethod));
            NotifyOfPropertyChange(nameof(ScanStatus));
        }
    }

    public UfoDevice SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Equals(_selectedDevice, value)) return;
            _selectedDevice = value;
            NotifyOfPropertyChange(nameof(SelectedDevice));
        }
    }

    public UfoTwOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider, ScriptViewModel scriptViewModel)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        _isMediaPlaying = scriptViewModel.IsPlaying ? 1 : 0;
        foreach (var name in new[] { "Lnip", "Rnip" })
        {
            var axis = DeviceAxis.Parse(name);
            if (axis != null) AxisSettings[axis].Enabled = true;
        }

        AxisControls = DeviceAxis.All.Where(IsUfoAxis)
            .Select(axis => new UfoAxisControl(axis, AxisSettings[axis]))
            .ToArray();
    }

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new AsyncFixedUpdateContext
        {
            UpdateInterval = 40,
            MinimumUpdateInterval = 20,
            MaximumUpdateInterval = 200
        },
        _ => null
    };

    public async Task OnRefreshDevices()
    {
        if (!IsDisconnected) return;
        SetScanStatus(SelectedMethod == UfoConnectionMethod.BluetoothLe ? "Scanning for UFO-TW (maximum 3 seconds)…" : "Scanning serial ports…");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var scanner = CreateConnection();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var devices = await scanner.ScanAsync(timeout.Token);
            Execute.OnUIThread(() =>
            {
                var previousId = SelectedDevice?.Id ?? SelectedDeviceId;
                AvailableDevices.Clear();
                foreach (var device in devices) AvailableDevices.Add(device);
                SelectedDevice = AvailableDevices.FirstOrDefault(x => string.Equals(x.Id, previousId, StringComparison.Ordinal))
                    ?? AvailableDevices.FirstOrDefault();
                ScanStatus = devices.Count == 0
                    ? (SelectedMethod == UfoConnectionMethod.BluetoothLe ? $"No UFO-TW advertisement found in {stopwatch.Elapsed.TotalSeconds:0.00}s. Other Bluetooth devices were ignored." : "No serial port found.")
                    : $"Found {devices.Count} candidate(s) in {stopwatch.Elapsed.TotalSeconds:0.00}s. Connection will verify the UFO control service before accepting one.";
                NotifyOfPropertyChange(nameof(ScanStatus));
            });
            Logger.Info("UFO-TW {0} scan completed in {1:0.000}s with {2} filtered candidate(s)", SelectedMethod, stopwatch.Elapsed.TotalSeconds, devices.Count);
        }
        catch (OperationCanceledException)
        {
            SetScanStatus("Scan timed out; no verified UFO-TW advertisement was found.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to scan UFO-TW devices");
            SetScanStatus($"Scan failed: {exception.Message}");
        }
    }

    protected override async ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (DeviceAxis.Parse("Lnip") == null || DeviceAxis.Parse("Rnip") == null)
            throw new OutputTargetException("The selected MFP device profile does not contain the Lnip/Rnip axes");
        if (SelectedDevice == null) await OnRefreshDevices();
        if (SelectedDevice == null) throw new OutputTargetException("No UFO-TW device was found");
        return true;
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        IUfoConnection connection = null;
        var verified = false;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            connection = CreateConnection();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            await connection.ConnectAsync(SelectedDevice, deadline.Token);

            _connection = connection;
            verified = true;
            SelectedDeviceId = SelectedDevice.Id;
            SetScanStatus($"Connected and verified in {stopwatch.Elapsed.TotalSeconds:0.00}s: {SelectedDevice.DisplayName}");
            Logger.Info("Connected and verified UFO-TW via {0} in {1:0.000}s [Device: {2}]", SelectedMethod, stopwatch.Elapsed.TotalSeconds, SelectedDevice.DisplayName);
            Status = ConnectionStatus.Connected;
            EventAggregator.Publish(new SyncRequestMessage());

            byte[] previous = null;
            await FixedUpdateAsync(() => !token.IsCancellationRequested, async (_, _) =>
            {
                var testActive = AxisControls.Any(x => x.TestEnabled);
                if (Volatile.Read(ref _isMediaPlaying) == 0 && !testActive) return;

                var left = GetOutputValue("Lnip");
                var right = GetOutputValue("Rnip");
                var preview = connection.Preview(left, right);
                var changed = previous == null || !preview.AsSpan().SequenceEqual(previous);
                if (connection.NeedsHeartbeat || changed || Interlocked.Exchange(ref _forceSend, 0) != 0)
                {
                    await SendAsync(connection, left, right, token);
                    previous = preview;
                }
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Logger.Error(exception, "UFO-TW connection failed");
            if (!verified && SelectedMethod == UfoConnectionMethod.BluetoothLe && SelectedDevice != null)
            {
                var rejected = SelectedDevice;
                Execute.OnUIThread(() =>
                {
                    AvailableDevices.Remove(rejected);
                    SelectedDevice = AvailableDevices.FirstOrDefault();
                    if (string.Equals(SelectedDeviceId, rejected.Id, StringComparison.Ordinal)) SelectedDeviceId = null;
                    ScanStatus = $"Rejected {rejected.DisplayName}: it did not pass UFO GATT verification. You can scan or select another device.";
                    NotifyOfPropertyChange(nameof(ScanStatus));
                });
            }
            else if (verified)
            {
                SetScanStatus($"Connection interrupted; the verified device remains selected for quick reconnect. {exception.Message}");
            }
            if (connectionType != ConnectionType.AutoConnect)
                _ = DialogHelper.ShowErrorAsync(exception, "Error when connecting to UFO-TW", "RootDialog");
        }
        finally
        {
            _connection = null;
            if (connection != null)
            {
                await _sendGate.WaitAsync();
                try
                {
                    try { await connection.DisconnectAsync(); } catch (Exception exception) { Logger.Warn(exception, "Failed to stop UFO-TW during disconnect"); }
                    connection.Dispose();
                }
                finally { _sendGate.Release(); }
            }
        }
    }

    public void Handle(MediaPlayingChangedMessage message)
    {
        var wasPlaying = Interlocked.Exchange(ref _isMediaPlaying, message.IsPlaying ? 1 : 0);
        if (!message.IsPlaying && wasPlaying != 0) _ = SendStopOnceAsync();
        if (message.IsPlaying && wasPlaying == 0) Interlocked.Exchange(ref _forceSend, 1);
    }

    private async Task SendStopOnceAsync()
    {
        var connection = _connection;
        if (connection == null) return;
        try { await SendAsync(connection, 0.5, 0.5, CancellationToken.None); }
        catch (Exception exception) { Logger.Warn(exception, "Failed to send UFO-TW pause stop command"); }
    }

    private async Task SendAsync(IUfoConnection connection, double left, double right, CancellationToken token)
    {
        await _sendGate.WaitAsync(token);
        try
        {
            if (!ReferenceEquals(_connection, connection)) return;
            await connection.SendAsync(left, right, token);
        }
        finally { _sendGate.Release(); }
    }

    private double GetOutputValue(string name)
    {
        var axis = DeviceAxis.Parse(name);
        var control = AxisControls.FirstOrDefault(x => x.Axis == axis);
        if (control?.TestEnabled == true) return control.TestValue;
        if (axis == null || !AxisSettings[axis].Enabled) return 0.5;
        var settings = AxisSettings[axis];
        return MathUtils.Lerp(settings.Minimum, settings.Maximum, GetValue(axis));
    }

    private IUfoConnection CreateConnection() => SelectedMethod == UfoConnectionMethod.UsbSerial ? new UfoSerialConnection() : new UfoBleConnection();
    private static bool IsUfoAxis(DeviceAxis axis) => axis.Name.Equals("Lnip", StringComparison.OrdinalIgnoreCase) || axis.Name.Equals("Rnip", StringComparison.OrdinalIgnoreCase);
    private void SetScanStatus(string status) => Execute.OnUIThread(() =>
    {
        ScanStatus = status;
        NotifyOfPropertyChange(nameof(ScanStatus));
    });

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);
        if (action == SettingsAction.Saving)
        {
            settings[nameof(SelectedMethod)] = SelectedMethod.Id;
            settings[nameof(SelectedDeviceId)] = SelectedDeviceId;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedMethod), out var method))
                _selectedMethod = ConnectionMethods.FirstOrDefault(x => x.Id == method || x.DisplayName == method) ?? UfoConnectionMethod.BluetoothLe;
            if (settings.TryGetValue<string>(nameof(SelectedDeviceId), out var id)) SelectedDeviceId = id;
        }
    }

    public override void RegisterProperties(IPropertyManager properties)
    {
        base.RegisterProperties(properties);
        properties.RegisterProperty($"{Identifier}::ConnectionMethod", () => SelectedMethod);
        properties.RegisterProperty($"{Identifier}::Device", () => SelectedDevice?.DisplayName ?? SelectedDeviceId);
    }

    public override void UnregisterProperties(IPropertyManager properties)
    {
        base.UnregisterProperties(properties);
        properties.UnregisterProperty($"{Identifier}::ConnectionMethod");
        properties.UnregisterProperty($"{Identifier}::Device");
    }
}

internal sealed class UfoAxisControl(DeviceAxis axis, DeviceAxisSettings settings) : PropertyChangedBase
{
    public DeviceAxis Axis { get; } = axis;
    public DeviceAxisSettings Settings { get; } = settings;
    public string Name => Axis.Name;
    public bool TestEnabled { get; set; }
    public double TestValue { get; set; } = 0.5;
    public void Reset() => TestValue = 0.5;
}

internal sealed record UfoDevice(string Id, string DisplayName, object NativeValue);

internal sealed record UfoConnectionMethod(string Id, string DisplayName)
{
    public static readonly UfoConnectionMethod BluetoothLe = new("ble", "Bluetooth LE");
    public static readonly UfoConnectionMethod UsbSerial = new("serial", "USB Serial");
    public static readonly IReadOnlyList<UfoConnectionMethod> All = [BluetoothLe, UsbSerial];
    public override string ToString() => DisplayName;
}

internal interface IUfoConnection : IDisposable
{
    bool NeedsHeartbeat { get; }
    Task<IReadOnlyList<UfoDevice>> ScanAsync(CancellationToken token);
    Task ConnectAsync(UfoDevice device, CancellationToken token);
    Task SendAsync(double left, double right, CancellationToken token);
    Task DisconnectAsync();
    byte[] Preview(double left, double right);
}

internal sealed class UfoSerialConnection : IUfoConnection
{
    private SerialPort _port;
    public bool NeedsHeartbeat => true;

    public Task<IReadOnlyList<UfoDevice>> ScanAsync(CancellationToken token)
    {
        var ports = new List<(string Port, string Label, bool IsEspressif)>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var label = Convert.ToString(item["Name"]);
                    var pnpId = Convert.ToString(item["PNPDeviceID"]);
                    var match = Regex.Match(label ?? string.Empty, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                    if (match.Success)
                        ports.Add((match.Groups[1].Value, label, pnpId?.Contains("VID_303A", StringComparison.OrdinalIgnoreCase) == true));
                }
            }
        }
        catch { }

        foreach (var port in SerialPort.GetPortNames())
            if (!ports.Any(x => x.Port.Equals(port, StringComparison.OrdinalIgnoreCase))) ports.Add((port, port, false));

        IReadOnlyList<UfoDevice> result = ports.OrderByDescending(x => x.IsEspressif)
            .ThenBy(x => x.Port, StringComparer.OrdinalIgnoreCase)
            .Select(x => new UfoDevice("SERIAL:" + x.Port, x.IsEspressif ? $"{x.Label} — ESP32" : x.Label, x.Port)).ToArray();
        return Task.FromResult(result);
    }

    public async Task ConnectAsync(UfoDevice device, CancellationToken token)
    {
        _port = new SerialPort((string)device.NativeValue, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, DtrEnable = false, RtsEnable = false,
            ReadTimeout = 100, WriteTimeout = 250, Encoding = Encoding.ASCII, NewLine = "\n"
        };
        _port.Open();
        await Task.Delay(900, token);
        try { _port.DiscardInBuffer(); } catch { }
        await SendAsync(0.5, 0.5, token);
    }

    public Task SendAsync(double left, double right, CancellationToken token)
    {
        if (_port?.IsOpen != true) throw new IOException("USB serial is not connected");
        var packet = Preview(left, right);
        _port.Write($"UFO,{packet[1]},{packet[2]}\n");
        return Task.CompletedTask;
    }

    public byte[] Preview(double left, double right) => [0, UfoEncoding.Compatibility(left), UfoEncoding.Compatibility(right)];
    public Task DisconnectAsync()
    {
        try { if (_port?.IsOpen == true) _port.Write("UFO,0,0\n"); } catch { }
        _port?.Dispose();
        _port = null;
        return Task.CompletedTask;
    }
    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}

internal enum UfoBleProtocol { Compatibility, Genuine, GenuineLegacy }
internal sealed record UfoBleEndpoint(ulong Address, BluetoothAddressType AddressType, bool AdvertisedKnownService);

internal sealed class UfoBleConnection : IUfoConnection
{
    internal static readonly Guid CompatibilityService = Guid.Parse("40ee0200-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid CompatibilityCharacteristic = Guid.Parse("40ee0202-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid GenuineService = Guid.Parse("40ee2222-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid GenuineLegacyService = Guid.Parse("40ee1111-63ec-4b7f-8ce7-712efd55b90e");
    internal static readonly Guid GenuineLegacyCharacteristic = Guid.Parse("40ee2222-63ec-4b7f-8ce7-712efd55b90e");

    private BluetoothLEDevice _device;
    private GattDeviceService _service;
    private GattCharacteristic _characteristic;
    private GattSession _session;
    private UfoBleProtocol _protocol;
    public bool NeedsHeartbeat => false;

    public async Task<IReadOnlyList<UfoDevice>> ScanAsync(CancellationToken token)
    {
        var found = new ConcurrentDictionary<ulong, UfoDevice>();
        var firstKnownService = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> received = (_, args) =>
        {
            if (args?.Advertisement == null || args.BluetoothAddress == 0) return;
            var name = args.Advertisement.LocalName;
            var knownService = args.Advertisement.ServiceUuids?.Any(IsKnownService) == true;
            if (!knownService && !IsExactUfoName(name)) return;
            var displayName = string.IsNullOrWhiteSpace(name) ? "UFO-TW" : name;
            found.AddOrUpdate(args.BluetoothAddress,
                _ => new UfoDevice($"BLE:{args.BluetoothAddress:X12}", $"{displayName} [{args.BluetoothAddress:X12}]", new UfoBleEndpoint(args.BluetoothAddress, args.BluetoothAddressType, knownService)),
                (_, existing) =>
                {
                    var previous = (UfoBleEndpoint)existing.NativeValue;
                    return new UfoDevice(existing.Id, $"{displayName} [{args.BluetoothAddress:X12}]", new UfoBleEndpoint(args.BluetoothAddress, args.BluetoothAddressType, knownService || previous.AdvertisedKnownService));
                });
            if (knownService) firstKnownService.TrySetResult();
        };
        watcher.Received += received;
        try
        {
            watcher.Start();
            var maximum = Task.Delay(3000, token);
            if (await Task.WhenAny(firstKnownService.Task, maximum) == firstKnownService.Task) await Task.Delay(300, token);
            else await maximum;
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= received;
        }
        return found.Values.OrderByDescending(x => ((UfoBleEndpoint)x.NativeValue).AdvertisedKnownService)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task ConnectAsync(UfoDevice device, CancellationToken token)
    {
        if (device.NativeValue is not UfoBleEndpoint endpoint) throw new ArgumentException("Invalid BLE endpoint");
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await ReleaseAsync(false);
                _device = await WithTimeout(ct => BluetoothLEDevice.FromBluetoothAddressAsync(endpoint.Address, endpoint.AddressType).AsTask(ct), token);
                if (_device == null) throw new IOException("Windows could not open this BLE device");
                var services = await WithTimeout(ct => _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (services.Status != GattCommunicationStatus.Success) throw new IOException($"GATT service enumeration returned {services.Status}");
                (_service, _protocol) = FindService(services.Services);
                foreach (var other in services.Services) if (!ReferenceEquals(other, _service)) other.Dispose();

                var characteristicId = _protocol == UfoBleProtocol.GenuineLegacy ? GenuineLegacyCharacteristic : CompatibilityCharacteristic;
                var characteristics = await WithTimeout(ct => _service.GetCharacteristicsForUuidAsync(characteristicId, BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (characteristics.Status != GattCommunicationStatus.Success)
                    throw new IOException($"GATT characteristic enumeration returned {characteristics.Status}");
                _characteristic = characteristics.Characteristics.FirstOrDefault(x => x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                    || x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse));
                if (_characteristic == null)
                    throw new IOException("The UFO control characteristic is missing or not writable");
                _session = await WithTimeout(ct => GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct), token);
                if (_session != null) _session.MaintainConnection = true;
                await Task.Delay(250, token);
                await SendAsync(0.5, 0.5, token);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                await ReleaseAsync(false);
                if (attempt < 3) await Task.Delay(300 * attempt, token);
            }
        }
        throw new IOException("The device failed UFO-TW GATT verification after 3 attempts", last);
    }

    public byte[] Preview(double left, double right) => _protocol == UfoBleProtocol.Compatibility
        ? [0, UfoEncoding.Compatibility(left), UfoEncoding.Compatibility(right)]
        : [5, UfoEncoding.Genuine(left), UfoEncoding.Genuine(right)];

    public async Task SendAsync(double left, double right, CancellationToken token)
    {
        if (_characteristic == null) throw new IOException("BLE is not connected");
        var packet = Preview(left, right);
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var writer = new DataWriter();
                writer.WriteBytes(packet);
                var option = _protocol == UfoBleProtocol.Compatibility && _characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
                var status = await WithTimeout(ct => _characteristic.WriteValueAsync(writer.DetachBuffer(), option).AsTask(ct), token);
                if (status != GattCommunicationStatus.Success) throw new IOException($"BLE write returned {status}");
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                if (attempt < 3) await Task.Delay(100 * attempt, token);
            }
        }
        throw new IOException("BLE write failed after 3 attempts", last);
    }

    public Task DisconnectAsync() => ReleaseAsync(true);
    private async Task ReleaseAsync(bool stop)
    {
        if (stop && _characteristic != null) try { await SendAsync(0.5, 0.5, CancellationToken.None); } catch { }
        _characteristic = null;
        if (_session != null) { _session.MaintainConnection = false; _session.Dispose(); }
        _session = null;
        _service?.Dispose(); _service = null;
        _device?.Dispose(); _device = null;
    }

    private static (GattDeviceService, UfoBleProtocol) FindService(IReadOnlyList<GattDeviceService> services)
    {
        foreach (var candidate in new[]
        {
            (CompatibilityService, UfoBleProtocol.Compatibility),
            (GenuineService, UfoBleProtocol.Genuine),
            (GenuineLegacyService, UfoBleProtocol.GenuineLegacy)
        })
        {
            var service = services.FirstOrDefault(x => x.Uuid == candidate.Item1);
            if (service != null) return (service, candidate.Item2);
        }
        throw new IOException("No UFO-TW, compatibility, or XToys GATT service was found");
    }

    private static bool IsKnownService(Guid uuid) => uuid == CompatibilityService || uuid == GenuineService || uuid == GenuineLegacyService;
    internal static bool IsExactUfoName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return normalized == "UFOTW";
    }

    private static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try { return await operation(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Windows BLE operation timed out after 6 seconds"); }
    }
    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}

internal static class UfoEncoding
{
    public static byte Compatibility(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var signed = Math.Clamp(value, 0, 1) * 2 - 1;
        var magnitude = (byte)Math.Clamp((int)Math.Round(Math.Abs(signed) * 127, MidpointRounding.AwayFromZero), 0, 127);
        return magnitude == 0 ? (byte)0 : signed < 0 ? (byte)(magnitude | 128) : magnitude;
    }

    public static byte Genuine(double value)
    {
        if (!double.IsFinite(value)) return 128;
        var signed = Math.Clamp(value, 0, 1) * 2 - 1;
        var magnitude = (byte)Math.Clamp((int)Math.Round(Math.Abs(signed) * 99, MidpointRounding.AwayFromZero), 0, 99);
        return signed < 0 ? magnitude : (byte)(128 | magnitude);
    }
}
