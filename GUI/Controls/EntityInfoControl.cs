using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Forms
{
    partial class EntityInfoControl : UserControl
    {
        public DataGridView OutputsGrid => dataGridOutputs;
        public DataGridView InputsGrid => dataGridInputs;

        public EntityInfoControl()
        {
            InitializeComponent();

            components ??= new System.ComponentModel.Container();
            components.Add(tabPageOutputs);
        }

        public EntityInfoControl(VrfGuiContext vrfGuiContext) : this()
        {
            ResourceAddDataGridExternalRef(vrfGuiContext);
        }

        public void ResourceAddDataGridExternalRef(VrfGuiContext vrfGuiContext)
        {
            AddDataGridExternalRefAction(vrfGuiContext, dataGridProperties, ColumnValue.Name);
        }

        public void ShowPropertiesTab()
        {
            tabControl.SelectedIndex = 0;
        }

        public void ShowOutputsTabIfAnyData()
        {
            if (dataGridOutputs.RowCount > 0)
            {
                if (tabPageOutputs.Parent == null)
                {
                    tabControl.TabPages.Add(tabPageOutputs);
                }
            }
            else
            {
                if (tabPageOutputs.Parent != null)
                {
                    tabControl.TabPages.Remove(tabPageOutputs);
                }
            }
        }

        public void ShowInputsTabIfAnyData()
        {
            if (dataGridInputs.RowCount > 0)
            {
                if (tabPageInputs.Parent == null)
                {
                    // Insert after Outputs tab (index 2) if it exists, otherwise at end
                    var insertIndex = tabControl.TabPages.IndexOf(tabPageOutputs);
                    if (insertIndex >= 0)
                    {
                        tabControl.TabPages.Insert(insertIndex + 1, tabPageInputs);
                    }
                    else
                    {
                        tabControl.TabPages.Add(tabPageInputs);
                    }
                }
            }
            else
            {
                if (tabPageInputs.Parent != null)
                {
                    tabControl.TabPages.Remove(tabPageInputs);
                }
            }
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
            dataGridInputs.Rows.Clear();
        }

        public void PopulateFromEntity(Entity entity)
        {
            foreach (var (key, value) in entity.Properties)
            {
                AddProperty(key, StringifyValue(value));
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddConnection(connection);
                }
            }
        }

        public void AddProperty(string name, string value)
        {
            dataGridProperties.Rows.Add([name, value]);
        }

        public void AddConnection(KVObject connectionData)
        {
            var outputName = connectionData.GetStringProperty("m_outputName");
            var targetName = connectionData.GetStringProperty("m_targetName");
            var inputName = connectionData.GetStringProperty("m_inputName");
            var parameter = connectionData.GetStringProperty("m_overrideParam");
            var delay = connectionData.GetFloatProperty("m_flDelay");
            var timesToFire = connectionData.GetInt32Property("m_nTimesToFire");
            var targetHammerId = connectionData.GetProperty<string>("m_targetHammerUniqueId") ?? string.Empty;

            var stimesToFire = timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };

            dataGridOutputs.Rows.Add([
                outputName,
                targetName,
                inputName,
                parameter,
                delay,
                stimesToFire,
                targetHammerId
            ]);
        }

        public void AddInputConnection(KVObject connectionData)
        {
            var sourceHammerId = connectionData.GetProperty<string>("sourceHammerUniqueId") ?? string.Empty;
            var sourceName = connectionData.GetProperty<string>("sourceName") ?? string.Empty;
            var outputName = connectionData.GetStringProperty("m_outputName");
            var inputName = connectionData.GetStringProperty("m_inputName");
            var parameter = connectionData.GetStringProperty("m_overrideParam");
            var delay = connectionData.GetFloatProperty("m_flDelay");
            var timesToFire = connectionData.GetInt32Property("m_nTimesToFire");

            var stimesToFire = timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };

            dataGridInputs.Rows.Add([
                sourceHammerId,
                sourceName,
                outputName,
                inputName,
                parameter,
                delay,
                stimesToFire
            ]);
        }

        public void SortConnections()
        {
            SortDataGridView(dataGridOutputs, ["TargetHammerUniqueId", "Delay"]);
            SortDataGridView(dataGridInputs, ["SourceHammerUniqueId", "InputDelay"]);
        }

        private static void SortDataGridView(DataGridView grid, string[] columnNames)
        {
            if (grid.RowCount <= 1)
            {
                return;
            }

            var comparer = new MultiColumnNumericStringComparer(ListSortDirection.Ascending, columnNames);
            var rows = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
            rows.Sort((a, b) => comparer.Compare(a, b));

            grid.Rows.Clear();
            foreach (var row in rows)
            {
                grid.Rows.Add(row);
            }
        }

        public void SelectTableRow(DataGridView targetGrid, DataGridViewRow sourceRow)
        {
            if (targetGrid == dataGridOutputs)
            {
                if (tabPageOutputs.Parent != null)
                {
                    tabControl.SelectedTab = tabPageOutputs;
                }

                SelectOutputRowFromInput(sourceRow);
            }
            else if (targetGrid == dataGridInputs)
            {
                if (tabPageInputs.Parent != null)
                {
                    tabControl.SelectedTab = tabPageInputs;
                }

                SelectInputRowFromOutput(sourceRow);
            }
        }

        private void SelectOutputRowFromInput(DataGridViewRow inputRow)
        {
            var outputName = inputRow.Cells[2].Value?.ToString() ?? string.Empty;
            var inputName = inputRow.Cells[3].Value?.ToString() ?? string.Empty;
            var parameter = inputRow.Cells[4].Value?.ToString() ?? string.Empty;
            var delay = inputRow.Cells[5].Value?.ToString() ?? string.Empty;

            for (var i = 0; i < dataGridOutputs.Rows.Count; i++)
            {
                var row = dataGridOutputs.Rows[i];
                if (row.Cells[0].Value?.ToString() == outputName &&
                    row.Cells[2].Value?.ToString() == inputName &&
                    row.Cells[3].Value?.ToString() == parameter &&
                    row.Cells[4].Value?.ToString() == delay)
                {
                    dataGridOutputs.ClearSelection();
                    dataGridOutputs.Rows[i].Selected = true;
                    dataGridOutputs.FirstDisplayedScrollingRowIndex = i;
                    break;
                }
            }
        }

        private void SelectInputRowFromOutput(DataGridViewRow outputRow)
        {
            var output = outputRow.Cells[0].Value?.ToString() ?? string.Empty;
            var targetInput = outputRow.Cells[2].Value?.ToString() ?? string.Empty;
            var parameter = outputRow.Cells[3].Value?.ToString() ?? string.Empty;
            var delay = outputRow.Cells[4].Value?.ToString() ?? string.Empty;

            for (var i = 0; i < dataGridInputs.Rows.Count; i++)
            {
                var row = dataGridInputs.Rows[i];
                if (row.Cells[2].Value?.ToString() == output &&
                    row.Cells[3].Value?.ToString() == targetInput &&
                    row.Cells[4].Value?.ToString() == parameter &&
                    row.Cells[5].Value?.ToString() == delay)
                {
                    dataGridInputs.ClearSelection();
                    dataGridInputs.Rows[i].Selected = true;
                    dataGridInputs.FirstDisplayedScrollingRowIndex = i;
                    break;
                }
            }
        }

        private void AddDataGridExternalRefAction(VrfGuiContext vrfGuiContext, DataGridView dataGrid, string columnName)
        {
            void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || sender is not DataGridView grid)
                {
                    return;
                }

                var row = grid.Rows[e.RowIndex];
                var colName = columnName;
                var name = (string)row.Cells[colName].Value!;

                var found = Types.Viewers.Resource.OpenExternalReference(vrfGuiContext, name);

                if (found && Parent is Form form)
                {
                    form.Close();
                }
            }

            void OnDisposed(object? sender, EventArgs e)
            {
                dataGrid.CellDoubleClick -= OnCellDoubleClick;
                dataGrid.Disposed -= OnDisposed;
            }

            dataGrid.CellDoubleClick += OnCellDoubleClick;
            dataGrid.Disposed += OnDisposed;
        }
    }
}
