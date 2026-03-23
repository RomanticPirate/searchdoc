using System.Text.Json;

namespace DocumentExplorerApp;

public sealed class DocumentIndexData
{
    public string RootFolder { get; set; } = string.Empty;

    public List<string> Patterns { get; set; } = [];

    public DateTimeOffset IndexedAtUtc { get; set; }

    public List<DocumentIndexEntry> Entries { get; set; } = [];
}

public sealed class DocumentIndexEntry
{
    public string Path { get; set; } = string.Empty;

    public long LastWriteUtcTicks { get; set; }

    public long FileLength { get; set; }

    public string Status { get; set; } = "성공";

    public string Content { get; set; } = string.Empty;
}

public sealed record IndexingProgress(int CurrentFile, int TotalFiles, string CurrentPath, bool IsCompleted);

public static class DocumentIndexStore
{
    public static DocumentIndexData? Load()
    {
        if (!File.Exists(AppPaths.IndexPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.IndexPath);
            return JsonSerializer.Deserialize<DocumentIndexData>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(DocumentIndexData index)
    {
        AppPaths.EnsureAppDataDirectory();
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = false });
        File.WriteAllText(AppPaths.IndexPath, json);
    }
}

public sealed class IndexingProgressForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _descriptionLabel;
    private readonly Label _detailLabel;
    private readonly Label _countLabel;
    private readonly ProgressBar _progressBar;

    public IndexingProgressForm()
    {
        Text = "색인 작업 중";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        ControlBox = false;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ClientSize = new Size(560, 240);
        BackColor = Color.FromArgb(255, 252, 248);
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(18, 12, 18, 0),
            Text = "색인 작업 중",
            Font = new Font("Malgun Gothic", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
        };

        _descriptionLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 100,
            Padding = new Padding(18, 6, 18, 0),
            Text = "- 빠른 검색을 위해서 색인 작업이 진행됩니다.\r\n- 최초 검색 폴더를 지정할 때 '1회'만 진행됩니다.\r\n- 이후 변경되는 파일에 대해서만 추가 색인 작업이 진행됩니다.",
            ForeColor = Color.FromArgb(110, 96, 104),
        };

        _detailLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(18, 8, 18, 0),
            Text = string.Empty,
            ForeColor = Color.FromArgb(110, 96, 104),
            AutoEllipsis = true,
        };

        _countLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(18, 2, 18, 0),
            Text = "0 / 0",
            Font = new Font("Malgun Gothic", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
        };

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Continuous,
        };

        var progressHost = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(18, 0, 18, 18),
        };
        progressHost.Controls.Add(_progressBar);

        Controls.Add(progressHost);
        Controls.Add(_countLabel);
        Controls.Add(_detailLabel);
        Controls.Add(_descriptionLabel);
        Controls.Add(_titleLabel);
    }

    public void ShowCenteredOver(Form owner)
    {
        var x = owner.Left + ((owner.Width - Width) / 2);
        var y = owner.Top + ((owner.Height - Height) / 2);
        Location = new Point(Math.Max(owner.Left + 20, x), Math.Max(owner.Top + 20, y));
        Show(owner);
    }

    public void UpdateProgress(IndexingProgress progress)
    {
        var total = Math.Max(1, progress.TotalFiles);
        var current = Math.Min(progress.CurrentFile, total);

        _detailLabel.Text = string.IsNullOrWhiteSpace(progress.CurrentPath)
            ? string.Empty
            : Path.GetFileName(progress.CurrentPath);
        _countLabel.Text = $"{current} / {total}";

        _progressBar.Maximum = total;
        _progressBar.Value = Math.Max(0, current);
    }
}
