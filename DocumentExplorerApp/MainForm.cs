using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace DocumentExplorerApp;

public sealed class MainForm : Form
{
    private const int TopRowHeight = 42;
    private const int TopControlHeight = 32;
    private const int TopVerticalMargin = 5;
    private static readonly Color AppBackgroundColor = Color.FromArgb(255, 247, 242);
    private static readonly Color SurfaceColor = Color.FromArgb(255, 252, 248);
    private static readonly Color SurfaceAccentColor = Color.FromArgb(255, 240, 231);
    private static readonly Color BorderColor = Color.FromArgb(234, 210, 198);
    private static readonly Color AccentColor = Color.FromArgb(181, 79, 80);
    private static readonly Color AccentSoftColor = Color.FromArgb(232, 225, 221);
    private static readonly Color MintSelectionColor = Color.FromArgb(191, 228, 216);
    private static readonly Color TextColor = Color.FromArgb(49, 39, 53);
    private static readonly Color MutedTextColor = Color.FromArgb(132, 118, 125);
    private static readonly Color DisabledButtonBackColor = Color.FromArgb(236, 225, 221);
    private static readonly Color DisabledButtonForeColor = Color.FromArgb(156, 140, 146);

    private const string FailureTooltip =
        "실패 뜻:\n" +
        "1. 해당 프로그램이 설치되지 않은 구형 문서일 수 있어.\n" +
        "2. 파일이 잠겨 있거나 손상됐을 수 있어.\n" +
        "3. 인코딩을 읽지 못한 텍스트 파일일 수 있어.\n" +
        "4. COM 자동화 중 예외가 발생했을 수 있어.";

    private readonly DocumentSearcher _searcher = new();
    private readonly BindingList<SearchResult> _results = [];
    private readonly AppSettings _settings;
    private readonly Bitmap _failureIcon = CreateFailureIcon();
    private readonly Bitmap _successIcon = CreateSuccessIcon();
    private readonly ToolTip _paneToolTip = CreatePaneToolTip();
    private readonly Image _infoIconImage = CreateInfoIcon();
    private DocumentIndexData? _currentIndex;

    private TextBox _folderTextBox = null!;
    private TextBox _keywordTextBox = null!;
    private TextBox _patternTextBox = null!;
    private Button _browseFolderButton = null!;
    private Button _searchButton = null!;
    private SearchTargetToggle _searchTargetToggle = null!;
    private Button _resetPatternButton = null!;
    private Label _statusLabel = null!;
    private DataGridView _resultsGrid = null!;
    private RichTextBox _previewBox = null!;
    private LinkLabel _previewTitleLabel = null!;
    private LinkLabel _previewMetaLabel = null!;
    private Label _previewTypeLabel = null!;
    private Label _previewMatchLabel = null!;
    private Button _previewPreviousButton = null!;
    private Button _previewNextButton = null!;
    private Button _previewExpandButton = null!;
    private SplitContainer _mainSplit = null!;

    private CancellationTokenSource? _searchCts;
    private bool _restartRequested;
    private bool _isSearchBusy;
    private SearchTarget _searchTarget = SearchTarget.FileName;
    private SearchResult? _selectedPreviewResult;
    private int _selectedPreviewMatchIndex;
    private bool _previewShowFullDocument;
    public MainForm()
    {
        Program.Log("MainForm ctor start");

        Text = "찾아줘문서";
        Size = new Size(1320, 980);
        MinimumSize = new Size(1100, 840);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (executableIcon is not null)
        {
            Icon = executableIcon;
        }

        _settings = AppSettings.Load();
        Program.Log("Settings loaded");

        BuildLayout();
        Program.Log("Layout built");

        // 입력칸 외부 클릭 시 포커스 해제 (단축키 입력을 위해)
        Application.AddMessageFilter(new DefocusMessageFilter(this));

        BindInitialState();
        Program.Log("Initial state bound");

        Shown += (_, _) =>
        {
            ApplyMainSplitRatio();
            Program.Log("MainForm shown");
        };
    }

    private void BuildLayout()
    {
        Program.Log("BuildLayout start");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(12, 12, 12, 2),
            BackColor = AppBackgroundColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, TopRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, TopRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, TopRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        Controls.Add(root);
        Program.Log("Root created");

        root.Controls.Add(CreateFieldLabel("검색 폴더"), 0, 0);
        _folderTextBox = CreateInputTextBox();
        _folderTextBox.ReadOnly = true;
        _folderTextBox.TabStop = false;
        root.Controls.Add(CreateInputHost(_folderTextBox), 1, 0);
        _browseFolderButton = new Button { Text = "폴더 찾기", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        StyleActionButton(_browseFolderButton);
        _browseFolderButton.Click += async (_, _) => await PickFolderAsync();
        root.Controls.Add(_browseFolderButton, 2, 0);
        Program.Log("Top area created");

        root.Controls.Add(CreateFieldLabel("확장자 패턴"), 0, 1);
        _patternTextBox = CreateInputTextBox();
        root.Controls.Add(CreateInputHost(_patternTextBox), 1, 1);
        _resetPatternButton = new Button { Text = "확장자 초기화", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        StyleActionButton(_resetPatternButton);
        _resetPatternButton.Click += (_, _) => _patternTextBox.Text = DocumentSearcher.DefaultPatterns;
        root.Controls.Add(_resetPatternButton, 2, 1);
        Program.Log("Pattern area created");

        root.Controls.Add(CreateFieldLabel("검색어 (Ctrl+F)"), 0, 2);
        var searchTargetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchTargetPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, TopRowHeight));

        var searchInputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        searchInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));
        searchInputPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, TopRowHeight));

        _keywordTextBox = CreateInputTextBox();
        _keywordTextBox.KeyDown += KeywordTextBoxOnKeyDown;
        _keywordTextBox.Enter += (_, _) => UpdateSearchButtonVisualState();
        _keywordTextBox.Leave += (_, _) => BeginInvoke((Action)UpdateSearchButtonVisualState);
        _keywordTextBox.TextChanged += KeywordTextBoxOnTextChanged;
        var keywordInputHost = CreateInputHost(_keywordTextBox);
        keywordInputHost.Dock = DockStyle.Fill;
        searchInputPanel.Controls.Add(keywordInputHost, 0, 0);
        _searchButton = new Button { Text = "검색 (Enter)", Width = 172, Anchor = AnchorStyles.Left };
        StyleActionButton(_searchButton);
        _searchButton.Font = new Font("Malgun Gothic", 12F, FontStyle.Bold, GraphicsUnit.Point);
        _searchButton.Margin = new Padding(0, TopVerticalMargin, 0, TopVerticalMargin);
        _searchButton.Click += async (_, _) => await RequestSearchAsync();
        searchInputPanel.Controls.Add(_searchButton, 1, 0);
        searchTargetPanel.Controls.Add(searchInputPanel, 0, 0);

        _searchTargetToggle = new SearchTargetToggle
        {
            Anchor = AnchorStyles.Right,
            AutoSize = false,
            Margin = new Padding(16, 9, 0, 9),
        };
        _searchTargetToggle.SelectedTargetChanged += (_, target) => SetSearchTarget(target);
        searchTargetPanel.Controls.Add(_searchTargetToggle, 1, 0);

        root.SetColumnSpan(searchTargetPanel, 2);
        root.Controls.Add(searchTargetPanel, 1, 2);

        _statusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
        };

        var creditLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Text = "Made by NX-JW",
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = MutedTextColor,
            Margin = new Padding(0),
        };

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusPanel.Controls.Add(_statusLabel, 0, 0);
        statusPanel.Controls.Add(creditLabel, 1, 0);

        root.Controls.Add(statusPanel, 0, 4);
        root.SetColumnSpan(statusPanel, 3);
        Program.Log("Action area created");

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AppBackgroundColor,
            Padding = new Padding(0, 4, 0, 0),
        };
        root.Controls.Add(_mainSplit, 0, 3);
        root.SetColumnSpan(_mainSplit, 3);
        _mainSplit.HandleCreated += (_, _) =>
        {
            _mainSplit.Panel1MinSize = 360;
            _mainSplit.Panel2MinSize = 360;
            ApplyMainSplitRatio();
        };
        _mainSplit.SizeChanged += (_, _) =>
        {
            ApplyMainSplitRatio();
        };
        Program.Log("Split created");

        var leftPanel = BuildPane("검색 결과", "파일 리스트를 더블 클릭하면 파일이 열립니다.", out var leftContent);
        _mainSplit.Panel1.Controls.Add(leftPanel);
        Program.Log("Left pane created");

        _resultsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            DataSource = _results,
            BackgroundColor = SurfaceColor,
            BorderStyle = BorderStyle.None,
            GridColor = BorderColor,
            EnableHeadersVisualStyles = false,
            ScrollBars = ScrollBars.Both,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        };
        _resultsGrid.DefaultCellStyle.BackColor = SurfaceColor;
        _resultsGrid.DefaultCellStyle.ForeColor = TextColor;
        _resultsGrid.DefaultCellStyle.SelectionBackColor = MintSelectionColor;
        _resultsGrid.DefaultCellStyle.SelectionForeColor = TextColor;
        _resultsGrid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        _resultsGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _resultsGrid.ColumnHeadersDefaultCellStyle.BackColor = AccentColor;
        _resultsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _resultsGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = AccentColor;
        _resultsGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        _resultsGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        _resultsGrid.ColumnHeadersHeight = 34;
        _resultsGrid.RowTemplate.Height = 30;
        _resultsGrid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "StatusIcon",
            HeaderText = "",
            Width = 22,
            ImageLayout = DataGridViewImageCellLayout.Normal,
            ValuesAreIcons = false,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                NullValue = null,
                Padding = new Padding(0),
            },
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SearchResult.Status),
            HeaderText = "상태",
            Width = 70,
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SearchResult.FileName),
            HeaderText = "파일명",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 180,
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SearchResult.DirectoryPath),
            HeaderText = "경로",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 320,
        });
        _resultsGrid.SelectionChanged += (_, _) => ShowSelectedPreview();
        _resultsGrid.CellDoubleClick += (_, _) => OpenSelectedFile();
        _resultsGrid.CellFormatting += ResultsGridOnCellFormatting;
        _resultsGrid.CellToolTipTextNeeded += ResultsGridOnCellToolTipTextNeeded;
        _resultsGrid.CellPainting += ResultsGridOnCellPainting;
        _resultsGrid.DataError += (_, e) => e.ThrowException = false;
        leftContent.Controls.Add(_resultsGrid);
        Program.Log("Grid created");

        var rightPanel = BuildPane("미리보기", "· 문서명을 클릭하면 해당 파일이 열립니다.\n· 경로를 클릭하면 해당 경로가 열립니다.", out var rightContent);
        _mainSplit.Panel2.Controls.Add(rightPanel);
        Program.Log("Right pane created");

        var previewShell = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            Padding = new Padding(0),
        };
        rightContent.Controls.Add(previewShell);

        var previewCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceColor,
            Padding = new Padding(18, 18, 18, 0),
            Margin = new Padding(0),
        };
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        previewShell.Controls.Add(previewCard);

        var previewContentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceAccentColor,
            Margin = new Padding(0),
            Padding = new Padding(1),
        };
        previewCard.Controls.Add(previewContentPanel, 0, 0);

        var previewContentInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(18, 18, 18, 18),
        };
        previewContentInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        previewContentInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewContentPanel.Controls.Add(previewContentInner);

        var previewHeaderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        previewHeaderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        previewHeaderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        previewContentInner.Controls.Add(previewHeaderPanel, 0, 0);

        _previewTitleLabel = new LinkLabel
        {
            Dock = DockStyle.Fill,
            Font = new Font("Malgun Gothic", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkColor = AccentColor,
            ActiveLinkColor = AccentColor,
            VisitedLinkColor = AccentColor,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        _previewTitleLabel.LinkClicked += PreviewTitleLabelOnLinkClicked;
        previewHeaderPanel.Controls.Add(_previewTitleLabel, 0, 0);

        _previewMetaLabel = new LinkLabel
        {
            Dock = DockStyle.Fill,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkColor = AccentColor,
            ActiveLinkColor = AccentColor,
            VisitedLinkColor = AccentColor,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        _previewMetaLabel.LinkClicked += PreviewMetaLabelOnLinkClicked;
        previewHeaderPanel.Controls.Add(_previewMetaLabel, 0, 1);

        var previewBodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceAccentColor,
            Margin = new Padding(0),
            Padding = new Padding(1),
        };
        previewContentInner.Controls.Add(previewBodyPanel, 0, 1);

        var previewBodyInnerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        previewBodyPanel.Controls.Add(previewBodyInnerPanel);

        _previewBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Malgun Gothic", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false,
            HideSelection = true,
            BackColor = Color.White,
            ForeColor = TextColor,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
        };
        previewBodyInnerPanel.Controls.Add(_previewBox);

        var previewNavPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = SurfaceColor,
            Padding = new Padding(0),
        };

        var previewNavGroup = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = SurfaceColor,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(0),
        };
        previewNavGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewNavGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewNavGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewNavGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewNavPanel.Controls.Add(previewNavGroup);

        previewNavPanel.Resize += (_, _) =>
        {
            previewNavGroup.Location = new Point(
                Math.Max(0, previewNavPanel.ClientSize.Width - previewNavGroup.Width),
                Math.Max(0, previewNavPanel.ClientSize.Height - previewNavGroup.Height));
        };

        _previewExpandButton = new Button { Text = "전체 보기", Width = 102, Height = 34, Margin = new Padding(0, 0, 12, 0), Visible = false };
        StylePreviewNavButton(_previewExpandButton);
        _previewExpandButton.Click += (_, _) =>
        {
            _previewShowFullDocument = true;
            RenderSelectedPreview();
        };
        previewNavGroup.Controls.Add(_previewExpandButton, 0, 0);

        _previewMatchLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 8, 12, 0),
        };
        previewNavGroup.Controls.Add(_previewMatchLabel, 1, 0);

        _previewPreviousButton = new Button { Text = "이전 (A)", Width = 96, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        StylePreviewNavButton(_previewPreviousButton);
        _previewPreviousButton.Click += (_, _) => MovePreviewMatch(-1);
        previewNavGroup.Controls.Add(_previewPreviousButton, 2, 0);

        _previewNextButton = new Button { Text = "다음 (S)", Width = 96, Height = 34, Margin = new Padding(0) };
        StylePreviewNavButton(_previewNextButton);
        _previewNextButton.Click += (_, _) => MovePreviewMatch(1);
        previewNavGroup.Controls.Add(_previewNextButton, 3, 0);

        previewCard.Controls.Add(previewNavPanel, 0, 1);
        Program.Log("Preview box created");
    }

    private Panel BuildPane(string title, string tooltipText, out Panel contentPanel)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceAccentColor,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 10, 0),
        };

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Margin = new Padding(0),
            BackColor = SurfaceAccentColor,
            Padding = new Padding(12, 0, 10, 0),
        };

        var headerContent = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 7, 0, 0),
            BackColor = SurfaceAccentColor,
        };

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 4, 0),
            BackColor = SurfaceAccentColor,
            ForeColor = AccentColor,
            Font = new Font("Malgun Gothic", 10F, FontStyle.Bold, GraphicsUnit.Point),
        };

        var infoIcon = new PictureBox
        {
            Width = 16,
            Height = 16,
            Margin = new Padding(0, 2, 0, 0),
            Padding = new Padding(0),
            Image = _infoIconImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Cursor = Cursors.Hand,
        };

        headerContent.Controls.Add(titleLabel);
        headerContent.Controls.Add(infoIcon);
        headerPanel.Controls.Add(headerContent);
        _paneToolTip.SetToolTip(infoIcon, tooltipText);

        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = SurfaceColor,
        };

        panel.Controls.Add(contentPanel);
        panel.Controls.Add(headerPanel);
        return panel;
    }

    private static ToolTip CreatePaneToolTip()
    {
        return new ToolTip
        {
            AutomaticDelay = 1,
            InitialDelay = 1,
            ReshowDelay = 1,
            AutoPopDelay = 20000,
            ShowAlways = true,
            UseAnimation = false,
            UseFading = false,
        };
    }

    private static Bitmap CreateInfoIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var fillBrush = new SolidBrush(Color.FromArgb(255, 250, 246));
        using var borderPen = new Pen(Color.FromArgb(205, 158, 137), 1.2F);
        graphics.FillEllipse(fillBrush, 1.5F, 1.5F, 13F, 13F);
        graphics.DrawEllipse(borderPen, 1.5F, 1.5F, 13F, 13F);

        using var stemPen = new Pen(AccentColor, 1.6F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        using var dotBrush = new SolidBrush(AccentColor);
        graphics.FillEllipse(dotBrush, 7F, 3.5F, 2F, 2F);
        graphics.DrawLine(stemPen, 8F, 7F, 8F, 11F);

        return bitmap;
    }

    private static Bitmap CreateSuccessIcon()
    {
        var bitmap = new Bitmap(12, 12);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var fillBrush = new SolidBrush(Color.FromArgb(44, 163, 76));
        graphics.FillEllipse(fillBrush, 0.5F, 0.5F, 11F, 11F);

        using var pen = new Pen(Color.White, 1.6F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        graphics.DrawLines(pen,
        [
            new PointF(3.2F, 6.1F),
            new PointF(5.1F, 8.1F),
            new PointF(8.7F, 3.8F),
        ]);

        return bitmap;
    }

    private static Bitmap CreateFailureIcon()
    {
        using var source = SystemIcons.Warning.ToBitmap();
        var bitmap = new Bitmap(12, 12);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, new Rectangle(0, 0, 12, 12));
        return bitmap;
    }

    private void ApplyMainSplitRatio()
    {
        if (_mainSplit is null || !_mainSplit.IsHandleCreated)
        {
            return;
        }

        var available = Math.Max(_mainSplit.Width, _mainSplit.ClientSize.Width);
        if (available <= _mainSplit.Panel1MinSize + _mainSplit.Panel2MinSize)
        {
            return;
        }

        var target = (int)(available * (3d / 9d));
        var maxLeft = available - _mainSplit.Panel2MinSize - 12;
        _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize, Math.Min(target, maxLeft));
    }

    private void BindInitialState()
    {
        _folderTextBox.Text = string.IsNullOrWhiteSpace(_settings.LastFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : _settings.LastFolder;
        _patternTextBox.Text = DocumentSearcher.DefaultPatterns;
        _currentIndex = DocumentIndexStore.Load();
        _statusLabel.Text = "대기 중";
        SetSearchTarget(SearchTarget.FileName);
        ClearPreviewPanel();
        UpdateSearchButtonVisualState();
    }

    private void KeywordTextBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
        _ = RequestSearchAsync();
    }

    private void KeywordTextBoxOnTextChanged(object? sender, EventArgs e)
    {
        UpdateSearchButtonVisualState();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            _keywordTextBox.Focus();
            _keywordTextBox.SelectAll();
            return true;
        }

        if (keyData == Keys.Enter && _searchButton.Enabled)
        {
            _ = RequestSearchAsync();
            return true;
        }

        if (keyData is Keys.Q or Keys.W)
        {
            var focusedControl = ActiveControl;
            if (focusedControl is not TextBoxBase)
            {
                SetSearchTarget(keyData == Keys.Q ? SearchTarget.FileName : SearchTarget.DocumentContent);
                return true;
            }
        }

        if (keyData is Keys.A or Keys.S)
        {
            var focusedControl = ActiveControl;
            if (focusedControl is TextBoxBase)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (_searchTarget == SearchTarget.DocumentContent && _selectedPreviewResult is not null)
            {
                MovePreviewMatch(keyData == Keys.A ? -1 : 1);
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 앱 레벨에서 마우스 클릭을 감지해, 텍스트박스 외부 클릭 시 포커스를 해제합니다.
    /// </summary>
    private sealed class DefocusMessageFilter : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private readonly MainForm _form;

        public DefocusMessageFilter(MainForm form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDOWN && _form.ActiveControl is TextBoxBase)
            {
                var clicked = Control.FromHandle(m.HWnd);
                if (clicked is not null && !IsTextInput(clicked))
                {
                    _form.ActiveControl = null;
                }
            }
            return false;
        }

        private static bool IsTextInput(Control? c)
        {
            while (c is not null)
            {
                if (c is TextBoxBase) return true;
                c = c.Parent;
            }
            return false;
        }
    }

    private async Task RequestSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(_keywordTextBox.Text))
        {
            ClearSearchResultsToIdle();
            return;
        }

        if (_searchCts is not null)
        {
            _restartRequested = true;
            _searchCts.Cancel();
            return;
        }

        await StartSearchCoreAsync();
    }

    private async Task PickFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(_folderTextBox.Text)
                ? _folderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _folderTextBox.Text = dialog.SelectedPath;
        SaveLastFolder(dialog.SelectedPath);
        var indexed = await EnsureIndexReadyAsync(forceRebuild: false);
        if (indexed && _currentIndex is not null)
        {
            _statusLabel.Text = $"색인 완료: {_currentIndex.Entries.Count}개 파일";
        }

        if (!string.IsNullOrWhiteSpace(_keywordTextBox.Text))
        {
            await RequestSearchAsync();
        }
    }

    private string[] GetCurrentPatterns()
    {
        return _patternTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private bool IsCurrentIndexValid(string rootFolder, IReadOnlyList<string> patterns)
    {
        if (_currentIndex is null)
        {
            return false;
        }

        if (_currentIndex.FormatVersion != DocumentIndexData.CurrentFormatVersion)
        {
            return false;
        }

        if (!string.Equals(_currentIndex.RootFolder, rootFolder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_currentIndex.Patterns.Count != patterns.Count)
        {
            return false;
        }

        for (var i = 0; i < patterns.Count; i++)
        {
            if (!string.Equals(_currentIndex.Patterns[i], patterns[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> EnsureIndexReadyAsync(bool forceRebuild)
    {
        var rootFolder = _folderTextBox.Text.Trim();
        var patterns = GetCurrentPatterns();

        if (!Directory.Exists(rootFolder) || patterns.Length == 0)
        {
            return false;
        }

        if (!forceRebuild && IsCurrentIndexValid(rootFolder, patterns))
        {
            return true;
        }

        SetBusyState(true);
        Enabled = false;

        using var popup = new IndexingProgressForm();
        using var indexingCts = new CancellationTokenSource();
        var canceled = false;
        popup.CancelRequested += (_, _) =>
        {
            canceled = true;
            popup.SetCancelState(true);
            _statusLabel.Text = "색인을 취소하는 중...";
            indexingCts.Cancel();
        };
        popup.ShowCenteredOver(this);
        popup.UpdateProgress(new IndexingProgress(0, 1, string.Empty, false));
        popup.Refresh();
        _statusLabel.Text = "문서 색인 준비 중...";
        _statusLabel.Text = "문서 색인 중...";

        try
        {
            var progress = new Progress<IndexingProgress>(item =>
            {
                popup.UpdateProgress(item);
                if (item.TotalFiles > 0 && item.CurrentFile > 0)
                {
                    _statusLabel.Text = $"{item.CurrentFile} / {item.TotalFiles} 색인 중 {Path.GetFileName(item.CurrentPath)}";
                }
                else
                {
                    _statusLabel.Text = "문서 색인 준비 중...";
                }
                if (item.TotalFiles > 0 && item.CurrentFile > 0)
                {
                    _statusLabel.Text = $"{item.CurrentFile} / {item.TotalFiles} 색인 중 {Path.GetFileName(item.CurrentPath)}";
                }
                else
                {
                    _statusLabel.Text = "문서 색인 준비 중...";
                }
            });

            _currentIndex = await Task.Run(() => _searcher.BuildOrUpdateIndex(
                rootFolder,
                patterns,
                forceRebuild ? null : _currentIndex,
                progress,
                indexingCts.Token));

            DocumentIndexStore.Save(_currentIndex);
            _statusLabel.Text = $"색인 완료: {_currentIndex.Entries.Count}개 파일";
            return true;
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "색인을 취소했어.";
            return false;
        }
        finally
        {
            Enabled = true;
            popup.Close();
            SetBusyState(false);
            if (canceled)
            {
                _statusLabel.Text = "색인을 취소했어.";
            }
        }
    }

    private async Task StartSearchCoreAsync()
    {
        var rootFolder = _folderTextBox.Text.Trim();
        if (!Directory.Exists(rootFolder))
        {
            MessageBox.Show(this, "유효한 폴더를 선택해야 해.", "폴더 오류");
            return;
        }

        var patterns = GetCurrentPatterns();

        if (patterns.Length == 0)
        {
            MessageBox.Show(this, "적어도 하나의 확장자 패턴이 필요해.", "패턴 오류");
            return;
        }

        SaveLastFolder(rootFolder);
        var indexed = await EnsureIndexReadyAsync(forceRebuild: !IsCurrentIndexValid(rootFolder, patterns));
        if (!indexed)
        {
            return;
        }

        if (_currentIndex is not null)
        {
            _statusLabel.Text = $"색인 완료: {_currentIndex.Entries.Count}개 파일";
        }
        _results.Clear();
        ClearPreviewPanel();
        SetBusyState(true);
        _restartRequested = false;

        _searchCts = new CancellationTokenSource();
        var progress = new Progress<SearchProgress>(HandleProgress);

        try
        {
            await Task.Run(() => _searcher.Search(
                _currentIndex!,
                _keywordTextBox.Text,
                _searchTarget,
                progress,
                _searchCts.Token));
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = _restartRequested ? "검색을 다시 시작하는 중..." : "검색을 취소했어.";
        }
        finally
        {
            _searchCts.Dispose();
            _searchCts = null;
            SetBusyState(false);
        }

        if (_restartRequested)
        {
            await StartSearchCoreAsync();
        }
    }

    private void HandleProgress(SearchProgress progress)
    {
        if (progress.TotalFiles > 0 && progress.CurrentFile > 0)
        {
            _statusLabel.Text = $"{progress.CurrentFile} / {progress.TotalFiles} 검사 중: {Path.GetFileName(progress.CurrentPath)}";
        }

        if (progress.Result is not null)
        {
            _results.Add(progress.Result);
            if (_results.Count <= 3)
            {
                RefreshResultColumnWidths();
            }
        }

        if (progress.IsCompleted)
        {
            var successCount = _results.Count(static item => item.Status == "성공");
            var failureCount = _results.Count(static item => item.Status == "실패");
            _statusLabel.Text = $"완료: {successCount}건 성공, {failureCount}건 실패";

            if (_results.Count > 0)
            {
                _resultsGrid.ClearSelection();
                _resultsGrid.Rows[0].Selected = true;
                _resultsGrid.CurrentCell = _resultsGrid.Rows[0].Cells[1];
            }
        }
    }

    private void SetBusyState(bool busy)
    {
        _isSearchBusy = busy;
        _folderTextBox.Enabled = !busy;
        _keywordTextBox.Enabled = !busy;
        _patternTextBox.Enabled = !busy;
        _browseFolderButton.Enabled = !busy;
        _resetPatternButton.Enabled = !busy;
        _searchTargetToggle.Enabled = !busy;

        if (busy)
        {
            _statusLabel.Text = "검색 시작 중...";
        }

        UpdateSearchButtonVisualState();
    }

    private void ClearSearchResultsToIdle()
    {
        _results.Clear();
        ClearPreviewPanel();
        _statusLabel.Text = "대기 중";
    }

    private void UpdateSearchButtonVisualState()
    {
        if (_searchButton is null)
        {
            return;
        }

        var hasKeyword = !string.IsNullOrWhiteSpace(_keywordTextBox.Text);
        _searchButton.Enabled = !_isSearchBusy && hasKeyword;

        if (!_searchButton.Enabled)
        {
            _searchButton.BackColor = DisabledButtonBackColor;
            _searchButton.ForeColor = DisabledButtonForeColor;
            return;
        }

        _searchButton.BackColor = hasKeyword ? AccentColor : DisabledButtonBackColor;
        _searchButton.ForeColor = hasKeyword ? Color.White : DisabledButtonForeColor;
    }

    private void RefreshResultColumnWidths()
    {
        if (_resultsGrid.Columns.Count < 4)
        {
            return;
        }

        _resultsGrid.AutoResizeColumn(2, DataGridViewAutoSizeColumnMode.AllCells);
        _resultsGrid.AutoResizeColumn(3, DataGridViewAutoSizeColumnMode.AllCells);
        _resultsGrid.Columns[2].Width = Math.Max(220, _resultsGrid.Columns[2].Width);
        _resultsGrid.Columns[3].Width = Math.Max(420, _resultsGrid.Columns[3].Width);
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 8, 0),
        };
    }

    private static TextBox CreateInputTextBox()
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Margin = new Padding(0),
        };
    }

    private static Panel CreateInputHost(TextBox textBox)
    {
        var isReadOnly = textBox.ReadOnly;
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = isReadOnly ? Color.FromArgb(247, 241, 236) : SurfaceColor,
            Margin = new Padding(0, TopVerticalMargin, 0, TopVerticalMargin),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = isReadOnly ? Cursors.Default : Cursors.IBeam,
        };

        textBox.BackColor = host.BackColor;
        textBox.ForeColor = isReadOnly ? MutedTextColor : TextColor;

        void LayoutTextBox()
        {
            var horizontalPadding = 10;
            var textHeight = Math.Max(18, TextRenderer.MeasureText("가", textBox.Font).Height);
            var textWidth = Math.Max(0, host.ClientSize.Width - (horizontalPadding * 2));
            var textTop = Math.Max(0, (host.ClientSize.Height - textHeight) / 2);
            textBox.SetBounds(horizontalPadding, textTop, textWidth, textHeight + 2);
        }

        host.Resize += (_, _) => LayoutTextBox();
        host.MouseDown += (_, _) =>
        {
            if (isReadOnly)
            {
                return;
            }

            textBox.Focus();
            textBox.SelectionStart = textBox.TextLength;
        };
        host.Enter += (_, _) =>
        {
            if (!isReadOnly)
            {
                textBox.Focus();
            }
        };
        host.Controls.Add(textBox);
        LayoutTextBox();
        return host;
    }

    private static void StyleActionButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentColor;
        button.ForeColor = Color.White;
        button.Padding = new Padding(14, 0, 14, 0);
        button.Margin = new Padding(0, TopVerticalMargin, 8, TopVerticalMargin);
        button.Height = TopControlHeight;
        button.AutoSize = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private static void StylePreviewNavButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = BorderColor;
        button.BackColor = SurfaceColor;
        button.ForeColor = TextColor;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private void SetSearchTarget(SearchTarget searchTarget)
    {
        _searchTarget = searchTarget;
        if (_searchTargetToggle.SelectedTarget != searchTarget)
        {
            _searchTargetToggle.SelectedTarget = searchTarget;
        }

        _searchTargetToggle.Invalidate();
        UpdateSearchButtonVisualState();
    }

    private SearchResult? GetSelectedResult()
    {
        return _resultsGrid.CurrentRow?.DataBoundItem as SearchResult;
    }

    private void ShowSelectedPreview()
    {
        _selectedPreviewResult = GetSelectedResult();
        _selectedPreviewMatchIndex = 0;
        _previewShowFullDocument = false;
        RenderSelectedPreview();
    }

    private void MovePreviewMatch(int direction)
    {
        if (_selectedPreviewResult is null)
        {
            return;
        }

        var preview = PreviewDocumentBuilder.Build(
            _selectedPreviewResult,
            _searchTarget,
            _currentIndex,
            _keywordTextBox.Text.Trim(),
            _selectedPreviewMatchIndex,
            _previewShowFullDocument);

        if (preview.Matches.Count <= 1)
        {
            return;
        }

        _selectedPreviewMatchIndex = Math.Clamp(_selectedPreviewMatchIndex + direction, 0, preview.Matches.Count - 1);
        RenderSelectedPreview();
    }

    private void RenderSelectedPreview()
    {
        if (_selectedPreviewResult is null)
        {
            ClearPreviewPanel();
            return;
        }

        var preview = PreviewDocumentBuilder.Build(
            _selectedPreviewResult,
            _searchTarget,
            _currentIndex,
            _keywordTextBox.Text.Trim(),
            _selectedPreviewMatchIndex,
            _previewShowFullDocument);

        _selectedPreviewMatchIndex = preview.SelectedMatchIndex;
        _previewTitleLabel.Text = preview.Title;
        _previewTitleLabel.Links.Clear();
        _previewTitleLabel.Links.Add(0, _previewTitleLabel.Text.Length, _selectedPreviewResult.Path);
        _previewMetaLabel.Text = _selectedPreviewResult.DirectoryPath;
        _previewMetaLabel.Links.Clear();
        _previewMetaLabel.Links.Add(0, _previewMetaLabel.Text.Length, _selectedPreviewResult.DirectoryPath);
        var previewExtension = Path.GetExtension(_selectedPreviewResult.Path);
        _previewBox.Font = GetPreviewFont(previewExtension);
        ConfigurePreviewBoxLayout(previewExtension);
        var selectedMatchIndexInPreview = -1;
        var previewBody = preview.Body;
        var startMarkerIndex = previewBody.IndexOf(PreviewDocumentBuilder.SelectedMatchStartMarker, StringComparison.Ordinal);
        if (startMarkerIndex >= 0)
        {
            selectedMatchIndexInPreview = NormalizeRichTextIndex(previewBody, startMarkerIndex);
            previewBody = previewBody.Remove(startMarkerIndex, PreviewDocumentBuilder.SelectedMatchStartMarker.Length);
            var endMarkerIndex = previewBody.IndexOf(PreviewDocumentBuilder.SelectedMatchEndMarker, startMarkerIndex, StringComparison.Ordinal);
            if (endMarkerIndex >= 0)
            {
                previewBody = previewBody.Remove(endMarkerIndex, PreviewDocumentBuilder.SelectedMatchEndMarker.Length);
            }
        }

        _previewBox.Clear();
        _previewBox.Text = previewBody;

        if (preview.HighlightKeyword)
        {
            HighlightKeyword(_keywordTextBox.Text.Trim(), selectedMatchIndexInPreview);
        }
        else
        {
            _previewBox.Select(0, 0);
        }

        var hasMultipleMatches = preview.Matches.Count > 1;
        _previewExpandButton.Visible = _searchTarget == SearchTarget.DocumentContent && preview.IsTruncated;
        _previewExpandButton.Enabled = preview.IsTruncated;
        _previewPreviousButton.Enabled = hasMultipleMatches && _selectedPreviewMatchIndex > 0;
        _previewNextButton.Enabled = hasMultipleMatches && _selectedPreviewMatchIndex < preview.Matches.Count - 1;
        _previewMatchLabel.Text = preview.Matches.Count == 0
            ? "검색 위치 정보 없음"
            : $"검색 위치 {_selectedPreviewMatchIndex + 1} / {preview.Matches.Count} · 줄 {preview.Matches[_selectedPreviewMatchIndex].LineNumber}";
    }

    private static Font GetPreviewFont(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" or ".csv" or ".tsv" => new Font("GulimChe", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ".json" or ".xml" or ".yaml" or ".yml" or ".css" or ".js" or ".ts" or ".py" or ".cs" or ".java" or ".sql" => new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
            _ => new Font("Malgun Gothic", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
        };
    }

    private void ConfigurePreviewBoxLayout(string extension)
    {
        var isTabular = extension.ToLowerInvariant() is ".xlsx" or ".xls" or ".csv" or ".tsv";
        _previewBox.WordWrap = !isTabular;
        _previewBox.ScrollBars = isTabular ? RichTextBoxScrollBars.Both : RichTextBoxScrollBars.Vertical;
    }

    private void ClearPreviewPanel()
    {
        _selectedPreviewResult = null;
        _selectedPreviewMatchIndex = 0;
        _previewShowFullDocument = false;
        _previewTitleLabel.Links.Clear();
        _previewTitleLabel.Text = string.Empty;
        _previewMetaLabel.Links.Clear();
        _previewMetaLabel.Text = string.Empty;
        _previewMatchLabel.Text = string.Empty;
        _previewExpandButton.Visible = false;
        _previewExpandButton.Enabled = false;
        _previewPreviousButton.Enabled = false;
        _previewNextButton.Enabled = false;
        _previewBox.Clear();
    }

    private void PreviewTitleLabelOnLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (e.Link.LinkData is string path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
    }

    private void PreviewMetaLabelOnLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (e.Link.LinkData is string path && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
    }

    private void ResultsGridOnCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var column = _resultsGrid.Columns[e.ColumnIndex];
        if (column.Name != "StatusIcon" && column.DataPropertyName != nameof(SearchResult.Status))
        {
            return;
        }

        if (_resultsGrid.Rows[e.RowIndex].DataBoundItem is SearchResult result && result.Status == "실패")
        {
            e.ToolTipText = FailureTooltip;
        }
    }

    private void ResultsGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_resultsGrid.Columns[e.ColumnIndex].Name != "StatusIcon")
        {
            return;
        }

        if (_resultsGrid.Rows[e.RowIndex].DataBoundItem is not SearchResult result)
        {
            return;
        }

        e.Value = result.Status switch
        {
            "성공" => _successIcon,
            "실패" => _failureIcon,
            _ => null,
        };
        e.FormattingApplied = true;
    }

    private void ResultsGridOnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (_searchTarget != SearchTarget.FileName || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_resultsGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(SearchResult.FileName))
        {
            return;
        }

        if (_resultsGrid.Rows[e.RowIndex].DataBoundItem is not SearchResult result)
        {
            return;
        }

        var keyword = _keywordTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        var fileName = result.FileName;
        var matchIndex = fileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
        {
            return;
        }

        e.PaintBackground(e.CellBounds, e.State.HasFlag(DataGridViewElementStates.Selected));
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);

        var font = e.CellStyle.Font ?? Font;
        var foreColor = e.State.HasFlag(DataGridViewElementStates.Selected) ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
        var textY = e.CellBounds.Top + ((e.CellBounds.Height - font.Height) / 2);
        var startX = e.CellBounds.Left + 6;

        var prefix = fileName[..matchIndex];
        var matched = fileName.Substring(matchIndex, keyword.Length);
        var suffix = fileName[(matchIndex + keyword.Length)..];

        var prefixSize = TextRenderer.MeasureText(e.Graphics, prefix, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var matchedSize = TextRenderer.MeasureText(e.Graphics, matched, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);

        TextRenderer.DrawText(e.Graphics, prefix, font, new Point(startX, textY), foreColor, TextFormatFlags.NoPadding);

        var highlightRect = new Rectangle(startX + prefixSize.Width, textY - 1, matchedSize.Width + 2, font.Height + 2);
        using var highlightBrush = new SolidBrush(Color.Black);
        e.Graphics.FillRectangle(highlightBrush, highlightRect);
        TextRenderer.DrawText(e.Graphics, matched, font, new Point(highlightRect.Left + 1, textY), Color.White, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, suffix, font, new Point(highlightRect.Right + 1, textY), foreColor, TextFormatFlags.NoPadding);

        e.Handled = true;
    }

    private void SetPreview(string text)
    {
        _previewBox.Clear();
        _previewBox.Text = text;

        if (_searchTarget == SearchTarget.DocumentContent)
        {
            HighlightKeyword(_keywordTextBox.Text.Trim());
        }
    }

    private void HighlightKeyword(string keyword, int selectedMatchIndexInPreview = -1)
    {
        if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrEmpty(_previewBox.Text))
        {
            return;
        }

        var firstMatchIndex = _previewBox.Text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        var focusedMatchIndex = selectedMatchIndexInPreview >= 0
            ? selectedMatchIndexInPreview
            : firstMatchIndex;

        _previewBox.SelectAll();
        _previewBox.SelectionBackColor = _previewBox.BackColor;
        _previewBox.SelectionColor = _previewBox.ForeColor;

        var source = _previewBox.Text;
        var index = 0;
        while (index < source.Length)
        {
            index = source.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            _previewBox.Select(index, keyword.Length);
            _previewBox.SelectionBackColor = Color.Black;
            _previewBox.SelectionColor = Color.White;
            index += keyword.Length;
        }

        if (focusedMatchIndex >= 0)
        {
            _previewBox.Select(focusedMatchIndex, keyword.Length);
            _previewBox.SelectionBackColor = AccentColor;
            _previewBox.SelectionColor = Color.White;

            var matchLine = _previewBox.GetLineFromCharIndex(focusedMatchIndex);
            var targetLine = Math.Max(0, matchLine - 3);
            var targetIndex = _previewBox.GetFirstCharIndexFromLine(targetLine);
            if (targetIndex >= 0)
            {
                _previewBox.Select(targetIndex, 0);
                _previewBox.ScrollToCaret();
            }
        }
        else
        {
            _previewBox.Select(0, 0);
        }
    }

    private static int NormalizeRichTextIndex(string source, int charIndex)
    {
        if (charIndex <= 0)
        {
            return 0;
        }

        var safeLength = Math.Min(charIndex, source.Length);
        var normalizedIndex = 0;
        for (var i = 0; i < safeLength; i++)
        {
            if (source[i] != '\r')
            {
                normalizedIndex++;
            }
        }

        return normalizedIndex;
    }

    private void OpenSelectedFile()
    {
        var result = GetSelectedResult();
        if (result is null)
        {
            MessageBox.Show(this, "먼저 결과 목록에서 파일을 선택해야 해.", "선택 필요");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = result.Path,
            UseShellExecute = true,
        });
    }

    private void SaveLastFolder(string folder)
    {
        _settings.LastFolder = folder;
        _settings.Save();
    }
}

public sealed class AppSettings
{
    public string LastFolder { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        AppPaths.EnsureAppDataDirectory();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}


