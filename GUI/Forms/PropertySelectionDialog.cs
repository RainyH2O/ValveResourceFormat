using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

public class PropertySelectionDialog : ThemedForm
{
    private readonly List<EntityLump.Entity> _allEntities;
    private readonly HashSet<string> _customExportProperties;
    private readonly List<EntityLump.Entity> _filteredEntities;
    private readonly TextBox _searchTextBox;
    private readonly TreeView _availableTreeView;
    private readonly ListBox _selectedListBox;
    private readonly CheckBox _includePropertyRelatedCheckBox;
    private readonly CheckBox _includeConnectionRelatedCheckBox;

    public HashSet<string> SelectedProperties { get; } = [];
    public List<EntityLump.Entity> FinalEntityList { get; private set; } = [];
    public string ExportedJson { get; private set; } = string.Empty;

#pragma warning disable CA2000 // Controls are transferred to the form's Controls collection and disposed with it
    public PropertySelectionDialog(List<EntityLump.Entity> filteredEntities,
        HashSet<string>? preSelectedProperties = null,
        List<EntityLump.Entity>? allEntities = null)
    {
        _filteredEntities = filteredEntities;
        _allEntities = allEntities ?? filteredEntities;
        _customExportProperties = preSelectedProperties ?? [];

        Text = "Export Configuration";
        Width = 920;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;

        // Search controls
        var searchLabel = new Label { Text = "Search:", Left = 10, Top = 15, AutoSize = true };
        _searchTextBox = new ThemedTextBox { Left = 70, Top = 12, Width = 200 };
        _searchTextBox.TextChanged += (_, _) => FilterPropertiesTree(_searchTextBox.Text);

        var expandAllButton = new ThemedButton { Text = "Expand All", Left = 280, Top = 12, Width = 85, Height = 25 };
        expandAllButton.Click += (_, _) => _availableTreeView?.ExpandAll();

        var collapseAllButton = new ThemedButton { Text = "Collapse All", Left = 280, Top = 42, Width = 85, Height = 25 };
        collapseAllButton.Click += (_, _) => _availableTreeView?.CollapseAll();

        // Tree view and list box
        var availableLabel = new Label { Text = "Available Properties (by Entity Type):", Left = 10, Top = 45, AutoSize = true };
        _availableTreeView = new TreeView
        {
            Left = 10, Top = 70, Width = 350, Height = 400,
            CheckBoxes = true, Font = new Font("Consolas", 9),
        };
        _availableTreeView.NodeMouseClick += OnTreeViewNodeMouseClick;

        var selectedLabel = new Label { Text = "Selected Properties for Export:", Left = 500, Top = 45, AutoSize = true };
        _selectedListBox = new ListBox
        {
            Left = 500, Top = 70, Width = 400, Height = 400,
            Font = new Font("Consolas", 9), SelectionMode = SelectionMode.MultiExtended,
        };

        // Action buttons
        var addButton = new ThemedButton { Text = "\u2192 Add", Left = 380, Top = 160, Width = 100, Height = 32 };
        addButton.Click += (_, _) => AddSelectedProperties();

        var removeButton = new ThemedButton { Text = "\u2190 Remove", Left = 380, Top = 200, Width = 100, Height = 32 };
        removeButton.Click += (_, _) => RemoveSelectedProperties();

        var addAllButton = new ThemedButton { Text = "\u21D2 All", Left = 380, Top = 250, Width = 100, Height = 38 };
        addAllButton.Click += (_, _) => AddAllAvailableProperties();

        var addSmartButton = new ThemedButton { Text = "\u21D2 Smart", Left = 380, Top = 295, Width = 100, Height = 38 };
        addSmartButton.Click += (_, _) => AddSmartDefaultProperties();

        // Options
        _includePropertyRelatedCheckBox = new CheckBox
        {
            Text = "Include related entities (properties)",
            Left = 10, Top = 480, Width = 400,
        };
        _includeConnectionRelatedCheckBox = new CheckBox
        {
            Text = "Include related entities (connections)",
            Left = 10, Top = 500, Width = 400,
        };

        // OK/Cancel
        var okButton = new ThemedButton { Text = "Export", Left = 740, Top = 525, Width = 75, Height = 30, DialogResult = DialogResult.OK };
        okButton.Click += OnOkClick;

        var cancelButton = new ThemedButton { Text = "Cancel", Left = 820, Top = 525, Width = 75, Height = 30, DialogResult = DialogResult.Cancel };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange([
            searchLabel, _searchTextBox, expandAllButton, collapseAllButton,
            availableLabel, _availableTreeView, selectedLabel, _selectedListBox,
            addButton, removeButton, addAllButton, addSmartButton,
            _includePropertyRelatedCheckBox, _includeConnectionRelatedCheckBox,
            okButton, cancelButton
        ]);

        PopulateAvailablePropertiesTree();
        InitializeSelectedProperties();
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchTextBox.Dispose();
            _availableTreeView.Dispose();
            _selectedListBox.Dispose();
            _includePropertyRelatedCheckBox.Dispose();
            _includeConnectionRelatedCheckBox.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_selectedListBox.Items.Count == 0)
        {
            MessageBox.Show("Please select at least one property for export.", "No Properties Selected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        SelectedProperties.Clear();
        foreach (string property in _selectedListBox.Items)
        {
            SelectedProperties.Add(property);
        }

        var includePropertyRelated = _includePropertyRelatedCheckBox.Checked;
        var includeConnectionRelated = _includeConnectionRelatedCheckBox.Checked;

        FinalEntityList = (includePropertyRelated || includeConnectionRelated)
            ? GetEntitiesWithRelated(_filteredEntities, _allEntities, includePropertyRelated, includeConnectionRelated)
            : new List<EntityLump.Entity>(_filteredEntities);

        var totalAvailableProperties = GetAllAvailableProperties(_filteredEntities).Count;
        ExportedJson = SelectedProperties.Count == totalAvailableProperties
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

    private void PopulateAvailablePropertiesTree()
    {
        _availableTreeView.Nodes.Clear();
        var propertiesByClassname = new Dictionary<string, HashSet<string>>();

        foreach (var entity in _filteredEntities)
        {
            var classname = entity.GetProperty<string>("classname", "unknown");
            if (!propertiesByClassname.TryGetValue(classname, out var properties))
            {
                properties = [];
                propertiesByClassname[classname] = properties;
            }

            foreach (var prop in entity.Properties.Properties)
            {
                properties.Add(prop.Key);
            }
        }

        foreach (var kvp in propertiesByClassname.OrderBy(x => x.Key))
        {
            var classnameNode = new TreeNode($"{kvp.Key} ({kvp.Value.Count} properties)") { Tag = kvp.Key };

            foreach (var property in kvp.Value.OrderBy(x => x))
            {
                classnameNode.Nodes.Add(new TreeNode(property) { Tag = property });
            }

            _availableTreeView.Nodes.Add(classnameNode);
        }

        _availableTreeView.ExpandAll();
    }

    private void InitializeSelectedProperties()
    {
        _selectedListBox.Items.Clear();

        var properties = _customExportProperties.Count > 0
            ? _customExportProperties
            : EntityPropertyManager.GetSmartProperties(_filteredEntities);

        foreach (var property in properties.OrderBy(x => x))
        {
            _selectedListBox.Items.Add(property);
        }
    }

    private void FilterPropertiesTree(string searchText)
    {
        _availableTreeView.BeginUpdate();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            ResetTreeNodeColors(_availableTreeView.Nodes);
            _availableTreeView.EndUpdate();
            return;
        }

        FilterTreeNodes(_availableTreeView.Nodes, searchText);
        _availableTreeView.EndUpdate();
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
            var childrenMatch = node.Nodes.Count > 0 && FilterTreeNodes(node.Nodes, searchText);

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

    private void AddSelectedProperties()
    {
        var selected = new List<string>();

        foreach (TreeNode classnameNode in _availableTreeView.Nodes)
        {
            foreach (TreeNode propertyNode in classnameNode.Nodes)
            {
                if (propertyNode.Checked && propertyNode.Tag is string property)
                {
                    selected.Add(property);
                    propertyNode.Checked = false;
                }
            }
        }

        UpdateListBoxProperties(selected);
    }

    private void RemoveSelectedProperties()
    {
        var selectedItems = _selectedListBox.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _selectedListBox.Items.Remove(item);
        }
    }

    private void AddAllAvailableProperties()
    {
        UpdateListBoxProperties(GetAllAvailableProperties(_filteredEntities), clearExisting: true);
    }

    private void AddSmartDefaultProperties()
    {
        UpdateListBoxProperties(EntityPropertyManager.GetSmartProperties(_filteredEntities), clearExisting: true);
    }

    private void UpdateListBoxProperties(IEnumerable<string> properties, bool clearExisting = false)
    {
        var existingItems = clearExisting ? new HashSet<string>() : _selectedListBox.Items.Cast<string>().ToHashSet();

        if (clearExisting)
        {
            _selectedListBox.Items.Clear();
        }

        foreach (var property in properties)
        {
            existingItems.Add(property);
        }

        var sorted = existingItems.OrderBy(x => x).ToArray();
        _selectedListBox.Items.Clear();
        _selectedListBox.Items.AddRange(sorted);
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

        Dictionary<string, List<EntityLump.Entity>>? connectionLookup = null;
        if (includeConnectionRelated)
        {
            connectionLookup = BuildConnectionLookup(allEntities);
        }

        var processed = new HashSet<EntityLump.Entity>();
        var result = new List<EntityLump.Entity>();
        var queue = new Queue<EntityLump.Entity>(filteredEntities);

        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();
            if (!processed.Add(entity))
            {
                continue;
            }

            result.Add(entity);

            if (includePropertyRelated)
            {
                ProcessPropertyRelated(entity, namedEntitiesLookup, processed, queue);
            }

            if (includeConnectionRelated && connectionLookup != null)
            {
                ProcessConnectionRelated(entity, namedEntitiesLookup, processed, queue);
            }
        }

        return result;
    }

    private static Dictionary<string, List<EntityLump.Entity>> BuildConnectionLookup(List<EntityLump.Entity> allEntities)
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
                        list = [];
                        lookup[targetName] = list;
                    }
                    list.Add(entity);
                }
            }
        }

        return lookup;
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

            foreach (var referenced in FindEntitiesByName(namedEntitiesLookup, referencedName))
            {
                if (!processed.Contains(referenced))
                {
                    queue.Enqueue(referenced);
                }
            }
        }
    }

    private static void ProcessConnectionRelated(
        EntityLump.Entity entity,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        HashSet<EntityLump.Entity> processed,
        Queue<EntityLump.Entity> queue)
    {
        if (entity.Connections == null)
        {
            return;
        }

        foreach (var connection in entity.Connections)
        {
            var targetName = connection.GetStringProperty("m_targetName");
            if (string.IsNullOrEmpty(targetName))
            {
                continue;
            }

            foreach (var target in FindEntitiesByName(namedEntitiesLookup, targetName))
            {
                if (!processed.Contains(target))
                {
                    queue.Enqueue(target);
                }
            }
        }
    }

    private static IEnumerable<EntityLump.Entity> FindEntitiesByName(
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return [];
        }

        var exactMatches = namedEntitiesLookup[targetName];
        if (exactMatches.Any())
        {
            return exactMatches;
        }

        // Fuzzy matching for names with suffixes like "&0000"
        var fuzzyMatches = new List<EntityLump.Entity>();
        foreach (var group in namedEntitiesLookup)
        {
            var entityName = group.Key;
            if (entityName.Length < targetName.Length ||
                !entityName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entityName.Length == targetName.Length)
            {
                fuzzyMatches.AddRange(group);
            }
            else
            {
                var nextChar = entityName[targetName.Length];
                if (nextChar is '&' or '_' or '.' or '#' || char.IsDigit(nextChar))
                {
                    fuzzyMatches.AddRange(group);
                }
            }
        }

        return fuzzyMatches;
    }

    private static string SerializeEntitiesFull(List<EntityLump.Entity> entities)
    {
        return MapExtract.SerializeEntities(entities);
    }

    private static string SerializeEntitiesWithSelectedProperties(
        List<EntityLump.Entity> entities,
        HashSet<string> selectedProperties)
    {
        var propertiesByType = new Dictionary<string, HashSet<string>>();

        foreach (var group in entities.GroupBy(e => e.GetProperty<string>("classname", "unknown")))
        {
            var prefixProperties = EntityPropertyManager.GetPropertiesForClassname(group.Key);
            var finalProperties = new HashSet<string>(prefixProperties, StringComparer.OrdinalIgnoreCase);
            finalProperties.UnionWith(selectedProperties);
            propertiesByType[group.Key] = finalProperties;
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
        var filtered = new Dictionary<string, object>();

        foreach (var (key, kvValue) in connection.Properties)
        {
            if (EntityPropertyManager.ConnectionProperties.Contains(key) && kvValue.Value != null)
            {
                filtered[key] = ConvertEntityValue(kvValue.Value);
            }
        }

        return filtered;
    }
}
