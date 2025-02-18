using System.Data;
using System.Linq;
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

    private readonly Dictionary<string, TextBox> filterTextBoxes = new();

    private DataTable dataTable;

    public EntityListForm(List<EntityLump.Entity> entities)
    {
        InitializeComponent();
        SetupColumns();
        BindData(entities);
        AddFilterControls();
        entityDataGridView.CellDoubleClick += EntityDataGridView_CellDoubleClick;
    }

    public event EventHandler<string> OnOriginDoubleClicked;

    private void SetupColumns()
    {
        foreach (var kvp in columnsToDisplay)
        {
            var columnName = kvp.Key;
            var columnWidth = kvp.Value;
            if (entityDataGridView.Columns[columnName] == null)
            {
                var column = new DataGridViewTextBoxColumn
                {
                    Name = columnName,
                    DataPropertyName = columnName,
                    HeaderText = columnName,
                    Width = columnWidth
                };
                entityDataGridView.Columns.Add(column);
            }
        }
    }

    private void BindData(List<EntityLump.Entity> entities)
    {
        dataTable = new DataTable();
        foreach (var kvp in columnsToDisplay)
        {
            if (!dataTable.Columns.Contains(kvp.Key))
            {
                dataTable.Columns.Add(kvp.Key);
            }
        }

        foreach (var entity in entities)
        {
            var row = dataTable.NewRow();
            foreach (var column in columnsToDisplay.Keys)
            {
                if (entity.Properties.ContainsKey(column))
                {
                    row[column] = entity.Properties.Properties[column].Value;
                }
                else
                {
                    row[column] = "";
                }
            }

            dataTable.Rows.Add(row);
        }

        entityDataGridView.AutoGenerateColumns = false;
        entityDataGridView.DataSource = dataTable;
    }

    private void AddFilterControls()
    {
        var tableLayoutPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = entityDataGridView.Columns.Count + 2,
            RowCount = 1,
            AutoSize = true
        };

        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.RowHeadersWidth));
        tableLayoutPanel.Controls.Add(new Label { Width = entityDataGridView.RowHeadersWidth });

        for (var i = 0; i < entityDataGridView.Columns.Count; i++)
        {
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.Columns[i].Width));
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Tag = entityDataGridView.Columns[i].Name
            };
            textBox.TextChanged += FilterTextBox_TextChanged;
            tableLayoutPanel.Controls.Add(textBox);
            filterTextBoxes[entityDataGridView.Columns[i].Name] = textBox;
        }

        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        tableLayoutPanel.Controls.Add(new Label { Width = 20 });

        Controls.Add(tableLayoutPanel);
    }

    private void FilterTextBox_TextChanged(object sender, EventArgs e)
    {
        var filterExpression = string.Empty;
        foreach (var textBox in filterTextBoxes.Values.Where(textBox => !string.IsNullOrEmpty(textBox.Text)))
        {
            if (!string.IsNullOrEmpty(filterExpression))
            {
                filterExpression += " AND ";
            }

            var escapedText = EscapeFilterValue(textBox.Text);
            filterExpression += $"{textBox.Tag} LIKE '%{escapedText}%'";
        }

        dataTable.DefaultView.RowFilter = filterExpression;
    }

    /// <summary>
    /// <para>If a pattern in a LIKE clause contains any of these special characters * % [ ], those characters must be escaped in brackets [ ] like this [*], [%], [[] or []].</para>
    /// <para>If the pattern is not in a like clause then you can pass valueIsForLIKEcomparison = false to not escape brackets.</para>
    /// <para>Examples:</para>
    /// <para>- strFilter = "[Something] LIKE '%" + DataTableHelper.EscapeLikeValue(filterValue) + "%'";</para>
    /// <para></para>
    /// <para>http://www.csharp-examples.net/dataview-rowfilter/</para>
    /// </summary>
    /// <param name="filterValue">LIKE filterValue. This should not be the entire filter string... just the part that is being compared.</param>
    /// <param name="valueIsForLIKEcomparison">Whether or not the filterValue is being used in a LIKE comparison.</param>
    /// <returns></returns>
    private static string EscapeFilterValue(string filterValue, bool valueIsForLIKEcomparison = true)
    {
        const string lb = "~~LeftBracket~~";
        const string rb = "~~RightBracket~~";
        filterValue = filterValue.Replace("[", lb).Replace("]", rb).Replace("*", "[*]").Replace("%", "[%]").Replace("'", "''");
        if (valueIsForLIKEcomparison)
        {
            filterValue = filterValue.Replace(lb, "[[]").Replace(rb, "[]]");
        }
        else
        {
            filterValue = filterValue.Replace(lb, "[").Replace(rb, "]");
        }

        return filterValue;
    }

    private void EntityDataGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = entityDataGridView.Columns[e.ColumnIndex].Name;
        if (columnName == "origin")
        {
            var cellValue = entityDataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            HandleOriginDoubleClick(cellValue);
        }
    }

    private void HandleOriginDoubleClick(string origin)
    {
        OnOriginDoubleClicked?.Invoke(this, origin);
    }
}
