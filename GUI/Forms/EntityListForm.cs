using System.Windows.Forms;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

public partial class EntityListForm : Form
{
    private readonly Dictionary<string, int> columnsToDisplay = new()
    {
        { "classname", 150 },
        { "targetname", 200 },
        { "spawnflags", 100 },
        { "origin", 200 },
        { "hammeruniqueid", 100 }
    };

    public EntityListForm(List<EntityLump.Entity> entities)
    {
        InitializeComponent();
        SetupColumns(entities);
        BindData(entities);
    }

    private void SetupColumns(List<EntityLump.Entity> entities)
    {
        if (entities == null || entities.Count == 0)
        {
            return;
        }

        foreach (var kvp in columnsToDisplay)
        {
            var columnName = kvp.Key;
            var columnWidth = kvp.Value;
            var column = new DataGridViewTextBoxColumn
            {
                Name = columnName,
                HeaderText = columnName,
                Width = columnWidth
            };
            entityDataGridView.Columns.Add(column);
        }
    }

    private void BindData(List<EntityLump.Entity> entities)
    {
        entityDataGridView.Rows.Clear();
        foreach (var entity in entities)
        {
            var row = new List<object>();
            foreach (DataGridViewColumn column in entityDataGridView.Columns)
            {
                if (entity.Properties.ContainsKey(column.Name))
                {
                    row.Add(entity.Properties.Properties[column.Name].Value);
                }
                else
                {
                    row.Add(null);
                }
            }

            entityDataGridView.Rows.Add(row.ToArray());
        }
    }
}
