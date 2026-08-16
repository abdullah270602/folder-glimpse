using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core.Input;

public enum MouseTriggerButton { Left, Right, Middle }

public readonly record struct MouseTriggerInput(MouseTriggerButton Button, HoverPoint Point,
    bool ControlDown, bool ShiftDown, bool AltDown, bool WindowsDown, bool Injected,
    nint ForegroundWindow, DateTimeOffset Now);

public static class MouseTriggerPolicy
{
    public static MouseTriggerOptions Match(MouseTriggerInput input)
    {
        if (input.Injected || input.ShiftDown || input.AltDown || input.WindowsDown) return MouseTriggerOptions.None;
        return input.Button switch
        {
            MouseTriggerButton.Middle when !input.ControlDown => MouseTriggerOptions.MiddleClick,
            MouseTriggerButton.Left when input.ControlDown => MouseTriggerOptions.ControlLeftClick,
            MouseTriggerButton.Right when input.ControlDown => MouseTriggerOptions.ControlRightClick,
            _ => MouseTriggerOptions.None
        };
    }

    public static bool CanCapture(MouseTriggerOptions configured, MouseTriggerInput input,
        ExplorerSnapshot? target, TimeSpan maxAge)
    {
        var gesture = Match(input);
        return gesture != MouseTriggerOptions.None && configured.HasFlag(gesture) &&
            IsFreshTargetAtPoint(target, input.Point, input.ForegroundWindow, input.Now, maxAge);
    }

    public static bool IsFreshTargetAtPoint(ExplorerSnapshot? target, HoverPoint point, nint foregroundWindow,
        DateTimeOffset now, TimeSpan maxAge)
    {
        if (target is not { IsEligible: true, ItemBounds: { } bounds } ||
            foregroundWindow != target.ForegroundWindow || string.IsNullOrWhiteSpace(target.FolderPath)) return false;
        var age = now - target.CapturedAt;
        return age >= TimeSpan.Zero && age <= maxAge && point.X >= bounds.Left && point.X < bounds.Right &&
            point.Y >= bounds.Top && point.Y < bounds.Bottom;
    }
}
