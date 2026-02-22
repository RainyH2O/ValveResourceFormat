using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

public class FilterForm : ThemedForm
{
    private readonly CheckedListBox checkedListBox;
    private bool allSelected = true;

    public List<EntityLump.Entity> FilteredEntities { get; private set; } = [];

    private readonly List<EntityLump.Entity> entities;

    public FilterForm(List<EntityLump.Entity> entities)
    {
        this.entities = entities;

        Text = "Filter Entity Types";
        Width = 350;
        Height = 450;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        checkedListBox = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
        };

        var filterButton = new ThemedButton
        {
            Text = "Filter",
            Dock = DockStyle.Bottom,
            Height = 30,
        };
        filterButton.Click += OnFilterClick;

        Controls.Add(checkedListBox);
        Controls.Add(filterButton);

        PopulateCheckedListBox();

        KeyDown += OnKeyDown;
    }

    private void PopulateCheckedListBox()
    {
        var classnames = entities
            .Select(e => e.GetProperty<string>("classname") ?? "unknown")
            .Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        checkedListBox.BeginUpdate();
        foreach (var classname in classnames)
        {
            checkedListBox.Items.Add(classname, true);
        }
        checkedListBox.EndUpdate();
    }

    private void OnFilterClick(object? sender, EventArgs e)
    {
        var selectedClassnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in checkedListBox.CheckedItems)
        {
            selectedClassnames.Add(item.ToString()!);
        }

        FilteredEntities = entities
            .Where(entity => selectedClassnames.Contains(entity.GetProperty<string>("classname") ?? "unknown"))
            .ToList();

        DialogResult = DialogResult.Continue;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            checkedListBox.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A)
        {
            e.Handled = true;
            allSelected = !allSelected;

            for (var i = 0; i < checkedListBox.Items.Count; i++)
            {
                checkedListBox.SetItemChecked(i, allSelected);
            }
        }
    }
}
