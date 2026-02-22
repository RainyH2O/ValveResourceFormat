using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;
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
            components.Add(tabPageInputs);
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

        private TabPage[] TabPageOrder => [tabPageProperties, tabPageOutputs, tabPageInputs];

        public void ShowPopulatedTabs()
        {
            SetTabVisible(tabPageOutputs, dataGridOutputs.RowCount > 0);
            SetTabVisible(tabPageInputs, dataGridInputs.RowCount > 0);
        }

        private void SetTabVisible(TabPage page, bool shouldShow)
        {
            bool isShown = tabControl.TabPages.Contains(page);

            if (shouldShow && !isShown)
            {
                tabControl.TabPages.Insert(GetInsertIndex(page), page);
            }
            else if (!shouldShow && isShown)
            {
                tabControl.TabPages.Remove(page);
            }
        }

        private int GetInsertIndex(TabPage page)
        {
            int targetOrder = Array.IndexOf(TabPageOrder, page);
            int index = 0;

            for (int i = 0; i < targetOrder; i++)
            {
                if (tabControl.TabPages.Contains(TabPageOrder[i]))
                {
                    index++;
                }
            }

            return index;
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
            dataGridInputs.Rows.Clear();
        }

        public void PopulateFromEntity(Entity entity)
        {
            foreach (var child in entity.Children)
            {
                var resourcePath = ResourcePath(child.Value);
                AddProperty(child.Key, resourcePath ?? StringifyValue(child.Value), resourcePath);
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddOutputConnection(connection);
                }
            }
        }
        public void PopulateFromEntity(List<Entity> entities, Entity entity)
        {
            var targetResolver = new EntityIOTargetResolver(entities);

            foreach (var child in entity.Children)
            {
                var resourcePath = ResourcePath(child.Value);
                AddProperty(child.Key, resourcePath ?? StringifyValue(child.Value), resourcePath);
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddOutputConnection(connection, targetResolver);
                }
            }

            foreach (var connection in targetResolver.GetInputConnections(entity))
            {
                AddInputConnection(connection);
            }
        }

        public void AddProperty(string name, string value, string? externalReference = null)
        {
            var rowIndex = dataGridProperties.Rows.Add([name, value]);

            if (externalReference != null)
            {
                dataGridProperties.Rows[rowIndex].Cells[ColumnValue.Name].Tag = externalReference;
            }
        }

        /// <summary>
        /// The bare text of a string property. The KV3 form a value serializes to carries its quotes
        /// and, for a resource, its type prefix (<c>resource_name:"particles/foo.vpcf"</c>), which is
        /// neither what the grid should show nor a path anything can be looked up by.
        /// </summary>
        private static string? ResourcePath(KVObject value)
            => value.ValueType == KVValueType.String ? (string)value : null;

        public void AddOutputConnection(Connection connectionData)
        {
            AddOutputConnection(connectionData, targetResolver: null);
        }

        private void AddOutputConnection(Connection connectionData, EntityIOTargetResolver? targetResolver)
        {
            var targetHammerIds = GetTargetHammerIds(connectionData, targetResolver);
            var rowIndex = dataGridOutputs.Rows.Add([
                connectionData.OutputName,
                connectionData.TargetName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire),
                targetHammerIds
            ]);
            dataGridOutputs.Rows[rowIndex].Tag = connectionData;
        }

        public void AddInputConnection(Connection connectionData)
        {
            var sourceName = connectionData.SourceEntity.TargetName ?? string.Empty;
            var sourceHammerId = connectionData.SourceEntity.GetStringProperty("hammeruniqueid");

            var rowIndex = dataGridInputs.Rows.Add([
                sourceHammerId,
                sourceName,
                connectionData.OutputName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire)
            ]);

            dataGridInputs.Rows[rowIndex].Tag = connectionData;
        }

        private static string GetTargetHammerIds(Connection connection, EntityIOTargetResolver? targetResolver)
        {
            if (targetResolver == null)
            {
                return string.Empty;
            }

            var targets = new List<Entity>();
            targetResolver.Resolve(connection, targets);

            return string.Join(",", targets
                .Select(static target => target.TryGetValue("hammeruniqueid", out var hammerId) ? hammerId.ToString() : string.Empty)
                .Where(static hammerId => !string.IsNullOrEmpty(hammerId))
                .Distinct(StringComparer.Ordinal));
        }

        public void SortConnections()
        {
            SortDataGridView(dataGridOutputs, [OutputsTargetHammerId.Name, OutputsDelay.Name]);
            SortDataGridView(dataGridInputs, [InputsSourceHammerId.Name, InputsDelay.Name]);
        }

        private static void SortDataGridView(DataGridView grid, string[] columnNames)
        {
            if (grid.RowCount <= 1)
            {
                return;
            }

            var comparer = new MultiColumnNumericStringComparer(ListSortDirection.Ascending, columnNames);
            var rows = grid.Rows.Cast<DataGridViewRow>().Where(static row => !row.IsNewRow).ToList();
            rows.Sort(comparer.Compare);

            grid.Rows.Clear();
            foreach (var row in rows)
            {
                grid.Rows.Add(row);
            }
        }

        public void SelectConnection(DataGridView targetGrid, Connection connection)
        {
            var targetTab = targetGrid == dataGridOutputs ? tabPageOutputs : tabPageInputs;
            if (targetTab.Parent != null)
            {
                tabControl.SelectedTab = targetTab;
            }

            for (var i = 0; i < targetGrid.RowCount; i++)
            {
                var row = targetGrid.Rows[i];
                if (!ReferenceEquals(row.Tag, connection))
                {
                    continue;
                }

                targetGrid.ClearSelection();
                row.Selected = true;
                targetGrid.CurrentCell = row.Cells[0];
                targetGrid.FirstDisplayedScrollingRowIndex = i;
                return;
            }
        }

        private static string GetStringTimesToFire(int timesToFire)
        {
            return timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };
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
                var cell = row.Cells[colName];
                var name = cell.Tag as string ?? (string)cell.Value!;

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
