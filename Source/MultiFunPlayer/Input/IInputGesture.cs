namespace MultiFunPlayer.Input;

internal interface IInputGesture
{
    IInputGestureDescriptor Descriptor { get; }
}

internal interface IButtonInputGesture : IInputGesture
{
    bool State { get; }
}

internal interface IAxisInputGesture : IInputGesture
{
    public double Value { get; }
    public double Delta { get; }
    public double DeltaTime { get; }
}

internal abstract class AbstractButtonInputGesture(IButtonInputGestureDescriptor descriptor, bool state) : IButtonInputGesture
{
    public IInputGestureDescriptor Descriptor { get; } = descriptor;
    public bool State { get; } = state;

    public override bool Equals(object obj) => obj is IButtonInputGesture gesture && Descriptor.Equals(gesture.Descriptor);
    public override int GetHashCode() => HashCode.Combine(Descriptor);
}

internal abstract class AbstractAxisInputGesture(IAxisInputGestureDescriptor descriptor, double value, double delta, double deltaTime) : IAxisInputGesture
{
    public IInputGestureDescriptor Descriptor { get; } = descriptor;
    public double Value { get; } = value;
    public double Delta { get; } = delta;
    public double DeltaTime { get; } = deltaTime;

    public override bool Equals(object obj) => obj is IAxisInputGesture gesture && Descriptor.Equals(gesture.Descriptor);
    public override int GetHashCode() => HashCode.Combine(Descriptor);
}