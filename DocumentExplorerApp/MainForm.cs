using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace DocumentExplorerApp;

public sealed class MainForm : Form
{
    private static readonly Color AppBackgroundColor = Color.FromArgb(255, 247, 242);
    private static readonly Color SurfaceColor = Color.FromArgb(255, 252, 248);
    private static readonly Color SurfaceAccentColor = Color.FromArgb(255, 240, 231);
    private static readonly Color BorderColor = Color.FromArgb(234, 210, 198);
    private static readonly Color AccentColor = Color.FromArgb(181, 79, 80);
    private static readonly Color AccentSoftColor = Color.FromArgb(232, 225, 221);
    private static readonly Color MintSelectionColor = Color.FromArgb(191, 228, 216);
    private static readonly Color TextColor = Color.FromArgb(49, 39, 53);
    private static readonly Color MutedTextColor = Color.FromArgb(132, 118, 125);

    private const string FailureTooltip =
        "실패 뜻:\n" +
        "1. 해당 프로그램이 설치되지 않은 구형 문서일 수 있어.\n" +
        "2. 파일이 잠겨 있거나 손상됐을 수 있어.\n" +
        "3. 인코딩을 읽지 못한 텍스트 파일일 수 있어.\n" +
        "4. COM 자동화 중 예외가 발생했을 수 있어.";

    private readonly DocumentSearcher _searcher = new();
    private readonly BindingList<SearchResult> _results = [];
    private readonly AppSettings _settings;
    private readonly Bitmap _failureIcon = SystemIcons.Warning.ToBitmap();

    private TextBox _folderTextBox = null!;
    private TextBox _keywordTextBox = null!;
    private TextBox _patternTextBox = null!;
    private Button _fileNameModeButton = null!;
    private Button _contentModeButton = null!;
    private Button _resetPatternButton = null!;
    private Button _openFolderButton = null!;
    private Label _statusLabel = null!;
    private DataGridView _resultsGrid = null!;
    private RichTextBox _previewBox = null!;

    private CancellationTokenSource? _searchCts;
    private bool _restartRequested;
    private SearchTarget _searchTarget = SearchTarget.FileName;

    public MainForm()
    {
        Program.Log("MainForm ctor start");

        Text = "찾아줘문서";
        Size = new Size(1320, 860);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _settings = AppSettings.Load();
        Program.Log("Settings loaded");

        BuildLayout();
        Program.Log("Layout built");

        BindInitialState();
        Program.Log("Initial state bound");

        Shown += (_, _) => Program.Log("MainForm shown");
    }

    private void BuildLayout()
    {
        Program.Log("BuildLayout start");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(12),
            BackColor = AppBackgroundColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        Program.Log("Root created");

        root.Controls.Add(CreateFieldLabel("검색 폴더"), 0, 0);
        _folderTextBox = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = SurfaceColor, ForeColor = TextColor, Margin = new Padding(0, 7, 0, 7) };
        root.Controls.Add(_folderTextBox, 1, 0);
        var browseButton = new Button { Text = "폴더 선택", Dock = DockStyle.Fill };
        StyleActionButton(browseButton);
        browseButton.Click += (_, _) => PickFolder();
        root.Controls.Add(browseButton, 2, 0);
        Program.Log("Top area created");

        root.Controls.Add(CreateFieldLabel("검색어"), 0, 1);
        var searchTargetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchTargetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _keywordTextBox = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = SurfaceColor, ForeColor = TextColor, Margin = new Padding(0, 7, 0, 7) };
        _keywordTextBox.KeyDown += KeywordTextBoxOnKeyDown;
        searchTargetPanel.Controls.Add(_keywordTextBox, 0, 0);

        _fileNameModeButton = new Button { Text = "파일명", Width = 84, Margin = new Padding(8, 0, 0, 0) };
        StyleModeButton(_fileNameModeButton);
        _fileNameModeButton.Click += (_, _) => SetSearchTarget(SearchTarget.FileName);
        searchTargetPanel.Controls.Add(_fileNameModeButton, 1, 0);

        _contentModeButton = new Button { Text = "문서 내용", Width = 96, Margin = new Padding(6, 0, 0, 0) };
        StyleModeButton(_contentModeButton);
        _contentModeButton.Click += (_, _) => SetSearchTarget(SearchTarget.DocumentContent);
        searchTargetPanel.Controls.Add(_contentModeButton, 2, 0);

        root.SetColumnSpan(searchTargetPanel, 2);
        root.Controls.Add(searchTargetPanel, 1, 1);

        root.Controls.Add(CreateFieldLabel("확장자 패턴"), 0, 2);
        _patternTextBox = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = SurfaceColor, ForeColor = TextColor, Margin = new Padding(0, 7, 0, 7) };
        root.Controls.Add(_patternTextBox, 1, 2);
        _resetPatternButton = new Button { Text = "확장자 초기화", Dock = DockStyle.Fill };
        StyleActionButton(_resetPatternButton);
        _resetPatternButton.Click += (_, _) => _patternTextBox.Text = DocumentSearcher.DefaultPatterns;
        root.Controls.Add(_resetPatternButton, 2, 2);
        Program.Log("Pattern area created");

        var actionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AppBackgroundColor,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        _openFolderButton = new Button { Text = "선택한 파일의 폴더 열기", Width = 190 };
        StyleActionButton(_openFolderButton);
        _openFolderButton.Click += (_, _) => OpenSelectedFolder();
        actionPanel.Controls.Add(_openFolderButton);

        _statusLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
        };

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
            BackColor = AppBackgroundColor,
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusPanel.Controls.Add(actionPanel, 0, 0);
        statusPanel.Controls.Add(_statusLabel, 1, 0);

        root.Controls.Add(statusPanel, 0, 3);
        root.SetColumnSpan(statusPanel, 3);
        Program.Log("Action area created");

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AppBackgroundColor,
            Padding = new Padding(0, 4, 0, 0),
        };
        root.Controls.Add(split, 0, 4);
        root.SetColumnSpan(split, 3);
        split.HandleCreated += (_, _) =>
        {
            split.Panel1MinSize = 360;
            split.Panel2MinSize = 360;
            var available = Math.Max(split.Width, split.ClientSize.Width);
            if (available > split.Panel1MinSize + split.Panel2MinSize)
            {
                split.SplitterDistance = Math.Min(500, available - split.Panel2MinSize - 12);
            }
        };
        Program.Log("Split created");

        var leftPanel = BuildPane("검색 결과", out var leftContent);
        split.Panel1.Controls.Add(leftPanel);
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
            Width = 34,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            ValuesAreIcons = false,
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
            Width = 210,
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SearchResult.DirectoryPath),
            HeaderText = "경로",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _resultsGrid.SelectionChanged += (_, _) => ShowSelectedPreview();
        _resultsGrid.CellDoubleClick += (_, _) => OpenSelectedFile();
        _resultsGrid.CellFormatting += ResultsGridOnCellFormatting;
        _resultsGrid.CellToolTipTextNeeded += ResultsGridOnCellToolTipTextNeeded;
        _resultsGrid.CellPainting += ResultsGridOnCellPainting;
        _resultsGrid.DataError += (_, e) => e.ThrowException = false;
        leftContent.Controls.Add(_resultsGrid);
        Program.Log("Grid created");

        var rightPanel = BuildPane("미리보기", out var rightContent);
        split.Panel2.Controls.Add(rightPanel);
        Program.Log("Right pane created");

        _previewBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 10),
            DetectUrls = false,
            HideSelection = false,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
        };
        rightContent.Controls.Add(_previewBox);
        _previewBox.BringToFront();
        Program.Log("Preview box created");
    }

    private static Panel BuildPane(string title, out Panel contentPanel)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceAccentColor,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 10, 0),
        };

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            BackColor = SurfaceAccentColor,
            ForeColor = AccentColor,
            Font = new Font("Malgun Gothic", 10F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(12, 0, 0, 0),
        };

        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = SurfaceColor,
        };

        panel.Controls.Add(contentPanel);
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private void BindInitialState()
    {
        _folderTextBox.Text = string.IsNullOrWhiteSpace(_settings.LastFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : _settings.LastFolder;
        _patternTextBox.Text = DocumentSearcher.DefaultPatterns;
        _statusLabel.Text = "대기 중";
        SetSearchTarget(SearchTarget.FileName);
        SetPreview(string.Empty);
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

    private async Task RequestSearchAsync()
    {
        if (_searchCts is not null)
        {
            _restartRequested = true;
            _searchCts.Cancel();
            return;
        }

        await StartSearchCoreAsync();
    }

    private void PickFolder()
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
    }

    private async Task StartSearchCoreAsync()
    {
        var rootFolder = _folderTextBox.Text.Trim();
        if (!Directory.Exists(rootFolder))
        {
            MessageBox.Show(this, "유효한 폴더를 선택해야 해.", "폴더 오류");
            return;
        }

        var patterns = _patternTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        if (patterns.Length == 0)
        {
            MessageBox.Show(this, "적어도 하나의 확장자 패턴이 필요해.", "패턴 오류");
            return;
        }

        SaveLastFolder(rootFolder);
        _results.Clear();
        SetPreview(string.Empty);
        SetBusyState(true);
        _restartRequested = false;

        _searchCts = new CancellationTokenSource();
        var progress = new Progress<SearchProgress>(HandleProgress);

        try
        {
            await Task.Run(() => _searcher.Search(
                rootFolder,
                _keywordTextBox.Text,
                patterns,
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
        _folderTextBox.Enabled = !busy;
        _patternTextBox.Enabled = !busy;
        _resetPatternButton.Enabled = !busy;
        _fileNameModeButton.Enabled = !busy;
        _contentModeButton.Enabled = !busy;

        if (busy)
        {
            _statusLabel.Text = "검색 시작 중...";
        }
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

    private static void StyleActionButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentColor;
        button.ForeColor = Color.White;
        button.Padding = new Padding(14, 0, 14, 0);
        button.Margin = new Padding(0, 4, 8, 4);
        button.Height = 30;
        button.AutoSize = false;
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleModeButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = BorderColor;
        button.Padding = new Padding(12, 0, 12, 0);
        button.Margin = new Padding(8, 4, 0, 4);
        button.Height = 30;
        button.AutoSize = false;
        button.BackColor = SurfaceColor;
        button.ForeColor = MutedTextColor;
        button.UseVisualStyleBackColor = false;
    }

    private void SetSearchTarget(SearchTarget searchTarget)
    {
        _searchTarget = searchTarget;

        var activeBack = AccentColor;
        var activeFore = Color.White;
        var normalBack = AccentSoftColor;
        var normalFore = MutedTextColor;

        _fileNameModeButton.BackColor = searchTarget == SearchTarget.FileName ? activeBack : normalBack;
        _fileNameModeButton.ForeColor = searchTarget == SearchTarget.FileName ? activeFore : normalFore;
        _fileNameModeButton.FlatAppearance.BorderColor = searchTarget == SearchTarget.FileName ? AccentColor : BorderColor;
        _fileNameModeButton.Font = new Font(Font, searchTarget == SearchTarget.FileName ? FontStyle.Bold : FontStyle.Regular);

        _contentModeButton.BackColor = searchTarget == SearchTarget.DocumentContent ? activeBack : normalBack;
        _contentModeButton.ForeColor = searchTarget == SearchTarget.DocumentContent ? activeFore : normalFore;
        _contentModeButton.FlatAppearance.BorderColor = searchTarget == SearchTarget.DocumentContent ? AccentColor : BorderColor;
        _contentModeButton.Font = new Font(Font, searchTarget == SearchTarget.DocumentContent ? FontStyle.Bold : FontStyle.Regular);
    }

    private SearchResult? GetSelectedResult()
    {
        return _resultsGrid.CurrentRow?.DataBoundItem as SearchResult;
    }

    private void ShowSelectedPreview()
    {
        var result = GetSelectedResult();
        if (result is null)
        {
            return;
        }

        SetPreview(result.Snippet);
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

        e.Value = result.Status == "실패" ? _failureIcon : null;
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

    private void HighlightKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrEmpty(_previewBox.Text))
        {
            return;
        }

        var firstMatchIndex = _previewBox.Text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);

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

        if (firstMatchIndex >= 0)
        {
            var matchLine = _previewBox.GetLineFromCharIndex(firstMatchIndex);
            var targetLine = Math.Max(0, matchLine - 3);
            var targetIndex = _previewBox.GetFirstCharIndexFromLine(targetLine);
            if (targetIndex >= 0)
            {
                _previewBox.Select(targetIndex, 0);
                _previewBox.ScrollToCaret();
            }

            _previewBox.Select(firstMatchIndex, keyword.Length);
        }
        else
        {
            _previewBox.Select(0, 0);
        }
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

    private void OpenSelectedFolder()
    {
        var result = GetSelectedResult();
        if (result is null)
        {
            MessageBox.Show(this, "먼저 결과 목록에서 파일을 선택해야 해.", "선택 필요");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = result.DirectoryPath,
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
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "document_search_settings.json");

    public string LastFolder { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
