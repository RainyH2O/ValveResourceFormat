using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Forms;

public class JsonPreviewForm : ThemedForm
{
    private readonly string _json;
    private readonly int _entityCount;
    private readonly TextBox _editorPathTextBox;

#pragma warning disable CA2000 // Controls are transferred to their parent container's Controls collection and disposed with it
    public JsonPreviewForm(string json, int entityCount)
    {
        _json = json;
        _entityCount = entityCount;

        Text = "Export Preview";
        ClientSize = new Size(900, 640);
        MinimumSize = new Size(600, 400);
        StartPosition = FormStartPosition.CenterParent;

        SuspendLayout();

        // ── JSON text area ──────────────────────────────────────────────────
        var jsonTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9F),
            Text = json,
            WordWrap = false,
        };

        // ── Editor path row ─────────────────────────────────────────────────
        var editorRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 4),
        };
        editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var editorLabel = new Label
        {
            Text = "Editor:",
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 0),
        };

        _editorPathTextBox = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Leave empty to use system default for .json",
            Text = Settings.Config.EntityListEditorPath,
        };

        var browseButton = new ThemedButton { Text = "...", Width = 28, Height = 23, Margin = new Padding(4, 0, 0, 0) };
        browseButton.Click += OnBrowseEditorClick;

        editorRow.Controls.Add(editorLabel, 0, 0);
        editorRow.Controls.Add(_editorPathTextBox, 1, 0);
        editorRow.Controls.Add(browseButton, 2, 0);

        // ── Button row ──────────────────────────────────────────────────────
        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // entity count
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // spacer
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // open
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // save
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // close

        var entityCountLabel = new Label
        {
            Text = $"{entityCount} entities",
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var openButton = new ThemedButton { Text = "Open in Editor", Width = 110, Height = 26, Margin = new Padding(4, 0, 4, 0) };
        openButton.Click += OnOpenInEditorClick;

        var saveButton = new ThemedButton { Text = "Save to File", Width = 90, Height = 26, Margin = new Padding(0, 0, 4, 0) };
        saveButton.Click += OnSaveToFileClick;

        var closeButton = new ThemedButton { Text = "Close", Width = 80, Height = 26, DialogResult = DialogResult.Cancel };

        buttonRow.Controls.Add(entityCountLabel, 0, 0);
        buttonRow.Controls.Add(new Label(), 1, 0); // spacer
        buttonRow.Controls.Add(openButton, 2, 0);
        buttonRow.Controls.Add(saveButton, 3, 0);
        buttonRow.Controls.Add(closeButton, 4, 0);

        // ── Bottom panel ────────────────────────────────────────────────────
        var bottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        bottomPanel.Controls.Add(editorRow, 0, 0);
        bottomPanel.Controls.Add(buttonRow, 0, 1);

        Controls.Add(jsonTextBox);
        Controls.Add(bottomPanel);

        CancelButton = closeButton;

        ResumeLayout(true);
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editorPathTextBox.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnBrowseEditorClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Editor Executable",
            Filter = "Executables|*.exe|All files|*.*",
            CheckFileExists = true,
        };

        var current = _editorPathTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(current) && File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _editorPathTextBox.Text = dialog.FileName;
        }
    }

    private void PersistEditorPath()
    {
        var path = _editorPathTextBox.Text.Trim();
        if (Settings.Config.EntityListEditorPath != path)
        {
            Settings.Config.EntityListEditorPath = path;
            Settings.Save();
        }
    }

    private void OnSaveToFileClick(object? sender, EventArgs e)
    {
        PersistEditorPath();

        using var saveDialog = new SaveFileDialog
        {
            Title = "Choose save location",
            FileName = "entities.json",
            InitialDirectory = Settings.Config.SaveDirectory,
            DefaultExt = "json",
            Filter = "JSON files|*.json",
            AddToRecent = true,
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var directory = Path.GetDirectoryName(saveDialog.FileName);
        if (directory != null)
        {
            Settings.Config.SaveDirectory = directory;
        }

        try
        {
            File.WriteAllText(saveDialog.FileName, _json);
            MessageBox.Show(
                $"Successfully exported {_entityCount} entities to {Path.GetFileName(saveDialog.FileName)}",
                "Export Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnOpenInEditorClick(object? sender, EventArgs e)
    {
        PersistEditorPath();

        var tempFile = Path.Combine(Path.GetTempPath(), $"vrf_entities_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        try
        {
            File.WriteAllText(tempFile, _json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to write temporary file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var editorPath = _editorPathTextBox.Text.Trim();
            ProcessStartInfo psi = string.IsNullOrEmpty(editorPath)
                ? new ProcessStartInfo { FileName = tempFile, UseShellExecute = true }
                : new ProcessStartInfo { FileName = editorPath, Arguments = $"\"{tempFile}\"", UseShellExecute = false };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
