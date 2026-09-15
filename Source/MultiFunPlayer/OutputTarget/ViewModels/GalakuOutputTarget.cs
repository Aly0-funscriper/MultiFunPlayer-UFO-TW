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
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MultiFunPlayer.OutputTarget.ViewModels;

/// <summary>
/// Native BLE output for Galaku single-motor toys (e.g. "Galaku Ball vibrator" / 红丸, advertised as K118).
/// Protocol ported from buttplug-rs protocol_impl/galaku.rs (single feature branch).
/// </summary>
[DisplayName("Galaku")]
internal sealed class GalakuOutputTarget : AsyncAbstractOutputTarget, IHandle<MediaPlayingChangedMessage>
{
    private const string AxisName = "V0";
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private GalakuBleConnection _connection;
    private int _isMediaPlaying;
    private int _forceSend = 1;
    private ConnectionStatus _status;
    private GalakuDevice _selectedDevice;

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
    public ObservableCollection<GalakuDevice> AvailableDevices { get; } = [];
    public string ScanStatus { get; set; } = "Press Scan. Only Galaku advertisements (e.g. K118 = Ball vibrator / 红丸) are shown.";
    public string BatteryStatus { get; set; } = "-";
    public string SelectedDeviceId { get; set; }
    public bool TestEnabled { get; set; }
    public double TestValue { get; set; }
    public bool AxisEnabled
    {
        get { var axis = DeviceAxis.Parse(AxisName); return axis != null && AxisSettings[axis].Enabled; }
        set { var axis = DeviceAxis.Parse(AxisName); if (axis != null) AxisSettings[axis].Enabled = value; }
    }

    public GalakuDevice SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Equals(_selectedDevice, value)) return;
            _selectedDevice = value;
            NotifyOfPropertyChange(nameof(SelectedDevice));
        }
    }

    public GalakuOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider, ScriptViewModel scriptViewModel)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        _isMediaPlaying = scriptViewModel.IsPlaying ? 1 : 0;
        var axis = DeviceAxis.Parse(AxisName);
        if (axis != null) AxisSettings[axis].Enabled = true;
    }

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new AsyncFixedUpdateContext
        {
            UpdateInterval = 50,
            MinimumUpdateInterval = 20,
            MaximumUpdateInterval = 200
        },
        _ => null
    };

    public void ResetTest() => TestValue = 0;

    public async Task OnRefreshDevices()
    {
        if (!IsDisconnected) return;
        SetStatus(nameof(ScanStatus), "Scanning for Galaku devices (maximum 4 seconds)…");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var devices = await GalakuBleConnection.ScanAsync(timeout.Token);
            Execute.OnUIThread(() =>
            {
                var previousId = SelectedDevice?.Id ?? SelectedDeviceId;
                AvailableDevices.Clear();
                foreach (var device in devices) AvailableDevices.Add(device);
                SelectedDevice = AvailableDevices.FirstOrDefault(x => string.Equals(x.Id, previousId, StringComparison.Ordinal))
                    ?? AvailableDevices.FirstOrDefault();
                ScanStatus = devices.Count == 0
                    ? $"No Galaku advertisement found in {stopwatch.Elapsed.TotalSeconds:0.00}s. Turn the toy on and make sure no other app (Intiface, phone app) is connected to it."
                    : $"Found {devices.Count} Galaku device(s) in {stopwatch.Elapsed.TotalSeconds:0.00}s.";
                NotifyOfPropertyChange(nameof(ScanStatus));
            });
        }
        catch (OperationCanceledException) { SetStatus(nameof(ScanStatus), "Scan timed out."); }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to scan Galaku devices");
            SetStatus(nameof(ScanStatus), $"Scan failed: {exception.Message}");
        }
    }

    protected override async ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (DeviceAxis.Parse(AxisName) == null)
            throw new OutputTargetException("The selected MFP device profile does not have the V0 (Vibrate) axis enabled. Enable V0 in Settings > Device, then restart MultiFunPlayer.");
        if (SelectedDevice == null) await OnRefreshDevices();
        if (SelectedDevice == null) throw new OutputTargetException("No Galaku device was found");
        return true;
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        GalakuBleConnection connection = null;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            connection = new GalakuBleConnection();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                await connection.ConnectAsync(SelectedDevice, deadline.Token);
            }

            _connection = connection;
            SelectedDeviceId = SelectedDevice.Id;
            SetStatus(nameof(ScanStatus), $"Connected in {stopwatch.Elapsed.TotalSeconds:0.00}s: {SelectedDevice.DisplayName}");
            Logger.Info("Connected to Galaku in {0:0.000}s [Device: {1}]", stopwatch.Elapsed.TotalSeconds, SelectedDevice.DisplayName);
            Status = ConnectionStatus.Connected;
            EventAggregator.Publish(new SyncRequestMessage());
            _ = ReadBatteryAsync(connection, token);

            var previous = -1;
            var lastSend = Stopwatch.StartNew();
            await FixedUpdateAsync(() => !token.IsCancellationRequested, async (_, _) =>
            {
                var playing = Volatile.Read(ref _isMediaPlaying) != 0;
                if (!playing && !TestEnabled)
                {
                    if (previous > 0) { await SendAsync(connection, 0, token); previous = 0; }
                    return;
                }

                var speed = GalakuProtocol.ToSpeed(GetOutputValue());
                // resend periodically while running so a dropped packet cannot leave the motor stuck
                if (speed != previous || Interlocked.Exchange(ref _forceSend, 0) != 0 || (speed > 0 && lastSend.ElapsedMilliseconds > 1000))
                {
                    await SendAsync(connection, speed, token);
                    previous = speed;
                    lastSend.Restart();
                }
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Logger.Error(exception, "Galaku connection failed");
            SetStatus(nameof(ScanStatus), $"Connection failed: {exception.Message}");
            if (connectionType != ConnectionType.AutoConnect)
                _ = DialogHelper.ShowErrorAsync(exception, "Error when connecting to Galaku", "RootDialog");
        }
        finally
        {
            _connection = null;
            if (connection != null)
            {
                await _sendGate.WaitAsync();
                try
                {
                    try { await connection.DisconnectAsync(); } catch (Exception exception) { Logger.Warn(exception, "Failed to stop Galaku during disconnect"); }
                    connection.Dispose();
                }
                finally { _sendGate.Release(); }
            }
            SetStatus(nameof(BatteryStatus), "-");
        }
    }

    private async Task ReadBatteryAsync(GalakuBleConnection connection, CancellationToken token)
    {
        try
        {
            var level = await connection.ReadBatteryAsync(token);
            SetStatus(nameof(BatteryStatus), level == null ? "unknown" : $"{level}%");
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Galaku battery read failed");
            SetStatus(nameof(BatteryStatus), "unknown");
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
        try { await SendAsync(connection, 0, CancellationToken.None); }
        catch (Exception exception) { Logger.Warn(exception, "Failed to send Galaku pause stop command"); }
    }

    private async Task SendAsync(GalakuBleConnection connection, int speed, CancellationToken token)
    {
        await _sendGate.WaitAsync(token);
        try
        {
            if (!ReferenceEquals(_connection, connection)) return;
            await connection.SendSpeedAsync(speed, token);
        }
        finally { _sendGate.Release(); }
    }

    private double GetOutputValue()
    {
        if (TestEnabled) return TestValue;
        var axis = DeviceAxis.Parse(AxisName);
        if (axis == null || !AxisSettings[axis].Enabled) return 0;
        var settings = AxisSettings[axis];
        return MathUtils.Lerp(settings.Minimum, settings.Maximum, GetValue(axis));
    }

    private void SetStatus(string property, string value) => Execute.OnUIThread(() =>
    {
        if (property == nameof(ScanStatus)) ScanStatus = value; else BatteryStatus = value;
        NotifyOfPropertyChange(property);
    });

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);
        if (action == SettingsAction.Saving)
            settings[nameof(SelectedDeviceId)] = SelectedDeviceId;
        else if (action == SettingsAction.Loading && settings.TryGetValue<string>(nameof(SelectedDeviceId), out var id))
            SelectedDeviceId = id;
    }

    public override void RegisterProperties(IPropertyManager properties)
    {
        base.RegisterProperties(properties);
        properties.RegisterProperty($"{Identifier}::Device", () => SelectedDevice?.DisplayName ?? SelectedDeviceId);
    }

    public override void UnregisterProperties(IPropertyManager properties)
    {
        base.UnregisterProperties(properties);
        properties.UnregisterProperty($"{Identifier}::Device");
    }
}

internal sealed record GalakuDevice(string Id, string DisplayName, ulong Address, BluetoothAddressType AddressType);

internal static class GalakuProtocol
{
    public static readonly Guid Service = BluetoothUuidHelper.FromShortId(0x1000);
    public static readonly Guid TxCharacteristic = BluetoothUuidHelper.FromShortId(0x1001);
    public static readonly Guid BatteryCharacteristic = BluetoothUuidHelper.FromShortId(0x1002);

    private static readonly byte[,] KeyTable =
    {
        { 0, 24, 152, 247, 165, 61, 13, 41, 37, 80, 68, 70 },
        { 0, 69, 110, 106, 111, 120, 32, 83, 45, 49, 46, 55 },
        { 0, 101, 120, 32, 84, 111, 121, 115, 10, 142, 157, 163 },
        { 0, 197, 214, 231, 248, 10, 50, 32, 111, 98, 13, 10 },
    };

    // Known advertised names from buttplug device-config galaku.yml (single-motor "Type 0" group).
    // K118 = Galaku Ball vibrator (红丸).
    private static readonly HashSet<string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "K118", "GX85", "GX07", "GX17", "GX21", "GX22", "GX16", "GX29", "GX23", "GX25", "GX26", "GK03", "GX39",
        "G321", "G304", "G336", "G331", "G326", "G335", "G341", "G355", "G349", "G407", "G204", "G171", "G12D",
        "G123", "G23A", "A073", "GLMT", "G901", "G912", "G20B", "K112", "G202", "K107", "G203", "TXHL", "TXMM",
        "TXKL", "K108", "K109", "KWL2", "TFHL", "TFMM", "TFKL", "K120", "K12A", "K12C", "LL18",
    };

    public static string FriendlyName(string advertisedName) => advertisedName?.Trim().ToUpperInvariant() switch
    {
        "K118" => "Galaku Ball vibrator (红丸)",
        _ => $"Galaku {advertisedName?.Trim()}"
    };

    public static bool IsKnownName(string name) => !string.IsNullOrWhiteSpace(name) && KnownNames.Contains(name.Trim());

    public static int ToSpeed(double value)
        => !double.IsFinite(value) ? 0 : (int)Math.Round(Math.Clamp(value, 0, 1) * 100, MidpointRounding.AwayFromZero);

    /// <summary>Single-motor vibrate command: payload [90,0,0,1,49,speed,0,0,0,0].</summary>
    public static byte[] SpeedCommand(int speed)
        => Encode([90, 0, 0, 1, 49, (byte)Math.Clamp(speed, 0, 100), 0, 0, 0, 0]);

    public static byte[] BatteryCommand() => Encode([90, 0, 0, 1, 19, 0, 0, 0, 0, 0]);

    /// <summary>Prepends 0x23 header, appends additive checksum, then applies the Galaku table cipher.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        var plain = new byte[payload.Length + 2];
        plain[0] = 0x23;
        payload.CopyTo(plain.AsSpan(1));
        var sum = 0;
        for (var i = 0; i < plain.Length - 1; i++) sum += plain[i];
        plain[^1] = (byte)sum;

        var result = new byte[plain.Length];
        result[0] = plain[0];
        for (var i = 1; i < plain.Length; i++)
        {
            var key = KeyTable[result[i - 1] & 3, i];
            result[i] = (byte)((key ^ plain[0] ^ plain[i]) + key);
        }
        return result;
    }
}

internal sealed class GalakuBleConnection : IDisposable
{
    private BluetoothLEDevice _device;
    private GattDeviceService _service;
    private GattCharacteristic _tx;
    private GattSession _session;

    public static async Task<IReadOnlyList<GalakuDevice>> ScanAsync(CancellationToken token)
    {
        var found = new ConcurrentDictionary<ulong, GalakuDevice>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> received = (_, args) =>
        {
            if (args?.Advertisement == null || args.BluetoothAddress == 0) return;
            var name = args.Advertisement.LocalName;
            if (!GalakuProtocol.IsKnownName(name)) return;
            found[args.BluetoothAddress] = new GalakuDevice($"BLE:{args.BluetoothAddress:X12}",
                $"{GalakuProtocol.FriendlyName(name)} [{FormatAddress(args.BluetoothAddress)}]", args.BluetoothAddress, args.BluetoothAddressType);
        };
        watcher.Received += received;
        try
        {
            watcher.Start();
            await Task.Delay(4000, token);
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= received;
        }
        return found.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string FormatAddress(ulong address)
        => string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));

    public async Task ConnectAsync(GalakuDevice device, CancellationToken token)
    {
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Release();
                _device = await WithTimeout(ct => BluetoothLEDevice.FromBluetoothAddressAsync(device.Address, device.AddressType).AsTask(ct), token)
                    ?? throw new IOException("Windows could not open this BLE device");
                var services = await WithTimeout(ct => _device.GetGattServicesForUuidAsync(GalakuProtocol.Service, BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (services.Status != GattCommunicationStatus.Success) throw new IOException($"GATT service lookup returned {services.Status}");
                _service = services.Services.FirstOrDefault() ?? throw new IOException("Galaku service 0x1000 not found");
                foreach (var other in services.Services.Skip(1)) other.Dispose();

                var characteristics = await WithTimeout(ct => _service.GetCharacteristicsForUuidAsync(GalakuProtocol.TxCharacteristic, BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (characteristics.Status != GattCommunicationStatus.Success) throw new IOException($"GATT characteristic lookup returned {characteristics.Status}");
                _tx = characteristics.Characteristics.FirstOrDefault(x => x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                    || x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                    ?? throw new IOException("Galaku TX characteristic 0x1001 is missing or not writable");

                _session = await WithTimeout(ct => GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct), token);
                if (_session != null) _session.MaintainConnection = true;
                await Task.Delay(250, token);
                await SendSpeedAsync(0, token);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                Release();
                if (attempt < 3) await Task.Delay(400 * attempt, token);
            }
        }
        throw new IOException("Could not connect to the Galaku device after 3 attempts", last);
    }

    public Task SendSpeedAsync(int speed, CancellationToken token) => WriteAsync(GalakuProtocol.SpeedCommand(speed), token);

    private async Task WriteAsync(byte[] packet, CancellationToken token)
    {
        if (_tx == null) throw new IOException("Galaku is not connected");
        var option = _tx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var writer = new DataWriter();
                writer.WriteBytes(packet);
                var buffer = writer.DetachBuffer();
                var status = await WithTimeout(ct => _tx.WriteValueAsync(buffer, option).AsTask(ct), token);
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

    /// <summary>Subscribes to 0x1002, sends the battery request and returns the first notified byte.</summary>
    public async Task<int?> ReadBatteryAsync(CancellationToken token)
    {
        if (_service == null) return null;
        var result = await WithTimeout(ct => _service.GetCharacteristicsForUuidAsync(GalakuProtocol.BatteryCharacteristic, BluetoothCacheMode.Cached).AsTask(ct), token);
        var rx = result.Status == GattCommunicationStatus.Success ? result.Characteristics.FirstOrDefault() : null;
        if (rx == null || !rx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)) return null;

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> changed = (_, args) =>
        {
            if (args.CharacteristicValue == null || args.CharacteristicValue.Length == 0) return;
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            completion.TrySetResult(reader.ReadByte());
        };
        rx.ValueChanged += changed;
        try
        {
            await WithTimeout(ct => rx.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct), token);
            await WriteAsync(GalakuProtocol.BatteryCommand(), token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        finally { rx.ValueChanged -= changed; }
    }

    public async Task DisconnectAsync()
    {
        if (_tx != null) try { await SendSpeedAsync(0, CancellationToken.None); } catch { }
        Release();
    }

    private void Release()
    {
        _tx = null;
        if (_session != null) { _session.MaintainConnection = false; _session.Dispose(); }
        _session = null;
        _service?.Dispose(); _service = null;
        _device?.Dispose(); _device = null;
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
