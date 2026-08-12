using MultiFunPlayer.Common;
using MultiFunPlayer.Script;

namespace MultiFunPlayer.Tests;

public class ScriptResourceTests
{
    private static KeyframeCollection RandomKeyframes(int count, int seed)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, count).Select(i => new Keyframe(i + random.NextDouble(), random.NextDouble()))];
    }

    private static ChapterCollection RandomChapters(int count, int seed)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, count).Select(i => new Chapter(RandomString(random), random.Next(i * 100, i * 100 + 50), random.Next(i * 100 + 50, (i + 1) * 100)))];
    }

    private static BookmarkCollection RandomBookmarks(int count, int seed)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, count).Select(_ => new Bookmark(RandomString(random), random.Next(0, 100)))];
    }

    private static string RandomString(int seed) => RandomString(new Random(seed));
    private static string RandomString(Random random)
    {
        var bytes = (stackalloc byte[16]);
        random.NextBytes(bytes);
        return new Guid(bytes).ToString("n");
    }

    public static IEnumerable<object[]> EqualScriptResources => [
        [new ScriptResource(null, null, null, null, null),
         new ScriptResource(null, null, null, null, null)],

        [new ScriptResource(RandomString(0), null,            null,                  null,                 null),
         new ScriptResource(RandomString(0), null,            null,                  null,                 null)],
        [new ScriptResource(RandomString(0), RandomString(0), null,                  null,                 null),
         new ScriptResource(RandomString(0), RandomString(0), null,                  null,                 null)],
        [new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), null,                 null),
         new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), null,                 null)],
        [new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), RandomChapters(2, 0), null),
         new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), RandomChapters(2, 0), null)],
        [new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), RandomChapters(2, 0), RandomBookmarks(2, 0)),
         new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), RandomChapters(2, 0), RandomBookmarks(2, 0))],
    ];

    [Theory]
    [MemberData(nameof(EqualScriptResources))]
    public void ScriptResourceEqual(ScriptResource first, ScriptResource second)
        => Assert.True(first.Equals(second) && first == second && first.GetHashCode() == second.GetHashCode());

    public static IEnumerable<object[]> NotEqualScriptResources => [
        [new ScriptResource(null, null, null, null, null),
         null],

        [new ScriptResource(RandomString(0), null,            null,                  null,                 null),
         new ScriptResource(RandomString(1), null,            null,                  null,                 null)],
        [new ScriptResource(RandomString(0), RandomString(0), null,                  null,                 null),
         new ScriptResource(RandomString(1), RandomString(1), null,                  null,                 null)],
        [new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), null,                 null),
         new ScriptResource(RandomString(1), RandomString(1), RandomKeyframes(2, 1), null,                 null)],
        [new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), RandomChapters(2, 0), null),
         new ScriptResource(RandomString(1), RandomString(1), RandomKeyframes(2, 1), RandomChapters(2, 1), null)],
        [new ScriptResource(RandomString(0), RandomString(0), RandomKeyframes(2, 0), RandomChapters(2, 0), RandomBookmarks(2, 0)),
         new ScriptResource(RandomString(1), RandomString(1), RandomKeyframes(2, 1), RandomChapters(2, 1), RandomBookmarks(2, 1))],

        [new ScriptResource(null, null, RandomKeyframes(2, 0), null,                 null),
         new ScriptResource(null, null, RandomKeyframes(3, 0), null,                 null)],
        [new ScriptResource(null, null, RandomKeyframes(2, 0), RandomChapters(2, 0), null),
         new ScriptResource(null, null, RandomKeyframes(3, 0), RandomChapters(3, 0), null)],
        [new ScriptResource(null, null, RandomKeyframes(2, 0), RandomChapters(2, 0), RandomBookmarks(2, 0)),
         new ScriptResource(null, null, RandomKeyframes(3, 0), RandomChapters(3, 0), RandomBookmarks(3, 0))],
    ];

    [Theory]
    [MemberData(nameof(NotEqualScriptResources))]
    public void ScriptResourceNotEqual(ScriptResource first, ScriptResource second)
        => Assert.True(!first.Equals(second) && first != second && first?.GetHashCode() != second?.GetHashCode());
}
