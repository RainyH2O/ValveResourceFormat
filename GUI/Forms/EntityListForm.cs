using System.Data;
using System.Linq;
using System.Windows.Forms;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

public partial class EntityListForm : Form
{
    public event EventHandler<string>? OnEntityClicked;
    public event EventHandler<string>? OnEntityDoubleClicked;

    private readonly List<(string ColumnName, int ColumnWidth)> _columnsToDisplay =
    [
        ("classname", 150),
        ("targetname", 150),
        ("spawnflags", 100),
        ("origin", 150),
        ("hammeruniqueid", 100)
    ];

    private readonly Dictionary<string, TextBox> _filterTextBoxes = new();
    private List<EntityLump.Entity>? _entities;
    private DataTable? _dataTable;

    public EntityListForm(List<EntityLump.Entity>? entities)
    {
        InitializeComponent();
        InitDataTable(entities);
    }

    private void InitDataTable(List<EntityLump.Entity>? entities)
    {
        foreach (var (columnName, columnWidth) in _columnsToDisplay)
        {
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

        AddFilterRowControls();
        BindData(entities);
    }

    private void AddFilterRowControls()
    {
        var filterRowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = entityDataGridView.Columns.Count + 2,
            RowCount = 1,
            AutoSize = true
        };

        filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.RowHeadersWidth));
        filterRowPanel.Controls.Add(new Label { Width = entityDataGridView.RowHeadersWidth });

        for (var i = 0; i < entityDataGridView.Columns.Count; i++)
        {
            filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.Columns[i].Width));
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Tag = entityDataGridView.Columns[i].Name
            };
            textBox.TextChanged += FilterTextBox_TextChanged;
            filterRowPanel.Controls.Add(textBox);
            _filterTextBoxes[entityDataGridView.Columns[i].Name] = textBox;
        }

        filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        filterRowPanel.Controls.Add(new Label { Width = 20 });

        Controls.Add(filterRowPanel);
    }

    private void BindData(List<EntityLump.Entity>? entities)
    {
        _entities = entities;
        _dataTable = new DataTable();
        foreach (var (columnName, _) in _columnsToDisplay)
        {
            if (!_dataTable.Columns.Contains(columnName))
            {
                _dataTable.Columns.Add(columnName);
            }
        }

        foreach (var entity in _entities!)
        {
            var row = _dataTable.NewRow();
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                if (entity.ContainsKey(columnName))
                {
                    row[columnName] = entity.GetProperty(columnName).Value;
                }
                else
                {
                    row[columnName] = "";
                }
            }

            _dataTable.Rows.Add(row);
        }

        entityDataGridView.AutoGenerateColumns = false;
        entityDataGridView.DataSource = _dataTable;
    }

    private void FilterTextBox_TextChanged(object? sender, EventArgs e)
    {
        var filterExpression = string.Empty;
        foreach (var textBox in _filterTextBoxes.Values.Where(textBox => !string.IsNullOrEmpty(textBox.Text)))
        {
            if (!string.IsNullOrEmpty(filterExpression))
            {
                filterExpression += " AND ";
            }

            var escapedText = EscapeFilterValue(textBox.Text);
            filterExpression += $"{textBox.Tag} LIKE '%{escapedText}%'";
        }

        _dataTable!.DefaultView.RowFilter = filterExpression;
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
        filterValue = filterValue.Replace("[", lb).Replace("]", rb).Replace("*", "[*]").Replace("%", "[%]")
            .Replace("'", "''");
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

    private void EntityDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        var columnIndex = entityDataGridView.Columns["hammeruniqueid"]!.Index;
        if (columnIndex < 0)
        {
            return;
        }

        var hammerUniqueId = entityDataGridView.Rows[e.RowIndex].Cells[columnIndex].Value?.ToString();
        OnEntityClicked?.Invoke(this, hammerUniqueId!);
    }

    private void EntityDataGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        OnEntityDoubleClicked?.Invoke(this, "");
    }

    private void FilterPanelTextBox_TextChanged(object sender, EventArgs e)
    {
        var keyFilter = string.Empty;
        var valueFilter = string.Empty;
        var isExactMatch = false;
        var outputFilter = string.Empty;
        var targetFilter = string.Empty;
        var inputFilter = string.Empty;

        GetControlsValue(ref keyFilter, ref valueFilter, ref isExactMatch, ref outputFilter, ref targetFilter,
            ref inputFilter);

        UpdateTableColumns(keyFilter, valueFilter, outputFilter, targetFilter, inputFilter);

        var filteredEntities =
            FilterEntities(keyFilter, valueFilter, isExactMatch, outputFilter, targetFilter, inputFilter);

        UpdateDataTable(filteredEntities, keyFilter, valueFilter, isExactMatch, outputFilter, targetFilter,
            inputFilter);
    }

    private void GetControlsValue(ref string keyFilter, ref string valueFilter, ref bool isExactMatch,
        ref string outputFilter, ref string targetFilter, ref string inputFilter)
    {
        foreach (var control in tableLayoutPanel.Controls)
        {
            if (control is not TableLayoutPanel panel)
            {
                continue;
            }

            foreach (var filterPanel in panel.Controls)
            {
                if (filterPanel is not TableLayoutPanel filter)
                {
                    continue;
                }

                foreach (var filterControl in filter.Controls)
                {
                    switch (filterControl)
                    {
                        case TextBox textBox when textBox.Tag?.ToString() == "Key":
                            keyFilter = textBox.Text;
                            break;
                        case TextBox textBox when textBox.Tag?.ToString() == "Value":
                            valueFilter = textBox.Text;
                            break;
                        case CheckBox { Text: "Exact" } checkBox:
                            isExactMatch = checkBox.Checked;
                            break;
                        case TextBox textBox when textBox.Tag?.ToString() == "Output":
                            outputFilter = textBox.Text;
                            break;
                        case TextBox textBox when textBox.Tag?.ToString() == "Target":
                            targetFilter = textBox.Text;
                            break;
                        case TextBox textBox when textBox.Tag?.ToString() == "Input":
                            inputFilter = textBox.Text;
                            break;
                    }
                }
            }
        }
    }

    private void UpdateTableColumns(string keyFilter, string valueFilter, string outputFilter, string targetFilter,
        string inputFilter)
    {
        if (string.IsNullOrEmpty(inputFilter))
        {
            var inputIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Input");
            if (inputIndex != -1)
            {
                _columnsToDisplay.RemoveAt(inputIndex);
                ProcessUpdateColumn();
            }
        }
        else
        {
            var inputIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Input");
            if (inputIndex == -1)
            {
                _columnsToDisplay.Insert(2, ("Input", 150));
                ProcessUpdateColumn();
            }
        }

        if (string.IsNullOrEmpty(targetFilter))
        {
            var targetIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Target");
            if (targetIndex != -1)
            {
                _columnsToDisplay.RemoveAt(targetIndex);
                ProcessUpdateColumn();
            }
        }
        else
        {
            var targetIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Target");
            if (targetIndex == -1)
            {
                _columnsToDisplay.Insert(2, ("Target", 150));
                ProcessUpdateColumn();
            }
        }

        if (string.IsNullOrEmpty(outputFilter))
        {
            var outputIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Output");
            if (outputIndex != -1)
            {
                _columnsToDisplay.RemoveAt(outputIndex);
                ProcessUpdateColumn();
            }
        }
        else
        {
            var outputIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Output");
            if (outputIndex == -1)
            {
                _columnsToDisplay.Insert(2, ("Output", 150));
                ProcessUpdateColumn();
            }
        }

        if (string.IsNullOrEmpty(valueFilter))
        {
            var valueIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Value");
            if (valueIndex != -1)
            {
                _columnsToDisplay.RemoveAt(valueIndex);
                ProcessUpdateColumn();
            }
        }
        else
        {
            var valueIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Value");
            if (valueIndex == -1)
            {
                _columnsToDisplay.Insert(2, ("Value", 150));
                ProcessUpdateColumn();
            }
        }

        if (string.IsNullOrEmpty(keyFilter))
        {
            var keyIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Key");
            if (keyIndex != -1)
            {
                _columnsToDisplay.RemoveAt(keyIndex);
                ProcessUpdateColumn();
            }
        }
        else
        {
            var keyIndex = _columnsToDisplay.FindIndex(x => x.ColumnName == "Key");
            if (keyIndex == -1)
            {
                _columnsToDisplay.Insert(2, ("Key", 150));
                ProcessUpdateColumn();
            }
        }
    }

    private void ProcessUpdateColumn()
    {
        entityDataGridView.Columns.Clear();
        foreach (var (columnName, columnWidth) in _columnsToDisplay)
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

        foreach (var (columnName, _) in _columnsToDisplay)
        {
            if (!_dataTable!.Columns.Contains(columnName))
            {
                _dataTable.Columns.Add(columnName);
            }
        }
    }

    private List<EntityLump.Entity> FilterEntities(string keyFilter, string valueFilter, bool isExactMatch,
        string outputFilter, string targetFilter, string inputFilter)
    {
        var filteredEntities = _entities!.AsEnumerable();

        // Filter by key
        if (!string.IsNullOrEmpty(keyFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return isExactMatch
                    ? entity.ContainsKey(keyFilter)
                    : entity.Properties.Properties.Any(p =>
                        p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase));
            });
        }

        // Filter by value
        if (!string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return isExactMatch
                    ? entity.Properties.Properties.Any(p =>
                        p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase))
                    : entity.Properties.Properties.Any(p =>
                        p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase));
            });
        }

        // Filter by output
        if (!string.IsNullOrEmpty(outputFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return entity.Connections?.Any(c =>
                    c.GetStringProperty("m_outputName")
                        .Contains(outputFilter, StringComparison.OrdinalIgnoreCase)) == true;
            });
        }

        // Filter by target
        if (!string.IsNullOrEmpty(targetFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return entity.Connections?.Any(c =>
                    c.GetStringProperty("m_targetName")
                        .Contains(targetFilter, StringComparison.OrdinalIgnoreCase)) == true;
            });
        }

        // Filter by input
        if (!string.IsNullOrEmpty(inputFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return entity.Connections?.Any(c =>
                           c.GetStringProperty("m_inputName")
                               .Contains(inputFilter, StringComparison.OrdinalIgnoreCase)) ==
                       true;
            });
        }

        return filteredEntities.ToList();
    }

    private void UpdateDataTable(List<EntityLump.Entity> filteredEntities, string keyFilter, string valueFilter,
        bool isExactMatch, string outputFilter, string targetFilter, string inputFilter)
    {
        // Clear existing data
        _dataTable?.Clear();

        // Bind filtered entities to dataTable
        foreach (var entity in filteredEntities)
        {
            var row = _dataTable?.NewRow();
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                if (columnName == "Key")
                {
                    var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                        isExactMatch
                            ? p.Key.Equals(keyFilter, StringComparison.OrdinalIgnoreCase)
                            : p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase));
                    if (matchedProperty.Key != null)
                    {
                        row![columnName] = matchedProperty.Key;
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (columnName == "Value")
                {
                    var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                        isExactMatch
                            ? p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase)
                            : p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase));
                    if (matchedProperty.Value.Value != null)
                    {
                        row![columnName] = matchedProperty.Value.Value.ToString();
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (columnName == "Output")
                {
                    var matchedProperty = entity.Connections.FirstOrDefault(c =>
                        c.GetStringProperty("m_outputName").Contains(outputFilter, StringComparison.OrdinalIgnoreCase));
                    if (matchedProperty != null)
                    {
                        row![columnName] = matchedProperty.GetStringProperty("m_outputName");
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (columnName == "Target")
                {
                    var matchedProperty = entity.Connections.FirstOrDefault(c =>
                        c.GetStringProperty("m_targetName").Contains(targetFilter, StringComparison.OrdinalIgnoreCase));
                    if (matchedProperty != null)
                    {
                        row![columnName] = matchedProperty.GetStringProperty("m_targetName");
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (columnName == "Input")
                {
                    var matchedProperty = entity.Connections.FirstOrDefault(c =>
                        c.GetStringProperty("m_inputName").Contains(inputFilter, StringComparison.OrdinalIgnoreCase));
                    if (matchedProperty != null)
                    {
                        row![columnName] = matchedProperty.GetStringProperty("m_inputName");
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (entity.ContainsKey(columnName))
                {
                    row![columnName] = entity.GetProperty(columnName).Value;
                }
                else
                {
                    row![columnName] = "";
                }
            }

            _dataTable?.Rows.Add(row!);
        }

        entityDataGridView.DataSource = _dataTable;
    }
}
