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

        /// <summary>
        /// Raised when a property naming a file or an entity, or the entity on the other end of a
        /// connection, is double clicked.
        /// </summary>
        public event EventHandler<LinkedResource>? LinkedResourceActivated;

        /// <summary>
        /// Raised when the fire button of an output or input connection is clicked.
        /// </summary>
        public event EventHandler<Connection>? ConnectionFireRequested;
        public event DataGridViewCellEventHandler? PropertiesCellDoubleClick;
        public event DataGridViewCellEventHandler? OutputsCellDoubleClick;
        public event DataGridViewCellEventHandler? InputsCellDoubleClick;

        public EntityInfoControl()
        {
            InitializeComponent();
            InitializeColumnLayout();

            components ??= new System.ComponentModel.Container();
            components.Add(tabPageOutputs);
            components.Add(tabPageInputs);
            components.Add(tabPageReferences);

            dataGridProperties.CellDoubleClick += OnPropertiesCellDoubleClick;
            dataGridOutputs.CellDoubleClick += OnOutputsCellDoubleClick;
            dataGridInputs.CellDoubleClick += OnInputsCellDoubleClick;
            dataGridReferences.CellDoubleClick += OnReferencesCellDoubleClick;
            dataGridOutputs.CellContentClick += OnOutputsCellContentClick;
            dataGridInputs.CellContentClick += OnInputsCellContentClick;
        }

        /// <summary>
        /// Gets or sets whether connections show a button to fire them, for hosts with a live entity world.
        /// </summary>
        public bool CanFireConnections
        {
            get => OutputsFire.Visible;
            set
            {
                OutputsFire.Visible = value;
                InputsFire.Visible = value;
            }
        }

        private TabPage[] TabPageOrder => [tabPageProperties, tabPageOutputs, tabPageInputs, tabPageReferences];

        public void ShowPopulatedTabs()
        {
            SetTabVisible(tabPageOutputs, dataGridOutputs.RowCount > 0);
            SetTabVisible(tabPageInputs, dataGridInputs.RowCount > 0);
            SetTabVisible(tabPageReferences, dataGridReferences.RowCount > 0);
            UpdatePreferredColumnWidths();
        }

        private void SetTabVisible(TabPage page, bool shouldShow)
        {
            var isShown = tabControl.TabPages.Contains(page);

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
            var targetOrder = Array.IndexOf(TabPageOrder, page);
            var index = 0;

            for (var i = 0; i < targetOrder; i++)
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
            dataGridReferences.Rows.Clear();
            tabControl.SelectedTab = tabPageProperties;
        }

        /// <summary>
        /// Shows an entity's properties, connections and references. <paramref name="entities"/> is the
        /// world the entity is in, searched for the entities its properties name, the entities whose
        /// properties name it, the connections targeting it and the targets of its own.
        /// </summary>
        public void PopulateFromEntity(List<Entity> entities, Entity entity)
        {
            var targetResolver = new EntityIOTargetResolver(entities);

            foreach (var child in entity.Children)
            {
                var text = BareText(child.Value);
                LinkedResource? link = text switch
                {
                    null or "" => null,
                    _ when FindEntities(entities, text) is [_, ..] namedEntities => new LinkedEntities(namedEntities),
                    _ => new LinkedFile(text),
                };

                AddProperty(child.Key, text ?? StringifyValue(child.Value), link);
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

            AddReferences(entities, entity);
            SortConnections();
        }

        /// <summary>
        /// Lists the properties of other entities that name this one
        /// </summary>
        private void AddReferences(List<Entity> entities, Entity entity)
        {
            if (string.IsNullOrEmpty(entity.TargetName))
            {
                return;
            }

            foreach (var source in entities)
            {
                if (source == entity)
                {
                    continue;
                }

                foreach (var child in source.Children)
                {
                    if (child.Key == "targetname")
                    {
                        continue;
                    }

                    var text = BareText(child.Value);

                    if (string.IsNullOrEmpty(text) || !EntityNameMatches(text, entity.TargetName))
                    {
                        continue;
                    }

                    var rowIndex = dataGridReferences.Rows.Add([
                        source.TargetName ?? "",
                        source.GetStringProperty("classname", string.Empty),
                        child.Key,
                        text,
                    ]);

                    dataGridReferences.Rows[rowIndex].Tag = new LinkedEntities([source]);
                }
            }
        }

        private static List<Entity> FindEntities(List<Entity> entities, string targetPattern)
        {
            return entities.FindAll(candidate => candidate.TargetName is { } targetName && EntityNameMatches(targetPattern, targetName));
        }

        public void AddProperty(string name, string value, LinkedResource? link = null)
        {
            var rowIndex = dataGridProperties.Rows.Add([name, value]);
            dataGridProperties.Rows[rowIndex].Tag = link;
        }

        /// <summary>
        /// The bare text of a string property. The KV3 form a value serializes to carries its quotes
        /// and, for a resource, its type prefix (<c>resource_name:"particles/foo.vpcf"</c>), which is
        /// neither what the grid should show nor a path anything can be looked up by.
        /// </summary>
        internal static string? BareText(KVObject value)
            => value.ValueType == KVValueType.String ? (string)value : null;

        private void AddOutputConnection(Connection connectionData, EntityIOTargetResolver? targetResolver)
        {
            var targets = new List<Entity>();
            targetResolver?.Resolve(connectionData, targets);

            var targetHammerIds = string.Join(",", targets
                .Select(static target => target.TryGetValue("hammeruniqueid", out var hammerId) ? hammerId.ToString() : string.Empty)
                .Where(static hammerId => !string.IsNullOrEmpty(hammerId))
                .Distinct(StringComparer.Ordinal));
            var rowIndex = dataGridOutputs.Rows.Add([
                connectionData.OutputName,
                connectionData.TargetName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire),
                targetHammerIds
            ]);

            var row = dataGridOutputs.Rows[rowIndex];
            row.Tag = targets.Count > 0 ? new LinkedEntities(targets) : null;
            row.Cells[OutputsFire.Index].Tag = connectionData;
        }

        private void AddInputConnection(Connection connectionData)
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

            var row = dataGridInputs.Rows[rowIndex];
            row.Tag = new LinkedEntities([connectionData.SourceEntity]);
            row.Cells[InputsFire.Index].Tag = connectionData;
        }

        private void SortConnections()
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
                if (!ReferenceEquals(row.Tag, connection)
                    && !row.Cells.Cast<DataGridViewCell>().Any(cell => ReferenceEquals(cell.Tag, connection)))
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

        private void OnPropertiesCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColumnValue.Index)
            {
                return;
            }

            if (PropertiesCellDoubleClick != null)
            {
                PropertiesCellDoubleClick.Invoke(dataGridProperties, e);
                return;
            }

            if (dataGridProperties.Rows[e.RowIndex].Tag is LinkedResource link)
            {
                LinkedResourceActivated?.Invoke(this, link);
            }
        }

        private void OnOutputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (OutputsCellDoubleClick != null)
            {
                OutputsCellDoubleClick.Invoke(dataGridOutputs, e);
                return;
            }

            ShowLinkedResource(dataGridOutputs, OutputsTargetEntity, e);
        }

        private void OnInputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (InputsCellDoubleClick != null)
            {
                InputsCellDoubleClick.Invoke(dataGridInputs, e);
                return;
            }

            ShowLinkedResource(dataGridInputs, InputsSource, e);
        }

        private void OnReferencesCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            ShowLinkedResource(dataGridReferences, ReferencesSource, e);
        }

        private void OnOutputsCellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            FireConnection(dataGridOutputs, OutputsFire, e);
        }

        private void OnInputsCellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            FireConnection(dataGridInputs, InputsFire, e);
        }

        private void FireConnection(DataGridView grid, DataGridViewColumn fireColumn, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != fireColumn.Index || grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not Connection connection)
            {
                return;
            }

            ConnectionFireRequested?.Invoke(this, connection);
        }

        private void ShowLinkedResource(DataGridView grid, DataGridViewColumn linkColumn, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != linkColumn.Index || grid.Rows[e.RowIndex].Tag is not LinkedResource link)
            {
                return;
            }

            LinkedResourceActivated?.Invoke(this, link);
        }
    }
}
