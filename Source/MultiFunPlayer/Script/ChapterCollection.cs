using System.Collections;

namespace MultiFunPlayer.Script;

public sealed class ChapterCollection : IReadOnlyList<Chapter>
{
    private readonly List<Chapter> _items;

    public ChapterCollection() => _items = [];
    public ChapterCollection(int capacity) => _items = new List<Chapter>(capacity);
    public ChapterCollection(IEnumerable<Chapter> collection)
    {
        _items = [];
        foreach (var chapter in collection)
            Add(chapter);
    }

    public bool Add(Chapter chapter) => Add(chapter.Name, chapter.StartPosition, chapter.EndPosition);
    public bool Add(string name, TimeSpan startPosition, TimeSpan endPosition) => Add(name, startPosition.TotalSeconds, endPosition.TotalSeconds);
    public bool Add(string name, double startPosition, double endPosition)
    {
        if (startPosition > endPosition)
            (startPosition, endPosition) = (endPosition, startPosition);

        foreach (var chapter in _items)
            if (chapter.StartPosition >= startPosition && chapter.EndPosition <= endPosition)
                return false;

        _ = TryFindIntersecting(startPosition, out var startIntersect);
        _ = TryFindIntersecting(endPosition, out var endIntersect);
        if (startIntersect == endIntersect && startIntersect != null)
            return false;

        if (startIntersect != null)
            startPosition = startIntersect.EndPosition;

        if (endIntersect != null)
            endPosition = endIntersect.StartPosition;

        var index = SearchForIndexAfter(startPosition);
        _items.Insert(index, new Chapter(name, startPosition, endPosition));
        return true;
    }

    public bool TryFindIntersecting(double position, out Chapter chapter)
    {
        chapter = _items.FirstOrDefault(x => position >= x.StartPosition && position <= x.EndPosition);
        return chapter != null;
    }

    public bool TryFindIntersecting(double position, double epsilon, out Chapter chapter)
    {
        chapter = _items.FirstOrDefault(x => position >= x.StartPosition && position <= x.EndPosition)
               ?? _items.FirstOrDefault(x => position >= x.StartPosition - epsilon && position <= x.EndPosition + epsilon);
        return chapter != null;
    }

    public bool TryFindByName(string name, out Chapter chapter)
    {
        chapter = _items.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return chapter != null;
    }

    public int SearchForIndexBefore(double position) => SearchForIndexAfter(position) - 1;
    public int SearchForIndexAfter(double position)
    {
        if (_items.Count == 0 || position < _items[0].StartPosition)
            return 0;

        if (position > _items[^1].StartPosition)
            return Count;

        var bestIndex = _items.BinarySearch(new Chapter(null, position, position), ChapterStartPositionComparer.Default);
        if (bestIndex >= 0)
            return bestIndex;

        bestIndex = ~bestIndex;
        return bestIndex == Count ? Count : bestIndex;
    }

    public override bool Equals(object obj)
    => obj is ChapterCollection collection
        && collection.Count == _items.Count
        && this.SequenceEqual(collection);

    public override int GetHashCode()
    {
        var result = new HashCode();
        foreach (var item in _items)
            result.Add(item.GetHashCode());
        return result.ToHashCode();
    }

    #region IReadOnlyList
    public Chapter this[int index] => _items[index];
    #endregion

    #region IReadOnlyCollection
    public int Count => _items.Count;
    #endregion

    #region IEnumerable
    public IEnumerator<Chapter> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    #endregion
}
