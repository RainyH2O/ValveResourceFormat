using System.Drawing;
using System.Windows.Forms;
using GUI.Controls;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

internal sealed class EntitySelectionForm : ThemedForm
{
    private readonly DataGridView EntityGrid;

    public SceneNode? SelectedNode => EntityGrid.CurrentRow?.Tag as SceneNode;

#pragma warning disable CA2000 // Controls are owned and disposed by the form.
    public EntitySelectionForm(IReadOnlyList<SceneNode> matches, string searchTerm)
    {
        Text = $"Select Entity - {matches.Count} matches for \"{searchTerm}\"";
        ClientSize = new Size(640, 420);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        EntityGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        EntityGrid.Columns.Add("TargetName", "Entity Name");
        EntityGrid.Columns.Add("ClassName", "Class Name");
        EntityGrid.Columns.Add("HammerId", "Hammer ID");
        EntityGrid.Columns[0].FillWeight = 45;
        EntityGrid.Columns[1].FillWeight = 35;
        EntityGrid.Columns[2].FillWeight = 20;
        EntityGrid.SortCompare += (_, e) =>
        {
            e.SortResult = SearchForm.NumericComparer.Compare(e.CellValue1?.ToString() ?? string.Empty, e.CellValue2?.ToString() ?? string.Empty);
            e.Handled = true;
        };

        foreach (var node in matches)
        {
            var index = EntityGrid.Rows.Add(
                node.EntityData?.GetStringProperty("targetname", "Unknown") ?? "Unknown",
                node.EntityData?.GetStringProperty("classname", "Unknown") ?? "Unknown",
                node.EntityData?.GetStringProperty("hammeruniqueid", string.Empty) ?? string.Empty);
            EntityGrid.Rows[index].Tag = node;
        }

        if (EntityGrid.Rows.Count > 0)
        {
            EntityGrid.CurrentCell = EntityGrid.Rows[0].Cells[0];
        }

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var cancelButton = new ThemedButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var okButton = new ThemedButton { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        buttonPanel.Controls.AddRange([cancelButton, okButton]);
        Controls.Add(EntityGrid);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        EntityGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && SelectedNode != null)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.OK;
            }
        };
        EntityGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                DialogResult = DialogResult.OK;
            }
        };
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EntityGrid.Dispose();
        }

        base.Dispose(disposing);
    }
}
