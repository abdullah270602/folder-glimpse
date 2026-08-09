using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FolderPeek.Tray;

internal sealed class ModernTrayMenuRenderer : ToolStripProfessionalRenderer
{
    internal static readonly object TitleItemTag = new();

    private TrayPalette _palette;

    internal ModernTrayMenuRenderer(bool dark) : base(new ModernColorTable(dark))
    {
        _palette = TrayPalette.Create(dark);
        RoundedEdges = false;
    }

    internal Color BackgroundColor => _palette.Background;
    internal Color ForegroundColor => _palette.Text;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(_palette.Background);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // The menu uses a compact custom check gutter instead of the legacy image column.
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || ReferenceEquals(e.Item.Tag, TitleItemTag)) return;
        var color = e.Item.Pressed ? _palette.Pressed : e.Item.Selected ? _palette.Hover : Color.Transparent;
        if (color == Color.Transparent) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(new Rectangle(5, 2, e.Item.Width - 10, e.Item.Height - 4), 6);
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);

        if (e.Item is ToolStripMenuItem { Checked: true }) DrawCheck(e.Graphics, e.Item.Height);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ReferenceEquals(e.Item.Tag, TitleItemTag)
            ? _palette.Text
            : e.Item.Enabled ? _palette.Text : _palette.DisabledText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        DrawCheck(e.Graphics, e.Item.Height);
    }

    private void DrawCheck(Graphics graphics, int itemHeight)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerY = itemHeight / 2f;
        using var pen = new Pen(_palette.Accent, 2.1f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(pen,
        [
            new PointF(13, centerY),
            new PointF(17, centerY + 4),
            new PointF(24, centerY - 4)
        ]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_palette.Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_palette.Border);
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _palette.SubtleText;
        base.OnRenderArrow(e);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ModernColorTable(bool dark) : ProfessionalColorTable
    {
        private readonly TrayPalette _palette = TrayPalette.Create(dark);
        public override Color ToolStripDropDownBackground => _palette.Background;
        public override Color ImageMarginGradientBegin => _palette.Background;
        public override Color ImageMarginGradientMiddle => _palette.Background;
        public override Color ImageMarginGradientEnd => _palette.Background;
        public override Color MenuBorder => _palette.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => _palette.Hover;
        public override Color MenuItemSelectedGradientBegin => _palette.Hover;
        public override Color MenuItemSelectedGradientEnd => _palette.Hover;
        public override Color MenuItemPressedGradientBegin => _palette.Pressed;
        public override Color MenuItemPressedGradientEnd => _palette.Pressed;
        public override Color SeparatorDark => _palette.Border;
        public override Color SeparatorLight => Color.Transparent;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
    }

    private readonly record struct TrayPalette(
        Color Background,
        Color Hover,
        Color Pressed,
        Color Text,
        Color SubtleText,
        Color DisabledText,
        Color Border,
        Color Accent)
    {
        internal static TrayPalette Create(bool dark)
        {
            if (SystemInformation.HighContrast)
                return new TrayPalette(SystemColors.Menu, SystemColors.Highlight, SystemColors.Highlight,
                    SystemColors.MenuText, SystemColors.MenuText, SystemColors.GrayText, SystemColors.WindowFrame, SystemColors.Highlight);

            return dark
                ? new TrayPalette(Color.FromArgb(32, 32, 32), Color.FromArgb(52, 52, 52), Color.FromArgb(60, 60, 60),
                    Color.White, Color.FromArgb(190, 190, 190), Color.FromArgb(126, 126, 126), Color.FromArgb(68, 68, 68), Color.FromArgb(96, 205, 255))
                : new TrayPalette(Color.White, Color.FromArgb(243, 243, 243), Color.FromArgb(232, 232, 232),
                    Color.FromArgb(26, 26, 26), Color.FromArgb(97, 97, 97), Color.FromArgb(154, 154, 154), Color.FromArgb(218, 218, 218), Color.FromArgb(0, 103, 192));
        }
    }
}
