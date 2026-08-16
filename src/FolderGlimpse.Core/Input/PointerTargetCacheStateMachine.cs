namespace FolderGlimpse.Core.Input;

public enum PointerTargetPhase { Idle, Dwelling, Resolving, Ready, Rejected }
public enum PointerTargetAction { None, Clear, Resolve }
public readonly record struct PointerTargetTransition(PointerTargetPhase Phase, PointerTargetAction Action, long Generation);

public sealed class PointerTargetCacheStateMachine
{
    private HoverPoint _origin;
    private DateTimeOffset _phaseStarted;
    private long _generation;

    public PointerTargetPhase Phase { get; private set; }
    public long Generation => _generation;

    public PointerTargetTransition Observe(HoverPoint point, DateTimeOffset now, int tolerancePixels,
        TimeSpan initialDelay, TimeSpan refreshDelay)
    {
        if (Phase == PointerTargetPhase.Idle || point.DistanceSquared(_origin) > (long)tolerancePixels * tolerancePixels)
        {
            _origin = point;
            _phaseStarted = now;
            Phase = PointerTargetPhase.Dwelling;
            return new(Phase, PointerTargetAction.Clear, ++_generation);
        }

        if ((Phase == PointerTargetPhase.Dwelling && now - _phaseStarted >= initialDelay) ||
            (Phase is PointerTargetPhase.Ready or PointerTargetPhase.Rejected && now - _phaseStarted >= refreshDelay))
        {
            _phaseStarted = now;
            Phase = PointerTargetPhase.Resolving;
            return new(Phase, PointerTargetAction.Resolve, ++_generation);
        }
        return new(Phase, PointerTargetAction.None, _generation);
    }

    public PointerTargetTransition Resolved(long generation, bool eligible, DateTimeOffset now)
    {
        if (Phase != PointerTargetPhase.Resolving || generation != _generation)
            return new(Phase, PointerTargetAction.None, _generation);
        Phase = eligible ? PointerTargetPhase.Ready : PointerTargetPhase.Rejected;
        _phaseStarted = now;
        return new(Phase, PointerTargetAction.None, _generation);
    }

    public PointerTargetTransition Cancel()
    {
        if (Phase == PointerTargetPhase.Idle) return new(Phase, PointerTargetAction.None, _generation);
        Phase = PointerTargetPhase.Idle;
        return new(Phase, PointerTargetAction.Clear, ++_generation);
    }
}
