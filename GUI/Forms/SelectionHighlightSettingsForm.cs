using System.Drawing;
using System.Windows.Forms;
using GUI.Controls;
using ValveResourceFormat.Renderer;

namespace GUI.Forms;

internal sealed class SelectionHighlightSettingsForm : ThemedForm
{
    private readonly Action<SelectionHighlightSettings> settingsChanged;
    private readonly NumericSetting outlineWidth;
    private readonly NumericSetting outlineSoftness;
    private readonly NumericSetting outlineIntensity;
    private readonly NumericSetting fillAlpha;
    private readonly NumericSetting markerThreshold;
    private readonly NumericSetting markerSize;
    private readonly NumericSetting markerCornerLength;
    private readonly NumericSetting markerLineWidth;
#pragma warning disable CA2213 // Controls are owned and disposed by the form's Controls collection
    private readonly CheckBox showDimensions;
    private readonly CheckBox showDistantMarkers;
    private readonly ThemedButton colorButton;
    private readonly Label performanceWarning;
#pragma warning restore CA2213
    private readonly Control[] markerControls;
    private bool updatingControls;

    public SelectionHighlightSettings Settings { get; private set; }

#pragma warning disable CA2000 // Controls are transferred to their parent containers and disposed with the form
    public SelectionHighlightSettingsForm(
        SelectionHighlightSettings settings,
        Action<SelectionHighlightSettings> settingsChanged)
    {
        Settings = settings;
        this.settingsChanged = settingsChanged;

        Text = "Selection Display Settings";
        ClientSize = new Size(620, 620);
        MinimumSize = new Size(560, 520);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        outlineWidth = AddNumericSetting(content, "Outline width", 1m, 10m, 1, "px", value => UpdateSettings(s => s with { OutlineWidth = value }));
        outlineSoftness = AddNumericSetting(content, "Glow softness", 0m, 3m, 2, string.Empty, value => UpdateSettings(s => s with { OutlineSoftness = value }));
        outlineIntensity = AddNumericSetting(content, "Glow intensity", 0m, 12m, 2, string.Empty, value => UpdateSettings(s => s with { OutlineIntensity = value }));
        fillAlpha = AddNumericSetting(content, "Fill opacity", 0m, 75m, 0, "%", value => UpdateSettings(s => s with { FillAlpha = value / 100f }));

        performanceWarning = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Outline widths above 8 px may reduce rendering performance.",
            ForeColor = Color.DarkOrange,
            Padding = new Padding(4, 2, 4, 8),
        };
        content.Controls.Add(performanceWarning);

        var colorRow = CreateRow("Highlight color");
        colorButton = new ThemedButton
        {
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Style = false,
            MinimumSize = new Size(0, 26),
        };
        colorButton.Click += OnColorButtonClick;
        colorRow.Controls.Add(colorButton, 1, 0);
        colorRow.SetColumnSpan(colorButton, 2);
        content.Controls.Add(colorRow);

        showDimensions = new CheckBox
        {
            AutoSize = true,
            Text = "Show entity dimensions",
            Padding = new Padding(4, 5, 4, 5),
        };
        showDimensions.CheckedChanged += (_, _) => UpdateSettings(s => s with { ShowDimensions = showDimensions.Checked });
        content.Controls.Add(showDimensions);

        showDistantMarkers = new CheckBox
        {
            AutoSize = true,
            Text = "Show distant corner markers",
            Padding = new Padding(4, 8, 4, 5),
        };
        showDistantMarkers.CheckedChanged += (_, _) =>
        {
            SetMarkerControlsEnabled(showDistantMarkers.Checked);
            UpdateSettings(s => s with { ShowDistantMarkers = showDistantMarkers.Checked });
        };
        content.Controls.Add(showDistantMarkers);

        markerThreshold = AddNumericSetting(content, "Marker threshold", 4m, 256m, 0, "px", value => UpdateSettings(s => s with { MarkerThreshold = value }));
        markerSize = AddNumericSetting(content, "Marker size", 16m, 256m, 0, "px", value => UpdateSettings(s => s with { MarkerSize = value }));
        markerCornerLength = AddNumericSetting(content, "Corner length", 4m, 128m, 0, "px", value => UpdateSettings(s => s with { MarkerCornerLength = value }));
        markerLineWidth = AddNumericSetting(content, "Marker line width", 1m, 12m, 0, "px", value => UpdateSettings(s => s with { MarkerLineWidth = (int)value }));
        markerControls = [markerThreshold.Row, markerSize.Row, markerCornerLength.Row, markerLineWidth.Row];

        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        scrollPanel.Controls.Add(content);

        var resetButton = new ThemedButton
        {
            Text = "Restore Defaults",
            AutoSize = true,
        };
        resetButton.Click += (_, _) =>
        {
            Settings = SelectionHighlightSettings.Default;
            LoadSettings();
            settingsChanged(Settings);
        };

        var closeButton = new ThemedButton
        {
            Text = "Close",
            AutoSize = true,
        };
        closeButton.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(resetButton);

        Controls.Add(scrollPanel);
        Controls.Add(buttons);
        AcceptButton = closeButton;

        LoadSettings();
    }
#pragma warning restore CA2000

    private void OnColorButtonClick(object? sender, EventArgs e)
    {
        using var picker = new BetterColorPicker(colorButton.BackColor, color =>
        {
            colorButton.BackColor = color;
            UpdateSettings(s => s with { Color = new Color32(color.R, color.G, color.B) });
        });
        picker.ShowDialog(this);
    }

    private void UpdateSettings(Func<SelectionHighlightSettings, SelectionHighlightSettings> update)
    {
        if (updatingControls)
        {
            return;
        }

        Settings = update(Settings);
        performanceWarning.Visible = Settings.OutlineWidth >= 8f;
        settingsChanged(Settings);
    }

    private void LoadSettings()
    {
        updatingControls = true;
        outlineWidth.SetValue(Settings.OutlineWidth);
        outlineSoftness.SetValue(Settings.OutlineSoftness);
        outlineIntensity.SetValue(Settings.OutlineIntensity);
        fillAlpha.SetValue(Settings.FillAlpha * 100f);
        markerThreshold.SetValue(Settings.MarkerThreshold);
        markerSize.SetValue(Settings.MarkerSize);
        markerCornerLength.SetValue(Settings.MarkerCornerLength);
        markerLineWidth.SetValue(Settings.MarkerLineWidth);
        showDimensions.Checked = Settings.ShowDimensions;
        showDistantMarkers.Checked = Settings.ShowDistantMarkers;
        colorButton.BackColor = Color.FromArgb(Settings.Color.R, Settings.Color.G, Settings.Color.B);
        performanceWarning.Visible = Settings.OutlineWidth >= 8f;
        SetMarkerControlsEnabled(Settings.ShowDistantMarkers);
        updatingControls = false;
    }

    private void SetMarkerControlsEnabled(bool enabled)
    {
        foreach (var control in markerControls)
        {
            control.Enabled = enabled;
        }
    }

    private static NumericSetting AddNumericSetting(
        TableLayoutPanel content,
        string label,
        decimal minimum,
        decimal maximum,
        int decimalPlaces,
        string suffix,
        Action<float> changed)
    {
        var row = CreateRow(label);
        var scale = Pow10(decimalPlaces);
        var slider = new TrackBar
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 30,
            Minimum = decimal.ToInt32(minimum * scale),
            Maximum = decimal.ToInt32(maximum * scale),
            TickStyle = TickStyle.None,
        };
        var input = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            DecimalPlaces = decimalPlaces,
            Minimum = minimum,
            Maximum = maximum,
            Increment = 1m / scale,
        };
        var suffixLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = suffix,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var valuePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
        };
        valuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        valuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26f));
        valuePanel.Controls.Add(input, 0, 0);
        valuePanel.Controls.Add(suffixLabel, 1, 0);

        var syncing = false;
        slider.ValueChanged += (_, _) =>
        {
            if (syncing)
            {
                return;
            }

            syncing = true;
            input.Value = slider.Value / scale;
            syncing = false;
            changed((float)input.Value);
        };
        input.ValueChanged += (_, _) =>
        {
            if (syncing)
            {
                return;
            }

            syncing = true;
            slider.Value = decimal.ToInt32(input.Value * scale);
            syncing = false;
            changed((float)input.Value);
        };

        row.Controls.Add(slider, 1, 0);
        row.Controls.Add(valuePanel, 2, 0);
        content.Controls.Add(row);
        return new(row, slider, input, scale);
    }

    private static TableLayoutPanel CreateRow(string label)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 2, 0, 2),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105f));
        row.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        return row;
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }

    private sealed record NumericSetting(
        Control Row,
        TrackBar Slider,
        NumericUpDown Input,
        decimal Scale)
    {
        public void SetValue(float value)
        {
            var decimalValue = Math.Clamp((decimal)value, Input.Minimum, Input.Maximum);
            Input.Value = decimalValue;
            Slider.Value = decimal.ToInt32(decimalValue * Scale);
        }
    }
}
