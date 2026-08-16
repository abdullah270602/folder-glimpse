using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Width > 0 && Height > 0;
}

public readonly record struct PixelSize(int Width, int Height);

public static class PopupPositioner
{
    public static PixelRect Place(PixelRect anchor, PixelRect workArea, PixelSize popup, int gap = 10)
        => Place(anchor, workArea, popup, PopupPlacementPreference.Auto, gap);

    public static PixelRect Place(
        PixelRect anchor,
        PixelRect workArea,
        PixelSize popup,
        PopupPlacementPreference preference,
        int gap = 10)
    {
        if (!workArea.IsValid || popup.Width <= 0 || popup.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        var width = Math.Min(popup.Width, workArea.Width);
        var height = Math.Min(popup.Height, workArea.Height);
        gap = Math.Max(0, gap);
        var normalizedPreference = Enum.IsDefined(preference) ? preference : PopupPlacementPreference.Auto;
        var candidates = CandidateOrder(normalizedPreference);
        foreach (var side in candidates)
        {
            if (Fits(side, anchor, workArea, width, height, gap))
            {
                return Candidate(side, anchor, workArea, width, height, gap);
            }
        }

        // There is no non-overlapping side large enough. Keep the preferred candidate visible.
        var fallback = normalizedPreference == PopupPlacementPreference.Auto
            ? PopupPlacementPreference.Right
            : normalizedPreference;
        return Clamp(Candidate(fallback, anchor, workArea, width, height, gap), workArea);
    }

    private static PopupPlacementPreference[] CandidateOrder(PopupPlacementPreference preference) => preference switch
    {
        PopupPlacementPreference.Right => [PopupPlacementPreference.Right, PopupPlacementPreference.Left, PopupPlacementPreference.Below, PopupPlacementPreference.Above],
        PopupPlacementPreference.Left => [PopupPlacementPreference.Left, PopupPlacementPreference.Right, PopupPlacementPreference.Below, PopupPlacementPreference.Above],
        PopupPlacementPreference.Below => [PopupPlacementPreference.Below, PopupPlacementPreference.Above, PopupPlacementPreference.Right, PopupPlacementPreference.Left],
        PopupPlacementPreference.Above => [PopupPlacementPreference.Above, PopupPlacementPreference.Below, PopupPlacementPreference.Right, PopupPlacementPreference.Left],
        _ => [PopupPlacementPreference.Right, PopupPlacementPreference.Left, PopupPlacementPreference.Below, PopupPlacementPreference.Above]
    };

    private static bool Fits(PopupPlacementPreference side, PixelRect anchor, PixelRect work, int width, int height, int gap)
    {
        var candidate = Candidate(side, anchor, work, width, height, gap);
        return candidate.Left >= work.Left && candidate.Right <= work.Right &&
            candidate.Top >= work.Top && candidate.Bottom <= work.Bottom;
    }

    private static PixelRect Candidate(PopupPlacementPreference side, PixelRect anchor, PixelRect work, int width, int height, int gap)
    {
        var centeredX = anchor.Left + ((anchor.Width - width) / 2);
        var centeredY = anchor.Top + ((anchor.Height - height) / 2);
        var x = side switch
        {
            PopupPlacementPreference.Right => anchor.Right + gap,
            PopupPlacementPreference.Left => anchor.Left - gap - width,
            _ => Math.Clamp(centeredX, work.Left, work.Right - width)
        };
        var y = side switch
        {
            PopupPlacementPreference.Below => anchor.Bottom + gap,
            PopupPlacementPreference.Above => anchor.Top - gap - height,
            _ => Math.Clamp(centeredY, work.Top, work.Bottom - height)
        };
        return new PixelRect(x, y, x + width, y + height);
    }

    private static PixelRect Clamp(PixelRect rect, PixelRect work)
    {
        var x = Math.Clamp(rect.Left, work.Left, work.Right - rect.Width);
        var y = Math.Clamp(rect.Top, work.Top, work.Bottom - rect.Height);
        return new PixelRect(x, y, x + rect.Width, y + rect.Height);
    }
}
