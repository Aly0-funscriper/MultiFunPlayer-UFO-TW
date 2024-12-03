namespace MultiFunPlayer.Common;

public readonly record struct TypedValue(Type Type, object Value)
{
    public TypedValue(object value) : this(value.GetType(), value) { }
}
