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
    {
        if (!workArea.IsValid || popup.Width <= 0 || popup.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        var width = Math.Min(popup.Width, workArea.Width);
        var height = Math.Min(popup.Height, workArea.Height);
        var right = anchor.Right + gap;
        var left = anchor.Left - gap - width;
        var x = right + width <= workArea.Right ? right :
            left >= workArea.Left ? left : Math.Clamp(right, workArea.Left, workArea.Right - width);
        var centeredY = anchor.Top + ((anchor.Height - height) / 2);
        var y = Math.Clamp(centeredY, workArea.Top, workArea.Bottom - height);
        return new PixelRect(x, y, x + width, y + height);
    }
}
