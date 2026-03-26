using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DocumentExplorerApp;

internal sealed class SettingsForm : Form
{
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static readonly Color BgColor = Color.FromArgb(255, 247, 242);
    private static readonly Color SurfaceColor = Color.FromArgb(255, 252, 248);
    private static readonly Color AccentColor = Color.FromArgb(181, 79, 80);
    private static readonly Color BorderColor = Color.FromArgb(234, 210, 198);
    private static readonly Color TextColor = Color.FromArgb(49, 39, 53);
    private static readonly Color MutedColor = Color.FromArgb(132, 118, 125);
    private static readonly Color ToggleActiveColor = Color.FromArgb(196, 108, 109);
    private static readonly Color ToggleBorderColor = Color.FromArgb(210, 168, 170);

    private readonly AppSettings _settings;

    // 레이아웃 토글 (자식 버튼 없이 단일 Paint로 그림 → 테두리 덮힘 없음)
    private Panel _layoutToggle = null!;
    private string _layoutMode;

    // 검색 확장자
    private TextBox _patternTextBox = null!;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _layoutMode = settings.LayoutMode;
        BuildLayout();
    }

    private void BuildLayout()
    {
        Text = "\U0001F527 설정";
        ClientSize = new Size(440, 290);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BgColor;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;
        Shown += (_, _) =>
        {
            _patternTextBox.SelectionStart = 0;
            _patternTextBox.SelectionLength = 0;
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20, 20, 20, 12),
            BackColor = BgColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));  // 모드 선택
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));  // 간격
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F)); // 검색 확장자
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // 여백(최소)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));  // 버튼
        Controls.Add(root);

        root.Controls.Add(BuildLayoutSection(), 0, 0);
        // row 1 = 간격 (빈 공간)
        root.Controls.Add(BuildPatternSection(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 4);
    }

    private Control BuildLayoutSection()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));  // 라벨
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));  // 토글

        var label = new Label
        {
            Text = "모드 선택",
            Font = new Font("Malgun Gothic", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = TextColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        outer.Controls.Add(label, 0, 0);

        // 커스텀 드로잉 토글: 자식 버튼 없이 Paint 하나로 모든 것을 그려
        // → 자식이 부모 테두리를 덮는 WinForms 렌더링 문제 완전 해결
        _layoutToggle = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
        };
        _layoutToggle.Paint += (_, e) => DrawToggle(e.Graphics, _layoutToggle.ClientRectangle);
        _layoutToggle.MouseDown += (_, e) =>
        {
            var mid = _layoutToggle.ClientRectangle.Width / 2;
            var selected = e.X < mid ? "Detailed" : "Simple";
            if (selected == "Simple" && string.IsNullOrWhiteSpace(_settings.LastFolder))
            {
                MessageBox.Show(this,
                    "기본 모드에서 검색 폴더가 설정되어 있어야 심플 모드를 이용할 수 있습니다.",
                    "알림");
                return;
            }
            _layoutMode = selected;
            _layoutToggle.Invalidate();
        };
        outer.Controls.Add(_layoutToggle, 0, 1);

        return outer;
    }

    // 토글 전체를 단일 Paint 콜에서 그림 → 테두리가 항상 맨 위에 그려짐
    private void DrawToggle(Graphics g, Rectangle r)
    {
        if (r.Width < 4 || r.Height < 4) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = r.Width - 1;
        int h = r.Height - 1;
        int mid = r.Width / 2;
        int radius = Math.Min(12, Math.Min(w, h));

        // 둥근 사각형 경로 생성
        using var path = new GraphicsPath();
        path.AddArc(0, 0, radius, radius, 180, 90);
        path.AddArc(w - radius, 0, radius, radius, 270, 90);
        path.AddArc(w - radius, h - radius, radius, radius, 0, 90);
        path.AddArc(0, h - radius, radius, radius, 90, 90);
        path.CloseFigure();

        // 1. 비활성 배경 전체 채우기
        using var bgBrush = new SolidBrush(SurfaceColor);
        g.FillPath(bgBrush, path);

        // 2. 활성 절반만 색칠 (클립 사용)
        using var activeBrush = new SolidBrush(ToggleActiveColor);
        var savedClip = g.Clip;
        using var activeClip = new Region(_layoutMode == "Detailed"
            ? new Rectangle(0, 0, mid, r.Height)
            : new Rectangle(mid, 0, r.Width - mid, r.Height));
        g.Clip = activeClip;
        g.FillPath(activeBrush, path);
        g.Clip = savedClip;

        // 3. 구분선
        using var divPen = new Pen(ToggleBorderColor, 1f);
        g.DrawLine(divPen, mid, 4, mid, h - 4);

        // 4. 테두리 (맨 마지막에 → 활성 채우기 위에 그려짐, 절대 덮히지 않음)
        using var borderPen = new Pen(ToggleBorderColor, 1.5f);
        g.DrawPath(borderPen, path);

        // 5. 텍스트
        using var font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        var leftColor  = _layoutMode == "Detailed" ? Color.White : MutedColor;
        var rightColor = _layoutMode == "Simple"   ? Color.White : MutedColor;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, "기본 모드", font, new Rectangle(1, 1, mid - 1, r.Height - 2), leftColor, flags);
        TextRenderer.DrawText(g, "심플 모드", font, new Rectangle(mid + 1, 1, r.Width - mid - 2, r.Height - 2), rightColor, flags);
    }

    private Control BuildPatternSection()
    {
        // TableLayoutPanel 외곽으로 안정적인 width 채움 보장
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));   // 라벨
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 입력행

        var label = new Label
        {
            Text = "검색 확장자",
            Font = new Font("Malgun Gothic", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = TextColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        outer.Controls.Add(label, 0, 0);

        var resetButton = new Button
        {
            Text = "원래대로",
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
        };
        resetButton.FlatAppearance.BorderSize = 0;
        resetButton.Click += (_, _) => _patternTextBox.Text = DocumentSearcher.DefaultPatterns;

        _patternTextBox = new TextBox
        {
            Text = _settings.SearchPatterns,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
        };

        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BgColor,
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        inputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        inputRow.Controls.Add(_patternTextBox, 0, 0);
        inputRow.Controls.Add(resetButton, 1, 0);

        outer.Controls.Add(inputRow, 0, 1);

        return outer;
    }

    private Panel BuildFooter()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BgColor,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var okButton = new Button
        {
            Text = "확인",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.Click += (_, _) =>
        {
            if (_layoutMode == "Simple" && string.IsNullOrWhiteSpace(_settings.LastFolder))
            {
                MessageBox.Show(this, "기본 모드에서 검색 폴더가 설정되어 있어야 심플 모드를 이용할 수 있습니다.", "알림");
                return;
            }

            _settings.LayoutMode = _layoutMode;
            _settings.SearchPatterns = _patternTextBox.Text;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = new Button
        {
            Text = "취소",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            UseVisualStyleBackColor = false,
        };
        cancelButton.FlatAppearance.BorderSize = 1;
        cancelButton.FlatAppearance.BorderColor = BorderColor;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Click += (_, _) => Close();
        CancelButton = cancelButton;

        panel.Controls.Add(new Panel { BackColor = BgColor }, 0, 0);
        panel.Controls.Add(okButton, 1, 0);
        panel.Controls.Add(cancelButton, 2, 0);

        return panel;
    }
}
