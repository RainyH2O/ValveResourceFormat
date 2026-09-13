using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;

namespace GUI.Forms;

internal sealed class EntityMapImportForm : ThemedForm
{
    private readonly ThemedTextBox JsonTextBox;
    private readonly ThemedTextBox ReportTextBox;
    private readonly Label StatusLabel;
    private readonly Font JsonFont = new("Consolas", 10F);
    private string DefaultFileName = "entities.vmap";

    private const string ExampleJson = """
        {
          "formatVersion": 1,
          "entities": [
            {
              "properties": {
                "classname": "trigger_once",
                "targetname": "entry_trigger",
                "origin": [0, 0, 64],
                "spawnflags": 1,
                "StartDisabled": 0
              },
              "box": { "size": [128, 128, 128] },
              "connections": [
                {
                  "output": "OnStartTouch",
                  "target": "door_relay",
                  "input": "Trigger",
                  "parameter": "",
                  "delay": 0,
                  "timesToFire": 1,
                  "targetType": 2
                }
              ]
            },
            {
              "properties": {
                "classname": "logic_relay",
                "targetname": "door_relay",
                "origin": [128, 0, 64],
                "StartDisabled": 0
              },
              "connections": [
                {
                  "output": "OnTrigger",
                  "target": "exit_door",
                  "input": "Open",
                  "parameter": "",
                  "delay": 0.5,
                  "timesToFire": -1,
                  "targetType": 2
                }
              ]
            },
            {
              "properties": {
                "classname": "func_door",
                "targetname": "exit_door",
                "origin": [256, 0, 64],
                "movedir": "0 90 0",
                "speed": 100,
                "wait": 4,
                "lip": 0,
                "spawnpos": 0
              },
              "box": { "size": [16, 128, 128] }
            }
          ]
        }
        """;

#pragma warning disable CA2000 // Controls are transferred to parent containers and disposed with the form.
    public EntityMapImportForm()
    {
        Text = "Create Entity Map from JSON";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(560, 440);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var instructions = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Create an editable Hammer map from entity JSON. Load or paste JSON, validate it, then save a .vmap file.",
            Margin = new Padding(0, 0, 0, 8),
        };

        var inputButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        var loadButton = new ThemedButton { Text = "Load JSON...", AutoSize = true };
        loadButton.Click += OnLoadJson;
        var pasteButton = new ThemedButton { Text = "Paste JSON", AutoSize = true };
        pasteButton.Click += OnPasteJson;
        var exampleButton = new ThemedButton { Text = "Use Example", AutoSize = true };
        exampleButton.Click += OnUseExample;
        inputButtons.Controls.AddRange([loadButton, pasteButton, exampleButton]);

        JsonTextBox = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            MaxLength = 0,
            Font = JsonFont,
            AccessibleName = "Entity JSON",
            MinimumSize = new Size(0, 120),
            PlaceholderText = "Load or paste JSON, or choose Use Example for a trigger, relay, and door.",
        };
        JsonTextBox.TextChanged += OnJsonChanged;

        StatusLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 4),
            Text = "Not validated",
        };
        ReportTextBox = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            AccessibleName = "Validation results and conversion notices",
            MinimumSize = new Size(0, 60),
            Text = "Validation checks the JSON structure and map conversion. Notices do not prevent saving.",
        };

        var actionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 8, 0, 0),
        };
        var closeButton = new ThemedButton { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveButton = new ThemedButton { Text = "Save VMAP...", AutoSize = true };
        saveButton.Click += OnSaveVmap;
        var validateButton = new ThemedButton { Text = "Validate", AutoSize = true };
        validateButton.Click += OnValidate;
        actionButtons.Controls.AddRange([closeButton, saveButton, validateButton]);
        CancelButton = closeButton;

        layout.Controls.Add(instructions, 0, 0);
        layout.Controls.Add(inputButtons, 0, 1);
        layout.Controls.Add(JsonTextBox, 0, 2);
        layout.Controls.Add(StatusLabel, 0, 3);
        layout.Controls.Add(ReportTextBox, 0, 4);
        layout.Controls.Add(actionButtons, 0, 5);
        Controls.Add(layout);
    }
#pragma warning restore CA2000

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        var area = Screen.FromControl(this).WorkingArea;
        var size = LogicalToDeviceUnits(new Size(920, 720));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        ClientSize = new Size(Math.Min(size.Width, area.Width * 9 / 10), Math.Min(size.Height, area.Height * 9 / 10));
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            JsonTextBox.Dispose();
            ReportTextBox.Dispose();
            StatusLabel.Dispose();
            JsonFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnJsonChanged(object? sender, EventArgs e)
    {
        StatusLabel.Text = "JSON changed - validate before saving";
        ReportTextBox.Clear();
    }

    private void OnLoadJson(object? sender, EventArgs e)
    {
        try
        {
            var path = AppFileDialogs.OpenFile("Load Entity JSON", "JSON files (*.json)|*.json|All files (*.*)|*.*");
            if (path == null)
            {
                return;
            }

            JsonTextBox.Text = File.ReadAllText(path).ReplaceLineEndings(Environment.NewLine);
            DefaultFileName = Path.ChangeExtension(Path.GetFileName(path), ".vmap");
        }
        catch (Exception exception)
        {
            ShowFailure("Unable to load JSON", exception);
        }
    }

    private void OnPasteJson(object? sender, EventArgs e)
    {
        try
        {
            var json = AppClipboard.GetText();
            if (string.IsNullOrWhiteSpace(json))
            {
                _ = AppMessageDialogs.ShowMessageAsync("The clipboard does not contain JSON text.", "Paste Entity JSON", MessageIcon.Warning);
                return;
            }

            JsonTextBox.Text = json.ReplaceLineEndings(Environment.NewLine);
            DefaultFileName = "entities.vmap";
        }
        catch (Exception exception)
        {
            ShowFailure("Unable to paste JSON", exception);
        }
    }

    private void OnUseExample(object? sender, EventArgs e)
    {
        JsonTextBox.Text = ExampleJson.ReplaceLineEndings(Environment.NewLine);
        DefaultFileName = "trigger_relay_door.vmap";
        ValidateJson();
    }

    private void OnValidate(object? sender, EventArgs e)
    {
        ValidateJson();
    }

    private EntityMapImportResult? ValidateJson()
    {
        try
        {
            var result = EntityMapImporter.Convert(JsonTextBox.Text);
            StatusLabel.Text = string.Create(CultureInfo.InvariantCulture,
                $"Valid: {result.EntityCount} entities; {result.Warnings.Count} conversion notices");
            ReportTextBox.Text = result.Warnings.Count == 0
                ? "No conversion notices. Open the saved .vmap in Hammer to check game-specific properties and assets."
                : string.Join(Environment.NewLine, result.Warnings);
            return result;
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "Validation failed - correct the JSON before saving";
            ReportTextBox.Text = exception.Message;
            return null;
        }
    }

    private void OnSaveVmap(object? sender, EventArgs e)
    {
        if (ValidateJson() is not { } result)
        {
            return;
        }

        try
        {
            var path = AppFileDialogs.SaveFile("Save Hammer Entity Map", DefaultFileName, "vmap", "Hammer maps (*.vmap)|*.vmap");
            if (path == null)
            {
                return;
            }

            if (!Path.GetExtension(path).Equals(".vmap", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Choose a file name ending in .vmap.");
            }

            var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Path.GetRandomFileName()}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, result.Data);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            DefaultFileName = Path.GetFileName(path);
            StatusLabel.Text = string.Create(CultureInfo.InvariantCulture,
                $"Saved {result.EntityCount} entities to {Path.GetFileName(path)}; {result.Warnings.Count} conversion notices");
        }
        catch (Exception exception)
        {
            ShowFailure("Unable to save VMAP", exception);
        }
    }

    private void ShowFailure(string title, Exception exception)
    {
        StatusLabel.Text = title;
        ReportTextBox.Text = exception.Message;
        _ = AppMessageDialogs.ShowMessageAsync(exception.Message, title, MessageIcon.Error);
    }
}
