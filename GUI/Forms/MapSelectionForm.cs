using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using ValvePak;

namespace GUI.Forms;

internal sealed class MapSelectionForm : ThemedForm
{
    private readonly ListView Maps;
    private bool AdjustingColumnWidths;

    public PackageEntry? SelectedEntry => Maps.SelectedItems.Count == 1
        ? (Maps.SelectedItems[0].Tag as MainForm.MapCandidate)?.Entry
        : null;

#pragma warning disable CA2000 // Controls are owned and disposed by the form.
    public MapSelectionForm(List<MainForm.MapCandidate> maps)
    {
        Text = "Select map";
        ClientSize = new Size(640, 420);
        MinimumSize = new Size(400, 260);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        Maps = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false,
            ShowItemToolTips = true,
            View = View.Details,
        };
        Maps.Columns.Add("Map");
        Maps.Columns.Add("Package");
        foreach (var map in maps)
        {
            var displayPath = map.DisplayPath;
            var mapPath = map.Entry.GetFullPath();
            var item = new ListViewItem(Path.GetFileName(mapPath))
            {
                Tag = map,
                ToolTipText = displayPath,
            };
            item.SubItems.Add(displayPath[..^(mapPath.Length + 1)]);
            Maps.Items.Add(item);
        }
        Maps.ClientSizeChanged += (_, _) => AdjustColumnWidths();
        Maps.HandleCreated += (_, _) => AdjustColumnWidths();
        Maps.FontChanged += (_, _) => AdjustColumnWidths();
        Maps.ItemActivate += (_, _) =>
        {
            if (SelectedEntry != null)
            {
                DialogResult = DialogResult.OK;
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var cancelButton = new ThemedButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var openButton = new ThemedButton { Text = "Open", AutoSize = true, DialogResult = DialogResult.OK };
        Maps.SelectedIndexChanged += (_, _) => openButton.Enabled = SelectedEntry != null;
        if (Maps.Items.Count > 0)
        {
            Maps.Items[0].Selected = true;
            Maps.Items[0].Focused = true;
        }
        buttons.Controls.AddRange([cancelButton, openButton]);
        Controls.Add(Maps);
        Controls.Add(buttons);
        AcceptButton = openButton;
        CancelButton = cancelButton;
    }
#pragma warning restore CA2000

    private void AdjustColumnWidths()
    {
        if (AdjustingColumnWidths || !Maps.IsHandleCreated || Maps.Columns.Count < 2)
        {
            return;
        }

        AdjustingColumnWidths = true;
        try
        {
            var mapWidth = TextRenderer.MeasureText("Map", Maps.Font).Width;
            foreach (ListViewItem item in Maps.Items)
            {
                mapWidth = Math.Max(mapWidth, TextRenderer.MeasureText(item.Text, Maps.Font).Width);
            }

            var availableWidth = Maps.ClientSize.Width;
            var maximumMapWidth = Math.Max(0, Math.Min(LogicalToDeviceUnits(420), availableWidth * 2 / 3));
            Maps.Columns[0].Width = Math.Min(maximumMapWidth, Math.Max(LogicalToDeviceUnits(140), mapWidth + LogicalToDeviceUnits(28)));
            // Native last-column autosizing fills the remaining client area, including scrollbar changes.
            Maps.Columns[1].Width = -2;
        }
        finally
        {
            AdjustingColumnWidths = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Maps.Dispose();
        }

        base.Dispose(disposing);
    }
}
