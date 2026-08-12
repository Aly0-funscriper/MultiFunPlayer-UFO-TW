namespace MultiFunPlayer.Input;

public interface IInputGesture
{
    IInputGestureDescriptor Descriptor { get; }
}

public interface IButtonInputGesture : IInputGesture
{
    bool State { get; }
}

public interface IToggleInputGesture : IInputGesture
{
    bool State { get; }
    bool IsInitialState { get; }
}

public interface IAxisInputGesture : IInputGesture
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

internal abstract class AbstractToggleInputGesture(IToggleInputGestureDescriptor descriptor, bool state, bool isInitialState) : IToggleInputGesture
{
    public IInputGestureDescriptor Descriptor { get; } = descriptor;
    public bool State { get; } = state;
    public bool IsInitialState { get; } = isInitialState;

    public override bool Equals(object obj) => obj is IToggleInputGesture gesture && Descriptor.Equals(gesture.Descriptor);
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