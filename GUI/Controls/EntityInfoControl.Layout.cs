using System.Windows.Forms;

namespace GUI.Forms;

partial class EntityInfoControl
{
    private readonly Dictionary<DataGridViewColumn, int> PreferredColumnWidths = [];
    private readonly Dictionary<DataGridViewColumn, int> ManualColumnWidths = [];
    private bool AdjustingColumnWidths;

    private void InitializeColumnLayout()
    {
        OutputsTargetHammerId.HeaderText = "Target ID";
        OutputsTargetHammerId.ToolTipText = "Hammer IDs of the resolved target entities";
        InputsSourceHammerId.HeaderText = "Source ID";
        InputsSourceHammerId.ToolTipText = "Hammer ID of the source entity";

        foreach (var grid in new[] { dataGridProperties, dataGridOutputs, dataGridInputs, dataGridReferences })
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            if (grid == dataGridOutputs || grid == dataGridInputs)
            {
                grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            }
            grid.ColumnWidthChanged += OnColumnWidthChanged;
            grid.SizeChanged += OnGridSizeChanged;
        }
    }

    private void OnColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
    {
        if (!AdjustingColumnWidths && e.Column is { } column && column.Resizable != DataGridViewTriState.False)
        {
            ManualColumnWidths[column] = column.Width;
        }
    }

    private void OnGridSizeChanged(object? sender, EventArgs e)
    {
        if (sender is DataGridView grid)
        {
            AdjustColumnWidths(grid);
        }
    }

    private void UpdatePreferredColumnWidths()
    {
        foreach (var grid in new[] { dataGridProperties, dataGridOutputs, dataGridInputs, dataGridReferences })
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column is DataGridViewButtonColumn)
                {
                    continue;
                }

                var compact = column == OutputsDelay || column == InputsDelay;
                var minimum = LogicalToDeviceUnits(compact ? 64 : 100);
                var maximum = LogicalToDeviceUnits(compact ? 100 : 320);
                PreferredColumnWidths[column] = Math.Clamp(
                    column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true), minimum, maximum);
            }
            AdjustColumnWidths(grid);
        }
    }

    private void AdjustColumnWidths(DataGridView grid)
    {
        if (AdjustingColumnWidths)
        {
            return;
        }

        AdjustingColumnWidths = true;
        try
        {
            var flexible = new List<DataGridViewColumn>();
            var total = 0;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (!column.Visible)
                {
                    continue;
                }

                if (ManualColumnWidths.TryGetValue(column, out var width)
                    || PreferredColumnWidths.TryGetValue(column, out width))
                {
                    column.Width = width;
                }
                total += column.Width;

                if (!ManualColumnWidths.ContainsKey(column)
                    && (column == ColumnValue || column == OutputsTargetEntity || column == OutputsParameter
                        || column == InputsSource || column == InputsParameter || column == ReferencesValue))
                {
                    flexible.Add(column);
                }
            }

            var extra = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2 - total;
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
    }
}
