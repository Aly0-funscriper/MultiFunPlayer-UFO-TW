namespace MultiFunPlayer.Common;

internal enum DeviceAxisUpdateType
{
    FixedUpdate,
    PolledUpdate
}

internal interface IDeviceAxisValueProvider
{
    public double GetValue(DeviceAxis axis);

    public void BeginEventPolling(object context);
    public void EndEventPolling(object context);

    public (DeviceAxis, DeviceAxisValueEvent) WaitForEventAny(IReadOnlyList<DeviceAxis> axes, object context, CancellationToken cancellationToken);
    public ValueTask<(DeviceAxis, DeviceAxisValueEvent)> WaitForEventAnyAsync(IReadOnlyList<DeviceAxis> axes, object context, CancellationToken cancellationToken);
    public (bool, DeviceAxisValueEvent) WaitForEvent(DeviceAxis axis, object context, CancellationToken cancellationToken);
    public ValueTask<(bool, DeviceAxisValueEvent)> WaitForEventAsync(DeviceAxis axis, object context, CancellationToken cancellationToken);
}

internal record DeviceAxisValueEvent(double TargetValue, double Duration);

internal sealed record class DeviceAxisScriptEvent(Keyframe From, Keyframe To)
    : DeviceAxisValueEvent(To?.Value ?? double.NaN, To?.Position - From?.Position ?? double.NaN);