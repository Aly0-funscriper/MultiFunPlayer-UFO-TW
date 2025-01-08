using MultiFunPlayer.Input;
using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutActionRunner
{
    bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback);

    void Invoke(string actionName, bool invokeDirectly);
    void Invoke<T0>(string actionName, T0 arg0, bool invokeDirectly);
    void Invoke<T0, T1>(string actionName, T0 arg0, T1 arg1, bool invokeDirectly);
    void Invoke<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2, bool invokeDirectly);
    void Invoke<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, bool invokeDirectly);
    void Invoke<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool invokeDirectly);

    ValueTask InvokeAsync(string actionName, bool invokeDirectly);
    ValueTask InvokeAsync<T0>(string actionName, T0 arg0, bool invokeDirectly);
    ValueTask InvokeAsync<T0, T1>(string actionName, T0 arg0, T1 arg1, bool invokeDirectly);
    ValueTask InvokeAsync<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2, bool invokeDirectly);
    ValueTask InvokeAsync<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, bool invokeDirectly);
    ValueTask InvokeAsync<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool invokeDirectly);
}

internal sealed class ShortcutActionRunner : IShortcutActionRunner, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IShortcutActionResolver _actionResolver;

    private BlockingCollection<(IInvokableItem Item, Action Callback)> _scheduledItems;
    private Thread _thread;

    public ShortcutActionRunner(IShortcutActionResolver actionResolver)
    {
        _actionResolver = actionResolver;
        _scheduledItems = [];

        _thread = new Thread(ConsumeItems);
        _thread.Start();
    }

    private void ConsumeItems()
    {
        foreach (var (item, callback) in _scheduledItems.GetConsumingEnumerable())
        {
            item.Invoke(_actionResolver);
            callback?.Invoke();
        }
    }

    private bool ScheduleInvoke(IInvokableItem item, Action callback) => _scheduledItems.TryAdd((item, callback));
    public bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback)
        => ScheduleInvoke(new GestureInvokableItem(configurations, gestureData), callback);

    public void Invoke(string actionName, bool invokeDirectly)
        => InvokeOrSchedule(new ManualInvokableItem(actionName), invokeDirectly);
    public void Invoke<T0>(string actionName, T0 arg0, bool invokeDirectly)
        => InvokeOrSchedule(new ManualInvokableItem<T0>(actionName, arg0), invokeDirectly);
    public void Invoke<T0, T1>(string actionName, T0 arg0, T1 arg1, bool invokeDirectly)
        => InvokeOrSchedule(new ManualInvokableItem<T0, T1>(actionName, arg0, arg1), invokeDirectly);
    public void Invoke<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2, bool invokeDirectly)
        => InvokeOrSchedule(new ManualInvokableItem<T0, T1, T2>(actionName, arg0, arg1, arg2), invokeDirectly);
    public void Invoke<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, bool invokeDirectly)
        => InvokeOrSchedule(new ManualInvokableItem<T0, T1, T2, T3>(actionName, arg0, arg1, arg2, arg3), invokeDirectly);
    public void Invoke<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool invokeDirectly)
        => InvokeOrSchedule(new ManualInvokableItem<T0, T1, T2, T3, T4>(actionName, arg0, arg1, arg2, arg3, arg4), invokeDirectly);

    public ValueTask InvokeAsync(string actionName, bool invokeDirectly)
        => InvokeOrScheduleAsync(new ManualInvokableItem(actionName), invokeDirectly);
    public ValueTask InvokeAsync<T0>(string actionName, T0 arg0, bool invokeDirectly)
        => InvokeOrScheduleAsync(new ManualInvokableItem<T0>(actionName, arg0), invokeDirectly);
    public ValueTask InvokeAsync<T0, T1>(string actionName, T0 arg0, T1 arg1, bool invokeDirectly)
        => InvokeOrScheduleAsync(new ManualInvokableItem<T0, T1>(actionName, arg0, arg1), invokeDirectly);
    public ValueTask InvokeAsync<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2, bool invokeDirectly)
        => InvokeOrScheduleAsync(new ManualInvokableItem<T0, T1, T2>(actionName, arg0, arg1, arg2), invokeDirectly);
    public ValueTask InvokeAsync<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, bool invokeDirectly)
        => InvokeOrScheduleAsync(new ManualInvokableItem<T0, T1, T2, T3>(actionName, arg0, arg1, arg2, arg3), invokeDirectly);
    public ValueTask InvokeAsync<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool invokeDirectly)
        => InvokeOrScheduleAsync(new ManualInvokableItem<T0, T1, T2, T3, T4>(actionName, arg0, arg1, arg2, arg3, arg4), invokeDirectly);

    private ValueTask InvokeOrScheduleAsync(IInvokableItem item, bool invokeDirectly)
    {
        if (invokeDirectly)
        {
            item.Invoke(_actionResolver);
            return ValueTask.CompletedTask;
        }
        else
        {
            var completionSource = new TaskCompletionSource();
            if (!ScheduleInvoke(item, completionSource.SetResult))
                return ValueTask.CompletedTask;

            return new ValueTask(completionSource.Task);
        }
    }

    private void InvokeOrSchedule(IInvokableItem item, bool invokeDirectly)
    {
        if (invokeDirectly)
        {
            item.Invoke(_actionResolver);
        }
        else
        {
            using var callbackEvent = new ManualResetEventSlim();
            if (!ScheduleInvoke(item, callbackEvent.Set))
                return;

            callbackEvent.Wait();
        }
    }

    private void Dispose(bool disposing)
    {
        _scheduledItems?.CompleteAdding();
        _thread?.Join();
        _scheduledItems?.Dispose();

        _scheduledItems = null;
        _thread = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private interface IInvokableItem
    {
        void Invoke(IShortcutActionResolver actionResolver);
    }

    private readonly record struct GestureInvokableItem(IEnumerable<IShortcutActionConfiguration> Configurations, IInputGestureData GestureData) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            foreach (var configuration in Configurations)
            {
                if (actionResolver.TryGetAction(configuration.Name, out var action))
                {
                    if (Logger.IsTraceEnabled)
                        Logger.Trace("Invoking \"{0}\" action [Configuration: \"{1}\", Gesture: {2}]",
                            configuration.Name, string.Join(", ", configuration.Settings.Select(s => s.ToString())), GestureData);

                    action.Invoke(configuration, GestureData).Preserve().GetAwaiter().GetResult();
                }
            }
        }
    }

    private readonly record struct ManualInvokableItem(string ActionName) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            if (actionResolver.TryGetAction(ActionName, out var action) && action is ShortcutAction concreteAction)
            {
                Logger.Trace("Invoking \"{name}\" action", ActionName);
                concreteAction.Invoke().Preserve().GetAwaiter().GetResult();
            }
        }
    }

    private readonly record struct ManualInvokableItem<T0>(string ActionName, T0 Arg0) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            if (actionResolver.TryGetAction(ActionName, out var action) && action is ShortcutAction<T0> concreteAction)
            {
                Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}\"]", ActionName, Arg0);
                concreteAction.Invoke(Arg0).Preserve().GetAwaiter().GetResult();
            }
        }
    }

    private readonly record struct ManualInvokableItem<T0, T1>(string ActionName, T0 Arg0, T1 Arg1) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            if (actionResolver.TryGetAction(ActionName, out var action) && action is ShortcutAction<T0, T1> concreteAction)
            {
                Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}\"]", ActionName, Arg0, Arg1);
                concreteAction.Invoke(Arg0, Arg1).Preserve().GetAwaiter().GetResult();
            }
        }
    }

    private readonly record struct ManualInvokableItem<T0, T1, T2>(string ActionName, T0 Arg0, T1 Arg1, T2 Arg2) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            if (actionResolver.TryGetAction(ActionName, out var action) && action is ShortcutAction<T0, T1, T2> concreteAction)
            {
                Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}\"]", ActionName, Arg0, Arg1, Arg2);
                concreteAction.Invoke(Arg0, Arg1, Arg2).Preserve().GetAwaiter().GetResult();
            }
        }
    }

    private readonly record struct ManualInvokableItem<T0, T1, T2, T3>(string ActionName, T0 Arg0, T1 Arg1, T2 Arg2, T3 Arg3) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            if (actionResolver.TryGetAction(ActionName, out var action) && action is ShortcutAction<T0, T1, T2, T3> concreteAction)
            {
                Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}, {4}\"]", ActionName, Arg0, Arg1, Arg2, Arg3);
                concreteAction.Invoke(Arg0, Arg1, Arg2, Arg3).Preserve().GetAwaiter().GetResult();
            }
        }
    }

    private readonly record struct ManualInvokableItem<T0, T1, T2, T3, T4>(string ActionName, T0 Arg0, T1 Arg1, T2 Arg2, T3 Arg3, T4 Arg4) : IInvokableItem
    {
        public void Invoke(IShortcutActionResolver actionResolver)
        {
            if (actionResolver.TryGetAction(ActionName, out var action) && action is ShortcutAction<T0, T1, T2, T3, T4> concreteAction)
            {
                Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}, {4}, {5}\"]", ActionName, Arg0, Arg1, Arg2, Arg3, Arg4);
                concreteAction.Invoke(Arg0, Arg1, Arg2, Arg3, Arg4).Preserve().GetAwaiter().GetResult();
            }
        }
    }
}