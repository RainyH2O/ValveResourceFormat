using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;
using System.ComponentModel;
using System.Linq;

namespace GUI.Forms
{
    partial class EntityInfoControl : UserControl
    {
        public DataGridView OutputsGrid => dataGridOutputs;
        public DataGridView InputsGrid => dataGridInputs;


        public EntityInfoControl()
        {
            InitializeComponent();
        }

        public EntityInfoControl(AdvancedGuiFileLoader? guiFileLoader) : this()
        {
            ResourceAddDataGridExternalRef(guiFileLoader);
        }

        public void ResourceAddDataGridExternalRef(AdvancedGuiFileLoader? guiFileLoader)
        {
            Resource.AddDataGridExternalRefAction(guiFileLoader, dataGridProperties, ColumnValue.Name, (referenceFound) =>
            {
                if (referenceFound)
                {
                    if (Parent is Form form)
                    {
                        form.Close();
                    }
                }
            });
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
                    var insertIndex = 1;
                    if (insertIndex <= tabControl.TabPages.Count)
                    {
                        tabControl.TabPages.Insert(insertIndex, tabPageOutputs);
                    }
                    else
                    {
                        tabControl.TabPages.Add(tabPageOutputs);
                    }
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
                    var insertIndex = tabPageOutputs.Parent != null ? 2 : 1;
                    if (insertIndex <= tabControl.TabPages.Count)
                    {
                        tabControl.TabPages.Insert(insertIndex, tabPageInputs);
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
            var targetHammerUniqueId = connectionData.GetStringProperty("targetHammerUniqueId");

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
                delay.ToString(NumberFormatInfo.InvariantInfo),
                stimesToFire,
                targetHammerUniqueId
            ]);
        }

        public void AddInputConnection(KVObject connectionData)
        {
            var sourceHammerUniqueId = connectionData.GetStringProperty("sourceHammerUniqueId");
            var sourceName = connectionData.GetStringProperty("sourceName");
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
                sourceHammerUniqueId,
                sourceName,
                outputName,
                inputName,
                parameter,
                delay.ToString(NumberFormatInfo.InvariantInfo),
                stimesToFire
            ]);
        }

        public void SortConnections()
        {
            dataGridOutputs.Sort(new MultiColumnNumericStringComparer(ListSortDirection.Ascending, [TargetHammerUniqueId.Name, Delay.Name]));
            dataGridInputs.Sort(new MultiColumnNumericStringComparer(ListSortDirection.Ascending, [SourceHammerUniqueId.Name, Delay.Name]));
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
            var sourceName = inputRow.Cells[1].Value?.ToString() ?? string.Empty;
            var outputName = inputRow.Cells[2].Value?.ToString() ?? string.Empty;
            var parameter = inputRow.Cells[4].Value?.ToString() ?? string.Empty;
            var inputName = inputRow.Cells[3].Value?.ToString() ?? string.Empty;
            var delay = inputRow.Cells[5].Value?.ToString() ?? string.Empty;

            for (var i = 0; i < dataGridOutputs.Rows.Count; i++)
            {
                var row = dataGridOutputs.Rows[i];
                var rowOutput = row.Cells[0].Value?.ToString() ?? string.Empty;
                var rowTarget = row.Cells[1].Value?.ToString() ?? string.Empty;
                var rowInput = row.Cells[2].Value?.ToString() ?? string.Empty;
                var rowParameter = row.Cells[3].Value?.ToString() ?? string.Empty;
                var rowDelay = row.Cells[4].Value?.ToString() ?? string.Empty;

                if (rowOutput == outputName && rowInput == inputName &&
                    rowParameter == parameter && rowDelay == delay)
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
            var targetEntity = outputRow.Cells[1].Value?.ToString() ?? string.Empty;
            var targetInput = outputRow.Cells[2].Value?.ToString() ?? string.Empty;
            var parameter = outputRow.Cells[3].Value?.ToString() ?? string.Empty;
            var delay = outputRow.Cells[4].Value?.ToString() ?? string.Empty;

            for (var i = 0; i < dataGridInputs.Rows.Count; i++)
            {
                var row = dataGridInputs.Rows[i];
                var rowSourceName = row.Cells[1].Value?.ToString() ?? string.Empty;
                var rowOutputName = row.Cells[2].Value?.ToString() ?? string.Empty;
                var rowInputName = row.Cells[3].Value?.ToString() ?? string.Empty;
                var rowParameter = row.Cells[4].Value?.ToString() ?? string.Empty;
                var rowDelay = row.Cells[5].Value?.ToString() ?? string.Empty;

                if (rowOutputName == output && rowInputName == targetInput &&
                    rowParameter == parameter && rowDelay == delay)
                {
                    dataGridInputs.ClearSelection();
                    dataGridInputs.Rows[i].Selected = true;
                    dataGridInputs.FirstDisplayedScrollingRowIndex = i;
                    break;
                }
            }
        }
    }
}
