using MultiFunPlayer.Common;

namespace MultiFunPlayer.Property;

internal interface IPropertyManager
{
    IReadOnlyObservableConcurrentCollection<string> AvailableProperties { get; }

    void RegisterProperty<TOut>(string propertyName, Func<TOut> getter);
    void RegisterProperty<T0, TOut>(string propertyName, Func<T0, TOut> getter);
    void RegisterProperty<T0, T1, TOut>(string propertyName, Func<T0, T1, TOut> getter);

    void UnregisterProperty(string propertyName);
    TOut GetValue<TOut>(string propertyName, params object[] arguments);
    TOut GetValue<TOut>(string propertyName);
    TOut GetValue<T0, TOut>(string propertyName, T0 arg0);
    TOut GetValue<T0, T1, TOut>(string propertyName, T0 arg0, T1 arg1);
}

internal sealed class PropertyManager : IPropertyManager
{
    private readonly ObservableConcurrentCollection<string> _availableProperties;
    private readonly Dictionary<string, IPropertyDelegate> _properties;

    public IReadOnlyObservableConcurrentCollection<string> AvailableProperties => _availableProperties;

    public PropertyManager()
    {
        _availableProperties = [];
        _properties = [];
    }

    public void RegisterProperty<TOut>(string propertyName, Func<TOut> getter)
    {
        _properties.Add(propertyName, new PropertyDelegate<TOut>(getter));
        _availableProperties.Add(propertyName);
    }

    public void RegisterProperty<T0, TOut>(string propertyName, Func<T0, TOut> getter)
    {
        _properties.Add(propertyName, new PropertyDelegate<T0, TOut>(getter));
        _availableProperties.Add(propertyName);
    }

    public void RegisterProperty<T0, T1, TOut>(string propertyName, Func<T0, T1, TOut> getter)
    {
        _properties.Add(propertyName, new PropertyDelegate<T0, T1, TOut>(getter));
        _availableProperties.Add(propertyName);
    }

    public void UnregisterProperty(string propertyName)
    {
        _availableProperties.Remove(propertyName);
        _properties.Remove(propertyName);
    }

    public TOut GetValue<TOut>(string propertyName, params object[] args)
    {
        var property = _properties[propertyName] as IPropertyDelegate<TOut>;
        return property.GetValue(args);
    }

    public TOut GetValue<TOut>(string propertyName)
    {
        var property = _properties[propertyName] as PropertyDelegate<TOut>;
        return property.GetValue();
    }

    public TOut GetValue<T0, TOut>(string propertyName, T0 arg0)
    {
        var property = _properties[propertyName] as PropertyDelegate<T0, TOut>;
        return property.GetValue(arg0);
    }

    public TOut GetValue<T0, T1, TOut>(string propertyName, T0 arg0, T1 arg1)
    {
        var property = _properties[propertyName] as PropertyDelegate<T0, T1, TOut>;
        return property.GetValue(arg0, arg1);
    }
}