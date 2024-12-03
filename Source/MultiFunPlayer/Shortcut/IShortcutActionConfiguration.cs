using MultiFunPlayer.Input;
using MultiFunPlayer.Settings;
using Newtonsoft.Json;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MultiFunPlayer.Shortcut;

internal enum ShortcutActionConfigurationState
{
    Valid,
    MissingAction,
    Placeholder
}

internal interface IShortcutActionConfiguration
{
    string Name { get; }
    IReadOnlyList<IShortcutSetting> Settings { get; }
    ShortcutActionConfigurationState State { get; set; }

    object[] GetActionParams(IInputGestureData gestureData = null);
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ShortcutActionConfiguration : PropertyChangedBase, IShortcutActionConfiguration
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private object[] _valuesBuffer;

    [JsonProperty] public string Name { get; }
    [JsonProperty] public IReadOnlyList<IShortcutSetting> Settings { get; }
    public ShortcutActionConfigurationState State { get; set; }

    public ShortcutActionConfiguration(string actionName, IEnumerable<IShortcutSetting> settings)
    {
        Name = actionName;
        Settings = [.. settings];

        foreach (var setting in Settings)
        {
            if (setting is INotifyPropertyChanged settingPropertyChanged)
                settingPropertyChanged.PropertyChanged += OnSettingPropertyChanged;
            if (setting.Value is INotifyPropertyChanged valuePropertyChanged)
                valuePropertyChanged.PropertyChanged += OnSettingPropertyChanged;
        }
    }

    public ShortcutActionConfiguration(string actionName, IEnumerable<IShortcutSetting> settings, IEnumerable<TypedValue> values) : this(actionName, settings)
    {
        foreach (var (setting, value) in Settings.Zip(values))
            Populate(setting, value.Value, value.Type);

        void Populate(IShortcutSetting setting, object value, Type valueType)
        {
            var settingType = setting.GetType().GetGenericArguments()[0];
            var typeMatches = value == null ? !settingType.IsValueType || Nullable.GetUnderlyingType(settingType) != null
                                            : valueType == settingType || valueType.IsAssignableTo(settingType);

            if (!typeMatches)
            {
                Logger.Warn("Action \"{0}\" setting type mismatch! [\"{1}\" != \"{2}\"]", Name, settingType, valueType);
            }
            else
            {
                if (setting.Value is INotifyPropertyChanged oldPropertyChanged)
                    oldPropertyChanged.PropertyChanged -= OnSettingPropertyChanged;

                var coercedValue = setting.TemplateContext?.CoerceValue(value) ?? value;
                if (!Equals(coercedValue, value))
                    Logger.Warn("Action \"{0}\" setting value coerced from \"{1}\" to \"{2}\"", Name, value, coercedValue);

                setting.Value = coercedValue;
                if (setting.Value is INotifyPropertyChanged newPropertyChanged)
                    newPropertyChanged.PropertyChanged += OnSettingPropertyChanged;
            }
        }
    }

    [DependsOn(nameof(State))]
    public string DisplayName
    {
        get
        {
            if (Settings.Count == 0)
                return Name;

            var values = State != ShortcutActionConfigurationState.Placeholder
                ? Settings.Select(s => s.ToString())
                : Settings.Select(s =>
                  {
                      var method = s.Type.GetMethod("ToString", []);
                      if (method.DeclaringType != typeof(object) && method.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
                          return s.ToString();
                      return "?";
                  });

            return $"{Name} [{string.Join(", ", values)}]";
        }
    }

    public object[] GetActionParams(IInputGestureData gestureData = null)
    {
        _valuesBuffer ??= new object[gestureData == null ? Settings.Count : Settings.Count + 1];

        var i = 0;
        if (gestureData != null)
            _valuesBuffer[i++] = gestureData;
        foreach (var setting in Settings)
            _valuesBuffer[i++] = setting.Value;

        return _valuesBuffer;
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingPropertyChanged(object sender, PropertyChangedEventArgs e) => NotifyOfPropertyChange(() => DisplayName);
}