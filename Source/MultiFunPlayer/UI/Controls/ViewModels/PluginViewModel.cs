using MultiFunPlayer.Common;
using MultiFunPlayer.Plugin;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class PluginViewModel : Screen, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IShortcutManager _shortcutManager;
    private FileSystemWatcher _watcher;

    public ObservableConcurrentDictionary<FileInfo, PluginContainer> Containers { get; }

    public PluginViewModel(IShortcutManager shortcutManager)
    {
        _shortcutManager = shortcutManager;

        var pluginsDirectory = Directory.CreateDirectory("Plugins");

        Containers = new ObservableConcurrentDictionary<FileInfo, PluginContainer>(new FileInfoFullNameComparer());
        _watcher = new FileSystemWatcher()
        {
            Filter = "*.*",
            Path = pluginsDirectory.FullName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true
        };

        _watcher.Created += OnWatcherCreated;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.Deleted += OnWatcherDeleted;

        foreach (var fileInfo in pluginsDirectory.SafeEnumerateFiles("*.cs", IOUtils.CreateEnumerationOptions(true)))
            AddContainer(fileInfo);
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        Logger.Trace("Received watcher renamed event [From: \"{0}\", To: \"{1}\"", e.OldFullPath, e.FullPath);

        if (Directory.Exists(e.OldFullPath) || Directory.Exists(e.FullPath))
        {
            foreach (var (pluginFile, _) in Containers)
                if (IsBasePathOf(e.OldFullPath, pluginFile.DirectoryName))
                    RemoveContainer(pluginFile);

            var newDirectory = new DirectoryInfo(e.FullPath);
            foreach(var pluginFile in newDirectory.SafeEnumerateFiles("*.cs", IOUtils.CreateEnumerationOptions(true)))
                AddContainer(pluginFile);

            static bool IsBasePathOf(string basePath, string subPath)
            {
                var relativePath = Path.GetRelativePath(subPath.Replace('\\', '/'), basePath.Replace('\\', '/'));
                return relativePath == "." || relativePath.EndsWith("..");
            }
        }
        else if (File.Exists(e.OldFullPath) || File.Exists(e.FullPath))
        {
            RemoveContainer(new FileInfo(e.OldFullPath));
            AddContainer(new FileInfo(e.FullPath));
        }
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        Logger.Trace("Received watcher deleted event [Path: \"{0}\"", e.FullPath);
        RemoveContainer(new FileInfo(e.FullPath));
    }

    private void OnWatcherCreated(object sender, FileSystemEventArgs e)
    {
        Logger.Trace("Received watcher created event [Path: \"{0}\"", e.FullPath);
        AddContainer(new FileInfo(e.FullPath));
    }

    private void RemoveContainer(FileInfo fileInfo)
    {
        if (!Containers.TryGetValue(fileInfo, out var container))
            return;

        Logger.Debug("Removing container [Path: \"{0}\"", fileInfo);
        container.Dispose();

        Containers.Remove(fileInfo);
    }

    private void AddContainer(FileInfo fileInfo)
    {
        if (!fileInfo.AsRefreshed().Exists)
            return;

        if (fileInfo.Extension != ".cs")
            return;

        if (Containers.ContainsKey(fileInfo))
            return;

        Logger.Debug("Adding container [Path: \"{0}\"", fileInfo);
        Containers.Add(fileInfo, new PluginContainer(_shortcutManager, fileInfo));
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("Plugin")
             || !message.Settings.TryGetObject(out var settings, "Plugin"))
                return;

            settings[nameof(Containers)] = JToken.FromObject(Containers);
        }
        else if (message.Action == SettingsAction.Loading)
        {
            foreach (var (_, container) in Containers)
            {
                if (!message.Settings.TryGetObject(out var containerSettings, "Plugin", nameof(Containers), container.PluginFile.FullName))
                    containerSettings = [];

                containerSettings.Populate(container);
            }
        }
    }

    private void Dispose(bool disposing)
    {
        _watcher?.Dispose();
        _watcher = null;

        foreach (var (_, container) in Containers)
            container.Dispose();

        Containers.Clear();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private sealed class FileInfoFullNameComparer : IEqualityComparer<FileInfo>
    {
        public bool Equals(FileInfo x, FileInfo y) => EqualityComparer<string>.Default.Equals(x?.FullName, y?.FullName);
        public int GetHashCode([DisallowNull] FileInfo obj) => HashCode.Combine(obj.FullName);
    }
}
