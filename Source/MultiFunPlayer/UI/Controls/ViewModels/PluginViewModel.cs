using MultiFunPlayer.Common;
using MultiFunPlayer.Plugin;
using PropertyChanged;
using Stylet;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class PluginViewModel : Conductor<PluginContainer>.Collection.OneActive
{
    private readonly IReadOnlyObservableCollection<PluginContainer> _source;

    public bool ContentVisible { get; set; }

    public PluginViewModel(IPluginManager pluginManager)
    {
        _source = pluginManager.Containers;
        _source.CollectionChanged += OnSourceCollectionChanged;

        OnSourceCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    [SuppressPropertyChangedWarnings]
    private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        var oldItems = e.OldItems?.Cast<PluginContainer>() ?? [];
        var newItems = e.NewItems?.Cast<PluginContainer>() ?? [];
        var oldIndex = MapIndex(e.OldStartingIndex);
        var newIndex = MapIndex(e.NewStartingIndex);

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AddItems(newItems, newIndex);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveItems(oldItems);
                break;
            case NotifyCollectionChangedAction.Replace:
                RemoveItems(oldItems);
                AddItems(newItems, newIndex == -1 ? MapIndex(newItems.Min(_source.IndexOf)) : newIndex);
                break;
            case NotifyCollectionChangedAction.Move:
                RemoveItems(oldItems);
                AddItems(newItems, newIndex);
                break;
            case NotifyCollectionChangedAction.Reset:
                RemoveItems(Items);
                if (Items.Count != 0)
                    throw new UnreachableException();

                AddItems(_source, -1);
                break;
        }
    }

    private void AddItems(IEnumerable<PluginContainer> items, int index)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= OnContainerPropertyChanged;
            item.PropertyChanged += OnContainerPropertyChanged;
        }

        if (index == -1)
        {
            Items.AddRange(items.Where(c => c.View != null));
        }
        else
        {
            foreach (var item in items.Where(c => c.View != null))
                Items.Insert(index++, item);
        }
    }

    private void RemoveItems(IEnumerable<PluginContainer> items)
    {
        foreach (var item in items)
            item.PropertyChanged -= OnContainerPropertyChanged;

        Items.RemoveRange(items.Where(c => c.View != null));
    }

    private int MapIndex(int index)
    {
        if (index == -1)
            return -1;

        while (--index >= 0)
        {
            var ourIndex = Items.IndexOf(_source[index]);
            if (ourIndex >= 0)
                return ourIndex + 1;
        }

        return 0;
    }

    [SuppressPropertyChangedWarnings]
    private void OnContainerPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginContainer.View))
        {
            var container = (PluginContainer)sender;
            if (container.View == null)
                Items.Remove(container);
            else
                Items.Insert(MapIndex(_source.IndexOf(container)), container);
        }
    }
}
