using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core.Input;

public enum HoverPhase { Idle, Dwelling, Resolving, Rejected, Open, ClosingGrace }

public readonly record struct HoverPoint(int X, int Y)
{
    public long DistanceSquared(HoverPoint other)
    {
        var dx = (long)X - other.X;
        var dy = (long)Y - other.Y;
        return dx * dx + dy * dy;
    }
}

public enum HoverAction { None, Resolve, Open, Close }

public readonly record struct HoverTransition(HoverPhase Phase, HoverAction Action, long Generation);

public sealed class HoverPreviewStateMachine
{
    private HoverPoint _origin;
    private DateTimeOffset _phaseStarted;
    private long _generation;

    public HoverPhase Phase { get; private set; }
    public long Generation => _generation;

    public HoverTransition ObserveCandidate(HoverPoint point, DateTimeOffset now, int tolerancePixels, TimeSpan dwell)
    {
        if (Phase is HoverPhase.Open or HoverPhase.ClosingGrace)
            return new(Phase, HoverAction.None, _generation);

        if (Phase == HoverPhase.Idle || point.DistanceSquared(_origin) > (long)tolerancePixels * tolerancePixels)
        {
            _origin = point;
            _phaseStarted = now;
            Phase = HoverPhase.Dwelling;
            return new(Phase, HoverAction.None, ++_generation);
        }

        if (Phase == HoverPhase.Dwelling && now - _phaseStarted >= dwell)
        {
            Phase = HoverPhase.Resolving;
            return new(Phase, HoverAction.Resolve, _generation);
        }

        return new(Phase, HoverAction.None, _generation);
    }

    public HoverTransition Resolved(long generation, bool eligible)
    {
        if (Phase != HoverPhase.Resolving || generation != _generation)
            return new(Phase, HoverAction.None, _generation);
        if (!eligible)
        {
            // Do not repeatedly invoke UIA/Shell for the same stationary non-folder target.
            // Movement beyond tolerance or a context cancellation starts a new dwell.
            Phase = HoverPhase.Rejected;
            return new(Phase, HoverAction.None, _generation);
        }
        Phase = HoverPhase.Open;
        return new(Phase, HoverAction.Open, _generation);
    }

    public HoverTransition ObserveOpen(bool overSourceOrPreview, DateTimeOffset now, TimeSpan closeDelay)
    {
        if (Phase is not (HoverPhase.Open or HoverPhase.ClosingGrace))
            return new(Phase, HoverAction.None, _generation);
        if (overSourceOrPreview)
        {
            Phase = HoverPhase.Open;
            return new(Phase, HoverAction.None, _generation);
        }
        if (Phase == HoverPhase.Open)
        {
            Phase = HoverPhase.ClosingGrace;
            _phaseStarted = now;
            return new(Phase, HoverAction.None, _generation);
        }
        if (now - _phaseStarted >= closeDelay)
        {
            Phase = HoverPhase.Idle;
            return new(Phase, HoverAction.Close, ++_generation);
        }
        return new(Phase, HoverAction.None, _generation);
    }

    public HoverTransition Cancel()
    {
        if (Phase == HoverPhase.Idle)
            return new(Phase, HoverAction.None, _generation);
        var shouldClose = Phase is HoverPhase.Open or HoverPhase.ClosingGrace;
        Phase = HoverPhase.Idle;
        return new(Phase, shouldClose ? HoverAction.Close : HoverAction.None, ++_generation);
    }
}

public static class HoverEligibilityPolicy
{
    public static bool CanSample(bool enabled, HoverPreviewMode mode, bool keyboardIdle, bool activationInProgress) =>
        enabled && mode != HoverPreviewMode.Off && keyboardIdle && !activationInProgress;

    public static bool IsModifierMatch(HoverModifier modifier, bool controlDown, bool shiftDown, bool altDown, bool windowsDown) =>
        !altDown && !windowsDown && modifier switch
        {
            HoverModifier.None => !controlDown && !shiftDown,
            HoverModifier.Control => controlDown && !shiftDown,
            HoverModifier.Shift => shiftDown && !controlDown,
            _ => false
        };

    public static bool CanUseSelectedSnapshot(ExplorerSnapshot? snapshot, nint foreground, HoverPoint point,
        DateTimeOffset now, TimeSpan maxAge)
    {
        if (snapshot is not { IsEligible: true, ItemBounds: { } bounds } || snapshot.ForegroundWindow != foreground ||
            string.IsNullOrWhiteSpace(snapshot.FolderPath)) return false;
        var age = now - snapshot.CapturedAt;
        return age >= TimeSpan.Zero && age <= maxAge && point.X >= bounds.Left && point.X < bounds.Right &&
            point.Y >= bounds.Top && point.Y < bounds.Bottom;
    }
}
