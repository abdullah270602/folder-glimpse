namespace FolderGlimpse.Core.Input;

using FolderGlimpse.Core.Settings;

public enum PeekState
{
    Idle,
    Pending,
    MomentaryOpen,
    StickyOpen,
    ClosingUntilSpaceUp
}

public enum PeekAction
{
    None,
    OpenSticky,
    OpenMomentary,
    Close
}

public readonly record struct StateTransition(bool Suppress, PeekAction Action, PeekState State);

public sealed class PeekStateMachine
{
    private TapBehavior _pendingTapBehavior = TapBehavior.TogglePreview;
    public PeekState State { get; private set; }
    public bool OwnsSpaceGesture => State is PeekState.Pending or PeekState.MomentaryOpen or PeekState.ClosingUntilSpaceUp;

    public StateTransition SpaceDown(bool eligible, TapBehavior tapBehavior = TapBehavior.TogglePreview)
    {
        if (OwnsSpaceGesture)
        {
            return Result(true);
        }

        if (!eligible)
        {
            return Result(false);
        }

        if (State == PeekState.StickyOpen)
        {
            State = PeekState.ClosingUntilSpaceUp;
            return Result(true, PeekAction.Close);
        }

        State = PeekState.Pending;
        _pendingTapBehavior = tapBehavior;
        return Result(true);
    }

    public StateTransition HoldThresholdElapsed()
    {
        if (State != PeekState.Pending)
        {
            return Result(OwnsSpaceGesture);
        }

        State = PeekState.MomentaryOpen;
        return Result(true, PeekAction.OpenMomentary);
    }

    public StateTransition SpaceUp()
    {
        switch (State)
        {
            case PeekState.Pending:
                if (_pendingTapBehavior == TapBehavior.MomentaryOnly)
                {
                    State = PeekState.Idle;
                    return Result(true);
                }
                State = PeekState.StickyOpen;
                return Result(true, PeekAction.OpenSticky);
            case PeekState.MomentaryOpen:
                State = PeekState.Idle;
                return Result(true, PeekAction.Close);
            case PeekState.ClosingUntilSpaceUp:
                State = PeekState.Idle;
                return Result(true);
            default:
                return Result(false);
        }
    }

    public StateTransition Escape(bool sameEligibleContext)
    {
        if (State != PeekState.StickyOpen)
        {
            return Result(false);
        }

        State = PeekState.Idle;
        return Result(sameEligibleContext, PeekAction.Close);
    }

    public StateTransition ContextInvalidated()
    {
        var wasOpen = State is PeekState.StickyOpen or PeekState.MomentaryOpen;
        State = OwnsSpaceGesture ? PeekState.ClosingUntilSpaceUp : PeekState.Idle;
        return Result(false, wasOpen ? PeekAction.Close : PeekAction.None);
    }

    public StateTransition Reset()
    {
        var wasOpen = State is PeekState.StickyOpen or PeekState.MomentaryOpen;
        State = PeekState.Idle;
        return Result(false, wasOpen ? PeekAction.Close : PeekAction.None);
    }

    private StateTransition Result(bool suppress, PeekAction action = PeekAction.None) => new(suppress, action, State);
}
