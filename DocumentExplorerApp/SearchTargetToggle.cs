using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DocumentExplorerApp;

internal sealed class SearchTargetToggle : Control
{
    private Rectangle _fileNameRect;
    private Rectangle _contentRect;
    private SearchTarget _selectedTarget = SearchTarget.FileName;
    private readonly ToolTip _toolTip = new()
    {
        AutomaticDelay = 1,
        InitialDelay = 1,
        ReshowDelay = 1,
        AutoPopDelay = 20000,
        ShowAlways = true,
        UseAnimation = false,
        UseFading = false,
    };
    private string? _currentTooltipZone;

    public event EventHandler<SearchTarget>? SelectedTargetChanged;

    public bool UseIconMode { get; set; } = false;

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
        Cursor = Cursors.Hand;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        RecalcAutoSize();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayoutRects();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RecalcAutoSize();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RecalcAutoSize();
    }

    /// <summary>텍스트/이모지를 측정해서 컨트롤 크기를 자동 결정 (DPI 대응)</summary>
    private void RecalcAutoSize()
    {
        using var g = CreateGraphics();
        var font = UseIconMode
            ? new Font("Malgun Gothic", 10F, FontStyle.Regular, GraphicsUnit.Point)
            : Font;

        var leftText = UseIconMode ? "📄" : "파일명";
        var rightText = UseIconMode ? "🔍" : "문서 내용";

        var leftSize = TextRenderer.MeasureText(g, leftText, font);
        var rightSize = TextRenderer.MeasureText(g, rightText, font);

        var cellWidth = Math.Max(leftSize.Width, rightSize.Width) + 8; // 좌우 패딩
        var h = Math.Max(leftSize.Height, rightSize.Height) + 6;      // 상하 패딩

        var w = cellWidth * 2 + 2; // 2셀 + 보더
        Size = new Size(w, h);
        MinimumSize = new Size(w, h);

        if (!UseIconMode) font.Dispose();
        UpdateLayoutRects();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? zone = null;
        string? tip = null;
        if (_fileNameRect.Contains(e.Location))
        {
            zone = "filename";
            tip = "단축키 : Ctrl + A";
        }
        else if (_contentRect.Contains(e.Location))
        {
            zone = "content";
            tip = "단축키 : Ctrl + S";
        }

        if (zone != _currentTooltipZone)
        {
            _currentTooltipZone = zone;
            _toolTip.SetToolTip(this, tip);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _currentTooltipZone = null;
        _toolTip.SetToolTip(this, null);
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

        if (UseIconMode)
        {
            using var iconFont = new Font("Malgun Gothic", 10F, FontStyle.Regular, GraphicsUnit.Point);
            DrawLabel(e.Graphics, _fileNameRect, "📄", _selectedTarget == SearchTarget.FileName, iconFont);
            DrawLabel(e.Graphics, _contentRect, "🔍", _selectedTarget == SearchTarget.DocumentContent, iconFont);
        }
        else
        {
            DrawLabel(e.Graphics, _fileNameRect, "파일명", _selectedTarget == SearchTarget.FileName, Font);
            DrawLabel(e.Graphics, _contentRect, "문서 내용", _selectedTarget == SearchTarget.DocumentContent, Font);
        }
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
        var color = selected ? Color.White : Color.FromArgb(195, 182, 188);
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
