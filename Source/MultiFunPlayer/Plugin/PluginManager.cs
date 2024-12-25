using MultiFunPlayer.Common;
using NLog;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MultiFunPlayer.Plugin;

internal interface IPluginManager : IDisposable
{
    public IReadOnlyObservableConcurrentCollection<PluginContainer> Containers { get; }
}

internal sealed class PluginManager : IPluginManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly FileInfoFullNameComparer _comparer = new();
    private readonly ObservableConcurrentCollection<PluginContainer> _containers = [];
    private FileSystemWatcher _watcher;

    public IReadOnlyObservableConcurrentCollection<PluginContainer> Containers => _containers;

    public PluginManager()
    {
        var pluginsDirectory = Directory.CreateDirectory("Plugins");

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
            foreach (var container in _containers)
                if (IsBasePathOf(e.OldFullPath, container.File.DirectoryName))
                    RemoveContainer(container.File);

            var newDirectory = new DirectoryInfo(e.FullPath);
            foreach (var pluginFile in newDirectory.SafeEnumerateFiles("*.cs", IOUtils.CreateEnumerationOptions(true)))
                AddContainer(pluginFile);
        }
        else if (File.Exists(e.OldFullPath) || File.Exists(e.FullPath))
        {
            RemoveContainer(new FileInfo(e.OldFullPath));
            AddContainer(new FileInfo(e.FullPath));

            if (TryChangeExtension(e.OldFullPath, ".xaml", ".cs", out var csOldFullPath))
                RemoveContainer(new FileInfo(csOldFullPath));
            if (TryChangeExtension(e.FullPath, ".xaml", ".cs", out var csFullPath))
                AddContainer(new FileInfo(csFullPath));
        }
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        Logger.Trace("Received watcher deleted event [Path: \"{0}\"", e.FullPath);

        foreach (var container in _containers)
            if (IsBasePathOf(e.FullPath, container.File.DirectoryName))
                RemoveContainer(container.File);

        RemoveContainer(new FileInfo(e.FullPath));

        if (TryChangeExtension(e.FullPath, ".xaml", ".cs", out var csFullPath))
            foreach (var container in _containers)
                if (_comparer.Equals(container.File, new FileInfo(csFullPath)))
                    container.QueueCompile();
    }

    private bool IsBasePathOf(string basePath, string subPath)
    {
        var relativePath = Path.GetRelativePath(subPath.Replace('\\', '/'), basePath.Replace('\\', '/'));
        return relativePath == "." || relativePath.EndsWith("..");
    }

    private bool TryChangeExtension(string path, string extensionFrom, string extensionTo, out string result)
        => !string.IsNullOrWhiteSpace(result = string.Equals(Path.GetExtension(path), extensionFrom, StringComparison.OrdinalIgnoreCase)
                                                ? Path.ChangeExtension(path, extensionTo) : null);

    private void OnWatcherCreated(object sender, FileSystemEventArgs e)
    {
        Logger.Trace("Received watcher created event [Path: \"{0}\"", e.FullPath);
        AddContainer(new FileInfo(e.FullPath));

        if (TryChangeExtension(e.FullPath, ".xaml", ".cs", out var csFullPath))
            foreach (var container in _containers)
                if (_comparer.Equals(container.File, new FileInfo(csFullPath)))
                    container.QueueCompile();
    }

    private void RemoveContainer(FileInfo fileInfo)
    {
        var container = _containers.SingleOrDefault(c => _comparer.Equals(c.File, fileInfo));
        if (container == null)
            return;

        Logger.Debug("Removing container [Path: \"{0}\"", fileInfo);
        container.Dispose();

        _containers.Remove(container);
    }

    private void AddContainer(FileInfo fileInfo)
    {
        if (!fileInfo.AsRefreshed().Exists)
            return;

        if (fileInfo.Extension != ".cs")
            return;

        if (_containers.Any(c => _comparer.Equals(c.File, fileInfo)))
            return;

        Logger.Debug("Adding container [Path: \"{0}\"", fileInfo);
        var container = new PluginContainer(fileInfo);
        _containers.Add(container);
    }

    private void Dispose(bool disposing)
    {
        _watcher?.Dispose();
        _watcher = null;

        foreach (var container in _containers)
            container.Dispose();

        _containers.Clear();
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
