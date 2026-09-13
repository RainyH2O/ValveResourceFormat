using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

public partial class EntityListForm
{
    private const string SortOrderColumn = "__SortOrder";
    private readonly Dictionary<string, int> ManualColumnWidths = [];
    private readonly Dictionary<string, int> PreferredColumnWidths = [];
    private static Size? LastClientSize;
    private bool AdjustingColumnWidths;
    private bool FilterAlignmentPending;
    private string? SortColumn;
    private bool SortDescending;

    protected override void OnShown(EventArgs e)
    {
        var area = Screen.FromControl(this).WorkingArea;
        var size = LastClientSize ?? LogicalToDeviceUnits(new Size(960, 640));
        ClientSize = new Size(Math.Min(size.Width, area.Width * 9 / 10), Math.Min(size.Height, area.Height * 9 / 10));
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        base.OnShown(e);
        AdjustColumnWidths();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            LastClientSize = ClientSize;
        }
        base.OnFormClosed(e);
    }

    private void OnGridScroll(object? sender, ScrollEventArgs e)
    {
        AlignFilterRow();
        QueueFilterAlignment();
    }

    private void OnGridSizeChanged(object? sender, EventArgs e)
    {
        AdjustColumnWidths();
        QueueFilterAlignment();
    }

    private void QueueFilterAlignment()
    {
        if (!IsHandleCreated || Disposing || IsDisposed || FilterAlignmentPending)
        {
            return;
        }

        // Scroll events precede the grid's final visible-column layout.
        FilterAlignmentPending = true;
        BeginInvoke(() =>
        {
            FilterAlignmentPending = false;
            if (!Disposing && !IsDisposed)
            {
                AlignFilterRow();
            }
        });
    }

    private void AlignFilterRow()
    {
        if (_filterRowPanel == null)
        {
            return;
        }

        foreach (DataGridViewColumn column in entityDataGridView.Columns)
        {
            if (_filterTextBoxes.TryGetValue(column.Name, out var textBox))
            {
                var bounds = entityDataGridView.GetColumnDisplayRectangle(column.Index, false);
                textBox.SetBounds(bounds.Left, 0, bounds.Width, textBox.PreferredHeight);
                textBox.Visible = bounds.Right > 0 && bounds.Left < entityDataGridView.ClientSize.Width;
                _filterRowPanel.Height = textBox.PreferredHeight;
            }
        }
    }

    private void AdjustColumnWidths()
    {
        if (AdjustingColumnWidths || entityDataGridView.Columns.Count == 0)
        {
            return;
        }

        AdjustingColumnWidths = true;
        try
        {
            var flexible = new List<DataGridViewColumn>();
            var total = 0;
            foreach (DataGridViewColumn column in entityDataGridView.Columns)
            {
                if (!ManualColumnWidths.TryGetValue(column.Name, out var width))
                {
                    if (!PreferredColumnWidths.TryGetValue(column.Name, out width))
                    {
                        var baseline = _columnsToDisplay.First(item => item.ColumnName == column.Name).ColumnWidth;
                        var maximum = column.Name is "spawnflags" or "hammeruniqueid" ? baseline : 320;
                        width = Math.Clamp(column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.DisplayedCells, true),
                            LogicalToDeviceUnits(baseline), LogicalToDeviceUnits(maximum));
                        PreferredColumnWidths[column.Name] = width;
                    }
                    if (column.Name is not ("spawnflags" or "hammeruniqueid" or "origin"))
                    {
                        flexible.Add(column);
                    }
                }
                column.Width = width;
                total += width;
            }

            var extra = entityDataGridView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2 - total;
            if (extra > 0 && flexible.Count > 0)
            {
                foreach (var column in flexible)
                {
                    column.Width += extra / flexible.Count;
                }
            }
        }
        finally
        {
            AdjustingColumnWidths = false;
        }
        AlignFilterRow();
    }

    private void OnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
        {
            return;
        }
        var column = entityDataGridView.Columns[e.ColumnIndex];
        SortDescending = SortColumn == column.Name && !SortDescending;
        SortColumn = column.Name;
        ApplyNaturalSort();
    }

    private void ApplyNaturalSort()
    {
        if (_dataTable == null || SortColumn == null)
        {
            return;
        }

        if (!entityDataGridView.Columns.Contains(SortColumn))
        {
            SortColumn = null;
            _dataTable.DefaultView.Sort = string.Empty;
            return;
        }

        var selected = entityDataGridView.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => GetEntityAtRow(row.Index)).OfType<EntityLump.Entity>().ToHashSet();
        var wasSynchronizing = synchronizingSelection;
        synchronizingSelection = true;
        try
        {
            // DataView cannot accept a string comparer; sort by a stable numeric rank instead.
            var rows = _dataTable.Rows.Cast<DataRow>()
                .OrderBy(row => row.Field<string>(SortColumn), SearchForm.NumericComparer).ToArray();
            _dataTable.BeginLoadData();
            try
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    rows[i][SortOrderColumn] = i;
                }
            }
            finally
            {
                _dataTable.EndLoadData();
            }
            _dataTable.DefaultView.Sort = $"{SortOrderColumn} {(SortDescending ? "DESC" : "ASC")}";
            foreach (DataGridViewColumn column in entityDataGridView.Columns)
            {
                column.HeaderCell.SortGlyphDirection = column.Name == SortColumn
                    ? (SortDescending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            }
            entityDataGridView.ClearSelection();
            foreach (DataGridViewRow row in entityDataGridView.Rows)
            {
                row.Selected = GetEntityAtRow(row.Index) is { } entity && selected.Contains(entity);
            }
        }
        finally
        {
            synchronizingSelection = wasSynchronizing;
        }
    }
}
