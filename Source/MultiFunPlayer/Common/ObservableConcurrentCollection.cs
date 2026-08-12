using PropertyChanged;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;

namespace MultiFunPlayer.Common;

public interface IReadOnlyObservableConcurrentCollection<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    public int IndexOf(T item);
}

[DoNotNotify]
public sealed class ObservableConcurrentCollection<T> : IList<T>, IReadOnlyObservableConcurrentCollection<T>, IList
{
    private readonly Lock _syncRoot = new();
    private readonly SynchronizationContext _context;
    private readonly List<T> _items;

    public ObservableConcurrentCollection() : this([]) { }
    public ObservableConcurrentCollection(IEnumerable<T> elements)
    {
        _context = AsyncOperationManager.SynchronizationContext;
        _items = new List<T>(elements);

#pragma warning disable CS9216 // A value of type 'System.Threading.Lock' converted to a different type will use likely unintended monitor-based locking in 'lock' statement.
        BindingOperations.EnableCollectionSynchronization(this, _syncRoot, static (_, syncRoot, action, _) =>
        {
            lock ((Lock)syncRoot)
                action();
        });
#pragma warning restore CS9216 // A value of type 'System.Threading.Lock' converted to a different type will use likely unintended monitor-based locking in 'lock' statement.
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyObserversOfChange(NotifyCollectionChangedEventArgs e)
    {
        _context.Send(_ =>
        {
            CollectionChanged?.Invoke(this, e);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }, null);
    }

    public void Refresh() => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    public int Count
    {
        get { lock (_syncRoot) { return _items.Count; } }
    }

    public T this[int index]
    {
        get { lock (_syncRoot) { return _items[index]; } }
        set
        {
            if (index < 0 || index >= _items.Count)
                throw new IndexOutOfRangeException();

            var oldItem = default(T);
            lock (_syncRoot)
            {
                oldItem = _items[index];
                _items[index] = value;
            }

            NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, oldItem, value));
        }
    }

    public void Add(T item)
    {
        lock (_syncRoot)
            _items.Add(item);

        NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, Count - 1));
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Insert(Count, item);
    }

    public void SetFrom(IEnumerable<T> items)
    {
        lock (_syncRoot)
        {
            _items.Clear();
            _items.AddRange(items);
        }

        Refresh();
    }

    public void Clear()
    {
        lock (_syncRoot)
            _items.Clear();

        Refresh();
    }

    public void Insert(int index, T item)
    {
        if (index < 0 || index > _items.Count)
            throw new IndexOutOfRangeException();

        lock (_syncRoot)
            _items.Insert(index, item);

        NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex)
            return;

        if (oldIndex < 0 || oldIndex >= _items.Count)
            throw new IndexOutOfRangeException();

        if (newIndex < 0 || newIndex > _items.Count)
            throw new IndexOutOfRangeException();

        var item = default(T);
        lock (_syncRoot)
        {
            item = _items[oldIndex];
            _items.RemoveAt(oldIndex);
            _items.Insert(newIndex, item);
        }

        NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex));
    }

    public bool Remove(T item)
    {
        var index = -1;
        lock (_syncRoot)
        {
            index = _items.IndexOf(item);
            if (index < 0)
                return false;

            _items.RemoveAt(index);
        }

        NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        return true;
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Remove(item);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
            throw new IndexOutOfRangeException();

        var item = default(T);
        lock (_syncRoot)
        {
            item = _items[index];
            _items.RemoveAt(index);
        }

        NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    int IList.Add(object value)
    {
        lock (_syncRoot)
        {
            _items.Add((T)value);
            return _items.Count - 1;
        }
    }

    public void CopyTo(T[] array, int index) { lock (_syncRoot) { _items.CopyTo(array, index); } }
    public bool Contains(T item) { lock (_syncRoot) { return _items.Contains(item); } }
    public int IndexOf(T item) { lock (_syncRoot) { return _items.IndexOf(item); } }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<T> GetEnumerator() { lock (_syncRoot) { return _items.ToList().GetEnumerator(); } }

    bool IList.Contains(object value) => value is T x && Contains(x);
    int IList.IndexOf(object value) => value is T x ? IndexOf(x) : -1;
    void IList.Insert(int index, object value)
    {
        if (value is T x)
            Insert(index, x);
    }

    void IList.Remove(object value)
    {
        if (value is T x)
            Remove(x);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        lock (_syncRoot)
            Array.Copy(_items.ToArray(), 0, array, index, Count);
    }

    bool ICollection<T>.IsReadOnly => false;

    public bool IsFixedSize => false;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    object ICollection.SyncRoot => null;

    object IList.this[int index] { get => this[index]; set => this[index] = (T)value; }
}