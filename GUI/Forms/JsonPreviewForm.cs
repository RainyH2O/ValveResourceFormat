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
    private readonly string DefaultExportFileName;
    private readonly TextBox _editorPathTextBox;
    private readonly TextBox _exportPathTextBox;
    private bool _exportPathPrompted;

#pragma warning disable CA2000 // Controls are transferred to their parent container's Controls collection and disposed with it
    public string ExportPath => _exportPathTextBox.Text.Trim();

    public JsonPreviewForm(string json, int entityCount, string defaultExportFileName, string exportPath)
    {
        _json = json;
        _entityCount = entityCount;
        DefaultExportFileName = defaultExportFileName;

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

        var exportRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 4),
        };
        exportRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        exportRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        exportRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var exportLabel = new Label
        {
            Text = "Export path:",
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 0),
        };

        _exportPathTextBox = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Leave empty to use a temporary file",
            Text = exportPath,
        };

        var browseExportButton = new ThemedButton { Text = "...", Width = 28, Height = 23, Margin = new Padding(4, 0, 0, 0) };
        browseExportButton.Click += OnBrowseExportClick;

        exportRow.Controls.Add(exportLabel, 0, 0);
        exportRow.Controls.Add(_exportPathTextBox, 1, 0);
        exportRow.Controls.Add(browseExportButton, 2, 0);

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

        var openButton = new ThemedButton { Text = "Open in Editor", AutoSize = true, MinimumSize = new Size(100, 26), Height = 26, Margin = new Padding(4, 0, 4, 0) };
        openButton.Click += OnOpenInEditorClick;

        var saveButton = new ThemedButton { Text = "Save to File", AutoSize = true, MinimumSize = new Size(80, 26), Height = 26, Margin = new Padding(0, 0, 4, 0) };
        saveButton.Click += OnSaveToFileClick;

        var closeButton = new ThemedButton { Text = "Close", AutoSize = true, MinimumSize = new Size(70, 26), Height = 26, DialogResult = DialogResult.Cancel };

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
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        bottomPanel.Controls.Add(editorRow, 0, 0);
        bottomPanel.Controls.Add(exportRow, 0, 1);
        bottomPanel.Controls.Add(buttonRow, 0, 2);

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
            _exportPathTextBox.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        PersistEditorPath();
        base.OnFormClosed(e);
    }

    private void OnBrowseEditorClick(object? sender, EventArgs e)
    {
        var editorPath = AppFileDialogs.OpenFile("Select Editor Executable", "Executables|*.exe|All files|*.*");
        if (editorPath != null)
        {
            _editorPathTextBox.Text = editorPath;
        }
    }

    private void OnBrowseExportClick(object? sender, EventArgs e)
    {
        var exportPath = SelectExportPath();
        if (exportPath != null)
        {
            SetExportPath(exportPath);
        }
    }

    private void PersistEditorPath()
    {
        var editorPath = _editorPathTextBox.Text.Trim();
        if (Settings.Config.EntityListEditorPath != editorPath)
        {
            Settings.Config.EntityListEditorPath = editorPath;
            Settings.Save();
        }
    }

    private string? SelectExportPath()
    {
        var currentPath = _exportPathTextBox.Text.Trim();
        var defaultFileName = string.IsNullOrEmpty(currentPath) ? DefaultExportFileName : currentPath;
        return AppFileDialogs.SaveFile("Choose save location", defaultFileName, "json", "JSON files|*.json");
    }

    private void SetExportPath(string path)
    {
        _exportPathTextBox.Text = path;
    }

    private void OnSaveToFileClick(object? sender, EventArgs e)
    {
        var savePath = SelectExportPath();
        if (savePath == null)
        {
            return;
        }

        try
        {
            File.WriteAllText(savePath, _json);
            SetExportPath(savePath);
            _ = AppMessageDialogs.ShowMessageAsync(
                $"Successfully exported {_entityCount} entities to {Path.GetFileName(savePath)}",
                "Export Success");
        }
        catch (Exception ex)
        {
            _ = AppMessageDialogs.ShowMessageAsync($"Export failed: {ex.Message}", "Error", MessageIcon.Error);
        }
    }

    private void OnOpenInEditorClick(object? sender, EventArgs e)
    {
        string? exportPath = _exportPathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(exportPath) && !_exportPathPrompted)
        {
            _exportPathPrompted = true;
            exportPath = SelectExportPath();
            if (exportPath != null)
            {
                SetExportPath(exportPath);
            }
        }

        if (string.IsNullOrEmpty(exportPath))
        {
            var fileName = $"{Path.GetFileNameWithoutExtension(DefaultExportFileName)}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            exportPath = Path.Combine(Path.GetTempPath(), fileName);
        }

        try
        {
            File.WriteAllText(exportPath, _json);
        }
        catch (Exception ex)
        {
            _ = AppMessageDialogs.ShowMessageAsync($"Failed to write export file: {ex.Message}", "Error", MessageIcon.Error);
            return;
        }

        try
        {
            var editorPath = _editorPathTextBox.Text.Trim();
            ProcessStartInfo psi = string.IsNullOrEmpty(editorPath)
                ? new ProcessStartInfo { FileName = exportPath, UseShellExecute = true }
                : new ProcessStartInfo { FileName = editorPath, Arguments = $"\"{exportPath}\"", UseShellExecute = false };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _ = AppMessageDialogs.ShowMessageAsync($"Failed to open editor: {ex.Message}", "Error", MessageIcon.Error);
        }
    }
}
