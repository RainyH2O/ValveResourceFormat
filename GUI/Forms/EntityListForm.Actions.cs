using System.Drawing;
using System.Windows.Forms;

namespace GUI.Forms;

public partial class EntityListForm
{
    private readonly ToolTip ActionToolTip = new();
    private Label? PinnedCountLabel;

    internal void SetPinnedHighlightCount(int count)
    {
        if (PinnedCountLabel != null)
        {
            PinnedCountLabel.Text = $"Pinned: {count}";
        }
    }

#pragma warning disable CA2000 // Controls are owned and disposed by their parent containers.
    private TableLayoutPanel CreateActionBar(Button add, Button remove, Button clear, Button filters, Button widths, Button export)
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var groups = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        var pinned = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 12, 0),
        };
        PinnedCountLabel = new Label
        {
            Text = "Pinned: 0",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        pinned.Controls.AddRange([PinnedCountLabel, add, remove, clear]);
        var list = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        list.Controls.AddRange([filters, widths, syncMapSelectionCheckBox]);
        groups.Controls.AddRange([pinned, list]);
        export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        export.Margin = new Padding(12, 3, 3, 3);
        bar.Controls.Add(groups, 0, 0);
        bar.Controls.Add(export, 1, 0);
        ActionToolTip.SetToolTip(syncMapSelectionCheckBox, "Follow map selection in visible rows without changing filters. List selection always selects entities in the map.");
        ActionToolTip.SetToolTip(add, "Keep selected entities highlighted, including in Walk mode.");
        ActionToolTip.SetToolTip(remove, "Remove selected entities from pinned highlights. Ordinary selection is kept.");
        ActionToolTip.SetToolTip(clear, "Clear all pinned highlights. Ordinary selection is kept.");
        return bar;
    }
#pragma warning restore CA2000
}
