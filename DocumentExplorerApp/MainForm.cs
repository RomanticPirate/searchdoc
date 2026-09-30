using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace DocumentExplorerApp;

public sealed class MainForm : Form
{
    private const int TopRowHeight = 38;
    private const int TopControlHeight = 28;
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

    private readonly DocumentSearcher _searcher = new();
    private readonly BindingList<SearchResult> _results = [];
    private readonly AppSettings _settings;
    private readonly ToolTip _paneToolTip = CreatePaneToolTip();
    private readonly Image _infoIconImage = CreateInfoIcon();
    private DocumentIndexData? _currentIndex;

    private TextBox _folderTextBox = null!;
    private TextBox _keywordTextBox = null!;

    private Button _browseFolderButton = null!;
    private Button _searchButton = null!;
    private SearchTargetToggle _searchTargetToggle = null!;
    private Button _advancedButton = null!;
    private Label _statusLabel = null!;
    private DataGridView _resultsGrid = null!;
    private Label _noResultsLabel = null!;
    private RichTextBox _previewBox = null!;
    private LinkLabel _previewTitleLabel = null!;
    private LinkLabel _previewMetaLabel = null!;
    private Label _previewTypeLabel = null!;
    private Label _previewMatchLabel = null!;
    private Button _previewPreviousButton = null!;
    private Button _previewNextButton = null!;
    private Button _previewExpandButton = null!;
    private SplitContainer _mainSplit = null!;
    private TableLayoutPanel _root = null!;

    private CancellationTokenSource? _searchCts;
    private DefocusMessageFilter? _defocusFilter;
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
        Size = new Size(1100, 840);
        MinimumSize = new Size(1100, 840);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Malgun Gothic", 8F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (executableIcon is not null)
        {
            Icon = executableIcon;
        }

        _settings = AppSettings.Load();
        Program.Log("Settings loaded");

        if (_settings.GetEffectiveFolders().Count == 0)
        {
            using var setupForm = new InitialSetupForm();
            if (setupForm.ShowDialog() == DialogResult.OK && setupForm.SelectedFolders.Count > 0)
            {
                _settings.SearchFolders = setupForm.SelectedFolders;
                _settings.Save();
            }
            else
            {
                Environment.Exit(0);
                return;
            }
        }

        BuildLayout();
        Program.Log("Layout built");

        // 입력칸 외부 클릭 시 포커스 해제 (단축키 입력을 위해)
        _defocusFilter = new DefocusMessageFilter(this);
        Application.AddMessageFilter(_defocusFilter);

        BindInitialState();
        Program.Log("Initial state bound");

        Shown += (_, _) =>
        {
            ApplyMainSplitRatio();
            Program.Log("MainForm shown");
        };

        void SaveWindowSize()
        {
            if (WindowState != FormWindowState.Normal) return;
            if (IsSimpleMode) { _settings.SimpleWindowWidth = Width; _settings.SimpleWindowHeight = Height; }
            else              { _settings.DetailedWindowWidth = Width; _settings.DetailedWindowHeight = Height; }
            _settings.Save();
        }

        ResizeEnd += (_, _) => SaveWindowSize();

    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _infoIconImage?.Dispose();
            _paneToolTip?.Dispose();
            _searchCts?.Dispose();
            _searchCts = null;
            if (_defocusFilter is not null)
                Application.RemoveMessageFilter(_defocusFilter);
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        Program.Log("BuildLayout start");
        if (IsSimpleMode)
            BuildSimpleLayout();
        else
            BuildDetailedLayout();
    }

    private void BuildSimpleLayout()
    {
        Program.Log("BuildSimpleLayout start");
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        MinimumSize = new Size(480, 300); // 생성자의 1100x840 초기값 즉시 교체 + UI 잘림 방지 최솟값

        // 타이틀바
        var titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = _titleBarHeight,
            BackColor = AppBackgroundColor,
            Padding = new Padding(0),
        };
        titleBar.Paint += (_, pe) =>
        {
            using var pen = new Pen(BorderColor, 1f);
            pe.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
        };
        var titleLeft = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackgroundColor,
        };
        titleLeft.MouseDown += (s, e) => { };
        var searchIconLabel = new Label
        {
            Text = "🔍",
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = AppBackgroundColor,
            ForeColor = AccentColor,
        };
        var appTitleLabel = new Label
        {
            Text = "찾아줘문서",
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Malgun Gothic", 8F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = AppBackgroundColor,
            ForeColor = TextColor,
            Padding = new Padding(2, 0, 0, 0),
        };
        var closeBtn = new Button
        {
            Text = "×",
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppBackgroundColor,
            ForeColor = TextColor,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            TabStop = false,
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        AutoSizeButtonToText(closeBtn, 12, 0);
        closeBtn.Click += (_, _) => Close();
        closeBtn.MouseEnter += (_, _) => closeBtn.BackColor = Color.FromArgb(196, 108, 109);
        closeBtn.MouseLeave += (_, _) => closeBtn.BackColor = AppBackgroundColor;
        var minimizeBtn = new Button
        {
            Text = "−",
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppBackgroundColor,
            ForeColor = TextColor,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            TabStop = false,
        };
        minimizeBtn.FlatAppearance.BorderSize = 0;
        AutoSizeButtonToText(minimizeBtn, 12, 0);
        minimizeBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        minimizeBtn.MouseEnter += (_, _) => minimizeBtn.BackColor = BorderColor;
        minimizeBtn.MouseLeave += (_, _) => minimizeBtn.BackColor = AppBackgroundColor;
        // 드래그 이동 핸들러
        Point _dragStart = default;
        MouseEventHandler onTitleDown = (s, e) => { if (e.Button == MouseButtons.Left) _dragStart = Cursor.Position - (Size)Location; };
        MouseEventHandler onTitleMove = (s, e) => { if (e.Button == MouseButtons.Left) Location = Cursor.Position - (Size)_dragStart; };
        titleBar.MouseDown += onTitleDown; titleBar.MouseMove += onTitleMove;
        titleLeft.MouseDown += onTitleDown; titleLeft.MouseMove += onTitleMove;
        searchIconLabel.MouseDown += onTitleDown; searchIconLabel.MouseMove += onTitleMove;
        appTitleLabel.MouseDown += onTitleDown; appTitleLabel.MouseMove += onTitleMove;

        // 버튼 순서: minimizeBtn 먼저(오른쪽 끝), closeBtn 나중(그 왼쪽)
        titleBar.Controls.Add(titleLeft);
        titleBar.Controls.Add(minimizeBtn);
        titleBar.Controls.Add(closeBtn);
        titleLeft.Controls.Add(appTitleLabel);
        titleLeft.Controls.Add(searchIconLabel);

        var root = _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 4, 8, 4),
            BackColor = AppBackgroundColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F)); // row 0: 검색어
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // row 1: 결과/미리보기
        Controls.Add(root);      // Fill: 먼저 추가
        Controls.Add(titleBar);  // Top: 나중 추가 → 상단 공간 먼저 차지
        Program.Log("Root created");

        // 심플 모드: 검색 폴더 UI 없음 (진입 전 폴더 설정 검증 완료)
        // 코드 호환성을 위해 인스턴스만 생성
        _folderTextBox = CreateInputTextBox();
        _folderTextBox.ReadOnly = true;
        _folderTextBox.TabStop = false;

        _browseFolderButton = new Button { Text = "폴더 설정" };
        StyleActionButton(_browseFolderButton);
        _browseFolderButton.Click += async (_, _) => await PickFolderAsync();

        _advancedButton = new Button
        {
            Text = "⚙",
            Font = new Font("Malgun Gothic", 8F, FontStyle.Regular, GraphicsUnit.Point),
        };
        StyleAdvancedButton(_advancedButton, false);
        _advancedButton.Padding = new Padding(0);
        _advancedButton.Margin = new Padding(4, 4, 0, 4);
        AutoSizeButtonToText(_advancedButton, 8, 6);
        _advancedButton.Click += (_, _) =>
        {
            // 모드 전환 전에 현재 모드의 창 크기를 저장 (전환 후에는 IsSimpleMode가 바뀜)
            if (WindowState == FormWindowState.Normal)
            {
                if (IsSimpleMode) { _settings.SimpleWindowWidth = Width; _settings.SimpleWindowHeight = Height; }
                else { _settings.DetailedWindowWidth = Width; _settings.DetailedWindowHeight = Height; }
            }
            var previousFolders = _settings.SearchFolders.ToList();
            using var settingsForm = new SettingsForm(_settings);
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                _settings.Save();
                var foldersChanged = !previousFolders.SequenceEqual(_settings.SearchFolders, StringComparer.OrdinalIgnoreCase);
                if (foldersChanged)
                {
                    _folderTextBox.Text = string.Join("; ", _settings.SearchFolders);
                    _ = EnsureIndexReadyAsync(forceRebuild: true);
                }
                if (settingsForm.LayoutModeChanged)
                    Application.Restart();
            }
        };
        Program.Log("Top area created");

        var searchTargetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchTargetPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

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
        searchInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchInputPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        _keywordTextBox = CreateInputTextBox();
        _keywordTextBox.Font = new Font("Malgun Gothic", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _keywordTextBox.KeyDown += KeywordTextBoxOnKeyDown;
        _keywordTextBox.Enter += (_, _) => UpdateSearchButtonVisualState();
        _keywordTextBox.Leave += (_, _) => BeginInvoke((Action)UpdateSearchButtonVisualState);
        _keywordTextBox.TextChanged += KeywordTextBoxOnTextChanged;
        var keywordInputHost = CreateInputHost(_keywordTextBox);
        keywordInputHost.Dock = DockStyle.Fill;
        searchInputPanel.Controls.Add(keywordInputHost, 0, 0);
        _searchButton = new Button { Text = "🔍", Anchor = AnchorStyles.Left };
        StyleActionButton(_searchButton);
        _searchButton.Font = new Font("Malgun Gothic", 11F, FontStyle.Regular, GraphicsUnit.Point);
        _searchButton.Padding = new Padding(0);
        _searchButton.Margin = new Padding(0, 4, 0, 4);
        AutoSizeButtonToText(_searchButton, 14, 4);
        _searchButton.Click += async (_, _) => { if (_searchButton.Tag is true) await RequestSearchAsync(); };
        _paneToolTip.SetToolTip(_keywordTextBox, "단축키 : Ctrl + F");
        _paneToolTip.SetToolTip(_searchButton, "단축키 : Enter");
        searchInputPanel.Controls.Add(_searchButton, 1, 0);
        searchTargetPanel.Controls.Add(searchInputPanel, 0, 0);

        _searchTargetToggle = new SearchTargetToggle
        {
            Anchor = AnchorStyles.Right,
            AutoSize = false,
            Margin = new Padding(8, 4, 0, 4),
            UseIconMode = true,
        };
        // 토글 내부에서 파일명/문서내용 영역별 자체 툴팁 처리
        _searchTargetToggle.SelectedTargetChanged += (_, target) =>
        {
            SetSearchTarget(target);
            _settings.LastSearchTarget = target == SearchTarget.DocumentContent ? "DocumentContent" : "FileName";
            _settings.Save();
        };
        searchTargetPanel.Controls.Add(_searchTargetToggle, 1, 0);
        _advancedButton.Margin = new Padding(4, TopVerticalMargin, 0, TopVerticalMargin);
        searchTargetPanel.Controls.Add(_advancedButton, 2, 0);

        root.Controls.Add(searchTargetPanel, 0, 0);

        _statusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
        };
        // (statusPanel 생성/추가 코드 제거, _statusLabel은 Controls에 추가하지 않음)
        Program.Log("Action area created");

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AppBackgroundColor,
            Padding = new Padding(0),
            FixedPanel = FixedPanel.None, // 원인7: 자체 비례 조정 허용(ApplyMainSplitRatio가 제어)
        };
        root.Controls.Add(_mainSplit, 0, 1);
        _mainSplit.HandleCreated += (_, _) =>
        {
            _mainSplit.Panel1MinSize = 120;
            _mainSplit.Panel2MinSize = 120;
            ApplyMainSplitRatio();
        };
        _mainSplit.SizeChanged += (_, _) => ApplyMainSplitRatio();
        Program.Log("Split created");

        var leftPanel = BuildPane(out var leftContent);
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
        _resultsGrid.ColumnHeadersVisible = false;
        _resultsGrid.RowTemplate.Height = 28;
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            DataPropertyName = nameof(SearchResult.FileName),
            HeaderText = "파일명",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 60,
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DirectoryPath",
            DataPropertyName = nameof(SearchResult.DirectoryPath),
            HeaderText = "경로",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 320,
            Visible = false,
        });
        _resultsGrid.SelectionChanged += (_, _) => ShowSelectedPreview();
        _resultsGrid.CellDoubleClick += (_, _) => OpenSelectedFile();
        AttachResultsContextMenu();
        _resultsGrid.CellPainting += ResultsGridOnCellPainting;
        _resultsGrid.DataError += (_, e) => e.ThrowException = false;
        leftContent.Controls.Add(_resultsGrid);
        AttachNoResultsLabel(leftContent);
        Program.Log("Grid created");

        var rightPanel = BuildPane(out var rightContent);
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
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
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
            Padding = new Padding(10, 8, 10, 8),
        };
        previewContentInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F)); // 파일명/경로 숨김
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
            Font = new Font("Malgun Gothic", 10F, FontStyle.Bold, GraphicsUnit.Point),
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
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false,
            HideSelection = true,
            BackColor = Color.White,
            ForeColor = TextColor,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
        };
        previewBodyInnerPanel.Controls.Add(_previewBox);

        var previewNavGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(4, 3, 4, 3),
            Margin = new Padding(0),
        };
        previewCard.Controls.Add(previewNavGroup, 0, 1);

        _previewNextButton = new Button { Text = "→", Width = 36, Height = 22, Margin = new Padding(0, 0, 0, 0) };
        StylePreviewNavButton(_previewNextButton);
        _previewNextButton.Click += (_, _) => { if (IsNavButtonActive(_previewNextButton)) MovePreviewMatch(1); };
        previewNavGroup.Controls.Add(_previewNextButton);

        _previewPreviousButton = new Button { Text = "←", Width = 36, Height = 22, Margin = new Padding(0, 0, 4, 0) };
        StylePreviewNavButton(_previewPreviousButton);
        _previewPreviousButton.Click += (_, _) => { if (IsNavButtonActive(_previewPreviousButton)) MovePreviewMatch(-1); };
        previewNavGroup.Controls.Add(_previewPreviousButton);
        AttachNavTooltips(previewNavGroup);

        _previewMatchLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 4, 8, 0),
        };
        previewNavGroup.Controls.Add(_previewMatchLabel);

        _previewExpandButton = new Button { Text = "전체 보기", Width = 70, Height = 22, Margin = new Padding(0, 0, 6, 0), Visible = false };
        StylePreviewNavButton(_previewExpandButton);
        _previewExpandButton.Click += (_, _) =>
        {
            _previewShowFullDocument = true;
            RenderSelectedPreview();
        };
        previewNavGroup.Controls.Add(_previewExpandButton);
        Program.Log("Preview box created");
    }

    private Panel BuildPane(out Panel contentPanel)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceAccentColor,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 10, 0),
        };

        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = SurfaceColor,
        };

        panel.Controls.Add(contentPanel);
        return panel;
    }

    private Panel BuildPaneWithHeader(string title, string tooltipText, out Panel contentPanel)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = SurfaceAccentColor, Padding = new Padding(1), Margin = new Padding(0, 0, 10, 0) };
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = SurfaceAccentColor, Padding = new Padding(12, 0, 10, 0) };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = SurfaceAccentColor,
            ForeColor = AccentColor,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Bold, GraphicsUnit.Point),
        };
        headerPanel.Controls.Add(titleLabel);
        contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = SurfaceColor };
        panel.Controls.Add(contentPanel);
        panel.Controls.Add(headerPanel);
        return panel;
    }

    private void BuildDetailedLayout()
    {
        Program.Log("BuildDetailedLayout start");
        AutoScaleMode = AutoScaleMode.Dpi;

        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.Sizable;
        var dw = _settings.DetailedWindowWidth;
        var dh = _settings.DetailedWindowHeight;
        Size = (dw > 0 && dh > 0) ? new Size(dw, dh) : new Size(1100, 840);
        MinimumSize = new Size(900, 600);

        var root = _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(12, 10, 12, 4),
            BackColor = AppBackgroundColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F)); // row 0: 검색어
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // row 1: 결과/미리보기
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F)); // row 2: 상태바
        Controls.Add(root);
        Program.Log("Detailed root created");

        // 폴더 텍스트박스는 내부 참조용으로만 유지 (UI에 표시하지 않음)
        _folderTextBox = CreateInputTextBox();
        _folderTextBox.ReadOnly = true;
        _folderTextBox.TabStop = false;
        _browseFolderButton = new Button { Text = "폴더 설정" };
        _browseFolderButton.Click += async (_, _) => await PickFolderAsync();

        _advancedButton = new Button
        {
            Text = "⚙",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Malgun Gothic", 11F, FontStyle.Regular, GraphicsUnit.Point),
        };
        StyleAdvancedButton(_advancedButton, false);
        _advancedButton.Margin = new Padding(4, 4, 0, 4);
        AutoSizeButtonToText(_advancedButton, 14, 6);
        _advancedButton.Click += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
            {
                if (IsSimpleMode) { _settings.SimpleWindowWidth = Width; _settings.SimpleWindowHeight = Height; }
                else { _settings.DetailedWindowWidth = Width; _settings.DetailedWindowHeight = Height; }
            }
            var previousFolders = _settings.SearchFolders.ToList();
            using var settingsForm = new SettingsForm(_settings);
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                _settings.Save();
                var foldersChanged = !previousFolders.SequenceEqual(_settings.SearchFolders, StringComparer.OrdinalIgnoreCase);
                if (foldersChanged)
                {
                    _folderTextBox.Text = string.Join("; ", _settings.SearchFolders);
                    _ = EnsureIndexReadyAsync(forceRebuild: true);
                }
                if (settingsForm.LayoutModeChanged)
                    Application.Restart();
            }
        };
        Program.Log("Detailed top area created");

        // row 0: 검색어
        root.Controls.Add(CreateFieldLabel("검색어"), 0, 0);

        var searchTargetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchTargetPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        var searchInputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        searchInputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchInputPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        _keywordTextBox = CreateInputTextBox();
        _keywordTextBox.Font = new Font("Malgun Gothic", 11F, FontStyle.Regular, GraphicsUnit.Point);
        _keywordTextBox.KeyDown += KeywordTextBoxOnKeyDown;
        _keywordTextBox.Enter += (_, _) => UpdateSearchButtonVisualState();
        _keywordTextBox.Leave += (_, _) => BeginInvoke((Action)UpdateSearchButtonVisualState);
        _keywordTextBox.TextChanged += KeywordTextBoxOnTextChanged;
        var keywordInputHost = CreateInputHost(_keywordTextBox);
        keywordInputHost.Dock = DockStyle.Fill;
        searchInputPanel.Controls.Add(keywordInputHost, 0, 0);
        searchTargetPanel.Controls.Add(searchInputPanel, 0, 0);

        _searchButton = new Button
        {
            Text = "검색",
            Width = 150,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Malgun Gothic", 11F, FontStyle.Bold, GraphicsUnit.Point),
        };
        StyleActionButton(_searchButton);
        _searchButton.Margin = new Padding(4, TopVerticalMargin, 0, TopVerticalMargin);
        _searchButton.Height = 32;
        _searchButton.Click += async (_, _) => { if (_searchButton.Tag is true) await RequestSearchAsync(); };
        _paneToolTip.SetToolTip(_searchButton, "단축키 : Enter");
        _paneToolTip.SetToolTip(_keywordTextBox, "단축키 : Ctrl + F");
        searchTargetPanel.Controls.Add(_searchButton, 1, 0);

        _searchTargetToggle = new SearchTargetToggle
        {
            Anchor = AnchorStyles.Right,
            AutoSize = false,
            Size = new Size(180, 24),
            Margin = new Padding(16, 9, 0, 9),
            UseIconMode = false,
        };
        _searchTargetToggle.SelectedTargetChanged += (_, target) =>
        {
            SetSearchTarget(target);
            _settings.LastSearchTarget = target == SearchTarget.DocumentContent ? "DocumentContent" : "FileName";
            _settings.Save();
        };
        // 토글 내부에서 파일명/문서내용 영역별 자체 툴팁 처리
        searchTargetPanel.Controls.Add(_searchTargetToggle, 2, 0);
        searchTargetPanel.Controls.Add(_advancedButton, 3, 0);

        root.SetColumnSpan(searchTargetPanel, 2);
        root.Controls.Add(searchTargetPanel, 1, 0);
        Program.Log("Detailed search area created");

        // row 1: SplitContainer
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AppBackgroundColor,
            Padding = new Padding(0, 4, 0, 0),
        };
        root.Controls.Add(_mainSplit, 0, 1);
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
        Program.Log("Detailed split created");

        var leftPanel = BuildPaneWithHeader("검색 결과", "", out var leftContent);
        _mainSplit.Panel1.Controls.Add(leftPanel);
        Program.Log("Detailed left pane created");

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
            ColumnHeadersVisible = true,
            RowTemplate = { Height = 30 },
            ColumnHeadersHeight = 34,
        };
        _resultsGrid.DefaultCellStyle.BackColor = SurfaceColor;
        _resultsGrid.DefaultCellStyle.ForeColor = TextColor;
        _resultsGrid.DefaultCellStyle.SelectionBackColor = MintSelectionColor;
        _resultsGrid.DefaultCellStyle.SelectionForeColor = TextColor;
        _resultsGrid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        _resultsGrid.ColumnHeadersDefaultCellStyle.BackColor = AccentColor;
        _resultsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _resultsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            DataPropertyName = nameof(SearchResult.FileName),
            HeaderText = "파일명",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 180,
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DirectoryPath",
            DataPropertyName = nameof(SearchResult.DirectoryPath),
            HeaderText = "경로",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 320,
            Visible = true,
        });
        _resultsGrid.SelectionChanged += (_, _) => ShowSelectedPreview();
        _resultsGrid.CellDoubleClick += (_, _) => OpenSelectedFile();
        AttachResultsContextMenu();
        _resultsGrid.CellPainting += ResultsGridOnCellPainting;
        _resultsGrid.DataError += (_, e) => e.ThrowException = false;
        leftContent.Controls.Add(_resultsGrid);
        AttachNoResultsLabel(leftContent);
        Program.Log("Detailed grid created");

        var rightPanel = BuildPaneWithHeader("미리보기", "", out var rightContent);
        _mainSplit.Panel2.Controls.Add(rightPanel);
        Program.Log("Detailed right pane created");

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
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
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
            Padding = new Padding(10, 8, 10, 8),
        };
        previewContentInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F)); // 파일명+경로 표시
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
            Font = new Font("Malgun Gothic", 10F, FontStyle.Bold, GraphicsUnit.Point),
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
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false,
            HideSelection = true,
            BackColor = Color.White,
            ForeColor = TextColor,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
        };
        previewBodyInnerPanel.Controls.Add(_previewBox);

        var previewNavGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(4, 3, 4, 3),
            Margin = new Padding(0),
        };
        previewCard.Controls.Add(previewNavGroup, 0, 1);

        _previewNextButton = new Button { Text = "다음 →", Width = 80, Height = 24, Margin = new Padding(0, 0, 0, 0) };
        StylePreviewNavButton(_previewNextButton);
        _previewNextButton.Click += (_, _) => { if (IsNavButtonActive(_previewNextButton)) MovePreviewMatch(1); };
        previewNavGroup.Controls.Add(_previewNextButton);

        _previewPreviousButton = new Button { Text = "← 이전", Width = 80, Height = 24, Margin = new Padding(0, 0, 4, 0) };
        StylePreviewNavButton(_previewPreviousButton);
        _previewPreviousButton.Click += (_, _) => { if (IsNavButtonActive(_previewPreviousButton)) MovePreviewMatch(-1); };
        previewNavGroup.Controls.Add(_previewPreviousButton);
        AttachNavTooltips(previewNavGroup);

        _previewMatchLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 4, 8, 0),
        };
        previewNavGroup.Controls.Add(_previewMatchLabel);

        _previewExpandButton = new Button { Text = "전체 보기", Width = 70, Height = 22, Margin = new Padding(0, 0, 6, 0), Visible = false };
        StylePreviewNavButton(_previewExpandButton);
        _previewExpandButton.Click += (_, _) =>
        {
            _previewShowFullDocument = true;
            RenderSelectedPreview();
        };
        previewNavGroup.Controls.Add(_previewExpandButton);

        // row 2: 상태바
        var statusBarPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackgroundColor,
            Margin = new Padding(0),
            Padding = new Padding(4, 0, 4, 0),
        };
        _statusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
        };
        var madeByLabel = new Label
        {
            Text = "Made by NX-JW",
            AutoSize = false,
            Width = 120,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Right,
            ForeColor = MutedTextColor,
            Font = new Font("Malgun Gothic", 7.5F, FontStyle.Regular, GraphicsUnit.Point),
        };
        statusBarPanel.Controls.Add(_statusLabel);
        statusBarPanel.Controls.Add(madeByLabel);
        root.Controls.Add(statusBarPanel, 0, 2);
        root.SetColumnSpan(statusBarPanel, 3);

        Program.Log("BuildDetailedLayout done");
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

    private void ApplyMainSplitRatio()
    {
        if (_mainSplit is null || !_mainSplit.IsHandleCreated) return;

        var available = Math.Max(_mainSplit.Width, _mainSplit.ClientSize.Width);
        var minTotal = _mainSplit.Panel1MinSize + _mainSplit.Panel2MinSize + _mainSplit.SplitterWidth;
        if (available <= minTotal)
        {
            // 원인6: 최솟값 미만이어도 Panel1MinSize로 고정해 잘못된 상태 방지
            try { _mainSplit.SplitterDistance = _mainSplit.Panel1MinSize; } catch { }
            return;
        }

        var target = (int)(available * (3d / 9d));
        var maxLeft = available - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth;
        _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize, Math.Min(target, maxLeft));
    }

    private bool IsSimpleMode => _settings.LayoutMode == "Simple";

    private int _titleBarHeight = 30;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // FormBorderStyle.None(심플 모드)에서도 리사이즈 가능하게
            // WS_THICKFRAME: OS가 창 끝 드래그로 리사이즈 처리를 시작함
            if (FormBorderStyle == FormBorderStyle.None)
                cp.Style |= 0x00040000;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // WM_WINDOWPOSCHANGING: 창 크기 변경 직전에 최솟값 강제
        if (m.Msg == 0x0046 && FormBorderStyle == FormBorderStyle.None)
        {
            int off = IntPtr.Size * 2; // HWND 2개 건너뜀
            int flags = System.Runtime.InteropServices.Marshal.ReadInt32(m.LParam, off + 16);
            if ((flags & 0x0001) == 0) // SWP_NOSIZE가 아닌 경우만
            {
                int cx = System.Runtime.InteropServices.Marshal.ReadInt32(m.LParam, off + 8);
                int cy = System.Runtime.InteropServices.Marshal.ReadInt32(m.LParam, off + 12);
                if (cx < 480)
                    System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, off + 8, 480);
                if (cy < 300)
                    System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, off + 12, 300);
            }
        }

        base.WndProc(ref m);

        // WM_SIZE 후 즉시 재그리기 (배경 노출 방지)
        if (m.Msg == 0x0005 && IsSimpleMode) // WM_SIZE
        {
            Invalidate(true);
            return;
        }

        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        const int HTCAPTION = 2;
        if (m.Msg != WM_NCHITTEST || m.Result != (IntPtr)HTCLIENT) return;

        var pos = PointToClient(Cursor.Position);

        // 심플 모드(Borderless): 가장자리에서 리사이즈 핸들 반환
        if (IsSimpleMode)
        {
            const int edge = 6;
            bool atL = pos.X < edge;
            bool atR = pos.X >= ClientSize.Width - edge;
            bool atT = pos.Y < edge;
            bool atB = pos.Y >= ClientSize.Height - edge;
            if (atT && atL) { m.Result = (IntPtr)13; return; } // HTTOPLEFT
            if (atT && atR) { m.Result = (IntPtr)14; return; } // HTTOPRIGHT
            if (atB && atL) { m.Result = (IntPtr)16; return; } // HTBOTTOMLEFT
            if (atB && atR) { m.Result = (IntPtr)17; return; } // HTBOTTOMRIGHT
            if (atL)        { m.Result = (IntPtr)10; return; } // HTLEFT
            if (atR)        { m.Result = (IntPtr)11; return; } // HTRIGHT
            if (atT)        { m.Result = (IntPtr)12; return; } // HTTOP
            if (atB)        { m.Result = (IntPtr)15; return; } // HTBOTTOM
        }

        // 타이틀바 드래그 (심플 모드 전용 커스텀 타이틀바)
        if (pos.Y < _titleBarHeight)
            m.Result = (IntPtr)HTCAPTION;
    }

    private void ApplyLayoutMode()
    {
        if (!IsSimpleMode) return;
        _root.SuspendLayout();
        // Simple 모드는 이미 BuildSimpleLayout으로 구성됐으므로
        // 창 크기만 설정
        MinimumSize = new Size(480, 300);
        var sw = _settings.SimpleWindowWidth;
        var sh = _settings.SimpleWindowHeight;
        if (sw > 0 && sh > 0)
            Size = new Size(sw, sh);
        else if (Size.Width > 700 || Size.Height > 500)
            Size = new Size(550, 400);
        _root.ResumeLayout(true);
    }

    private void BindInitialState()
    {
        _folderTextBox.Text = _settings.SearchFolders.Count > 0
            ? string.Join("; ", _settings.SearchFolders)
            : (_settings.LastFolder ?? "");
        _currentIndex = DocumentIndexStore.Load();
        _statusLabel.Text = "대기 중";
        var savedTarget = _settings.LastSearchTarget == "DocumentContent"
            ? SearchTarget.DocumentContent
            : SearchTarget.FileName;
        SetSearchTarget(savedTarget);
        ClearPreviewPanel();
        UpdateSearchButtonVisualState();
        ApplyLayoutMode();
    }

    private void KeywordTextBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (_searchButton.Tag is true)
                _ = RequestSearchAsync();
            else if (!_isSearchBusy)
                ShowSearchBlockedMessage();
            return;
        }

        if (e.Control && e.KeyCode == Keys.A)
        {
            SetSearchTarget(SearchTarget.FileName);
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.S)
        {
            SetSearchTarget(SearchTarget.DocumentContent);
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }
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

        if (keyData == Keys.Enter)
        {
            if (_searchButton.Tag is true)
            {
                _ = RequestSearchAsync();
                return true;
            }

            if (!_isSearchBusy)
            {
                ShowSearchBlockedMessage();
                return true;
            }
        }

        if (keyData == (Keys.Control | Keys.A))
        {
            SetSearchTarget(SearchTarget.FileName);
            _keywordTextBox.Focus();
            return true;
        }

        if (keyData == (Keys.Control | Keys.S))
        {
            SetSearchTarget(SearchTarget.DocumentContent);
            _keywordTextBox.Focus();
            return true;
        }

        if (keyData is Keys.Z or Keys.X)
        {
            var focusedControl = ActiveControl;
            if (focusedControl is not TextBoxBase)
            {
                if (_searchTarget == SearchTarget.DocumentContent && _selectedPreviewResult is not null)
                {
                    MovePreviewMatch(keyData == Keys.Z ? -1 : 1);
                    return true;
                }
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

        var previousFolder = _folderTextBox.Text;
        _folderTextBox.Text = dialog.SelectedPath;
        UpdateSearchButtonVisualState();

        var indexed = await EnsureIndexReadyAsync(forceRebuild: false);
        if (indexed)
        {
            // 색인 성공 시에만 폴더 저장
            SaveLastFolder(dialog.SelectedPath);
            if (_currentIndex is not null)
                _statusLabel.Text = $"색인 완료: {_currentIndex.Entries.Count}개 파일";

            if (!string.IsNullOrWhiteSpace(_keywordTextBox.Text))
                await RequestSearchAsync();
        }
        else
        {
            // 색인 취소/실패 시 이전 폴더로 복원
            _folderTextBox.Text = previousFolder;
            UpdateSearchButtonVisualState();
        }
    }

    private string[] GetCurrentPatterns()
    {
        return _settings.SearchPatterns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private bool IsCurrentIndexValid(IReadOnlyList<string> folders, IReadOnlyList<string> patterns)
    {
        if (_currentIndex is null) return false;
        if (_currentIndex.FormatVersion != DocumentIndexData.CurrentFormatVersion) return false;

        var indexFolders = _currentIndex.RootFolders.Count > 0
            ? _currentIndex.RootFolders
            : (string.IsNullOrWhiteSpace(_currentIndex.RootFolder) ? [] : new List<string> { _currentIndex.RootFolder });

        if (indexFolders.Count != folders.Count) return false;
        for (var i = 0; i < folders.Count; i++)
            if (!string.Equals(indexFolders[i], folders[i], StringComparison.OrdinalIgnoreCase))
                return false;

        if (_currentIndex.Patterns.Count != patterns.Count) return false;
        for (var i = 0; i < patterns.Count; i++)
            if (!string.Equals(_currentIndex.Patterns[i], patterns[i], StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    private async Task<bool> EnsureIndexReadyAsync(bool forceRebuild, bool refreshChangedFiles = false)
    {
        var folders = _settings.GetEffectiveFolders();
        var patterns = GetCurrentPatterns();

        if (folders.Count == 0 || patterns.Length == 0)
        {
            return false;
        }

        if (!forceRebuild && !refreshChangedFiles && IsCurrentIndexValid(folders, patterns))
        {
            return true;
        }

        SetBusyState(true);
        Enabled = false;

        using var popup = new IndexingProgressForm();
        using var indexingCts = new CancellationTokenSource();
        var canceled = false;
        var popupClosed = false;
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
        _statusLabel.Text = "문서 색인 중...";

        try
        {
            var progress = new Progress<IndexingProgress>(item =>
            {
                if (popupClosed) return;
                try { popup.UpdateProgress(item); } catch (ObjectDisposedException) { return; }
                _statusLabel.Text = item.TotalFiles > 0 && item.CurrentFile > 0
                    ? $"{item.CurrentFile} / {item.TotalFiles} 색인 중 {Path.GetFileName(item.CurrentPath)}"
                    : "문서 색인 준비 중...";
            });

            var previousIndex = forceRebuild ? null : _currentIndex;
            var updatedIndex = await Task.Run(() => _searcher.BuildOrUpdateIndex(
                folders,
                patterns,
                previousIndex,
                progress,
                indexingCts.Token));
            _currentIndex = updatedIndex;

            // 바뀐 파일이 없으면(기존 항목을 그대로 재사용) 대용량 색인 파일 저장을 생략
            var unchanged = previousIndex is not null &&
                previousIndex.Entries.Count == updatedIndex.Entries.Count &&
                previousIndex.Entries.Zip(updatedIndex.Entries).All(static pair => ReferenceEquals(pair.First, pair.Second));
            if (!unchanged)
            {
                await Task.Run(() => DocumentIndexStore.Save(updatedIndex));
            }
            _statusLabel.Text = $"색인 완료: {_currentIndex.Entries.Count}개 파일";
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Program.Log($"Indexing error: {ex}");
            return false;
        }
        finally
        {
            popupClosed = true;
            Enabled = true;
            try { popup.Close(); } catch { /* 이미 닫힌 경우 무시 */ }
            SetBusyState(false);
            _statusLabel.Text = _currentIndex is not null
                ? $"색인 완료: {_currentIndex.Entries.Count}개 파일"
                : "";
        }
    }

    private async Task StartSearchCoreAsync()
    {
        var folders = _settings.GetEffectiveFolders();
        if (folders.Count == 0)
        {
            MessageBox.Show(this, "설정에서 검색 폴더를 추가해주세요.", "알림");
            return;
        }

        var patterns = GetCurrentPatterns();

        if (patterns.Length == 0)
        {
            MessageBox.Show(this, "검색 확장자가 하나 이상 있어야 해요.\n설정에서 확장자를 추가해주세요.", "알림");
            return;
        }

        // 검색할 때마다 새로 생기거나 수정·삭제된 파일을 색인에 반영 (바뀐 파일만 다시 읽음)
        var indexed = await EnsureIndexReadyAsync(forceRebuild: !IsCurrentIndexValid(folders, patterns), refreshChangedFiles: true);
        if (!indexed)
        {
            return;
        }

        if (_currentIndex is not null)
        {
            _statusLabel.Text = $"색인 완료: {_currentIndex.Entries.Count}개 파일";
        }
        _results.Clear();
        _noResultsLabel.Visible = false;
        ClearPreviewPanel();
        SetBusyState(true);
        _restartRequested = false;

        _searchCts?.Dispose();
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

            // 검색이 끝났는데 결과가 없을 때만 안내 문구 표시 (최초 실행·대기 상태에서는 숨김)
            _noResultsLabel.Visible = _results.Count == 0;

            if (_results.Count > 0)
            {
                _resultsGrid.ClearSelection();
                _resultsGrid.Rows[0].Selected = true;
                _resultsGrid.CurrentCell = _resultsGrid.Rows[0].Cells["FileName"];
            }
        }
    }

    private void AttachNoResultsLabel(Control host)
    {
        _noResultsLabel = new Label
        {
            Text = "검색 결과가 없습니다.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Malgun Gothic", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(156, 140, 146),
            BackColor = _resultsGrid.BackgroundColor,
            Visible = false,
        };
        host.Controls.Add(_noResultsLabel);
        _noResultsLabel.BringToFront();
    }

    private void SetBusyState(bool busy)
    {
        _isSearchBusy = busy;
        _folderTextBox.Enabled = !busy;
        _keywordTextBox.Enabled = !busy;
        _browseFolderButton.Enabled = !busy;
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
        _noResultsLabel.Visible = false;
        ClearPreviewPanel();
        _statusLabel.Text = "대기 중";
    }

    private void ShowSearchBlockedMessage()
    {
        if (_settings.SearchFolders.Count == 0)
        {
            MessageBox.Show(this, "설정에서 검색 폴더를 추가해주세요.", "알림");
        }
    }

    private void UpdateSearchButtonVisualState()
    {
        if (_searchButton is null)
        {
            return;
        }

        var hasKeyword = !string.IsNullOrWhiteSpace(_keywordTextBox.Text);
        var hasFolder = _settings.SearchFolders.Count > 0;
        var canSearch = !_isSearchBusy && hasKeyword && hasFolder;
        _searchButton.Tag = canSearch;
        _searchButton.Cursor = canSearch ? Cursors.Hand : Cursors.Default;

        if (!canSearch)
        {
            _searchButton.BackColor = NavInactiveBackColor;
            _searchButton.ForeColor = NavInactiveForeColor;
            return;
        }

        _searchButton.BackColor = AccentColor;
        _searchButton.ForeColor = Color.White;
    }

    private void RefreshResultColumnWidths()
    {
        if (_resultsGrid.Columns["FileName"] is not { } nameColumn ||
            _resultsGrid.Columns["DirectoryPath"] is not { } pathColumn)
        {
            return;
        }

        _resultsGrid.AutoResizeColumn(nameColumn.Index, DataGridViewAutoSizeColumnMode.AllCells);
        _resultsGrid.AutoResizeColumn(pathColumn.Index, DataGridViewAutoSizeColumnMode.AllCells);
        nameColumn.Width = Math.Max(220, nameColumn.Width);
        pathColumn.Width = Math.Max(420, pathColumn.Width);
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
        host.Layout += (_, _) => LayoutTextBox();
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

    private static void StyleAdvancedButton(Button button, bool active)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = active ? Color.FromArgb(196, 108, 109) : Color.FromArgb(210, 195, 190);
        button.BackColor = active ? Color.FromArgb(196, 108, 109) : Color.FromArgb(255, 247, 242);
        button.ForeColor = active ? Color.White : Color.FromArgb(160, 140, 140);
        button.Margin = new Padding(4, TopVerticalMargin, 0, TopVerticalMargin);
        button.Height = TopControlHeight;
        button.AutoSize = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleActionButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = NavBorderColor;
        button.BackColor = AccentColor;
        button.ForeColor = Color.White;
        button.Padding = new Padding(14, 0, 14, 0);
        button.Margin = new Padding(0, TopVerticalMargin, 8, TopVerticalMargin);
        button.Height = TopControlHeight;
        button.AutoSize = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private static readonly Color NavActiveBackColor = Color.FromArgb(196, 108, 109);
    private static readonly Color NavActiveForeColor = Color.White;
    private static readonly Color NavInactiveBackColor = Color.FromArgb(255, 252, 248);
    private static readonly Color NavInactiveForeColor = Color.FromArgb(210, 200, 205);
    private static readonly Color NavBorderColor = Color.FromArgb(210, 168, 170);

    private static void StylePreviewNavButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = NavBorderColor;
        button.BackColor = NavInactiveBackColor;
        button.ForeColor = NavInactiveForeColor;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
        button.Tag = false; // 논리적 활성 상태
    }

    private static void SetNavButtonActive(Button button, bool active)
    {
        button.Tag = active;
        button.Cursor = active ? Cursors.Hand : Cursors.Default;
        if (active)
        {
            button.BackColor = NavActiveBackColor;
            button.ForeColor = NavActiveForeColor;
        }
        else
        {
            button.BackColor = NavInactiveBackColor;
            button.ForeColor = NavInactiveForeColor;
        }
    }

    private static bool IsNavButtonActive(Button button) => button.Tag is true;

    /// <summary>
    /// 비활성화 버튼에도 툴팁이 뜨도록 부모 FlowLayoutPanel의 MouseMove에서 처리.
    /// </summary>
    private void AttachNavTooltips(FlowLayoutPanel navGroup)
    {
        navGroup.MouseMove += (_, e) =>
        {
            var pt = navGroup.PointToClient(Cursor.Position);
            string? tip = null;
            if (_previewPreviousButton.Bounds.Contains(pt))
                tip = "단축키 : Z";
            else if (_previewNextButton.Bounds.Contains(pt))
                tip = "단축키 : X";

            var current = _paneToolTip.GetToolTip(navGroup);
            if (current != tip)
                _paneToolTip.SetToolTip(navGroup, tip);
        };
        navGroup.MouseLeave += (_, _) => _paneToolTip.SetToolTip(navGroup, null);
        // 활성 상태일 때도 동일 툴팁
        _paneToolTip.SetToolTip(_previewPreviousButton, "단축키 : Z");
        _paneToolTip.SetToolTip(_previewNextButton, "단축키 : X");
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
        SetNavButtonActive(_previewPreviousButton, hasMultipleMatches && _selectedPreviewMatchIndex > 0);
        SetNavButtonActive(_previewNextButton, hasMultipleMatches && _selectedPreviewMatchIndex < preview.Matches.Count - 1);
        _previewMatchLabel.Text = preview.Matches.Count == 0
            ? "검색 위치 정보 없음"
            : $"검색 위치 {_selectedPreviewMatchIndex + 1} / {preview.Matches.Count} · 줄 {preview.Matches[_selectedPreviewMatchIndex].LineNumber}";
    }

    /// <summary>버튼 크기를 텍스트에 맞게 자동 계산 (DPI 대응)</summary>
    private static void AutoSizeButtonToText(Button button, int hPad = 10, int vPad = 4)
    {
        var textSize = TextRenderer.MeasureText(button.Text, button.Font);
        button.Width = textSize.Width + hPad;
        button.Height = textSize.Height + vPad;
    }

    private static readonly Dictionary<string, Font> _previewFontCache = new();

    private static Font GetPreviewFont(string extension)
    {
        var key = extension.ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" or ".csv" or ".tsv" => "tabular",
            ".json" or ".xml" or ".yaml" or ".yml" or ".css" or ".js" or ".ts" or ".py" or ".cs" or ".java" or ".sql" => "code",
            _ => "default",
        };

        if (_previewFontCache.TryGetValue(key, out var cached))
            return cached;

        var font = key switch
        {
            "tabular" => new Font("GulimChe", 10F, FontStyle.Regular, GraphicsUnit.Point),
            "code" => new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
            _ => new Font("Malgun Gothic", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
        };
        _previewFontCache[key] = font;
        return font;
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
        SetNavButtonActive(_previewPreviousButton, false);
        SetNavButtonActive(_previewNextButton, false);
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
            })?.Dispose();
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
            })?.Dispose();
        }
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

        // 모든 검색어의 위치를 모아 겹치는 구간은 병합
        var ranges = new List<(int Start, int End)>();
        foreach (var token in DocumentSearcher.SplitTokens(keyword))
        {
            var from = 0;
            while (from < fileName.Length)
            {
                var found = fileName.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                ranges.Add((found, found + token.Length));
                from = found + token.Length;
            }
        }

        if (ranges.Count == 0)
        {
            return;
        }

        ranges.Sort();
        var merged = new List<(int Start, int End)> { ranges[0] };
        foreach (var range in ranges.Skip(1))
        {
            var last = merged[^1];
            if (range.Start <= last.End)
            {
                merged[^1] = (last.Start, Math.Max(last.End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        e.PaintBackground(e.CellBounds, e.State.HasFlag(DataGridViewElementStates.Selected));
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);

        var font = e.CellStyle.Font ?? Font;
        var foreColor = e.State.HasFlag(DataGridViewElementStates.Selected) ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
        var textY = e.CellBounds.Top + ((e.CellBounds.Height - font.Height) / 2);
        var startX = e.CellBounds.Left + 6;
        var maxSize = new Size(int.MaxValue, int.MaxValue);

        int MeasureWidth(string text) => text.Length == 0
            ? 0
            : TextRenderer.MeasureText(e.Graphics, text, font, maxSize, TextFormatFlags.NoPadding).Width;

        using var highlightBrush = new SolidBrush(Color.Black);
        var cursor = 0;
        var shift = 0; // 하이라이트 박스 여백(2px)만큼 뒤 글자를 밀어줌
        foreach (var (start, end) in merged)
        {
            if (start > cursor)
            {
                var plain = fileName[cursor..start];
                TextRenderer.DrawText(e.Graphics, plain, font, new Point(startX + shift + MeasureWidth(fileName[..cursor]), textY), foreColor, TextFormatFlags.NoPadding);
            }

            var matched = fileName[start..end];
            var highlightRect = new Rectangle(startX + shift + MeasureWidth(fileName[..start]), textY - 1, MeasureWidth(matched) + 2, font.Height + 2);
            e.Graphics.FillRectangle(highlightBrush, highlightRect);
            TextRenderer.DrawText(e.Graphics, matched, font, new Point(highlightRect.Left + 1, textY), Color.White, TextFormatFlags.NoPadding);
            cursor = end;
            shift += 2;
        }

        if (cursor < fileName.Length)
        {
            TextRenderer.DrawText(e.Graphics, fileName[cursor..], font, new Point(startX + shift + MeasureWidth(fileName[..cursor]), textY), foreColor, TextFormatFlags.NoPadding);
        }

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

        var tokens = DocumentSearcher.SplitTokens(keyword);
        var firstMatchIndex = DocumentSearcher.FirstTokenIndex(_previewBox.Text, keyword);
        var focusedMatchIndex = selectedMatchIndexInPreview >= 0
            ? selectedMatchIndexInPreview
            : firstMatchIndex;

        _previewBox.SelectAll();
        _previewBox.SelectionBackColor = _previewBox.BackColor;
        _previewBox.SelectionColor = _previewBox.ForeColor;

        var source = _previewBox.Text;
        foreach (var token in tokens)
        {
            var index = 0;
            while (index < source.Length)
            {
                index = source.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                _previewBox.Select(index, token.Length);
                _previewBox.SelectionBackColor = Color.Black;
                _previewBox.SelectionColor = Color.White;
                index += token.Length;
            }
        }

        if (focusedMatchIndex >= 0)
        {
            var focusedLength = tokens
                .Where(t => string.Compare(source, focusedMatchIndex, t, 0, t.Length, StringComparison.OrdinalIgnoreCase) == 0)
                .Select(t => t.Length)
                .DefaultIfEmpty(keyword.Length)
                .Max();
            _previewBox.Select(focusedMatchIndex, focusedLength);
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
        })?.Dispose();
    }

    private void AttachResultsContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("파일 열기", null, (_, _) => OpenSelectedFile());
        menu.Items.Add("파일 경로의 폴더 열기", null, (_, _) => OpenSelectedFileFolder());

        _resultsGrid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            {
                return;
            }

            if (!_resultsGrid.Rows[e.RowIndex].Selected)
            {
                _resultsGrid.ClearSelection();
                _resultsGrid.Rows[e.RowIndex].Selected = true;
            }

            var visibleCell = _resultsGrid.Rows[e.RowIndex].Cells
                .Cast<DataGridViewCell>()
                .FirstOrDefault(c => c.Visible);
            if (visibleCell is not null)
            {
                _resultsGrid.CurrentCell = visibleCell;
            }

            var cursor = _resultsGrid.PointToClient(Cursor.Position);
            menu.Show(_resultsGrid, cursor);
        };
    }

    private void OpenSelectedFileFolder()
    {
        var result = GetSelectedResult();
        if (result is null)
        {
            MessageBox.Show(this, "먼저 결과 목록에서 파일을 선택해야 해.", "선택 필요");
            return;
        }

        if (File.Exists(result.Path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{result.Path}\"",
                UseShellExecute = true,
            })?.Dispose();
            return;
        }

        var folder = Path.GetDirectoryName(result.Path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            })?.Dispose();
            return;
        }

        MessageBox.Show(this, "파일 또는 폴더를 찾을 수 없어.", "열기 실패");
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
    public List<string> SearchFolders { get; set; } = new();
    public string LastSearchTarget { get; set; } = "FileName";
    public string LayoutMode { get; set; } = "Detailed";
    public string SearchPatterns { get; set; } = DocumentSearcher.DefaultPatterns;
    public int DetailedWindowWidth { get; set; } = 0;
    public int DetailedWindowHeight { get; set; } = 0;
    public int SimpleWindowWidth { get; set; } = 0;
    public int SimpleWindowHeight { get; set; } = 0;

    /// <summary>유효한 검색 폴더 목록 반환 (SearchFolders가 비어있으면 LastFolder로 폴백)</summary>
    public List<string> GetEffectiveFolders()
    {
        if (SearchFolders.Count > 0)
            return SearchFolders.Where(Directory.Exists).ToList();
        if (!string.IsNullOrWhiteSpace(LastFolder) && Directory.Exists(LastFolder))
            return new List<string> { LastFolder };
        return new List<string>();
    }

    public static AppSettings Load()
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            // 기존 LastFolder → SearchFolders 마이그레이션
            if (settings.SearchFolders.Count == 0 && !string.IsNullOrWhiteSpace(settings.LastFolder))
            {
                settings.SearchFolders.Add(settings.LastFolder);
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        // SearchFolders와 LastFolder 동기화
        LastFolder = SearchFolders.Count > 0 ? SearchFolders[0] : string.Empty;
        AppPaths.EnsureAppDataDirectory();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}


