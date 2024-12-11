using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

public class PropertySelectionDialog : Form
{
    public enum ExportType
    {
        Full,
        Minimal,
        Custom
    }

    // Static readonly arrays for entity property mappings
    private static readonly string[] LightProperties =
        ["color", "brightness", "range", "intensity", "castshadows", "innerangle", "outerangle"];

    private static readonly string[] TriggerProperties = ["filtername", "wait", "startdisabled", "damage", "model"];

    private static readonly string[] FuncProperties =
        ["model", "speed", "health", "startdisabled", "movedir", "sounds"];

    private static readonly string[] DoorProperties = ["speed", "health", "locked", "message"];
    private static readonly string[] InfoProperties = ["enabled", "radius", "model"];
    private static readonly string[] EnvProperties = ["message", "health", "model", "scale"];
    private static readonly string[] PropProperties = ["model", "skin", "health", "solid"];
    private static readonly string[] NpcProperties = ["model", "health", "squadname", "spawnequipment"];
    private static readonly string[] WeaponProperties = ["model", "respawntime", "autorespawn"];
    private static readonly string[] SoundProperties = ["soundname", "volume", "radius", "looped"];
    private static readonly string[] LogicProperties = ["startdisabled", "initialstate", "threshold"];
    private static readonly string[] MathProperties = ["startdisabled", "min", "max", "startvalue"];
    private static readonly string[] FilterProperties = ["filtername", "negated"];
    private static readonly string[] TemplateProperties = ["template01", "template02", "template03", "template04"];
    private static readonly string[] WorldspawnProperties = ["message", "skyname", "chaptertitle", "gametitle"];
    private static readonly string[] PlayerProperties = ["model", "health"];
    private static readonly string[] GameTextProperties = ["message", "x", "y", "holdtime"];
    private static readonly string[] EnvMessageProperties = ["message", "messagevolume"];
    private readonly HashSet<string> _customExportProperties = new();

    private readonly List<EntityLump.Entity> _entities;
    private readonly bool _useCustomProperties;
    private Button _addAllButton = null!;
    private Button _addButton = null!;
    private Button _addSmartButton = null!;
    private TreeView _availableTreeView = null!;
    private Button _cancelButton = null!;
    private Button _collapseAllButton = null!;
    private Button _expandAllButton = null!;
    private CheckBox _includeRelatedCheckBox = null!;
    private Button _okButton = null!;
    private Button _removeButton = null!;

    // UI controls
    private TextBox _searchTextBox = null!;
    private ListBox _selectedListBox = null!;

    // Component management
    private Container? components;

    public PropertySelectionDialog(List<EntityLump.Entity> entities,
        HashSet<string>? preSelectedProperties = null,
        bool useCustomProperties = false)
    {
        _entities = entities;
        _customExportProperties = preSelectedProperties ?? new HashSet<string>();
        _useCustomProperties = useCustomProperties;

        InitializeComponent();
        InitializeDialog();
        SetupEventHandlers();
        PopulateControls();
    }

    public ExportType SelectedExportType { get; private set; }
    public bool IncludeRelatedEntities { get; private set; }
    public HashSet<string> SelectedProperties { get; } = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            // Explicit disposal for each component to satisfy code analysis
            DisposeComponent(_searchTextBox);
            DisposeComponent(_availableTreeView);
            DisposeComponent(_selectedListBox);
            DisposeComponent(_includeRelatedCheckBox);
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
        Height = 650;
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

        _includeRelatedCheckBox = CreateComponent(new CheckBox
        {
            Text = "Include related entities (connected via inputs/outputs)",
            Left = 10, Top = 485, Width = 500
        });

        _okButton = CreateComponent(new Button
        {
            Text = "Export", Left = 740, Top = 520, Width = 75, Height = 30,
            DialogResult = DialogResult.OK
        });

        _cancelButton = CreateComponent(new Button
        {
            Text = "Cancel", Left = 820, Top = 520, Width = 75, Height = 30,
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
            _addSmartButton, _includeRelatedCheckBox, _okButton, _cancelButton);
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
        _addAllButton.Click += (s, e) => AddAllAvailableProperties(_entities, _selectedListBox);
        _addSmartButton.Click += (s, e) => AddSmartDefaultProperties(_entities, _selectedListBox);
        _availableTreeView.NodeMouseClick += OnTreeViewNodeMouseClick;

        _okButton.Click += OkButton_Click;
    }

    private void PopulateControls()
    {
        PopulateAvailablePropertiesTree(_availableTreeView, _entities);
        InitializeSelectedProperties(_selectedListBox, _entities);
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

        var totalAvailableProperties = GetAllAvailableProperties(_entities).Count;
        SelectedExportType = SelectedProperties.Count == totalAvailableProperties ? ExportType.Full : ExportType.Custom;
        IncludeRelatedEntities = _includeRelatedCheckBox.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnTreeViewNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Node == null)
        {
            return;
        }

        // Skip clicks on checkbox area
        var checkBoxWidth = 16;
        var nodeX = e.Node.Bounds.X - checkBoxWidth;
        if (e.X <= nodeX + checkBoxWidth)
        {
            return;
        }

        // Property node - toggle checkbox
        if (e.Node.Parent != null && e.Node.Tag is string)
        {
            e.Node.Checked = !e.Node.Checked;
        }
        // Class node - toggle all child properties
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
        var allProperties = new HashSet<string>();

        foreach (var entity in entities)
        {
            var classname = entity.GetProperty<string>("classname", "");
            var smartProperties = GetAdditionalPropertiesForClassname(classname);
            allProperties.UnionWith(smartProperties);
        }

        allProperties.Add("classname");
        allProperties.Add("targetname");

        foreach (var property in allProperties.OrderBy(x => x))
        {
            listBox.Items.Add(property);
        }

        // Use custom configuration if available
        if (_useCustomProperties && _customExportProperties.Count > 0)
        {
            listBox.Items.Clear();
            foreach (var property in _customExportProperties.OrderBy(x => x))
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
            foreach (TreeNode classnameNode in treeView.Nodes)
            {
                classnameNode.BackColor = Color.White;
                classnameNode.ForeColor = Color.Black;
                foreach (TreeNode propertyNode in classnameNode.Nodes)
                {
                    propertyNode.BackColor = Color.White;
                    propertyNode.ForeColor = Color.Black;
                }
            }

            treeView.EndUpdate();
            return;
        }

        foreach (TreeNode classnameNode in treeView.Nodes)
        {
            var hasMatchingProperty = false;
            var classnameMatches = classnameNode.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase);

            foreach (TreeNode propertyNode in classnameNode.Nodes)
            {
                var propertyMatches = propertyNode.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase);

                if (propertyMatches || classnameMatches)
                {
                    propertyNode.BackColor = Color.LightYellow;
                    propertyNode.ForeColor = Color.Black;
                    hasMatchingProperty = true;
                }
                else
                {
                    propertyNode.BackColor = Color.White;
                    propertyNode.ForeColor = Color.Gray;
                }
            }

            if (hasMatchingProperty || classnameMatches)
            {
                classnameNode.BackColor = classnameMatches ? Color.LightBlue : Color.White;
                classnameNode.ForeColor = Color.Black;
                classnameNode.Expand();
            }
            else
            {
                classnameNode.BackColor = Color.White;
                classnameNode.ForeColor = Color.Gray;
                classnameNode.Collapse();
            }
        }

        treeView.EndUpdate();
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
        var smartProperties = new HashSet<string> { "classname", "targetname" };

        foreach (var entity in entities)
        {
            var classname = entity.GetProperty<string>("classname", "");
            var additionalProperties = GetAdditionalPropertiesForClassname(classname);
            smartProperties.UnionWith(additionalProperties);
        }

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

    /// <summary>
    ///     Returns additional properties to export based on entity classname
    /// </summary>
    public static HashSet<string> GetAdditionalPropertiesForClassname(string classname)
    {
        var properties = new HashSet<string>();

        // Common properties for most entities
        if (!classname.StartsWith("worldspawn"))
        {
            properties.Add("origin");
            properties.Add("angles");
            properties.Add("spawnflags");
            properties.Add("hammeruniqueid");
        }

        // Entity type specific properties
        if (classname.Contains("light") || classname.StartsWith("env_light"))
        {
            properties.UnionWith(LightProperties);
        }

        if (classname.StartsWith("trigger_"))
        {
            properties.UnionWith(TriggerProperties);
        }

        if (classname.StartsWith("func_"))
        {
            properties.UnionWith(FuncProperties);
        }

        if (classname.Contains("door"))
        {
            properties.UnionWith(DoorProperties);
        }

        if (classname.StartsWith("info_"))
        {
            properties.UnionWith(InfoProperties);
        }

        if (classname.StartsWith("env_"))
        {
            properties.UnionWith(EnvProperties);
        }

        if (classname.StartsWith("prop_"))
        {
            properties.UnionWith(PropProperties);
        }

        if (classname.StartsWith("npc_") || classname.Contains("zombie") || classname.Contains("soldier"))
        {
            properties.UnionWith(NpcProperties);
        }

        if (classname.StartsWith("weapon_") || classname.Contains("item_"))
        {
            properties.UnionWith(WeaponProperties);
        }

        if (classname.Contains("sound") || classname.StartsWith("ambient_"))
        {
            properties.UnionWith(SoundProperties);
        }

        if (classname.StartsWith("logic_"))
        {
            properties.UnionWith(LogicProperties);
        }

        if (classname.StartsWith("math_"))
        {
            properties.UnionWith(MathProperties);
        }

        if (classname.StartsWith("filter_"))
        {
            properties.UnionWith(FilterProperties);
        }

        if (classname.Contains("template"))
        {
            properties.UnionWith(TemplateProperties);
        }

        // Special individual entities
        switch (classname)
        {
            case "worldspawn":
                properties.UnionWith(WorldspawnProperties);
                break;
            case "player":
                properties.UnionWith(PlayerProperties);
                break;
            case "game_text":
                properties.UnionWith(GameTextProperties);
                break;
            case "env_message":
                properties.UnionWith(EnvMessageProperties);
                break;
        }

        return properties;
    }
}
