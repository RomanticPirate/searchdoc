using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DocumentExplorerApp;

internal sealed class SearchTargetToggle : Control
{
    private Rectangle _fileNameRect;
    private Rectangle _contentRect;
    private SearchTarget _selectedTarget = SearchTarget.FileName;

    public event EventHandler<SearchTarget>? SelectedTargetChanged;

    public SearchTarget SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (_selectedTarget == value)
            {
                return;
            }

            _selectedTarget = value;
            Invalidate();
            SelectedTargetChanged?.Invoke(this, value);
        }
    }

    public SearchTargetToggle()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Size = new Size(220, 24);
        MinimumSize = new Size(220, 24);
        Cursor = Cursors.Hand;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        UpdateLayoutRects();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayoutRects();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!Enabled)
        {
            return;
        }

        if (_fileNameRect.Contains(e.Location))
        {
            SelectedTarget = SearchTarget.FileName;
        }
        else if (_contentRect.Contains(e.Location))
        {
            SelectedTarget = SearchTarget.DocumentContent;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var outerRect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var outerPath = CreateRoundedPath(outerRect, 14);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(255, 252, 248));
        using var borderPen = new Pen(Color.FromArgb(210, 168, 170), 1f) { Alignment = PenAlignment.Inset };

        e.Graphics.FillPath(backgroundBrush, outerPath);

        var previousClip = e.Graphics.Clip;
        e.Graphics.SetClip(outerPath);
        using var selectedBrush = new SolidBrush(Color.FromArgb(196, 108, 109));
        e.Graphics.FillRectangle(selectedBrush, _selectedTarget == SearchTarget.FileName ? _fileNameRect : _contentRect);
        e.Graphics.Clip = previousClip;

        using var dividerPen = new Pen(Color.FromArgb(234, 210, 198), 1f);
        e.Graphics.DrawLine(dividerPen, _contentRect.Left, 3, _contentRect.Left, Height - 4);
        e.Graphics.DrawPath(borderPen, outerPath);

        DrawLabel(e.Graphics, _fileNameRect, "\uD30C\uC77C\uBA85", _selectedTarget == SearchTarget.FileName, Font);
        DrawLabel(e.Graphics, _contentRect, "\uBB38\uC11C \uB0B4\uC6A9", _selectedTarget == SearchTarget.DocumentContent, Font);
    }

    private void UpdateLayoutRects()
    {
        var innerX = 1;
        var innerY = 1;
        var innerWidth = Math.Max(0, Width - 2);
        var innerHeight = Math.Max(0, Height - 2);
        var leftWidth = innerWidth / 2;
        var rightWidth = Math.Max(0, innerWidth - leftWidth);

        _fileNameRect = new Rectangle(innerX, innerY, leftWidth, innerHeight);
        _contentRect = new Rectangle(innerX + leftWidth, innerY, rightWidth, innerHeight);
    }

    private static void DrawLabel(Graphics graphics, Rectangle rect, string text, bool selected, Font font)
    {
        var color = selected ? Color.White : Color.FromArgb(150, 136, 142);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            rect,
            color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.StartFigure();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
