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

internal sealed record class DeviceAxisAutoHomeEvent(double TargetValue, double Duration)
    : DeviceAxisValueEvent(TargetValue, Duration);

internal sealed record class DeviceAxisSpeedLimitedScriptEvent(DeviceAxisScriptEvent Event, double SpeedLimitUnitsPerSecond)
    : DeviceAxisValueEvent(GetTargetValue(Event, SpeedLimitUnitsPerSecond), Event.Duration)
{
    private static double GetTargetValue(DeviceAxisScriptEvent Event, double SpeedLimitUnitsPerSecond)
    {
        var from = Event?.From.Value ?? double.NaN;
        var to = Event?.To.Value ?? double.NaN;

        var step = to - from;
        if (!double.IsFinite(step))
            return Event.TargetValue;

        var speed = step / Event.Duration;
        if (Math.Abs(speed) < SpeedLimitUnitsPerSecond)
            return Event.TargetValue;

        return MathUtils.Clamp01(from + SpeedLimitUnitsPerSecond * Event.Duration * Math.Sign(speed));
    }
}
