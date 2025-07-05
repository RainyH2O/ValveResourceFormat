using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

public partial class EntityListForm : Form
{
    private readonly List<(string ColumnName, int ColumnWidth)> _columnsToDisplay =
    [
        ("classname", 150),
        ("targetname", 150),
        ("spawnflags", 100),
        ("origin", 150),
        ("hammeruniqueid", 100)
    ];

    private readonly Dictionary<string, TextBox> _filterTextBoxes = new();

    private HashSet<string> _customExportProperties = new();
    private DataTable? _dataTable;
    private List<EntityLump.Entity>? _entities;


    public EntityListForm(List<EntityLump.Entity>? entities)
    {
        InitializeComponent();
        InitDataTable(entities);
    }

    public event EventHandler<string>? OnEntityClicked;
    public event EventHandler<string>? OnEntityDoubleClicked;

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
    ///     Escapes special characters for LIKE clause in DataView filter
    /// </summary>
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
        keyFilter = keyTextBox.Text;
        valueFilter = valueTextBox.Text;
        isExactMatch = exactMatchCheckBox.Checked;
        outputFilter = outputTextBox.Text;
        targetFilter = targetTextBox.Text;
        inputFilter = inputTextBox.Text;
    }

    private void UpdateTableColumns(string keyFilter, string valueFilter, string outputFilter, string targetFilter,
        string inputFilter)
    {
        var dynamicColumns = new List<(string ColumnName, int ColumnWidth, bool ShouldShow)>
        {
            ("Key", 150, !string.IsNullOrEmpty(keyFilter)),
            ("Value", 150, !string.IsNullOrEmpty(valueFilter)),
            ("Output", 150, !string.IsNullOrEmpty(outputFilter)),
            ("Target", 150, !string.IsNullOrEmpty(targetFilter)),
            ("Input", 150, !string.IsNullOrEmpty(inputFilter))
        };

        var visibleDynamicColumns =
            dynamicColumns.Where(x => x.ShouldShow).Select(x => (x.ColumnName, x.ColumnWidth)).ToList();

        var staticColumns = _columnsToDisplay.Where(x => !dynamicColumns.Any(d => d.ColumnName == x.ColumnName))
            .ToList();

        _columnsToDisplay.Clear();

        var targetnameIndex = staticColumns.FindIndex(x => x.ColumnName == "targetname");
        if (targetnameIndex >= 0)
        {
            for (var i = 0; i <= targetnameIndex; i++)
            {
                _columnsToDisplay.Add(staticColumns[i]);
            }

            _columnsToDisplay.AddRange(visibleDynamicColumns);

            for (var i = targetnameIndex + 1; i < staticColumns.Count; i++)
            {
                _columnsToDisplay.Add(staticColumns[i]);
            }
        }

        ProcessUpdateColumn();
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

        if (!string.IsNullOrEmpty(keyFilter) && !string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                {
                    return entity.Properties.Properties.Any(p =>
                    {
                        var keyMatches = isExactMatch
                            ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                            : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);

                        if (!keyMatches)
                        {
                            return false;
                        }

                        return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                        {
                            return isExactMatch
                                ? p.Value.Value!.ToString()!.Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                                : p.Value.Value!.ToString()!.Contains(valueTerm, StringComparison.OrdinalIgnoreCase);
                        });
                    });
                });
            });
        }
        else if (!string.IsNullOrEmpty(keyFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                {
                    return isExactMatch
                        ? entity.ContainsKey(keyTerm)
                        : entity.Properties.Properties.Any(p =>
                            p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase));
                });
            });
        }
        else if (!string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                {
                    return isExactMatch
                        ? entity.Properties.Properties.Any(p =>
                            p.Value.Value!.ToString()!.Equals(valueTerm, StringComparison.OrdinalIgnoreCase))
                        : entity.Properties.Properties.Any(p =>
                            p.Value.Value!.ToString()!.Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
                });
            });
        }

        var hasConnectionFilter = !string.IsNullOrEmpty(outputFilter) || !string.IsNullOrEmpty(targetFilter) ||
                                  !string.IsNullOrEmpty(inputFilter);
        if (hasConnectionFilter)
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                if (entity.Connections == null)
                {
                    return false;
                }

                return entity.Connections.Any(connection =>
                {
                    var outputMatches = string.IsNullOrEmpty(outputFilter) ||
                                        FilterExpressionParser.MatchesExpression(outputFilter, outputTerm =>
                                            connection.GetStringProperty("m_outputName").Contains(outputTerm,
                                                StringComparison.OrdinalIgnoreCase));

                    var targetMatches = string.IsNullOrEmpty(targetFilter) ||
                                        FilterExpressionParser.MatchesExpression(targetFilter, targetTerm =>
                                            connection.GetStringProperty("m_targetName").Contains(targetTerm,
                                                StringComparison.OrdinalIgnoreCase));

                    var inputMatches = string.IsNullOrEmpty(inputFilter) ||
                                       FilterExpressionParser.MatchesExpression(inputFilter, inputTerm =>
                                           connection.GetStringProperty("m_inputName").Contains(inputTerm,
                                               StringComparison.OrdinalIgnoreCase));

                    return outputMatches && targetMatches && inputMatches;
                });
            });
        }

        return filteredEntities.ToList();
    }

    private void UpdateDataTable(List<EntityLump.Entity> filteredEntities, string keyFilter, string valueFilter,
        bool isExactMatch, string outputFilter, string targetFilter, string inputFilter)
    {
        _dataTable?.Clear();

        foreach (var entity in filteredEntities)
        {
            var row = _dataTable?.NewRow();
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                if (columnName == "Key")
                {
                    if (!string.IsNullOrEmpty(keyFilter) && !string.IsNullOrEmpty(valueFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                        {
                            return FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                            {
                                var keyMatches = isExactMatch
                                    ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                                    : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);

                                if (!keyMatches)
                                {
                                    return false;
                                }

                                return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                                {
                                    return isExactMatch
                                        ? p.Value.Value!.ToString()!.Equals(valueTerm,
                                            StringComparison.OrdinalIgnoreCase)
                                        : p.Value.Value!.ToString()!.Contains(valueTerm,
                                            StringComparison.OrdinalIgnoreCase);
                                });
                            });
                        });
                        row![columnName] = matchedProperty.Key ?? "";
                    }
                    else if (!string.IsNullOrEmpty(keyFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                            FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                                isExactMatch
                                    ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                                    : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase)));
                        row![columnName] = matchedProperty.Key ?? "";
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (columnName == "Value")
                {
                    if (!string.IsNullOrEmpty(keyFilter) && !string.IsNullOrEmpty(valueFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                        {
                            return FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                            {
                                var keyMatches = isExactMatch
                                    ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                                    : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);

                                if (!keyMatches)
                                {
                                    return false;
                                }

                                return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                                {
                                    return isExactMatch
                                        ? p.Value.Value!.ToString()!.Equals(valueTerm,
                                            StringComparison.OrdinalIgnoreCase)
                                        : p.Value.Value!.ToString()!.Contains(valueTerm,
                                            StringComparison.OrdinalIgnoreCase);
                                });
                            });
                        });
                        row![columnName] = matchedProperty.Value.Value?.ToString() ?? "";
                    }
                    else if (!string.IsNullOrEmpty(valueFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                            FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                                isExactMatch
                                    ? p.Value.Value!.ToString()!.Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                                    : p.Value.Value!.ToString()!.Contains(valueTerm,
                                        StringComparison.OrdinalIgnoreCase)));
                        row![columnName] = matchedProperty.Value.Value?.ToString() ?? "";
                    }
                    else
                    {
                        row![columnName] = "";
                    }
                }
                else if (columnName == "Output")
                {
                    var matchedConnection = FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter);
                    row![columnName] = matchedConnection?.GetStringProperty("m_outputName") ?? "";
                }
                else if (columnName == "Target")
                {
                    var matchedConnection = FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter);
                    row![columnName] = matchedConnection?.GetStringProperty("m_targetName") ?? "";
                }
                else if (columnName == "Input")
                {
                    var matchedConnection = FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter);
                    row![columnName] = matchedConnection?.GetStringProperty("m_inputName") ?? "";
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

    private static KVObject? FindMatchedConnection(EntityLump.Entity entity, string outputFilter, string targetFilter,
        string inputFilter)
    {
        if (entity.Connections == null)
        {
            return null;
        }

        return entity.Connections.FirstOrDefault(connection =>
        {
            var outputMatches = string.IsNullOrEmpty(outputFilter) ||
                                FilterExpressionParser.MatchesExpression(outputFilter, outputTerm =>
                                    connection.GetStringProperty("m_outputName")
                                        .Contains(outputTerm, StringComparison.OrdinalIgnoreCase));

            var targetMatches = string.IsNullOrEmpty(targetFilter) ||
                                FilterExpressionParser.MatchesExpression(targetFilter, targetTerm =>
                                    connection.GetStringProperty("m_targetName")
                                        .Contains(targetTerm, StringComparison.OrdinalIgnoreCase));

            var inputMatches = string.IsNullOrEmpty(inputFilter) ||
                               FilterExpressionParser.MatchesExpression(inputFilter, inputTerm =>
                                   connection.GetStringProperty("m_inputName")
                                       .Contains(inputTerm, StringComparison.OrdinalIgnoreCase));

            return outputMatches && targetMatches && inputMatches;
        });
    }

    private void ExportButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var filteredEntities = GetCurrentFilteredEntities();

            if (filteredEntities.Count == 0)
            {
                MessageBox.Show("No entities to export.", "Export Failed", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var dialog =
                new PropertySelectionDialog(filteredEntities, _customExportProperties, _entities);
            var dialogResult = dialog.ShowDialog(this);
            if (dialogResult != DialogResult.OK)
            {
                return;
            }

            // Update custom properties for future use
            _customExportProperties = dialog.SelectedProperties;

            using var saveDialog = new SaveFileDialog
            {
                Title = "Choose save location",
                FileName = "entities.json",
                InitialDirectory = Settings.Config.SaveDirectory,
                DefaultExt = "json",
                Filter = "JSON files|*.json",
                AddToRecent = true
            };

            var result = saveDialog.ShowDialog();
            if (result != DialogResult.OK)
            {
                return;
            }

            var directory = Path.GetDirectoryName(saveDialog.FileName);
            Settings.Config.SaveDirectory = directory;

            // Use the JSON already generated by PropertySelectionDialog
            File.WriteAllText(saveDialog.FileName, dialog.ExportedJson);

            MessageBox.Show(
                $"Successfully exported {dialog.FinalEntityList.Count} entities to {Path.GetFileName(saveDialog.FileName)}",
                "Export Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<EntityLump.Entity> GetCurrentFilteredEntities()
    {
        var filteredEntities = new List<EntityLump.Entity>();

        if (_entities == null || _dataTable == null)
        {
            return filteredEntities;
        }

        var keyFilter = string.Empty;
        var valueFilter = string.Empty;
        var isExactMatch = false;
        var outputFilter = string.Empty;
        var targetFilter = string.Empty;
        var inputFilter = string.Empty;

        GetControlsValue(ref keyFilter, ref valueFilter, ref isExactMatch, ref outputFilter, ref targetFilter,
            ref inputFilter);

        if (string.IsNullOrEmpty(keyFilter) && string.IsNullOrEmpty(valueFilter) &&
            string.IsNullOrEmpty(outputFilter) && string.IsNullOrEmpty(targetFilter) &&
            string.IsNullOrEmpty(inputFilter))
        {
            var hasColumnFilters = _filterTextBoxes.Values.Any(textBox => !string.IsNullOrEmpty(textBox.Text));
            if (!hasColumnFilters)
            {
                return _entities.ToList();
            }
        }

        return FilterEntities(keyFilter, valueFilter, isExactMatch, outputFilter, targetFilter, inputFilter);
    }
}

/// <summary>
///     Filter expression parser supporting AND/OR/NOT logic
///     Syntax: comma(,)=OR, plus(+)=AND, exclamation(!)=NOT
/// </summary>
public static class FilterExpressionParser
{
    public static bool MatchesExpression(string expression, Func<string, bool> valueMatchFunc)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        var orParts = expression.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var orPart in orParts)
        {
            if (EvaluateAndExpression(orPart.Trim(), valueMatchFunc))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EvaluateAndExpression(string andExpression, Func<string, bool> valueMatchFunc)
    {
        var andParts = andExpression.Split('+', StringSplitOptions.RemoveEmptyEntries);

        foreach (var andPart in andParts)
        {
            var part = andPart.Trim();
            var shouldMatch = true;

            if (part.StartsWith('!'))
            {
                shouldMatch = false;
                part = part.Substring(1).Trim();
            }

            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            var matches = valueMatchFunc(part);

            if (!shouldMatch)
            {
                matches = !matches;
            }

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }
}
