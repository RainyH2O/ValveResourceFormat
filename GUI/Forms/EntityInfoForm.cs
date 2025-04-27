using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms
{
    partial class EntityInfoForm : Form
    {
        public event EventHandler<string>? OnOutputConnectionDoubleClicked;
        public event EventHandler<string>? OnInputConnectionDoubleClicked;

        public EntityInfoForm(AdvancedGuiFileLoader guiFileLoader)
        {
            InitializeComponent();

            Icon = Program.MainForm.Icon;

            Resource.AddDataGridExternalRefAction(guiFileLoader, dataGridProperties, ColumnValue.Name, (referenceFound) =>
            {
                if (referenceFound)
                {
                    Close();
                }
            });
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape && ModifierKeys == Keys.None)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
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
                    tabControl.TabPages.Add(tabPageInputs);
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


        private void DataGridOutputs_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var outputIndex = dataGridOutputs.Columns["Output"]!.Index;
            var targetEntityIndex = dataGridOutputs.Columns["TargetEntity"]!.Index;
            var targetInputIndex = dataGridOutputs.Columns["TargetInput"]!.Index;
            var parameterIndex = dataGridOutputs.Columns["Parameter"]!.Index;
            var delayIndex = dataGridOutputs.Columns["Delay"]!.Index;
            var timesToFireIndex = dataGridOutputs.Columns["timesToFire"]!.Index;
            var targetHammerUniqueIdIndex = dataGridOutputs.Columns["TargetHammerUniqueId"]!.Index;

            if (targetHammerUniqueIdIndex < 0)
            {
                return;
            }

            var row = dataGridOutputs.Rows[e.RowIndex];
            var output = row.Cells[outputIndex].Value?.ToString() ?? string.Empty;
            var targetEntity = row.Cells[targetEntityIndex].Value?.ToString() ?? string.Empty;
            var targetInput = row.Cells[targetInputIndex].Value?.ToString() ?? string.Empty;
            var parameter = row.Cells[parameterIndex].Value?.ToString() ?? string.Empty;
            var delay = row.Cells[delayIndex].Value?.ToString() ?? "0";
            var timesToFire = row.Cells[timesToFireIndex].Value?.ToString() ?? string.Empty;
            var targetHammerUniqueId = row.Cells[targetHammerUniqueIdIndex].Value?.ToString() ?? string.Empty;
            var targetName = GetPropertiesValueForName("targetname");
            var hammerUniqueId = GetPropertiesValueForName("hammeruniqueid");

            var args = $"{output}|{targetEntity}|{targetInput}|{parameter}|{delay}|{timesToFire}|{targetHammerUniqueId}|{targetName}|{hammerUniqueId}";

            OnOutputConnectionDoubleClicked?.Invoke(this, args);
        }

        private void DataGridInputs_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var sourceHammerUniqueIdIndex = dataGridInputs.Columns["SourceHammerUniqueId"]!.Index;
            var sourceNameIndex = dataGridInputs.Columns["SourceName"]!.Index;
            var outputNameIndex = dataGridInputs.Columns["OutputName"]!.Index;
            var inputNameIndex = dataGridInputs.Columns["InputName"]!.Index;
            var parameterIndex = dataGridInputs.Columns["Parameter"]!.Index;
            var delayIndex = dataGridInputs.Columns["Delay"]!.Index;
            var timesToFireIndex = dataGridInputs.Columns["timesToFire"]!.Index;

            if (sourceHammerUniqueIdIndex < 0)
            {
                return;
            }

            var row = dataGridInputs.Rows[e.RowIndex];
            var sourceHammerUniqueId = row.Cells[sourceHammerUniqueIdIndex].Value?.ToString() ?? string.Empty;
            var sourceName = row.Cells[sourceNameIndex].Value?.ToString() ?? string.Empty;
            var outputName = row.Cells[outputNameIndex].Value?.ToString() ?? string.Empty;
            var inputName = row.Cells[inputNameIndex].Value?.ToString() ?? string.Empty;
            var parameter = row.Cells[parameterIndex].Value?.ToString() ?? string.Empty;
            var delay = row.Cells[delayIndex].Value?.ToString() ?? "0";
            var timesToFire = row.Cells[timesToFireIndex].Value?.ToString() ?? string.Empty;
            var targetName = GetPropertiesValueForName("targetname");
            var hammerUniqueId = GetPropertiesValueForName("hammeruniqueid");

            var args = $"{sourceHammerUniqueId}|{sourceName}|{outputName}|{inputName}|{parameter}|{delay}|{timesToFire}|{targetName}|{hammerUniqueId}";

            OnInputConnectionDoubleClicked?.Invoke(this, args);
        }

        private string GetPropertiesValueForName(string name)
        {
            foreach (DataGridViewRow row in dataGridProperties.Rows)
            {
                if (row.Cells[0].Value != null && row.Cells[0].Value?.ToString() == name)
                {
                    return row.Cells[1].Value != null ? row.Cells[1].Value?.ToString()! : string.Empty;
                }
            }

            return string.Empty;
        }

        public void SelectTab(string tabName, string args)
        {
            switch (tabName.ToLowerInvariant())
            {
                case "output":
                    if (tabPageOutputs.Parent != null)
                    {
                        tabControl.SelectedTab = tabPageOutputs;
                        SelectTableRow(tabName, args);
                    }

                    break;
                case "input":
                    if (tabPageInputs.Parent != null)
                    {
                        tabControl.SelectedTab = tabPageInputs;
                        SelectTableRow(tabName, args);
                    }

                    break;
                case "properties":
                    tabControl.SelectedTab = tabPageProperties;
                    break;
                default:
                    throw new ArgumentException("Invalid tab name", nameof(tabName));
            }
        }

        private void SelectTableRow(string tabName, string args)
        {
            switch (tabName.ToLowerInvariant())
            {
                case "output":
                {
                    var parts = args.Split('|');
                    if (parts.Length != 9)
                    {
                        Log.Error(nameof(EntityInfoForm), "Invalid arguments received in OnOutputConnectionDoubleClicked");
                        return;
                    }

                    // var sourceHammerUniqueId = parts[0];
                    // var sourceName = parts[1];
                    var outputName = parts[2];
                    var inputName = parts[3];
                    var parameter = parts[4];
                    var delay = parts[5];
                    var timesToFire = parts[6];
                    var targetName = parts[7];
                    var hammerUniqueId = parts[8];
                    var rowIdentifier = $"{outputName}|{targetName}|{inputName}|{parameter}|{delay}|{timesToFire}|{hammerUniqueId}";

                    var columnIndices = new[] { 0, 1, 2, 3, 4, 5, 6 };
                    for (var i = 0; i < dataGridOutputs.Rows.Count; i++)
                    {
                        var row = dataGridOutputs.Rows[i];
                        var cellValues = columnIndices.Select(index => row.Cells[index].Value?.ToString() ?? string.Empty).ToArray();
                        var rowValue = string.Join("|", cellValues);
                        if (rowValue != rowIdentifier)
                        {
                            continue;
                        }

                        dataGridOutputs.ClearSelection();
                        dataGridOutputs.Rows[i].Selected = true;
                        dataGridOutputs.FirstDisplayedScrollingRowIndex = i;
                        break;
                    }

                    break;
                }
                case "input":
                {
                    var parts = args.Split('|');
                    if (parts.Length != 9)
                    {
                        Log.Error(nameof(EntityInfoForm), "Invalid arguments received in OnOutputConnectionDoubleClicked");
                        return;
                    }

                    var output = parts[0];
                    // var targetEntity = parts[1];
                    var targetInput = parts[2];
                    var parameter = parts[3];
                    var delay = parts[4];
                    var timesToFire = parts[5];
                    // var targetHammerUniqueId = parts[6];
                    var targetName = parts[7];
                    var hammerUniqueId = parts[8];
                    var rowIdentifier = $"{hammerUniqueId}|{targetName}|{output}|{targetInput}|{parameter}|{delay}|{timesToFire}";

                    var columnIndices = new[] { 0, 1, 2, 3, 4, 5, 6 };
                    for (var i = 0; i < dataGridInputs.Rows.Count; i++)
                    {
                        var row = dataGridInputs.Rows[i];
                        var cellValues = columnIndices.Select(index => row.Cells[index].Value?.ToString() ?? string.Empty).ToArray();
                        var rowValue = string.Join("|", cellValues);
                        if (rowValue != rowIdentifier)
                        {
                            continue;
                        }

                        dataGridInputs.ClearSelection();
                        dataGridInputs.Rows[i].Selected = true;
                        dataGridInputs.FirstDisplayedScrollingRowIndex = i;
                        break;
                    }

                    break;
                }
                default:
                    throw new ArgumentException("Invalid tab name", nameof(tabName));
            }
        }
    }
}
