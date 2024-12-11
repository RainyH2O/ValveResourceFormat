using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.IO;
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

    // Custom export properties configuration
    private HashSet<string> _customExportProperties = new();
    private DataTable? _dataTable;
    private List<EntityLump.Entity>? _entities;
    private bool _useCustomProperties;

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
    ///     <para>
    ///         If a pattern in a LIKE clause contains any of these special characters * % [ ], those characters must be
    ///         escaped in brackets [ ] like this [*], [%], [[] or []].
    ///     </para>
    ///     <para>
    ///         If the pattern is not in a like clause then you can pass valueIsForLIKEcomparison = false to not escape
    ///         brackets.
    ///     </para>
    ///     <para>Examples:</para>
    ///     <para>- strFilter = "[Something] LIKE '%" + DataTableHelper.EscapeLikeValue(filterValue) + "%'";</para>
    ///     <para></para>
    ///     <para>http://www.csharp-examples.net/dataview-rowfilter/</para>
    /// </summary>
    /// <param name="filterValue">
    ///     LIKE filterValue. This should not be the entire filter string... just the part that is being
    ///     compared.
    /// </param>
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

        // Filter by key-value pair
        if (!string.IsNullOrEmpty(keyFilter) && !string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
            {
                return entity.Properties.Properties.Any(p =>
                {
                    var keyMatches = isExactMatch
                        ? p.Key.Equals(keyFilter, StringComparison.OrdinalIgnoreCase)
                        : p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase);

                    if (!keyMatches)
                    {
                        return false;
                    }

                    var valueMatches = isExactMatch
                        ? p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase)
                        : p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase);

                    return valueMatches;
                });
            });
        }
        else if (!string.IsNullOrEmpty(keyFilter))
        {
            // Filter by key
            filteredEntities = filteredEntities.Where(entity =>
            {
                return isExactMatch
                    ? entity.ContainsKey(keyFilter)
                    : entity.Properties.Properties.Any(p =>
                        p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase));
            });
        }
        else if (!string.IsNullOrEmpty(valueFilter))
        {
            // Filter by value
            filteredEntities = filteredEntities.Where(entity =>
            {
                return isExactMatch
                    ? entity.Properties.Properties.Any(p =>
                        p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase))
                    : entity.Properties.Properties.Any(p =>
                        p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase));
            });
        }

        // Filter by connection (Output-Target-Input relationship)
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
                                        connection.GetStringProperty("m_outputName").Contains(outputFilter,
                                            StringComparison.OrdinalIgnoreCase);

                    var targetMatches = string.IsNullOrEmpty(targetFilter) ||
                                        connection.GetStringProperty("m_targetName").Contains(targetFilter,
                                            StringComparison.OrdinalIgnoreCase);

                    var inputMatches = string.IsNullOrEmpty(inputFilter) ||
                                       connection.GetStringProperty("m_inputName").Contains(inputFilter,
                                           StringComparison.OrdinalIgnoreCase);

                    return outputMatches && targetMatches && inputMatches;
                });
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
                    if (!string.IsNullOrEmpty(keyFilter) && !string.IsNullOrEmpty(valueFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                        {
                            var keyMatches = isExactMatch
                                ? p.Key.Equals(keyFilter, StringComparison.OrdinalIgnoreCase)
                                : p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase);

                            if (!keyMatches)
                            {
                                return false;
                            }

                            var valueMatches = isExactMatch
                                ? p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase)
                                : p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase);

                            return valueMatches;
                        });
                        row![columnName] = matchedProperty.Key ?? "";
                    }
                    else if (!string.IsNullOrEmpty(keyFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                            isExactMatch
                                ? p.Key.Equals(keyFilter, StringComparison.OrdinalIgnoreCase)
                                : p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase));
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
                            var keyMatches = isExactMatch
                                ? p.Key.Equals(keyFilter, StringComparison.OrdinalIgnoreCase)
                                : p.Key.Contains(keyFilter, StringComparison.OrdinalIgnoreCase);

                            if (!keyMatches)
                            {
                                return false;
                            }

                            var valueMatches = isExactMatch
                                ? p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase)
                                : p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase);

                            return valueMatches;
                        });
                        row![columnName] = matchedProperty.Value.Value?.ToString() ?? "";
                    }
                    else if (!string.IsNullOrEmpty(valueFilter))
                    {
                        var matchedProperty = entity.Properties.Properties.FirstOrDefault(p =>
                            isExactMatch
                                ? p.Value.Value!.ToString()!.Equals(valueFilter, StringComparison.OrdinalIgnoreCase)
                                : p.Value.Value!.ToString()!.Contains(valueFilter, StringComparison.OrdinalIgnoreCase));
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
                                connection.GetStringProperty("m_outputName")
                                    .Contains(outputFilter, StringComparison.OrdinalIgnoreCase);

            var targetMatches = string.IsNullOrEmpty(targetFilter) ||
                                connection.GetStringProperty("m_targetName")
                                    .Contains(targetFilter, StringComparison.OrdinalIgnoreCase);

            var inputMatches = string.IsNullOrEmpty(inputFilter) ||
                               connection.GetStringProperty("m_inputName")
                                   .Contains(inputFilter, StringComparison.OrdinalIgnoreCase);

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
                new PropertySelectionDialog(filteredEntities, _customExportProperties, _useCustomProperties);
            var dialogResult = dialog.ShowDialog(this);
            if (dialogResult != DialogResult.OK)
            {
                return;
            }

            var exportType = dialog.SelectedExportType;
            var includeRelated = dialog.IncludeRelatedEntities;
            var selectedProperties = dialog.SelectedProperties;
            var entitiesToExport = includeRelated ? GetEntitiesWithRelated(filteredEntities) : filteredEntities;

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

            string entitiesJson;
            switch (exportType)
            {
                case PropertySelectionDialog.ExportType.Full:
                    entitiesJson = MapExtract.SerializeEntities(entitiesToExport);
                    break;
                case PropertySelectionDialog.ExportType.Minimal:
                case PropertySelectionDialog.ExportType.Custom:
                    _customExportProperties = selectedProperties;
                    _useCustomProperties = true;
                    entitiesJson = SerializeEntitiesMinimal(entitiesToExport);
                    break;
                default:
                    entitiesJson = MapExtract.SerializeEntities(entitiesToExport);
                    break;
            }

            File.WriteAllText(saveDialog.FileName, entitiesJson);

            var exportTypeText = exportType switch
            {
                PropertySelectionDialog.ExportType.Full => "full",
                PropertySelectionDialog.ExportType.Minimal => "minimal",
                PropertySelectionDialog.ExportType.Custom => "custom",
                _ => "unknown"
            };

            MessageBox.Show(
                $"Successfully exported {entitiesToExport.Count} entities ({exportTypeText}) to {Path.GetFileName(saveDialog.FileName)}",
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


    private List<EntityLump.Entity> GetEntitiesWithRelated(List<EntityLump.Entity> filteredEntities)
    {
        if (_entities == null)
        {
            return filteredEntities;
        }

        var visitedEntities = new HashSet<EntityLump.Entity>();
        var resultEntities = new List<EntityLump.Entity>();

        foreach (var entity in filteredEntities)
        {
            if (!visitedEntities.Contains(entity))
            {
                resultEntities.Add(entity);
                visitedEntities.Add(entity);
            }
        }

        foreach (var entity in filteredEntities)
        {
            FindRelatedEntities(entity, _entities, visitedEntities, resultEntities);
        }

        return resultEntities;
    }

    private static void FindRelatedEntities(EntityLump.Entity rootEntity, List<EntityLump.Entity> allEntities,
        HashSet<EntityLump.Entity> visitedEntities, List<EntityLump.Entity> resultEntities)
    {
        if (rootEntity.Connections == null)
        {
            return;
        }

        var namedEntities = allEntities
            .Where(entity => entity.Properties.Properties.TryGetValue("targetname", out var kvValue) &&
                             kvValue.Value != null)
            .ToList();

        foreach (var connection in rootEntity.Connections)
        {
            if (connection.Properties.TryGetValue("m_targetName", out var targetNameValue) &&
                targetNameValue.Value != null)
            {
                var targetName = targetNameValue.Value.ToString();

                foreach (var entity in namedEntities)
                {
                    if (entity.Properties.Properties.TryGetValue("targetname", out var entityTargetName) &&
                        entityTargetName.Value?.ToString() == targetName &&
                        entity != rootEntity && !visitedEntities.Contains(entity))
                    {
                        resultEntities.Add(entity);
                        visitedEntities.Add(entity);

                        FindRelatedEntities(entity, allEntities, visitedEntities, resultEntities);
                    }
                }
            }
        }

        if (rootEntity.Properties.Properties.TryGetValue("targetname", out var rootTargetName) &&
            rootTargetName.Value != null)
        {
            var rootName = rootTargetName.Value.ToString();

            foreach (var entity in allEntities)
            {
                if (entity.Connections != null && entity != rootEntity && !visitedEntities.Contains(entity))
                {
                    var hasConnectionToRoot = entity.Connections.Any(conn =>
                        conn.Properties.TryGetValue("m_targetName", out var targetValue) &&
                        targetValue.Value?.ToString() == rootName);

                    if (hasConnectionToRoot)
                    {
                        resultEntities.Add(entity);
                        visitedEntities.Add(entity);

                        FindRelatedEntities(entity, allEntities, visitedEntities, resultEntities);
                    }
                }
            }
        }
    }

    private string SerializeEntitiesMinimal(List<EntityLump.Entity> entities, HashSet<string>? customProperties = null)
    {
        var essentialProperties = new HashSet<string> { "classname", "targetname" };
        var essentialConnectionProps = new HashSet<string>
            { "m_outputName", "m_targetName", "m_inputName", "m_overrideParam", "m_flDelay", "m_nTimesToFire" };

        var filteredEntities = entities.Select(entity =>
        {
            var entityDict = new Dictionary<string, object>();
            var classname = entity.GetProperty<string>("classname") ?? "";
            var allowedProperties = _useCustomProperties && _customExportProperties.Count > 0
                ? _customExportProperties
                : essentialProperties.Union(PropertySelectionDialog.GetAdditionalPropertiesForClassname(classname))
                    .ToHashSet();

            foreach (var (key, value) in entity.Properties.Properties)
            {
                if (allowedProperties.Contains(key) && value.Value != null)
                {
                    entityDict[key] = ConvertEntityValue(value.Value);
                }
            }

            if (entity.Connections?.Count > 0)
            {
                var connections = entity.Connections
                    .Select(conn => FilterConnection(conn, essentialConnectionProps))
                    .Where(filteredConn => filteredConn.Count > 0)
                    .Cast<object>()
                    .ToList();

                if (connections.Count > 0)
                    entityDict["connections"] = connections;
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

    private static Dictionary<string, object> FilterConnection(KVObject connection, HashSet<string> allowedProps)
    {
        var filteredConnection = new Dictionary<string, object>();

        foreach (var (key, kvValue) in connection.Properties)
        {
            if (allowedProps.Contains(key) && kvValue.Value != null)
            {
                filteredConnection[key] = ConvertEntityValue(kvValue.Value);
            }
        }

        return filteredConnection;
    }


}
