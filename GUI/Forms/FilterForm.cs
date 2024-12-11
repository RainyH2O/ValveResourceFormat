using System.Linq;
using System.Windows.Forms;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

public partial class FilterForm : Form
{
    private readonly List<EntityLump.Entity> entities;
    public List<EntityLump.Entity>? filteredEntities { get; private set; }
    private bool allSelected;
    private bool keyHandled;

    public FilterForm(List<EntityLump.Entity> entities)
    {
        InitializeComponent();
        this.entities = entities;
        PopulateCheckedListBox();
    }

    private void PopulateCheckedListBox()
    {
        var classNames = entities
            .Where(entity => entity.Properties.Properties.ContainsKey("classname"))
            .Select(entity => entity.Properties.Properties["classname"].Value?.ToString())
            .Distinct()
            .ToList();
        classNames.Sort();

        checkedListBoxClassNames.Items.Clear();
        checkedListBoxClassNames.Items.AddRange(classNames.ToArray()!);
        checkedListBoxClassNames.CheckOnClick = true;
    }

    private void buttonFilter_Click(object sender, EventArgs e)
    {
        var selectedClassNames = new List<string>();
        for (var i = 0; i < checkedListBoxClassNames.Items.Count; i++)
        {
            if (checkedListBoxClassNames.GetItemChecked(i))
            {
                selectedClassNames.Add(checkedListBoxClassNames.Items[i].ToString()!);
            }
        }

        filteredEntities = entities
            .Where(entity => entity.Properties.Properties.ContainsKey("classname") &&
                             selectedClassNames.Contains(entity.Properties.Properties["classname"].Value?.ToString()!))
            .ToList();

        DialogResult = DialogResult.Continue;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A)
        {
            if (allSelected)
            {
                DeselectAllItems();
            }
            else
            {
                SelectAllItems();
            }
            allSelected = !allSelected;
            e.Handled = true;
            keyHandled = true;
        }
        else
        {
            base.OnKeyDown(e);
            keyHandled = false;
        }
    }

    private void SelectAllItems()
    {
        for (var i = 0; i < checkedListBoxClassNames.Items.Count; i++)
        {
            checkedListBoxClassNames.SetItemChecked(i, true);
        }
    }

    private void DeselectAllItems()
    {
        for (var i = 0; i < checkedListBoxClassNames.Items.Count; i++)
        {
            checkedListBoxClassNames.SetItemChecked(i, false);
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (keyHandled)
        {
            e.Handled = true;
        }
        else
        {
            base.OnKeyPress(e);
        }
    }
}
