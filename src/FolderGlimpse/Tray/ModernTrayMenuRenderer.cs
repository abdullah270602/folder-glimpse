using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FolderGlimpse.Tray;

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
        if (ReferenceEquals(e.Item.Tag, TitleItemTag))
        {
            DrawBrandMark(e.Graphics, new Rectangle(12, (e.Item.Height - 20) / 2, 20, 20));
            return;
        }
        if (!e.Item.Enabled) return;
        var color = e.Item.Pressed ? _palette.Pressed : e.Item.Selected ? _palette.Hover : Color.Transparent;
        if (color == Color.Transparent) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var clip = Rectangle.Round(e.Graphics.VisibleClipBounds);
        var width = Math.Min(e.Item.Width, clip.Width);
        var height = Math.Min(e.Item.Height, clip.Height);
        using var path = RoundedRectangle(new Rectangle(6, 3, Math.Max(1, width - 12), Math.Max(1, height - 6)), 6);
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (ReferenceEquals(e.Item.Tag, TitleItemTag))
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var textBounds = new Rectangle(e.TextRectangle.Left + 5, 0, Math.Max(1, e.TextRectangle.Width - 5), e.Item.Height);
            TextRenderer.DrawText(e.Graphics, "Folder", e.TextFont, textBounds, _palette.Text, flags);
            var folderWidth = TextRenderer.MeasureText(e.Graphics, "Folder", e.TextFont,
                new Size(int.MaxValue, e.Item.Height), flags).Width;
            textBounds.X += folderWidth;
            textBounds.Width = Math.Max(1, textBounds.Width - folderWidth);
            TextRenderer.DrawText(e.Graphics, "Glimpse", e.TextFont, textBounds, _palette.BrandBlue, flags);
            return;
        }
        var itemBounds = new Rectangle(12, 0, Math.Max(1, e.Item.Width - 24), e.Item.Height);
        var itemFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis;
        var itemColor = e.Item.Enabled ? _palette.Text : _palette.DisabledText;
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, itemBounds, itemColor, itemFlags);
    }

    private void DrawBrandMark(Graphics graphics, Rectangle bounds)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var s = bounds.Width / 20f;
        using var folderPen = new Pen(_palette.BrandBlue, 2.1f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(folderPen,
        [
            new PointF(bounds.Left + 1*s, bounds.Top + 6*s), new PointF(bounds.Left + 1*s, bounds.Top + 16*s),
            new PointF(bounds.Left + 3*s, bounds.Top + 18*s), new PointF(bounds.Left + 14*s, bounds.Top + 18*s),
            new PointF(bounds.Left + 16*s, bounds.Top + 16*s), new PointF(bounds.Left + 16*s, bounds.Top + 8*s),
            new PointF(bounds.Left + 14*s, bounds.Top + 6*s), new PointF(bounds.Left + 9*s, bounds.Top + 6*s),
            new PointF(bounds.Left + 7*s, bounds.Top + 3*s), new PointF(bounds.Left + 3*s, bounds.Top + 3*s),
            new PointF(bounds.Left + 1*s, bounds.Top + 6*s)
        ]);
        var document = new RectangleF(bounds.Left + 9*s, bounds.Top + 9*s, 10*s, 10*s);
        using var documentBrush = new SolidBrush(Color.FromArgb(248, 250, 252));
        using var documentPen = new Pen(Color.FromArgb(15, 23, 42), 1.8f * s) { LineJoin = LineJoin.Round };
        graphics.FillRectangle(documentBrush, document);
        graphics.DrawRectangle(documentPen, document.X, document.Y, document.Width, document.Height);
        using var bluePen = new Pen(_palette.BrandBlue, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var grayPen = new Pen(Color.FromArgb(148, 163, 184), 1.35f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(bluePen, bounds.Left + 12*s, bounds.Top + 13*s, bounds.Left + 17*s, bounds.Top + 13*s);
        graphics.DrawLine(grayPen, bounds.Left + 12*s, bounds.Top + 16*s, bounds.Left + 16*s, bounds.Top + 16*s);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        DrawCheck(e.Graphics, e.ImageRectangle);
    }

    private void DrawCheck(Graphics graphics, Rectangle checkBounds)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = checkBounds.Left + checkBounds.Width / 2f;
        var centerY = checkBounds.Top + checkBounds.Height / 2f;
        using var pen = new Pen(_palette.Accent, 2.1f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(pen,
        [
            new PointF(centerX - 6, centerY),
            new PointF(centerX - 2, centerY + 4),
            new PointF(centerX + 5, centerY - 4)
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
        Color Accent,
        Color BrandBlue)
    {
        internal static TrayPalette Create(bool dark)
        {
            if (SystemInformation.HighContrast)
                return new TrayPalette(SystemColors.Menu, SystemColors.Highlight, SystemColors.Highlight,
                    SystemColors.MenuText, SystemColors.MenuText, SystemColors.GrayText, SystemColors.WindowFrame, SystemColors.Highlight, SystemColors.Highlight);

            return dark
                ? new TrayPalette(Color.FromArgb(32, 32, 32), Color.FromArgb(52, 52, 52), Color.FromArgb(60, 60, 60),
                    Color.White, Color.FromArgb(190, 190, 190), Color.FromArgb(126, 126, 126), Color.FromArgb(68, 68, 68), Color.FromArgb(2, 106, 251), Color.FromArgb(2, 106, 251))
                : new TrayPalette(Color.White, Color.FromArgb(243, 243, 243), Color.FromArgb(232, 232, 232),
                    Color.FromArgb(26, 26, 26), Color.FromArgb(97, 97, 97), Color.FromArgb(154, 154, 154), Color.FromArgb(218, 218, 218), Color.FromArgb(2, 106, 251), Color.FromArgb(2, 106, 251));
        }
    }
}
