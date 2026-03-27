using System.Drawing;
using System.Windows.Forms;

namespace DocumentExplorerApp;

internal sealed class InitialSetupForm : Form
{
    private static readonly Color BgColor = Color.FromArgb(255, 247, 242);
    private static readonly Color SurfaceColor = Color.FromArgb(255, 252, 248);
    private static readonly Color AccentColor = Color.FromArgb(181, 79, 80);
    private static readonly Color BorderColor = Color.FromArgb(234, 210, 198);
    private static readonly Color TextColor = Color.FromArgb(49, 39, 53);
    private static readonly Color MutedColor = Color.FromArgb(132, 118, 125);

    private readonly ListBox _folderListBox;
    private readonly Button _okButton;

    public List<string> SelectedFolders { get; } = new();

    public InitialSetupForm()
    {
        Text = "찾아줘문서 — 검색 폴더 설정";
        ClientSize = new Size(500, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgColor;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 16),
            BackColor = BgColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));  // 안내 문구
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));  // 라벨
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // 폴더 리스트
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));  // 하단 버튼
        Controls.Add(root);

        // 안내 문구
        var descLabel = new Label
        {
            Text = "검색할 폴더를 추가해주세요.\n추가한 폴더 안의 문서를 빠르게 검색할 수 있습니다.",
            Dock = DockStyle.Fill,
            ForeColor = MutedColor,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 4),
        };
        root.Controls.Add(descLabel, 0, 0);

        // 라벨
        var label = new Label
        {
            Text = "검색 폴더",
            Font = new Font("Malgun Gothic", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = TextColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0),
        };
        root.Controls.Add(label, 0, 1);

        // 폴더 리스트 + 버튼
        var listRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BgColor,
        };
        listRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        listRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68F));
        listRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _folderListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 8, 0),
            SelectionMode = SelectionMode.One,
        };

        var sideButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BgColor,
        };
        sideButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        sideButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        sideButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        sideButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var addButton = new Button
        {
            Text = "추가",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
        };
        addButton.FlatAppearance.BorderSize = 0;
        addButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                InitialDirectory = _folderListBox.Items.Count > 0 && Directory.Exists((string)_folderListBox.Items[^1])
                    ? (string)_folderListBox.Items[^1]
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ShowNewFolderButton = false,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (string item in _folderListBox.Items)
                if (string.Equals(item, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase))
                    return;
            _folderListBox.Items.Add(dialog.SelectedPath);
            UpdateOkButton();
        };

        var removeButton = new Button
        {
            Text = "삭제",
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            UseVisualStyleBackColor = false,
        };
        removeButton.FlatAppearance.BorderSize = 1;
        removeButton.FlatAppearance.BorderColor = BorderColor;
        removeButton.Click += (_, _) =>
        {
            if (_folderListBox.SelectedIndex >= 0)
            {
                _folderListBox.Items.RemoveAt(_folderListBox.SelectedIndex);
                UpdateOkButton();
            }
        };

        sideButtons.Controls.Add(addButton, 0, 0);
        sideButtons.Controls.Add(removeButton, 0, 1);
        listRow.Controls.Add(_folderListBox, 0, 0);
        listRow.Controls.Add(sideButtons, 1, 0);
        root.Controls.Add(listRow, 0, 2);

        // 하단 버튼
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BgColor,
            Margin = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _okButton = new Button
        {
            Text = "확인",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
            Enabled = false,
        };
        _okButton.FlatAppearance.BorderSize = 0;
        _okButton.Click += (_, _) =>
        {
            foreach (string item in _folderListBox.Items)
                SelectedFolders.Add(item);
            DialogResult = DialogResult.OK;
            Close();
        };

        var closeButton = new Button
        {
            Text = "닫기",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            UseVisualStyleBackColor = false,
        };
        closeButton.FlatAppearance.BorderSize = 1;
        closeButton.FlatAppearance.BorderColor = BorderColor;
        closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        footer.Controls.Add(new Panel { BackColor = BgColor }, 0, 0);
        footer.Controls.Add(_okButton, 1, 0);
        footer.Controls.Add(closeButton, 2, 0);
        root.Controls.Add(footer, 0, 3);
    }

    private void UpdateOkButton()
    {
        _okButton.Enabled = _folderListBox.Items.Count > 0;
    }

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
}
