using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Toggle Flip")]
internal sealed class ToggleFlipShortcut(IShortcutActionRunner actionRunner, IToggleInputGestureDescriptor gesture)
    : AbstractShortcut<IToggleInputGesture, IToggleInputGestureData>(actionRunner, gesture)
{
    protected override void Update(IToggleInputGesture gesture) => Invoke(ToggleInputGestureData.FromGesture(gesture));
}