using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

public class PropertySelectionDialog : Form
{
    private readonly List<EntityLump.Entity> _allEntities;
    private readonly HashSet<string> _customExportProperties = new();
    private readonly List<EntityLump.Entity> _filteredEntities;
    private Button _addAllButton = null!;
    private Button _addButton = null!;
    private Button _addSmartButton = null!;
    private TreeView _availableTreeView = null!;
    private Button _cancelButton = null!;
    private Button _collapseAllButton = null!;
    private Button _expandAllButton = null!;
    private CheckBox _includeConnectionRelatedCheckBox = null!;
    private CheckBox _includePropertyRelatedCheckBox = null!;
    private Button _okButton = null!;
    private Button _removeButton = null!;
    private TextBox _searchTextBox = null!;
    private ListBox _selectedListBox = null!;
    private Container? components;

    public PropertySelectionDialog(List<EntityLump.Entity> filteredEntities,
        HashSet<string>? preSelectedProperties = null,
        List<EntityLump.Entity>? allEntities = null)
    {
        _filteredEntities = filteredEntities;
        _allEntities = allEntities ?? filteredEntities;
        _customExportProperties = preSelectedProperties ?? new HashSet<string>();

        InitializeComponent();
        InitializeDialog();
        SetupEventHandlers();
        PopulateControls();
    }

    public bool IncludeRelatedEntities { get; private set; }
    public bool IncludeConnectionRelatedEntities { get; private set; }
    public bool IncludePropertyRelatedEntities { get; private set; }
    public HashSet<string> SelectedProperties { get; } = new();
    public List<EntityLump.Entity> FinalEntityList { get; private set; } = new();
    public string ExportedJson { get; private set; } = string.Empty;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            DisposeComponent(_searchTextBox);
            DisposeComponent(_availableTreeView);
            DisposeComponent(_selectedListBox);
            DisposeComponent(_includeConnectionRelatedCheckBox);
            DisposeComponent(_includePropertyRelatedCheckBox);
            DisposeComponent(_addButton);
            DisposeComponent(_removeButton);
            DisposeComponent(_addAllButton);
            DisposeComponent(_addSmartButton);
            DisposeComponent(_expandAllButton);
            DisposeComponent(_collapseAllButton);
            DisposeComponent(_okButton);
            DisposeComponent(_cancelButton);

            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DisposeComponent(IComponent? component)
    {
        if (component != null)
        {
            components?.Remove(component);
            component.Dispose();
        }
    }

    private void InitializeComponent()
    {
        components = new Container();

        Text = "Export Configuration";
        Width = 920;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
    }

    private void InitializeDialog()
    {
        CreateControls();
        SetupLayout();
        SetupTooltips();
        ConfigureForm();
    }

    private void CreateControls()
    {
        _searchTextBox = CreateComponent(new TextBox { Left = 70, Top = 12, Width = 200 });

        _expandAllButton = CreateComponent(new Button
        {
            Text = "Expand All", Left = 280, Top = 12, Width = 85, Height = 25
        });

        _collapseAllButton = CreateComponent(new Button
        {
            Text = "Collapse All", Left = 280, Top = 42, Width = 85, Height = 25
        });

        _availableTreeView = CreateComponent(new TreeView
        {
            Left = 10, Top = 70, Width = 350, Height = 400,
            CheckBoxes = true, Font = new Font("Consolas", 9)
        });

        _selectedListBox = CreateComponent(new ListBox
        {
            Left = 500, Top = 70, Width = 400, Height = 400,
            Font = new Font("Consolas", 9), SelectionMode = SelectionMode.MultiExtended
        });

        _addButton = CreateComponent(new Button
        {
            Text = "→ Add", Left = 380, Top = 160, Width = 100, Height = 32
        });

        _removeButton = CreateComponent(new Button
        {
            Text = "← Remove", Left = 380, Top = 200, Width = 100, Height = 32
        });

        _addAllButton = CreateComponent(new Button
        {
            Text = "⇒ All", Left = 380, Top = 250, Width = 100, Height = 38,
            BackColor = Color.LightBlue, UseVisualStyleBackColor = false
        });

        _addSmartButton = CreateComponent(new Button
        {
            Text = "⇒ Smart", Left = 380, Top = 295, Width = 100, Height = 38,
            BackColor = Color.LightGreen, UseVisualStyleBackColor = false
        });

        _includePropertyRelatedCheckBox = CreateComponent(new CheckBox
        {
            Text = "Include related entities (properties)",
            Left = 10, Top = 480, Width = 400
        });

        _includeConnectionRelatedCheckBox = CreateComponent(new CheckBox
        {
            Text = "Include related entities (connections)",
            Left = 10, Top = 500, Width = 400
        });

        _okButton = CreateComponent(new Button
        {
            Text = "Export", Left = 740, Top = 525, Width = 75, Height = 30,
            DialogResult = DialogResult.OK
        });

        _cancelButton = CreateComponent(new Button
        {
            Text = "Cancel", Left = 820, Top = 525, Width = 75, Height = 30,
            DialogResult = DialogResult.Cancel
        });
    }

    private T CreateComponent<T>(T component) where T : IComponent
    {
        components?.Add(component);
        return component;
    }

    private void SetupLayout()
    {
        var searchLabel = new Label { Text = "Search:", Left = 10, Top = 15, AutoSize = true };
        var availableLabel = new Label
            { Text = "Available Properties (by Entity Type):", Left = 10, Top = 45, AutoSize = true };
        var selectedLabel = new Label
            { Text = "Selected Properties for Export:", Left = 500, Top = 45, AutoSize = true };

        Controls.AddRange(searchLabel, _searchTextBox, _expandAllButton, _collapseAllButton, availableLabel,
            _availableTreeView, selectedLabel, _selectedListBox, _addButton, _removeButton, _addAllButton,
            _addSmartButton, _includePropertyRelatedCheckBox, _includeConnectionRelatedCheckBox, _okButton,
            _cancelButton);
    }

    private void SetupTooltips()
    {
        using var toolTip = new ToolTip();
        toolTip.SetToolTip(_addAllButton, "Add all properties");
        toolTip.SetToolTip(_addSmartButton, "Add smart defaults");
    }

    private void ConfigureForm()
    {
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void SetupEventHandlers()
    {
        _searchTextBox.TextChanged += (s, e) => FilterPropertiesTree(_availableTreeView, _searchTextBox.Text);
        _expandAllButton.Click += (s, e) => _availableTreeView.ExpandAll();
        _collapseAllButton.Click += (s, e) => _availableTreeView.CollapseAll();
        _addButton.Click += (s, e) => AddSelectedProperties(_availableTreeView, _selectedListBox);
        _removeButton.Click += (s, e) => RemoveSelectedProperties(_selectedListBox);
        _addAllButton.Click += (s, e) => AddAllAvailableProperties(_filteredEntities, _selectedListBox);
        _addSmartButton.Click += (s, e) => AddSmartDefaultProperties(_filteredEntities, _selectedListBox);
        _availableTreeView.NodeMouseClick += OnTreeViewNodeMouseClick;

        _okButton.Click += OkButton_Click;
    }

    private void PopulateControls()
    {
        PopulateAvailablePropertiesTree(_availableTreeView, _filteredEntities);
        InitializeSelectedProperties(_selectedListBox, _filteredEntities);
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        if (_selectedListBox.Items.Count == 0)
        {
            MessageBox.Show("Please select at least one property for export.", "No Properties Selected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedProperties.Clear();
        foreach (string property in _selectedListBox.Items)
        {
            SelectedProperties.Add(property);
        }

        IncludePropertyRelatedEntities = _includePropertyRelatedCheckBox.Checked;
        IncludeConnectionRelatedEntities = _includeConnectionRelatedCheckBox.Checked;
        IncludeRelatedEntities = IncludePropertyRelatedEntities || IncludeConnectionRelatedEntities;

        FinalEntityList = IncludeRelatedEntities
            ? GetEntitiesWithRelated(_filteredEntities, _allEntities, IncludePropertyRelatedEntities,
                IncludeConnectionRelatedEntities)
            : new List<EntityLump.Entity>(_filteredEntities);

        var totalAvailableProperties = GetAllAvailableProperties(_filteredEntities).Count;
        var isFullExport = SelectedProperties.Count == totalAvailableProperties;

        ExportedJson = isFullExport
            ? SerializeEntitiesFull(FinalEntityList)
            : SerializeEntitiesWithSelectedProperties(FinalEntityList, SelectedProperties);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnTreeViewNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Node == null)
        {
            return;
        }

        var checkBoxWidth = 16;
        var nodeX = e.Node.Bounds.X - checkBoxWidth;
        if (e.X <= nodeX + checkBoxWidth)
        {
            return;
        }

        if (e.Node.Parent != null && e.Node.Tag is string)
        {
            e.Node.Checked = !e.Node.Checked;
        }
        else if (e.Node.Parent == null)
        {
            var newState = !e.Node.Checked;
            e.Node.Checked = newState;
            foreach (TreeNode childNode in e.Node.Nodes)
            {
                childNode.Checked = newState;
            }
        }
    }

    private static void PopulateAvailablePropertiesTree(TreeView treeView, List<EntityLump.Entity> entities)
    {
        treeView.Nodes.Clear();
        var propertiesByClassname = new Dictionary<string, HashSet<string>>();

        foreach (var entity in entities)
        {
            var classname = entity.GetProperty<string>("classname", "unknown");
            if (!propertiesByClassname.TryGetValue(classname, out var properties))
            {
                properties = new HashSet<string>();
                propertiesByClassname[classname] = properties;
            }

            foreach (var prop in entity.Properties.Properties)
            {
                properties.Add(prop.Key);
            }
        }

        foreach (var kvp in propertiesByClassname.OrderBy(x => x.Key))
        {
            var classnameNode = new TreeNode($"{kvp.Key} ({kvp.Value.Count} properties)")
            {
                Tag = kvp.Key
            };

            foreach (var property in kvp.Value.OrderBy(x => x))
            {
                var propertyNode = new TreeNode(property)
                {
                    Tag = property
                };
                classnameNode.Nodes.Add(propertyNode);
            }

            treeView.Nodes.Add(classnameNode);
        }

        treeView.ExpandAll();
    }

    private void InitializeSelectedProperties(ListBox listBox, List<EntityLump.Entity> entities)
    {
        listBox.Items.Clear();

        if (_customExportProperties.Count > 0)
        {
            foreach (var property in _customExportProperties.OrderBy(x => x))
            {
                listBox.Items.Add(property);
            }
        }
        else
        {
            var smartProperties = GetSmartPropertiesFromEntities(entities);
            foreach (var property in smartProperties.OrderBy(x => x))
            {
                listBox.Items.Add(property);
            }
        }
    }

    private static void FilterPropertiesTree(TreeView treeView, string searchText)
    {
        treeView.BeginUpdate();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            ResetTreeNodeColors(treeView.Nodes);
            treeView.EndUpdate();
            return;
        }

        FilterTreeNodes(treeView.Nodes, searchText);
        treeView.EndUpdate();
    }

    private static void ResetTreeNodeColors(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            node.BackColor = Color.White;
            node.ForeColor = Color.Black;

            if (node.Nodes.Count > 0)
            {
                ResetTreeNodeColors(node.Nodes);
            }
        }
    }

    private static bool FilterTreeNodes(TreeNodeCollection nodes, string searchText)
    {
        var hasMatchingChild = false;

        foreach (TreeNode node in nodes)
        {
            var nodeMatches = node.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
            var childrenMatch = false;

            if (node.Nodes.Count > 0)
            {
                childrenMatch = FilterTreeNodes(node.Nodes, searchText);
            }

            if (nodeMatches || childrenMatch)
            {
                node.BackColor = nodeMatches ? Color.LightYellow : Color.White;
                node.ForeColor = Color.Black;
                node.Expand();
                hasMatchingChild = true;
            }
            else
            {
                node.BackColor = Color.White;
                node.ForeColor = Color.Gray;
                if (node.Level < 2)
                {
                    node.Collapse();
                }
            }
        }

        return hasMatchingChild;
    }

    private static void UpdateListBoxProperties(ListBox listBox, IEnumerable<string> properties,
        bool clearExisting = false)
    {
        var propertiesList = properties.ToList();
        var existingItems = clearExisting ? new HashSet<string>() : listBox.Items.Cast<string>().ToHashSet();
        var newProperties = propertiesList.Where(p => !existingItems.Contains(p)).ToList();

        if (!clearExisting && newProperties.Count == 0)
        {
            return;
        }

        var topIndex = listBox.TopIndex;

        if (clearExisting)
        {
            listBox.Items.Clear();
            foreach (var property in propertiesList.OrderBy(x => x))
            {
                listBox.Items.Add(property);
            }
        }
        else
        {
            foreach (var property in newProperties)
            {
                listBox.Items.Add(property);
            }

            var sortedItems = listBox.Items.Cast<string>().OrderBy(x => x).ToArray();
            listBox.Items.Clear();
            listBox.Items.AddRange(sortedItems);
        }

        if (topIndex < listBox.Items.Count)
        {
            listBox.TopIndex = Math.Min(topIndex, Math.Max(0, listBox.Items.Count - 1));
        }
    }

    private static void AddSelectedProperties(TreeView treeView, ListBox listBox)
    {
        var selectedProperties = new List<string>();

        foreach (TreeNode classnameNode in treeView.Nodes)
        {
            foreach (TreeNode propertyNode in classnameNode.Nodes)
            {
                if (propertyNode.Checked && propertyNode.Tag is string property)
                {
                    selectedProperties.Add(property);
                    propertyNode.Checked = false;
                }
            }
        }

        UpdateListBoxProperties(listBox, selectedProperties);
    }

    private static void RemoveSelectedProperties(ListBox listBox)
    {
        var selectedItems = listBox.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            listBox.Items.Remove(item);
        }
    }

    private static void AddAllAvailableProperties(List<EntityLump.Entity> entities, ListBox listBox)
    {
        var allProperties = GetAllAvailableProperties(entities);
        UpdateListBoxProperties(listBox, allProperties, true);
    }

    private static void AddSmartDefaultProperties(List<EntityLump.Entity> entities, ListBox listBox)
    {
        var smartProperties = GetSmartPropertiesFromEntities(entities);
        UpdateListBoxProperties(listBox, smartProperties, true);
    }

    private static HashSet<string> GetAllAvailableProperties(List<EntityLump.Entity> entities)
    {
        var allProperties = new HashSet<string>();
        foreach (var entity in entities)
        {
            foreach (var prop in entity.Properties.Properties)
            {
                allProperties.Add(prop.Key);
            }
        }

        return allProperties;
    }

    private static List<EntityLump.Entity> GetEntitiesWithRelated(
        List<EntityLump.Entity> filteredEntities,
        List<EntityLump.Entity> allEntities,
        bool includePropertyRelated,
        bool includeConnectionRelated)
    {
        var namedEntitiesLookup = allEntities
            .Where(e => !string.IsNullOrEmpty(e.GetProperty<string>("targetname", "")))
            .ToLookup(e => e.GetProperty<string>("targetname", ""));

        if (includePropertyRelated && !includeConnectionRelated)
        {
            return FindEntitiesWithPropertyRelations(filteredEntities, namedEntitiesLookup);
        }

        if (includeConnectionRelated && !includePropertyRelated)
        {
            var entitiesByConnection = BuildConnectionLookup(allEntities);
            return FindEntitiesWithConnectionRelations(filteredEntities, namedEntitiesLookup, entitiesByConnection);
        }

        if (includePropertyRelated && includeConnectionRelated)
        {
            var entitiesByConnection = BuildConnectionLookup(allEntities);
            return FindEntitiesWithBothRelations(filteredEntities, namedEntitiesLookup, entitiesByConnection);
        }

        return new List<EntityLump.Entity>(filteredEntities);
    }

    private static List<EntityLump.Entity> FindEntitiesWithPropertyRelations(
        List<EntityLump.Entity> startEntities,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup)
    {
        var processed = new HashSet<EntityLump.Entity>();
        var result = new List<EntityLump.Entity>();
        var queue = new Queue<EntityLump.Entity>();

        foreach (var entity in startEntities)
        {
            queue.Enqueue(entity);
        }

        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();
            if (processed.Contains(entity))
            {
                continue;
            }

            processed.Add(entity);
            result.Add(entity);

            ProcessPropertyRelated(entity, namedEntitiesLookup, processed, queue);
        }

        return result;
    }

    private static List<EntityLump.Entity> FindEntitiesWithConnectionRelations(
        List<EntityLump.Entity> startEntities,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        Dictionary<string, List<EntityLump.Entity>> entitiesByConnection)
    {
        var processed = new HashSet<EntityLump.Entity>();
        var result = new List<EntityLump.Entity>();
        var queue = new Queue<EntityLump.Entity>();

        foreach (var entity in startEntities)
        {
            queue.Enqueue(entity);
        }

        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();
            if (processed.Contains(entity))
            {
                continue;
            }

            processed.Add(entity);
            result.Add(entity);

            ProcessConnectionRelated(entity, namedEntitiesLookup, entitiesByConnection, processed, queue);
        }

        return result;
    }

    private static List<EntityLump.Entity> FindEntitiesWithBothRelations(
        List<EntityLump.Entity> startEntities,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        Dictionary<string, List<EntityLump.Entity>> entitiesByConnection)
    {
        var processed = new HashSet<EntityLump.Entity>();
        var result = new List<EntityLump.Entity>();
        var queue = new Queue<EntityLump.Entity>();

        foreach (var entity in startEntities)
        {
            queue.Enqueue(entity);
        }

        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();
            if (processed.Contains(entity))
            {
                continue;
            }

            processed.Add(entity);
            result.Add(entity);

            ProcessPropertyRelated(entity, namedEntitiesLookup, processed, queue);
            ProcessConnectionRelated(entity, namedEntitiesLookup, entitiesByConnection, processed, queue);
        }

        return result;
    }

    private static Dictionary<string, List<EntityLump.Entity>> BuildConnectionLookup(
        List<EntityLump.Entity> allEntities)
    {
        var lookup = new Dictionary<string, List<EntityLump.Entity>>();

        foreach (var entity in allEntities.Where(e => e.Connections?.Count > 0))
        {
            foreach (var connection in entity.Connections!)
            {
                var targetName = connection.GetStringProperty("m_targetName");
                if (!string.IsNullOrEmpty(targetName))
                {
                    if (!lookup.TryGetValue(targetName, out var list))
                    {
                        list = new List<EntityLump.Entity>();
                        lookup[targetName] = list;
                    }

                    list.Add(entity);
                }
            }
        }

        return lookup;
    }

    private static void ProcessConnectionRelated(
        EntityLump.Entity entity,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        Dictionary<string, List<EntityLump.Entity>> entitiesByConnection,
        HashSet<EntityLump.Entity> processed,
        Queue<EntityLump.Entity> queue)
    {
        if (entity.Connections?.Count > 0)
        {
            foreach (var connection in entity.Connections)
            {
                var targetName = connection.GetStringProperty("m_targetName");
                if (!string.IsNullOrEmpty(targetName))
                {
                    // Use flexible entity lookup
                    var targetEntities = FindEntitiesByName(namedEntitiesLookup, targetName);
                    foreach (var target in targetEntities.Where(t => !processed.Contains(t)))
                    {
                        queue.Enqueue(target);
                    }
                }
            }
        }
    }

    private static void ProcessPropertyRelated(
        EntityLump.Entity entity,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        HashSet<EntityLump.Entity> processed,
        Queue<EntityLump.Entity> queue)
    {
        foreach (var (key, value) in entity.Properties.Properties)
        {
            if (value.Value == null || !EntityPropertyManager.ReferenceProperties.Contains(key))
            {
                continue;
            }

            var referencedName = value.Value.ToString()!.Trim();
            if (string.IsNullOrEmpty(referencedName) ||
                referencedName.StartsWith('!') ||
                referencedName.Contains(' ') ||
                referencedName.Contains(',') ||
                referencedName.Contains(';'))
            {
                continue;
            }

            // Use flexible entity lookup
            var referencedEntities = FindEntitiesByName(namedEntitiesLookup, referencedName);
            foreach (var referenced in referencedEntities.Where(r => !processed.Contains(r)))
            {
                queue.Enqueue(referenced);
            }
        }
    }

    /// <summary>
    ///     Find entities by name with fuzzy matching support
    /// </summary>
    private static IEnumerable<EntityLump.Entity> FindEntitiesByName(
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return Enumerable.Empty<EntityLump.Entity>();
        }

        // Try exact match first
        var exactMatches = namedEntitiesLookup[targetName];
        if (exactMatches.Any())
        {
            return exactMatches;
        }

        // Fallback to fuzzy matching for names with suffixes like "&0000"
        var fuzzyMatches = new List<EntityLump.Entity>();
        foreach (var group in namedEntitiesLookup)
        {
            var entityName = group.Key;

            // Check if starts with target name followed by separator
            if (entityName.Length >= targetName.Length &&
                entityName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase))
            {
                if (entityName.Length == targetName.Length)
                {
                    fuzzyMatches.AddRange(group);
                }
                else
                {
                    var nextChar = entityName[targetName.Length];
                    // Check for separators: &, _, ., #, digits
                    if (nextChar == '&' || nextChar == '_' || nextChar == '.' || nextChar == '#' ||
                        char.IsDigit(nextChar))
                    {
                        fuzzyMatches.AddRange(group);
                    }
                }
            }
        }

        return fuzzyMatches;
    }

    public static HashSet<string> GetSmartPropertiesFromEntities(List<EntityLump.Entity> entities)
    {
        return EntityPropertyManager.GetSmartProperties(entities);
    }

    private static string SerializeEntitiesFull(List<EntityLump.Entity> entities)
    {
        return MapExtract.SerializeEntities(entities);
    }

    private static string SerializeEntitiesWithSelectedProperties(List<EntityLump.Entity> entities,
        HashSet<string> selectedProperties)
    {
        var entitiesByType = entities.GroupBy(e => e.GetProperty<string>("classname", "unknown")).ToList();
        var propertiesByType = new Dictionary<string, HashSet<string>>();

        foreach (var group in entitiesByType)
        {
            var classname = group.Key;

            var prefixProperties = EntityPropertyManager.GetPropertiesForClassname(classname);

            var finalProperties = new HashSet<string>(prefixProperties, StringComparer.OrdinalIgnoreCase);
            finalProperties.UnionWith(selectedProperties);

            propertiesByType[classname] = finalProperties;
        }

        var filteredEntities = entities.Select(entity =>
        {
            var classname = entity.GetProperty<string>("classname", "unknown");
            var relevantProperties = propertiesByType.GetValueOrDefault(classname, selectedProperties);
            var entityDict = new Dictionary<string, object>();

            foreach (var (key, value) in entity.Properties.Properties)
            {
                if (relevantProperties.Contains(key) && value.Value != null)
                {
                    entityDict[key] = ConvertEntityValue(value.Value);
                }
            }

            if (entity.Connections?.Count > 0)
            {
                var connections = entity.Connections
                    .Select(FilterConnection)
                    .Where(c => c.Count > 0)
                    .Cast<object>()
                    .ToList();

                if (connections.Count > 0)
                {
                    entityDict["connections"] = connections;
                }
            }

            return entityDict;
        }).Where(dict => dict.Count > 0).ToList();

        return JsonSerializer.Serialize(filteredEntities, KVJsonContext.Options);
    }

    private static object ConvertEntityValue(object value)
    {
        return value switch
        {
            string str => str,
            bool boolean => boolean,
            Vector3 vector => new { vector.X, vector.Y, vector.Z },
            Vector2 vector => new { vector.X, vector.Y },
            KVObject { IsArray: true } kvArray => kvArray.Select(p => p.Value).ToArray(),
            _ when value.GetType().IsPrimitive => value,
            _ => value.ToString() ?? ""
        };
    }

    private static Dictionary<string, object> FilterConnection(KVObject connection)
    {
        var filteredConnection = new Dictionary<string, object>();

        foreach (var (key, kvValue) in connection.Properties)
        {
            if (EntityPropertyManager.ConnectionProperties.Contains(key) && kvValue.Value != null)
            {
                filteredConnection[key] = ConvertEntityValue(kvValue.Value);
            }
        }

        return filteredConnection;
    }

    /// <summary>
    ///     Test fuzzy entity name matching functionality
    /// </summary>
    public static int TestFuzzyEntityMatching(List<EntityLump.Entity> entities, string targetName)
    {
        var namedEntitiesLookup = entities
            .Where(e => !string.IsNullOrEmpty(e.GetProperty<string>("targetname", "")))
            .ToLookup(e => e.GetProperty<string>("targetname", ""));

        var matches = FindEntitiesByName(namedEntitiesLookup, targetName);
        return matches.Count();
    }
}
