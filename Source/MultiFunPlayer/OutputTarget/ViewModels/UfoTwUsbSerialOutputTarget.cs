using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("UFO-TW USB Serial")]
internal sealed class UfoTwUsbSerialOutputTarget : ThreadAbstractOutputTarget, IHandle<MediaPlayingChangedMessage>
{
    private readonly object _serialWriteGate = new();
    private SerialPort _activeSerialPort;
    private int _isMediaPlaying = 1;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SelectedSerialPort != null;
    public bool CanChangePort => IsDisconnected;

    public ObservableCollection<UfoUsbSerialPortInfo> SerialPorts { get; } = [];
    public UfoUsbSerialPortInfo SelectedSerialPort { get; set; }
    public string SelectedSerialPortDeviceId { get; set; }

    public UfoTwUsbSerialOutputTarget(
        int instanceIndex,
        IEventAggregator eventAggregator,
        IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        var leftAxis = DeviceAxis.Parse("Lnip");
        var rightAxis = DeviceAxis.Parse("Rnip");
        if (leftAxis != null)
            AxisSettings[leftAxis].Enabled = true;
        if (rightAxis != null)
            AxisSettings[rightAxis].Enabled = true;
    }

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new ThreadFixedUpdateContext()
        {
            UpdateInterval = 50,
            MinimumUpdateInterval = 20,
            MaximumUpdateInterval = 200,
        },
        _ => null,
    };

    protected override void OnInitialActivate()
    {
        base.OnInitialActivate();
        if (IsDisconnected)
            RefreshPorts();
    }

    public void RefreshPorts()
    {
        if (!IsDisconnected)
            return;

        var previousDeviceId = SelectedSerialPort?.DeviceId ?? SelectedSerialPortDeviceId;
        var ports = EnumeratePorts()
            .OrderByDescending(x => x.IsEspressif)
            .ThenBy(x => x.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SerialPorts.Clear();
        foreach (var port in ports)
            SerialPorts.Add(port);

        SelectedSerialPort = SerialPorts.FirstOrDefault(x =>
                string.Equals(x.DeviceId, previousDeviceId, StringComparison.OrdinalIgnoreCase))
            ?? SerialPorts.FirstOrDefault(x => x.IsEspressif)
            ?? SerialPorts.FirstOrDefault();
        SelectedSerialPortDeviceId = SelectedSerialPort?.DeviceId;
    }

    public void OnSelectedSerialPortChanged()
        => SelectedSerialPortDeviceId = SelectedSerialPort?.DeviceId;

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (SelectedSerialPort == null)
            RefreshPorts();

        if (SelectedSerialPort == null)
            throw new OutputTargetException("No USB serial port was found. Connect the ESP32 and press Refresh");

        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} [Port: {1}, Device: {2}]",
                Identifier, SelectedSerialPort.PortName, SelectedSerialPort.DeviceId);

        return ValueTask.FromResult(true);
    }

    protected override void Run(ConnectionType connectionType, CancellationToken token)
    {
        SerialPort serialPort = null;
        try
        {
            serialPort = new SerialPort(SelectedSerialPort.PortName, 115200, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = 100,
                WriteTimeout = 250,
                Encoding = Encoding.ASCII,
                NewLine = "\n",
            };
            serialPort.Open();

            // Native USB CDC can briefly re-enumerate or print boot text when
            // opened. DTR/RTS stay disabled, and this bounded delay lets the
            // MicroPython main loop become ready before the first heartbeat.
            Thread.Sleep(1000);
            try { serialPort.DiscardInBuffer(); }
            catch { }

            lock (_serialWriteGate)
                _activeSerialPort = serialPort;

            SendValues(serialPort, 0.5, 0.5);
            Status = ConnectionStatus.Connected;
            Logger.Info("Connected to UFO-TW USB serial [Port: {0}, DTR: false, RTS: false]", serialPort.PortName);
            EventAggregator.Publish(new SyncRequestMessage());

            FixedUpdate(() => !token.IsCancellationRequested && serialPort.IsOpen, (_, _) =>
            {
                var isPlaying = Volatile.Read(ref _isMediaPlaying) != 0;
                var left = isPlaying ? GetAxisValue("Lnip") : 0.5;
                var right = isPlaying ? GetAxisValue("Rnip") : 0.5;
                SendValues(serialPort, left, right);
            });
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to UFO-TW USB serial");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to UFO-TW USB serial", "RootDialog");
        }
        catch (Exception e)
        {
            Logger.Error(e, "{0} failed with exception", Identifier);
        }
        finally
        {
            if (serialPort != null)
            {
                try { SendValues(serialPort, 0.5, 0.5); }
                catch (Exception e) { Logger.Warn(e, "Failed to send UFO-TW USB stop command"); }
            }

            lock (_serialWriteGate)
                _activeSerialPort = null;

            try { serialPort?.Dispose(); }
            catch { }
        }
    }

    public void Handle(MediaPlayingChangedMessage message)
    {
        var wasPlaying = Interlocked.Exchange(ref _isMediaPlaying, message.IsPlaying ? 1 : 0);
        if (message.IsPlaying || wasPlaying == 0)
            return;

        lock (_serialWriteGate)
        {
            if (_activeSerialPort?.IsOpen != true)
                return;
            try
            {
                SendValuesCore(_activeSerialPort, 0.5, 0.5);
                Logger.Info("Media paused; sent UFO-TW USB stop command");
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Failed to send UFO-TW USB stop command after pause");
            }
        }
    }

    private double GetAxisValue(string name)
    {
        var axis = DeviceAxis.Parse(name);
        if (axis == null || !AxisSettings[axis].Enabled)
            return 0.5;
        return GetValue(axis);
    }

    private void SendValues(SerialPort serialPort, double left, double right)
    {
        lock (_serialWriteGate)
            SendValuesCore(serialPort, left, right);
    }

    private static void SendValuesCore(SerialPort serialPort, double left, double right)
    {
        if (serialPort?.IsOpen != true)
            return;

        serialPort.Write(EncodeSerialCommand(left, right));
    }

    internal static string EncodeSerialCommand(double left, double right)
    {
        var leftByte = UfoTwBleOutputTarget.EncodeMotorValue(left);
        var rightByte = UfoTwBleOutputTarget.EncodeMotorValue(right);
        return $"UFO,{leftByte},{rightByte}\n";
    }

    private static IEnumerable<UfoUsbSerialPortInfo> EnumeratePorts()
    {
        var results = new List<UfoUsbSerialPortInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,DeviceID,PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementObject item in searcher.Get())
            {
                try
                {
                    var name = Convert.ToString(item["Name"]);
                    var deviceId = Convert.ToString(item["DeviceID"]);
                    var pnpDeviceId = Convert.ToString(item["PNPDeviceID"]);
                    var match = Regex.Match(name ?? string.Empty, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                    if (!match.Success || string.IsNullOrWhiteSpace(deviceId))
                        continue;

                    var portName = Registry.GetValue(
                        $@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Enum\{deviceId}\Device Parameters",
                        "PortName",
                        match.Groups[1].Value)?.ToString();
                    if (string.IsNullOrWhiteSpace(portName))
                        portName = match.Groups[1].Value;

                    results.Add(new UfoUsbSerialPortInfo(
                        portName,
                        name,
                        deviceId,
                        pnpDeviceId,
                        pnpDeviceId?.Contains("VID_303A", StringComparison.OrdinalIgnoreCase) == true));
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch
        {
            foreach (var portName in SerialPort.GetPortNames())
                results.Add(new UfoUsbSerialPortInfo(portName, portName, portName, string.Empty, false));
        }

        return results
            .GroupBy(x => x.PortName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First());
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);
        if (action == SettingsAction.Saving)
        {
            settings[nameof(SelectedSerialPortDeviceId)] = SelectedSerialPort?.DeviceId ?? SelectedSerialPortDeviceId;
        }
        else if (action == SettingsAction.Loading
            && settings.TryGetValue<string>(nameof(SelectedSerialPortDeviceId), out var deviceId))
        {
            SelectedSerialPortDeviceId = deviceId;
        }
    }
}

internal sealed record UfoUsbSerialPortInfo(
    string PortName,
    string Name,
    string DeviceId,
    string PnpDeviceId,
    bool IsEspressif)
{
    public string DisplayName => IsEspressif ? $"{Name} - ESP32" : Name;
}
