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
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace MultiFunPlayer.OutputTarget.ViewModels;

internal enum VibratorModel
{
    /// <summary>Galaku single-motor toys, e.g. "Galaku Ball vibrator" / 红丸 (K118).</summary>
    Galaku = 0,
    /// <summary>MIZZZEE / 谜姬 toys using the 醉清风 "MZ" protocol, e.g. MIZZZEE MJHW SE (谜姬电动红丸).</summary>
    Mizzzee = 1,
}

internal enum VibratorSourceAxis { V0 = 0, L0 = 1 }

internal enum L0MappingMode
{
    /// <summary>Vibration follows how fast L0 is moving.</summary>
    Speed = 0,
    /// <summary>Vibration follows the L0 position (0-100%).</summary>
    Position = 1,
}

/// <summary>
/// Native BLE vibrator output. Supports Galaku (protocol ported from buttplug-rs galaku.rs)
/// and MIZZZEE / 谜姬 (protocol taken from the 醉清风 app, MZ devices, service 0xDDDD).
/// </summary>
[DisplayName("Galaku")]
internal sealed class GalakuOutputTarget : AsyncAbstractOutputTarget, IHandle<MediaPlayingChangedMessage>
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private IVibratorConnection _connection;
    private int _isMediaPlaying;
    private int _forceSend = 1;
    private ConnectionStatus _status;
    private VibratorDevice _selectedDevice;
    private VibratorModel _model = VibratorModel.Galaku;
    private VibratorSourceAxis _sourceAxis = VibratorSourceAxis.V0;

    // L0 speed tracking
    private double _lastL0 = double.NaN;
    private double _speedLevel;

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
    public ObservableCollection<VibratorDevice> AvailableDevices { get; } = [];
    public string ScanStatus { get; set; } = "Choose a model and press Scan.";
    public string BatteryStatus { get; set; } = "-";
    public string SelectedDeviceId { get; set; }
    public string ManualAddress { get; set; }
    public bool TestEnabled { get; set; }
    public double TestValue { get; set; }

    // ---- model ----
    public IReadOnlyList<string> ModelNames { get; } = ["Galaku 红丸 (K118 等)", "谜姬 MIZZZEE (MJHW SE 等)"];

    public VibratorModel Model
    {
        get => _model;
        set
        {
            if (_model == value) return;
            _model = value;
            NotifyOfPropertyChange(nameof(Model));
            NotifyOfPropertyChange(nameof(ModelIndex));
            NotifyOfPropertyChange(nameof(IsMizzzee));
            Execute.OnUIThread(() => { AvailableDevices.Clear(); SelectedDevice = null; });
            ScanStatus = "Model changed. Press Scan.";
            NotifyOfPropertyChange(nameof(ScanStatus));
        }
    }

    public int ModelIndex
    {
        get => (int)Model;
        set => Model = Enum.IsDefined(typeof(VibratorModel), value) ? (VibratorModel)value : VibratorModel.Galaku;
    }

    public bool IsMizzzee => Model == VibratorModel.Mizzzee;

    // ---- axis mapping ----
    public VibratorSourceAxis SourceAxis
    {
        get => _sourceAxis;
        set
        {
            if (_sourceAxis == value) return;
            _sourceAxis = value;
            _lastL0 = double.NaN; _speedLevel = 0;
            NotifyOfPropertyChange(nameof(SourceAxis));
            NotifyOfPropertyChange(nameof(MapL0));
            NotifyOfPropertyChange(nameof(AxisEnabled));
            NotifyOfPropertyChange(nameof(SourceAxisName));
            Interlocked.Exchange(ref _forceSend, 1);
        }
    }

    /// <summary>true = map L0 instead of V0.</summary>
    public bool MapL0
    {
        get => SourceAxis == VibratorSourceAxis.L0;
        set => SourceAxis = value ? VibratorSourceAxis.L0 : VibratorSourceAxis.V0;
    }

    public string SourceAxisName => SourceAxis.ToString();

    public IReadOnlyList<string> L0ModeNames { get; } = ["按运动速度 (speed)", "按位置百分比 (position)"];
    public L0MappingMode L0Mode { get; set; } = L0MappingMode.Speed;
    public int L0ModeIndex
    {
        get => (int)L0Mode;
        set { L0Mode = value == 1 ? L0MappingMode.Position : L0MappingMode.Speed; NotifyOfPropertyChange(nameof(L0Mode)); }
    }

    /// <summary>L0 speed (strokes of full range per second) that maps to 100% vibration.</summary>
    public double L0SpeedFullScale { get; set; } = 3.0;

    public bool AxisEnabled
    {
        get { var axis = DeviceAxis.Parse(SourceAxisName); return axis != null && AxisSettings[axis].Enabled; }
        set { var axis = DeviceAxis.Parse(SourceAxisName); if (axis != null) AxisSettings[axis].Enabled = value; }
    }

    public VibratorDevice SelectedDevice
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
        var axis = DeviceAxis.Parse("V0");
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

    // ---------------- scanning ----------------
    public async Task OnRefreshDevices()
    {
        if (!IsDisconnected) return;
        var model = Model;
        SetStatus(nameof(ScanStatus), $"Scanning for {VibratorProtocols.ModelLabel(model)} devices (maximum 4 seconds)…");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var devices = await VibratorScanner.ScanAsync(model, timeout.Token);
            Execute.OnUIThread(() =>
            {
                var previousId = SelectedDevice?.Id ?? SelectedDeviceId;
                AvailableDevices.Clear();
                foreach (var device in devices) AvailableDevices.Add(device);
                SelectedDevice = AvailableDevices.FirstOrDefault(x => string.Equals(x.Id, previousId, StringComparison.Ordinal))
                    ?? AvailableDevices.FirstOrDefault();
                ScanStatus = devices.Count == 0
                    ? $"No {VibratorProtocols.ModelLabel(model)} device found in {stopwatch.Elapsed.TotalSeconds:0.00}s. Turn the toy on and make sure no other app (Intiface, phone app) is connected to it, or enter its MAC address below."
                    : $"Found {devices.Count} device(s) in {stopwatch.Elapsed.TotalSeconds:0.00}s.";
                NotifyOfPropertyChange(nameof(ScanStatus));
            });
        }
        catch (OperationCanceledException) { SetStatus(nameof(ScanStatus), "Scan timed out."); }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to scan vibrator devices");
            SetStatus(nameof(ScanStatus), $"Scan failed: {exception.Message}");
        }
    }

    public void OnAddManualAddress()
    {
        if (!IsDisconnected) return;
        if (!VibratorScanner.TryParseAddress(ManualAddress, out var address))
        {
            SetStatus(nameof(ScanStatus), "Address format: D5:B8:83:CA:00:33");
            return;
        }

        var device = VibratorScanner.FromAddress(Model, address, null);
        Execute.OnUIThread(() =>
        {
            var existing = AvailableDevices.FirstOrDefault(x => x.Id == device.Id);
            if (existing == null) AvailableDevices.Insert(0, device);
            SelectedDevice = existing ?? device;
            ScanStatus = $"Added {device.DisplayName}";
            NotifyOfPropertyChange(nameof(ScanStatus));
        });
    }

    // ---------------- connection ----------------
    protected override async ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (DeviceAxis.Parse(SourceAxisName) == null)
            throw new OutputTargetException($"The selected MFP device profile does not have the {SourceAxisName} axis. Enable it in Settings > Device, then restart MultiFunPlayer.");

        if (SelectedDevice == null && !string.IsNullOrWhiteSpace(SelectedDeviceId) && SelectedDeviceId.StartsWith("BLE:", StringComparison.Ordinal)
            && ulong.TryParse(SelectedDeviceId.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out var savedAddress))
            SelectedDevice = VibratorScanner.FromAddress(Model, savedAddress, null);

        if (SelectedDevice == null || SelectedDevice.Model != Model) await OnRefreshDevices();
        if (SelectedDevice == null) throw new OutputTargetException($"No {VibratorProtocols.ModelLabel(Model)} device was found");
        return true;
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        IVibratorConnection connection = null;
        try
        {
            var device = SelectedDevice;
            var stopwatch = Stopwatch.StartNew();
            connection = device.Model == VibratorModel.Mizzzee ? new MizzzeeBleConnection() : new GalakuBleConnection();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                await connection.ConnectAsync(device, deadline.Token);
            }

            _connection = connection;
            SelectedDeviceId = device.Id;
            SetStatus(nameof(ScanStatus), $"Connected in {stopwatch.Elapsed.TotalSeconds:0.00}s: {device.DisplayName}");
            Logger.Info("Connected to {0} in {1:0.000}s [Device: {2}]", device.Model, stopwatch.Elapsed.TotalSeconds, device.DisplayName);
            Status = ConnectionStatus.Connected;
            EventAggregator.Publish(new SyncRequestMessage());
            _ = ReadBatteryAsync(connection, token);

            var previous = (-1, -1);
            var lastSend = Stopwatch.StartNew();
            var frameClock = Stopwatch.StartNew();
            await FixedUpdateAsync(() => !token.IsCancellationRequested, async (_, _) =>
            {
                var dt = Math.Clamp(frameClock.Elapsed.TotalSeconds, 0.001, 0.5);
                frameClock.Restart();

                var playing = Volatile.Read(ref _isMediaPlaying) != 0;
                if (!playing && !TestEnabled)
                {
                    _lastL0 = double.NaN; _speedLevel = 0;
                    if (previous.Item1 > 0 || previous.Item2 > 0) { await SendAsync(connection, 0, 0, token); previous = (0, 0); }
                    return;
                }

                var level = connection.ToLevel(GetOutputValue(dt));
                var current = (level, 0);
                var active = current.Item1 > 0 || current.Item2 > 0;

                // resend periodically while running so a dropped packet cannot leave the motor stuck
                // (谜姬 commands carry a 1.5 s hold time and therefore must be refreshed more often)
                if (current != previous || Interlocked.Exchange(ref _forceSend, 0) != 0 || (active && lastSend.ElapsedMilliseconds > connection.KeepAliveMs))
                {
                    await SendAsync(connection, current.Item1, current.Item2, token);
                    previous = current;
                    lastSend.Restart();
                }
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Logger.Error(exception, "Vibrator connection failed");
            SetStatus(nameof(ScanStatus), $"Connection failed: {exception.Message}");
            if (connectionType != ConnectionType.AutoConnect)
                _ = DialogHelper.ShowErrorAsync(exception, "Error when connecting to vibrator", "RootDialog");
        }
        finally
        {
            _connection = null;
            if (connection != null)
            {
                await _sendGate.WaitAsync();
                try
                {
                    try { await connection.DisconnectAsync(); } catch (Exception exception) { Logger.Warn(exception, "Failed to stop vibrator during disconnect"); }
                    connection.Dispose();
                }
                finally { _sendGate.Release(); }
            }
            SetStatus(nameof(BatteryStatus), "-");
        }
    }

    private async Task ReadBatteryAsync(IVibratorConnection connection, CancellationToken token)
    {
        try
        {
            var level = await connection.ReadBatteryAsync(token);
            SetStatus(nameof(BatteryStatus), level == null ? "unknown" : $"{level}%");
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Battery read failed");
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
        try { await SendAsync(connection, 0, 0, CancellationToken.None); }
        catch (Exception exception) { Logger.Warn(exception, "Failed to send pause stop command"); }
    }

    private async Task SendAsync(IVibratorConnection connection, int channel1, int channel2, CancellationToken token)
    {
        await _sendGate.WaitAsync(token);
        try
        {
            if (!ReferenceEquals(_connection, connection)) return;
            await connection.SendAsync(channel1, channel2, token);
        }
        finally { _sendGate.Release(); }
    }

    /// <summary>Returns the vibration strength in 0..1.</summary>
    private double GetOutputValue(double dt)
    {
        if (TestEnabled) return TestValue;
        var axis = DeviceAxis.Parse(SourceAxisName);
        if (axis == null || !AxisSettings[axis].Enabled) return 0;
        var settings = AxisSettings[axis];
        var value = GetValue(axis);

        if (SourceAxis == VibratorSourceAxis.L0 && L0Mode == L0MappingMode.Speed)
        {
            var level = VibratorProtocols.SpeedLevel(ref _lastL0, ref _speedLevel, value, dt, L0SpeedFullScale);
            return level <= 0 ? 0 : MathUtils.Lerp(settings.Minimum, settings.Maximum, level);
        }

        return MathUtils.Lerp(settings.Minimum, settings.Maximum, value);
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
        {
            settings[nameof(SelectedDeviceId)] = SelectedDeviceId;
            settings[nameof(Model)] = Model.ToString();
            settings[nameof(SourceAxis)] = SourceAxis.ToString();
            settings[nameof(L0Mode)] = L0Mode.ToString();
            settings[nameof(L0SpeedFullScale)] = L0SpeedFullScale;
            settings[nameof(ManualAddress)] = ManualAddress;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedDeviceId), out var id)) SelectedDeviceId = id;
            if (settings.TryGetValue<string>(nameof(Model), out var model) && Enum.TryParse<VibratorModel>(model, out var m)) Model = m;
            if (settings.TryGetValue<string>(nameof(SourceAxis), out var axis) && Enum.TryParse<VibratorSourceAxis>(axis, out var a)) SourceAxis = a;
            if (settings.TryGetValue<string>(nameof(L0Mode), out var mode) && Enum.TryParse<L0MappingMode>(mode, out var l)) L0Mode = l;
            if (settings.TryGetValue<double>(nameof(L0SpeedFullScale), out var scale) && double.IsFinite(scale) && scale > 0) L0SpeedFullScale = scale;
            if (settings.TryGetValue<string>(nameof(ManualAddress), out var manual)) ManualAddress = manual;
        }
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

internal sealed record VibratorDevice(string Id, string DisplayName, ulong Address, BluetoothAddressType AddressType, VibratorModel Model);

internal static class VibratorProtocols
{
    public static string ModelLabel(VibratorModel model) => model == VibratorModel.Mizzzee ? "谜姬 MIZZZEE" : "Galaku";

    /// <summary>
    /// Converts L0 movement into a 0..1 vibration level: |dL0/dt| / fullScale, rising immediately
    /// and decaying at 150%/s so short pauses between strokes do not make the motor stutter.
    /// </summary>
    public static double SpeedLevel(ref double lastPosition, ref double level, double position, double dt, double fullScale)
    {
        if (!double.IsFinite(position)) return level = 0;
        if (!double.IsFinite(lastPosition)) { lastPosition = position; return level = 0; }
        var speed = Math.Abs(position - lastPosition) / Math.Max(dt, 0.001);
        lastPosition = position;
        var target = Math.Clamp(speed / Math.Max(fullScale, 0.01), 0, 1);
        level = Math.Max(target, level - 1.5 * dt);
        if (level < 0.01) level = 0;
        return level;
    }
}

internal static class VibratorScanner
{
    public static async Task<IReadOnlyList<VibratorDevice>> ScanAsync(VibratorModel model, CancellationToken token)
    {
        var found = new ConcurrentDictionary<ulong, VibratorDevice>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> received = (_, args) =>
        {
            if (args?.Advertisement == null || args.BluetoothAddress == 0) return;
            var name = args.Advertisement.LocalName;
            if (!Matches(model, name)) return;
            found[args.BluetoothAddress] = Create(model, args.BluetoothAddress, args.BluetoothAddressType, name);
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

        // Devices already paired with Windows often stop advertising while Windows keeps them connected,
        // so also list paired BLE devices whose name matches the selected model.
        try
        {
            var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(token);
            foreach (var info in paired)
            {
                if (!Matches(model, info.Name)) continue;
                using var device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(token);
                if (device == null || device.BluetoothAddress == 0 || found.ContainsKey(device.BluetoothAddress)) continue;
                found[device.BluetoothAddress] = Create(model, device.BluetoothAddress, device.BluetoothAddressType, info.Name);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }

        return found.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool Matches(VibratorModel model, string name) => model == VibratorModel.Mizzzee
        ? MizzzeeProtocol.IsKnownName(name)
        : GalakuProtocol.IsKnownName(name);

    private static VibratorDevice Create(VibratorModel model, ulong address, BluetoothAddressType type, string name)
    {
        var friendly = model == VibratorModel.Mizzzee ? MizzzeeProtocol.FriendlyName(name) : GalakuProtocol.FriendlyName(name);
        return new VibratorDevice($"BLE:{address:X12}", $"{friendly} [{FormatAddress(address)}]", address, type, model);
    }

    public static VibratorDevice FromAddress(VibratorModel model, ulong address, string name)
        => new($"BLE:{address:X12}", $"{(string.IsNullOrWhiteSpace(name) ? VibratorProtocols.ModelLabel(model) : name)} [{FormatAddress(address)}]",
            address, BluetoothAddressType.Unspecified, model);

    public static bool TryParseAddress(string text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = text.Trim().Replace(":", "").Replace("-", "");
        return hex.Length == 12 && ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out address) && address != 0;
    }

    public static string FormatAddress(ulong address)
        => string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
}

internal interface IVibratorConnection : IDisposable
{
    int KeepAliveMs { get; }
    int ToLevel(double value);
    Task ConnectAsync(VibratorDevice device, CancellationToken token);
    Task SendAsync(int channel1, int channel2, CancellationToken token);
    Task<int?> ReadBatteryAsync(CancellationToken token);
    Task DisconnectAsync();
}

internal static class BleHelper
{
    public static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try { return await operation(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Windows BLE operation timed out after 6 seconds"); }
    }

    public static Task<BluetoothLEDevice> OpenAsync(VibratorDevice device, CancellationToken token)
        => WithTimeout(ct => (device.AddressType == BluetoothAddressType.Unspecified
            ? BluetoothLEDevice.FromBluetoothAddressAsync(device.Address)
            : BluetoothLEDevice.FromBluetoothAddressAsync(device.Address, device.AddressType)).AsTask(ct), token);

    public static async Task WriteAsync(GattCharacteristic characteristic, byte[] packet, CancellationToken token)
    {
        if (characteristic == null) throw new IOException("Device is not connected");
        var option = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var writer = new DataWriter();
                writer.WriteBytes(packet);
                var buffer = writer.DetachBuffer();
                var status = await WithTimeout(ct => characteristic.WriteValueAsync(buffer, option).AsTask(ct), token);
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
}

// =====================================================================================
// Galaku
// =====================================================================================

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

internal sealed class GalakuBleConnection : IVibratorConnection
{
    private BluetoothLEDevice _device;
    private GattDeviceService _service;
    private GattCharacteristic _tx;
    private GattSession _session;

    public int KeepAliveMs => 1000;
    public int ToLevel(double value) => GalakuProtocol.ToSpeed(value);

    public async Task ConnectAsync(VibratorDevice device, CancellationToken token)
    {
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Release();
                _device = await BleHelper.OpenAsync(device, token) ?? throw new IOException("Windows could not open this BLE device");
                var services = await BleHelper.WithTimeout(ct => _device.GetGattServicesForUuidAsync(GalakuProtocol.Service, BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (services.Status != GattCommunicationStatus.Success) throw new IOException($"GATT service lookup returned {services.Status}");
                _service = services.Services.FirstOrDefault() ?? throw new IOException("Galaku service 0x1000 not found");
                foreach (var other in services.Services.Skip(1)) other.Dispose();

                var characteristics = await BleHelper.WithTimeout(ct => _service.GetCharacteristicsForUuidAsync(GalakuProtocol.TxCharacteristic, BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (characteristics.Status != GattCommunicationStatus.Success) throw new IOException($"GATT characteristic lookup returned {characteristics.Status}");
                _tx = characteristics.Characteristics.FirstOrDefault(x => x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                    || x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                    ?? throw new IOException("Galaku TX characteristic 0x1001 is missing or not writable");

                _session = await BleHelper.WithTimeout(ct => GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct), token);
                if (_session != null) _session.MaintainConnection = true;
                await Task.Delay(250, token);
                await SendAsync(0, 0, token);
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

    public Task SendAsync(int channel1, int channel2, CancellationToken token)
        => BleHelper.WriteAsync(_tx, GalakuProtocol.SpeedCommand(channel1), token);

    /// <summary>Subscribes to 0x1002, sends the battery request and returns the first notified byte.</summary>
    public async Task<int?> ReadBatteryAsync(CancellationToken token)
    {
        if (_service == null) return null;
        var result = await BleHelper.WithTimeout(ct => _service.GetCharacteristicsForUuidAsync(GalakuProtocol.BatteryCharacteristic, BluetoothCacheMode.Cached).AsTask(ct), token);
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
            await BleHelper.WithTimeout(ct => rx.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct), token);
            await BleHelper.WriteAsync(_tx, GalakuProtocol.BatteryCommand(), token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        finally { rx.ValueChanged -= changed; }
    }

    public async Task DisconnectAsync()
    {
        if (_tx != null) try { await SendAsync(0, 0, CancellationToken.None); } catch { }
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

    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}

// =====================================================================================
// 谜姬 MIZZZEE (醉清风 app, "MZ" device type)
// =====================================================================================

internal static class MizzzeeProtocol
{
    public static readonly Guid Service = BluetoothUuidHelper.FromShortId(0xDDDD);
    public static readonly Guid TxCharacteristic = BluetoothUuidHelper.FromShortId(0xDDD1);
    public static readonly Guid NotifyCharacteristic = BluetoothUuidHelper.FromShortId(0xDDD2);
    public static readonly Guid BatteryService = BluetoothUuidHelper.FromShortId(0x180F);
    public static readonly Guid BatteryLevel = BluetoothUuidHelper.FromShortId(0x2A19);

    /// <summary>Hold time sent with every command: 150 × 10 ms = 1.5 s. Refreshed every 500 ms while running.</summary>
    public const int HoldTicks = 150;

    public static bool IsKnownName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("MIZZZEE", StringComparison.OrdinalIgnoreCase) || name.Contains("MIJI", StringComparison.OrdinalIgnoreCase);
    }

    public static string FriendlyName(string advertisedName)
    {
        var name = advertisedName?.Trim() ?? "";
        return name.Contains("MJHW", StringComparison.OrdinalIgnoreCase) ? $"谜姬电动红丸 ({name})" : $"谜姬 {name}";
    }

    public static int ToIntensity(double value)
        => !double.IsFinite(value) ? 0 : (int)Math.Round(Math.Clamp(value, 0, 1) * 99, MidpointRounding.AwayFromZero);

    /// <summary>AA cmd len payload… sum, where sum is the low byte of all previous bytes.</summary>
    public static byte[] Frame(byte command, ReadOnlySpan<byte> payload)
    {
        var result = new byte[payload.Length + 4];
        result[0] = 0xAA;
        result[1] = command;
        result[2] = (byte)payload.Length;
        payload.CopyTo(result.AsSpan(3));
        var sum = 0;
        for (var i = 0; i < result.Length - 1; i++) sum += result[i];
        result[^1] = (byte)sum;
        return result;
    }

    /// <summary>AA 08 06 m1 m2 m3 m4 TH TL SUM — m = 0..99, T = hold time in 10 ms (0xFFFF = forever).</summary>
    public static byte[] IntensityCommand(int channel1, int channel2, int ticks = HoldTicks)
    {
        static byte C(int v) => (byte)Math.Clamp(v, 0, 99);
        ticks = Math.Clamp(ticks, 0, 0xFFFF);
        return Frame(0x08, [C(channel1), C(channel2), 0, 0, (byte)(ticks >> 8), (byte)ticks]);
    }

    /// <summary>Stop command exactly as the 醉清风 app sends it: all channels 0, time 0x00FF.</summary>
    public static byte[] StopCommand() => Frame(0x08, [0, 0, 0, 0, 0x00, 0xFF]);

    /// <summary>Status notification on 0xDDD2: AA 01 len battery … → battery percentage.</summary>
    public static int? ParseBattery(ReadOnlySpan<byte> data)
        => data.Length >= 4 && data[0] == 0xAA && data[1] == 0x01 && data[3] <= 100 ? data[3] : null;
}

internal sealed class MizzzeeBleConnection : IVibratorConnection
{
    private BluetoothLEDevice _device;
    private GattDeviceService _service;
    private GattCharacteristic _tx;
    private GattCharacteristic _rx;
    private GattSession _session;
    private volatile int _battery = -1;
    private TaskCompletionSource<int> _batteryWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int KeepAliveMs => 500;
    public int ToLevel(double value) => MizzzeeProtocol.ToIntensity(value);

    public async Task ConnectAsync(VibratorDevice device, CancellationToken token)
    {
        Exception last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Release();
                _device = await BleHelper.OpenAsync(device, token) ?? throw new IOException("Windows could not open this BLE device");

                // Ask Windows to actually connect and keep the link before discovering services;
                // otherwise discovery on a paired/idle device can return an empty list.
                _session = await BleHelper.WithTimeout(ct => GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct), token);
                if (_session != null) _session.MaintainConnection = true;

                _service = await FindServiceAsync(token);

                var characteristics = await BleHelper.WithTimeout(ct => _service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct), token);
                if (characteristics.Status != GattCommunicationStatus.Success) throw new IOException($"GATT characteristic lookup returned {characteristics.Status}");
                _tx = characteristics.Characteristics.FirstOrDefault(x => x.Uuid == MizzzeeProtocol.TxCharacteristic)
                    ?? throw new IOException("谜姬 TX characteristic 0xDDD1 not found");
                _rx = characteristics.Characteristics.FirstOrDefault(x => x.Uuid == MizzzeeProtocol.NotifyCharacteristic);

                if (_rx != null && _rx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    _rx.ValueChanged += OnValueChanged;
                    try { await BleHelper.WithTimeout(ct => _rx.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct), token); }
                    catch (Exception exception) when (exception is not OperationCanceledException) { }
                }

                await Task.Delay(250, token);
                await BleHelper.WriteAsync(_tx, MizzzeeProtocol.StopCommand(), token);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                Release();
                if (attempt < 3) await Task.Delay(400 * attempt, token);
            }
        }
        throw new IOException("Could not connect to the 谜姬 device after 3 attempts", last);
    }

    private async Task<GattDeviceService> FindServiceAsync(CancellationToken token)
    {
        var seen = new List<string>();
        var lastStatus = GattCommunicationStatus.Success;
        // Uncached first (forces a real GATT discovery), then cached; a few tries while Windows finishes connecting.
        foreach (var mode in new[] { BluetoothCacheMode.Uncached, BluetoothCacheMode.Uncached, BluetoothCacheMode.Cached, BluetoothCacheMode.Uncached })
        {
            var result = await BleHelper.WithTimeout(ct => _device.GetGattServicesAsync(mode).AsTask(ct), token);
            lastStatus = result.Status;
            if (result.Status == GattCommunicationStatus.Success)
            {
                GattDeviceService match = null;
                seen.Clear();
                foreach (var service in result.Services)
                {
                    seen.Add(service.Uuid.ToString());
                    if (match == null && service.Uuid == MizzzeeProtocol.Service) match = service;
                    else service.Dispose();
                }
                if (match != null) return match;
            }
            await Task.Delay(500, token);
        }

        throw new IOException(seen.Count == 0
            ? $"谜姬 service 0xDDDD not found (GATT status {lastStatus}, no services returned; connection state {_device?.ConnectionStatus}). Make sure the 醉清风 app / web page / phone is disconnected from the toy."
            : $"谜姬 service 0xDDDD not found. Services on this device: {string.Join(", ", seen)}");
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue == null || args.CharacteristicValue.Length == 0) return;
        var data = new byte[args.CharacteristicValue.Length];
        using (var reader = DataReader.FromBuffer(args.CharacteristicValue)) reader.ReadBytes(data);
        var battery = MizzzeeProtocol.ParseBattery(data);
        if (battery == null) return;
        _battery = battery.Value;
        _batteryWaiter.TrySetResult(battery.Value);
    }

    public Task SendAsync(int channel1, int channel2, CancellationToken token)
        => BleHelper.WriteAsync(_tx, channel1 <= 0 && channel2 <= 0
            ? MizzzeeProtocol.StopCommand()
            : MizzzeeProtocol.IntensityCommand(channel1, channel2), token);

    public async Task<int?> ReadBatteryAsync(CancellationToken token)
    {
        if (_battery >= 0) return _battery;

        // standard Battery Service (0x180F / 0x2A19) — present on MJHW SE
        try
        {
            if (_device != null)
            {
                var services = await BleHelper.WithTimeout(ct => _device.GetGattServicesForUuidAsync(MizzzeeProtocol.BatteryService, BluetoothCacheMode.Cached).AsTask(ct), token);
                var service = services.Status == GattCommunicationStatus.Success ? services.Services.FirstOrDefault() : null;
                if (service != null)
                {
                    using (service)
                    {
                        var chars = await BleHelper.WithTimeout(ct => service.GetCharacteristicsForUuidAsync(MizzzeeProtocol.BatteryLevel, BluetoothCacheMode.Cached).AsTask(ct), token);
                        var level = chars.Status == GattCommunicationStatus.Success ? chars.Characteristics.FirstOrDefault() : null;
                        if (level != null)
                        {
                            var read = await BleHelper.WithTimeout(ct => level.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct), token);
                            if (read.Status == GattCommunicationStatus.Success && read.Value != null && read.Value.Length > 0)
                            {
                                using var reader = DataReader.FromBuffer(read.Value);
                                return reader.ReadByte();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }

        // fall back to the status notification on 0xDDD2
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try { return await _batteryWaiter.Task.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
    }

    public async Task DisconnectAsync()
    {
        if (_tx != null) try { await BleHelper.WriteAsync(_tx, MizzzeeProtocol.StopCommand(), CancellationToken.None); } catch { }
        Release();
    }

    private void Release()
    {
        if (_rx != null) _rx.ValueChanged -= OnValueChanged;
        _rx = null;
        _tx = null;
        if (_session != null) { _session.MaintainConnection = false; _session.Dispose(); }
        _session = null;
        _service?.Dispose(); _service = null;
        _device?.Dispose(); _device = null;
    }

    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}
