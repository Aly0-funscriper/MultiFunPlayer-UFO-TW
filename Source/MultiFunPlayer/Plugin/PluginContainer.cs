using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using NLog;
using Stylet;
using System.IO;
using System.Windows;

namespace MultiFunPlayer.Plugin;

internal enum PluginState
{
    Idle,
    Compiling,
    Running,
    Stopping,
    Faulted
}

internal sealed class PluginContainer : PropertyChangedBase, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IShortcutManager _shortcutManager;
    private PluginCompilationResult _compilationResult;

    public FileInfo PluginFile { get; }
    public Exception Exception { get; private set; }
    public PluginState State { get; private set; }

    public string Name => Path.GetFileNameWithoutExtension(PluginFile.Name);
    public UIElement View => _compilationResult?.PluginInstance?.View;

    public bool CanCompile => State is not PluginState.Compiling;

    public PluginContainer(IShortcutManager shortcutManager, FileInfo pluginFile)
    {
        _shortcutManager = shortcutManager;
        PluginFile = pluginFile;
        QueueCompile();
    }

    public void QueueCompile()
    {
        if (!PluginFile.Exists || State == PluginState.Compiling)
            return;

        State = PluginState.Compiling;
        PluginCompiler.QueueCompile(PluginFile, result =>
        {
            if (_compilationResult != null)
                Dispose();

            _compilationResult = result;
            if (_compilationResult.Success)
            {
                RegisterActions();
                _compilationResult.PluginInstance.InternalInitialize();
                State = PluginState.Running;
                Exception = null;
            }
            else
            {
                State = PluginState.Faulted;
                Exception = _compilationResult.Exception;
            }

            HandleSettings(SettingsAction.Loading);
            NotifyOfPropertyChange(nameof(View));
        });
    }

    public void ShowView()
    {
        if (_compilationResult?.Success == true)
            _ = DialogHelper.ShowAsync(() => new PluginDialog(_compilationResult.PluginInstance), "PluginDialog");
    }

    public void CloseView()
    {
        if (_compilationResult?.Success == true)
            DialogHelper.Close("PluginDialog");
    }

    private void RegisterActions()
    {
        _shortcutManager.RegisterAction($"Plugin::{Name}::ShowView", () => _compilationResult?.PluginInstance?.ShowView());
        _shortcutManager.RegisterAction($"Plugin::{Name}::CloseView", () => _compilationResult?.PluginInstance?.CloseView());
    }

    private void UnregisterActions()
    {
        _shortcutManager.UnregisterAction($"Plugin::{Name}::ShowView");
        _shortcutManager.UnregisterAction($"Plugin::{Name}::CloseView");
    }

    private void HandleSettings(SettingsAction action)
    {
        if (_compilationResult?.Success != true)
            return;

        var settingsFileName = $"{Path.GetFileNameWithoutExtension(PluginFile.Name)}.config.json";
        var settingsPath = Path.Join(PluginFile.DirectoryName, settingsFileName);
        var settings = SettingsHelper.ReadOrEmpty(settingsPath);

        try
        {
            _compilationResult.PluginInstance.HandleSettings(settings, action);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Plugin settings failed with exception [Action: {0}]", action);
        }

        if (action == SettingsAction.Saving && settings.HasValues)
            SettingsHelper.Write(settings, settingsPath);
    }

    private void Dispose(bool disposing)
    {
        State = PluginState.Stopping;

        CloseView();

        HandleSettings(SettingsAction.Saving);
        UnregisterActions();

        _compilationResult?.Dispose();
        _compilationResult = null;

        NotifyOfPropertyChange(nameof(View));
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
