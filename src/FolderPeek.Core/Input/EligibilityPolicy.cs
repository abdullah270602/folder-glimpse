namespace FolderPeek.Core.Input;

public sealed record ExplorerSnapshot(
    bool IsEligible,
    string Reason,
    nint ForegroundWindow,
    nint FocusWindow,
    int ExplorerProcessId,
    string? FolderPath,
    string? DisplayName,
    PixelRect? ItemBounds,
    DateTimeOffset CapturedAt,
    long Generation)
{
    public static ExplorerSnapshot Ineligible(string reason, DateTimeOffset now, long generation = 0) =>
        new(false, reason, 0, 0, 0, null, null, null, now, generation);
}

public readonly record struct InputContext(
    bool Enabled,
    bool IsInjected,
    bool HasModifiers,
    nint CurrentForegroundWindow,
    nint CurrentFocusWindow,
    DateTimeOffset Now);

public static class EligibilityPolicy
{
    public static bool CanOwnSpace(InputContext input, ExplorerSnapshot? snapshot, TimeSpan maxAge)
    {
        if (!input.Enabled || input.IsInjected || input.HasModifiers || snapshot is null || !snapshot.IsEligible)
        {
            return false;
        }

        var age = input.Now - snapshot.CapturedAt;
        return age >= TimeSpan.Zero && age <= maxAge &&
               snapshot.ForegroundWindow != 0 &&
               snapshot.ForegroundWindow == input.CurrentForegroundWindow &&
               snapshot.FocusWindow != 0 &&
               snapshot.FocusWindow == input.CurrentFocusWindow &&
               !string.IsNullOrWhiteSpace(snapshot.FolderPath);
    }
}
