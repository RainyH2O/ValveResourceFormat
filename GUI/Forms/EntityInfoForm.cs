using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms
{
    partial class EntityInfoForm : Form
    {
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
                stimesToFire
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
            dataGridOutputs.Sort(new MultiColumnNumericStringComparer(ListSortDirection.Ascending, [TargetEntity.Name, Delay.Name]));
            dataGridInputs.Sort(new MultiColumnNumericStringComparer(ListSortDirection.Ascending, [SourceHammerUniqueId.Name, Delay.Name]));
        }

        private class MultiColumnNumericStringComparer(ListSortDirection direction, string[] columnNames) : IComparer
        {
            public int Compare(object x, object y)
            {
                if (x is not DataGridViewRow row1 || y is not DataGridViewRow row2)
                {
                    return 0;
                }

                foreach (var columnName in columnNames)
                {
                    var xText = row1.Cells[columnName].Value as string;
                    var yText = row2.Cells[columnName].Value as string;

                    if (int.TryParse(xText, out var xNum) && int.TryParse(yText, out var yNum))
                    {
                        var result = xNum.CompareTo(yNum);
                        if (result != 0)
                        {
                            return direction == ListSortDirection.Ascending ? result : -result;
                        }
                    }
                    else
                    {
                        var result = string.Compare(xText, yText, StringComparison.Ordinal);
                        if (result != 0)
                        {
                            return direction == ListSortDirection.Ascending ? result : -result;
                        }
                    }
                }

                return 0;
            }
        }
    }
}
