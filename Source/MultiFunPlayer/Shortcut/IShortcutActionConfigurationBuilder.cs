using MultiFunPlayer.Common;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutActionConfigurationBuilder
{
    IShortcutActionConfiguration Build();
    IShortcutActionConfiguration Build(IEnumerable<TypedValue> values);
}

internal sealed class ShortcutActionConfigurationBuilder(string actionName, IEnumerable<IShortcutSettingBuilder> builders) : IShortcutActionConfigurationBuilder
{
    public IShortcutActionConfiguration Build() => new ShortcutActionConfiguration(actionName, builders.Select(b => b.Build()));
    public IShortcutActionConfiguration Build(IEnumerable<TypedValue> values) => new ShortcutActionConfiguration(actionName, builders.Select(b => b.Build()), values);
}