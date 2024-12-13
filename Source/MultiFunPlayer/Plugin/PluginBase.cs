using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Property;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using StyletIoC;
using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace MultiFunPlayer.Plugin;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public abstract class PluginBase : Screen
{
    private readonly MessageProxy _messageProxy;
    [DoNotNotify] protected internal CancellationTokenSource CancellationSource { get; }

    [Inject][DoNotNotify] internal IDeviceAxisValueProvider DeviceAxisValueProvider { get; set; }
    [Inject][DoNotNotify] internal IEventAggregator EventAggregator { get; set; }
    [Inject][DoNotNotify] internal IShortcutManager ShortcutManager { get; set; }
    [Inject][DoNotNotify] internal IShortcutActionRunner ShortcutActionRunner { get; set; }
    [Inject][DoNotNotify] internal IPropertyManager PropertyManager { get; set; }

    protected CancellationToken CancellationToken => CancellationSource.Token;

    protected PluginBase()
    {
        _messageProxy = new(HandleMessageInternal);
        CancellationSource = new CancellationTokenSource();
    }

    #region DeviceAxis
    protected double GetAxisValue(DeviceAxis axis)
        => DeviceAxisValueProvider.GetValue(axis);
    #endregion

    #region Shortcut
    [DoNotNotify] protected IReadOnlyObservableConcurrentCollection<string> AvailableActions
        => ShortcutManager.AvailableActions;

    protected void InvokeAction(string actionName, bool invokeDirectly = false)
        => ShortcutActionRunner.Invoke(actionName, invokeDirectly);
    protected void InvokeAction<T0>(string actionName, T0 arg0, bool invokeDirectly = false)
        => ShortcutActionRunner.Invoke(actionName, arg0, invokeDirectly);
    protected void InvokeAction<T0, T1>(string actionName, T0 arg0, T1 arg1, bool invokeDirectly = false)
        => ShortcutActionRunner.Invoke(actionName, arg0, arg1, invokeDirectly);
    protected void InvokeAction<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2, bool invokeDirectly = false)
        => ShortcutActionRunner.Invoke(actionName, arg0, arg1, arg2, invokeDirectly);
    protected void InvokeAction<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, bool invokeDirectly = false)
        => ShortcutActionRunner.Invoke(actionName, arg0, arg1, arg2, arg3, invokeDirectly);
    protected void InvokeAction<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool invokeDirectly = false)
        => ShortcutActionRunner.Invoke(actionName, arg0, arg1, arg2, arg3, arg4, invokeDirectly);

    protected ValueTask InvokeActionAsync(string actionName, bool invokeDirectly = false)
        => ShortcutActionRunner.InvokeAsync(actionName, invokeDirectly);
    protected ValueTask InvokeActionAsync<T0>(string actionName, T0 arg0, bool invokeDirectly = false)
        => ShortcutActionRunner.InvokeAsync(actionName, arg0, invokeDirectly);
    protected ValueTask InvokeActionAsync<T0, T1>(string actionName, T0 arg0, T1 arg1, bool invokeDirectly = false)
        => ShortcutActionRunner.InvokeAsync(actionName, arg0, arg1, invokeDirectly);
    protected ValueTask InvokeActionAsync<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2, bool invokeDirectly = false)
        => ShortcutActionRunner.InvokeAsync(actionName, arg0, arg1, arg2, invokeDirectly);
    protected ValueTask InvokeActionAsync<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, bool invokeDirectly = false)
        => ShortcutActionRunner.InvokeAsync(actionName, arg0, arg1, arg2, arg3, invokeDirectly);
    protected ValueTask InvokeActionAsync<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool invokeDirectly = false)
        => ShortcutActionRunner.InvokeAsync(actionName, arg0, arg1, arg2, arg3, arg4, invokeDirectly);

    protected void RegisterAction(string actionName, Func<ValueTask> action)
        => ShortcutManager.RegisterAction(actionName, action);
    protected void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<T0, ValueTask> action)
        => ShortcutManager.RegisterAction(actionName, settings0, action);
    protected void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<T0, T1, ValueTask> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, action);
    protected void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<T0, T1, T2, ValueTask> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, action);
    protected void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<T0, T1, T2, T3, ValueTask> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, action);
    protected void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Func<T0, T1, T2, T3, T4, ValueTask> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, settings4, action);

    protected void RegisterAction<TD>(string actionName, Func<TD, ValueTask> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, action);
    protected void RegisterAction<TD, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<TD, T0, ValueTask> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, action);
    protected void RegisterAction<TD, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<TD, T0, T1, ValueTask> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, action);
    protected void RegisterAction<TD, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<TD, T0, T1, T2, ValueTask> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, action);
    protected void RegisterAction<TD, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<TD, T0, T1, T2, T3, ValueTask> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, action);

    protected void RegisterAction(string actionName, Action action)
        => ShortcutManager.RegisterAction(actionName, action);
    protected void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<T0> action)
        => ShortcutManager.RegisterAction(actionName, settings0, action);
    protected void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<T0, T1> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, action);
    protected void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<T0, T1, T2> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, action);
    protected void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<T0, T1, T2, T3> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, action);
    protected void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Action<T0, T1, T2, T3, T4> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, settings4, action);

    protected void RegisterAction<TD>(string actionName, Action<TD> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, action);
    protected void RegisterAction<TD, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<TD, T0> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, action);
    protected void RegisterAction<TD, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<TD, T0, T1> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, action);
    protected void RegisterAction<TD, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<TD, T0, T1, T2> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, action);
    protected void RegisterAction<TD, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<TD, T0, T1, T2, T3> action) where TD : IInputGestureData
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, action);

    protected void UnregisterAction(string actionName) => ShortcutManager.UnregisterAction(actionName);
    #endregion

    #region Property
    [DoNotNotify] protected IReadOnlyObservableConcurrentCollection<string> AvailableProperties => PropertyManager.AvailableProperties;
    protected TOut ReadProperty<TOut>(string propertyName, params object[] arguments) => PropertyManager.GetValue<TOut>(propertyName, arguments);
    protected TOut ReadProperty<TOut>(string propertyName) => PropertyManager.GetValue<TOut>(propertyName);
    protected TOut ReadProperty<T0, TOut>(string propertyName, T0 arg0) => PropertyManager.GetValue<T0, TOut>(propertyName, arg0);
    protected TOut ReadProperty<T0, T1, TOut>(string propertyName, T0 arg0, T1 arg1) => PropertyManager.GetValue<T0, T1, TOut>(propertyName, arg0, arg1);
    #endregion

    #region Message
    protected void PublishMessage(MediaSpeedChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPositionChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPlayingChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPathChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaDurationChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaSeekMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPlayPauseMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaChangePathMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaChangeSpeedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(ChangeScriptMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(SyncRequestMessage message) => EventAggregator.Publish(message);

    protected virtual void HandleMessage(MediaSpeedChangedMessage message) { }
    protected virtual void HandleMessage(MediaPositionChangedMessage message) { }
    protected virtual void HandleMessage(MediaPlayPauseMessage message) { }
    protected virtual void HandleMessage(MediaPathChangedMessage message) { }
    protected virtual void HandleMessage(MediaDurationChangedMessage message) { }
    protected virtual void HandleMessage(MediaSeekMessage message) { }
    protected virtual void HandleMessage(MediaPlayingChangedMessage message) { }
    protected virtual void HandleMessage(MediaChangePathMessage message) { }
    protected virtual void HandleMessage(MediaChangeSpeedMessage message) { }
    protected virtual void HandleMessage(ScriptChangedMessage message) { }
    protected virtual void HandleMessage(SyncRequestMessage message) { }
    protected virtual void HandleMessage(PostScriptSearchMessage message) { }

    protected virtual ValueTask HandleMessageAsync(MediaSpeedChangedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaPositionChangedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaPlayPauseMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaPathChangedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaDurationChangedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaSeekMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaPlayingChangedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaChangePathMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(MediaChangeSpeedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(ScriptChangedMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(SyncRequestMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask HandleMessageAsync(PostScriptSearchMessage message, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private void HandleMessageInternal(object e)
    {
        try
        {
            if (e is MediaSpeedChangedMessage mediaSpeedChangedMessage) HandleMessage(mediaSpeedChangedMessage);
            else if (e is MediaPositionChangedMessage mediaPositionChangedMessage) HandleMessage(mediaPositionChangedMessage);
            else if (e is MediaPlayingChangedMessage mediaPlayingChangedMessage) HandleMessage(mediaPlayingChangedMessage);
            else if (e is MediaPathChangedMessage mediaPathChangedMessage) HandleMessage(mediaPathChangedMessage);
            else if (e is MediaDurationChangedMessage mediaDurationChangedMessage) HandleMessage(mediaDurationChangedMessage);
            else if (e is MediaSeekMessage mediaSeekMessage) HandleMessage(mediaSeekMessage);
            else if (e is MediaPlayPauseMessage mediaPlayPauseMessage) HandleMessage(mediaPlayPauseMessage);
            else if (e is MediaChangePathMessage mediaChangePathMessage) HandleMessage(mediaChangePathMessage);
            else if (e is MediaChangeSpeedMessage mediaChangeSpeedMessage) HandleMessage(mediaChangeSpeedMessage);
            else if (e is ScriptChangedMessage scriptChangedMessage) HandleMessage(scriptChangedMessage);
            else if (e is SyncRequestMessage syncRequestMessage) HandleMessage(syncRequestMessage);
            else if (e is PostScriptSearchMessage postScriptSearchMessage) HandleMessage(postScriptSearchMessage);
        }
        catch { }

        var cancellationSource = CancellationSource;
        if (cancellationSource == null)
            return;
        if (cancellationSource.IsCancellationRequested)
            return;

        var token = cancellationSource.Token;
        try
        {
            var valueTask = e switch
            {
                MediaSpeedChangedMessage mediaSpeedChangedMessage => HandleMessageAsync(mediaSpeedChangedMessage, token),
                MediaPositionChangedMessage mediaPositionChangedMessage => HandleMessageAsync(mediaPositionChangedMessage, token),
                MediaPlayingChangedMessage mediaPlayingChangedMessage => HandleMessageAsync(mediaPlayingChangedMessage, token),
                MediaPathChangedMessage mediaPathChangedMessage => HandleMessageAsync(mediaPathChangedMessage, token),
                MediaDurationChangedMessage mediaDurationChangedMessage => HandleMessageAsync(mediaDurationChangedMessage, token),
                MediaSeekMessage mediaSeekMessage => HandleMessageAsync(mediaSeekMessage, token),
                MediaPlayPauseMessage mediaPlayPauseMessage => HandleMessageAsync(mediaPlayPauseMessage, token),
                MediaChangePathMessage mediaChangePathMessage => HandleMessageAsync(mediaChangePathMessage, token),
                MediaChangeSpeedMessage mediaChangeSpeedMessage => HandleMessageAsync(mediaChangeSpeedMessage, token),
                ScriptChangedMessage scriptChangedMessage => HandleMessageAsync(scriptChangedMessage, token),
                SyncRequestMessage syncRequestMessage => HandleMessageAsync(syncRequestMessage, token),
                PostScriptSearchMessage postScriptSearchMessage => HandleMessageAsync(postScriptSearchMessage, token),
                _ => ValueTask.CompletedTask
            };

            valueTask.Preserve().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private sealed class MessageProxy(Action<object> callback) : IHandle<object>
    {
        public void Handle(object message) => callback(message);
    }
    #endregion

    public virtual UIElement CreateView() => null;

    protected virtual void OnInitialize() { }

    internal void InternalInitialize()
    {
        EventAggregator.Subscribe(_messageProxy);
        OnInitialize();
    }

    protected virtual void OnDispose() { }

    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "Internal dispose")]
    internal void InternalDispose()
    {
        EventAggregator.Unsubscribe(_messageProxy);

        CancellationSource.Cancel();
        CancellationSource.Dispose();

        OnDispose();
        GC.SuppressFinalize(this);
    }

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
            settings.MergeAll(JObject.FromObject(this));
        else if (action == SettingsAction.Loading)
            settings.Populate(this);
    }
}