using MultiFunPlayer.Common;

namespace MultiFunPlayer.Script;

public interface IScriptResource
{
    string Name { get; }
    string Source { get; }
    KeyframeCollection Keyframes { get; }
    ChapterCollection Chapters { get; }
    BookmarkCollection Bookmarks { get; }
}

public record ScriptResource(string Name, string Source, KeyframeCollection Keyframes, ChapterCollection Chapters, BookmarkCollection Bookmarks) : IScriptResource
{
    public ScriptResource() : this(null, null, null, null, null) { }

    public static LinkedScriptResource LinkTo(IScriptResource other) => other != null ? new LinkedScriptResource(other) : null;

    public virtual bool Equals(ScriptResource other)
        => other != null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && (Keyframes == other.Keyframes || Keyframes?.Equals(other.Keyframes) == true)
            && (Chapters == other.Chapters || Chapters?.Equals(other.Chapters) == true)
            && (Bookmarks == other.Bookmarks || Bookmarks?.Equals(other.Bookmarks) == true);

    public override int GetHashCode() => HashCode.Combine(Name, Source, Keyframes, Chapters, Bookmarks);
}

public sealed record LinkedScriptResource : ScriptResource
{
    public LinkedScriptResource(IScriptResource linked)
        : base(linked.Name, linked.Source, linked.Keyframes, linked.Chapters, linked.Bookmarks) { }
}