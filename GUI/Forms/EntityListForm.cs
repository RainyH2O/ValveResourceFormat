using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

public class EntityListForm : ThemedForm
{
    private static readonly List<(string ColumnName, int ColumnWidth)> DefaultColumns =
    [
        ("classname", 150),
        ("targetname", 150),
        ("spawnflags", 100),
        ("origin", 150),
        ("hammeruniqueid", 100)
    ];

    private readonly DataGridView entityDataGridView;
    private readonly Panel gridPanel;
    private readonly Dictionary<string, TextBox> _filterTextBoxes = [];
    private readonly TextBox keyTextBox;
    private readonly TextBox valueTextBox;
    private readonly CheckBox exactMatchCheckBox;
    private readonly TextBox outputTextBox;
    private readonly TextBox targetTextBox;
    private readonly TextBox inputTextBox;

    private List<(string ColumnName, int ColumnWidth)> _columnsToDisplay = [.. DefaultColumns];
    private HashSet<string> _customExportProperties = [];
    private DataTable? _dataTable;
    private List<EntityLump.Entity>? _entities;

    public event EventHandler<string>? OnEntityClicked;
    public event EventHandler<string>? OnEntityDoubleClicked;

#pragma warning disable CA2000 // Controls are transferred to their parent container's Controls collection and disposed with it
    public EntityListForm(List<EntityLump.Entity>? entities)
    {
        Text = "Entity List";
        ClientSize = new Size(720, 560);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterParent;

        SuspendLayout();

        // ── Data grid ──────────────────────────────────────────────────────
        entityDataGridView = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        entityDataGridView.CellClick += OnCellClick;
        entityDataGridView.CellDoubleClick += OnCellDoubleClick;

        // gridPanel holds the DataGridView (Fill, lower z-order) and the per-column
        // filter row (Top, higher z-order added later in AddFilterRowControls).
        gridPanel = new Panel { Dock = DockStyle.Fill };
        gridPanel.Controls.Add(entityDataGridView);

        // ── Filters group box ──────────────────────────────────────────────
        var filtersGroupBox = new GroupBox
        {
            Text = "Filters",
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 2, 6, 4),
        };

        var filtersInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
        };
        filtersInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        filtersInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        filtersInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        filtersInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Key = Value filter row
        var keyValueRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        keyValueRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        keyValueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        keyValueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        keyValueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
        keyValueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19F));

        keyTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Key" };
        keyTextBox.TextChanged += OnFilterPanelTextChanged;

        var equalLabel = new Label { Text = "=", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };

        valueTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Value" };
        valueTextBox.TextChanged += OnFilterPanelTextChanged;

        exactMatchCheckBox = new CheckBox { Text = "Exact", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        exactMatchCheckBox.CheckedChanged += OnFilterPanelTextChanged;

        keyValueRow.Controls.Add(keyTextBox, 0, 0);
        keyValueRow.Controls.Add(equalLabel, 1, 0);
        keyValueRow.Controls.Add(valueTextBox, 2, 0);
        keyValueRow.Controls.Add(exactMatchCheckBox, 3, 0);

        // Entity I/O filter row
        var ioRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        ioRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        ioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        ioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));

        outputTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Output" };
        outputTextBox.TextChanged += OnFilterPanelTextChanged;
        targetTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Target" };
        targetTextBox.TextChanged += OnFilterPanelTextChanged;
        inputTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Input" };
        inputTextBox.TextChanged += OnFilterPanelTextChanged;

        ioRow.Controls.Add(outputTextBox, 0, 0);
        ioRow.Controls.Add(targetTextBox, 1, 0);
        ioRow.Controls.Add(inputTextBox, 2, 0);

        var syntaxHint = new Label
        {
            Text = "Syntax: comma(,)=OR  plus(+)=AND  exclamation(!)=NOT",
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Microsoft Sans Serif", 7F, FontStyle.Italic),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 0, 2),
        };

        filtersInner.Controls.Add(keyValueRow, 0, 0);
        filtersInner.Controls.Add(ioRow, 0, 1);
        filtersInner.Controls.Add(syntaxHint, 0, 2);
        filtersGroupBox.Controls.Add(filtersInner);

        // ── Export button bar ──────────────────────────────────────────────
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 4, 4),
        };
        var exportButton = new ThemedButton { Text = "Export", Width = 80, Height = 24 };
        exportButton.Click += OnExportClick;
        buttonPanel.Controls.Add(exportButton);

        // ── Main layout ────────────────────────────────────────────────────
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // grid: fills remaining space
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // filters: auto-sizes to content
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // button bar: auto-sizes to content

        mainLayout.Controls.Add(gridPanel, 0, 0);
        mainLayout.Controls.Add(filtersGroupBox, 0, 1);
        mainLayout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(mainLayout);

        InitDataTable(entities);

        ResumeLayout(true);
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataTable?.Dispose();
            entityDataGridView.CellClick -= OnCellClick;
            entityDataGridView.CellDoubleClick -= OnCellDoubleClick;
            entityDataGridView.Dispose();
            gridPanel.Dispose();
            keyTextBox.Dispose();
            valueTextBox.Dispose();
            exactMatchCheckBox.Dispose();
            outputTextBox.Dispose();
            targetTextBox.Dispose();
            inputTextBox.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitDataTable(List<EntityLump.Entity>? entities)
    {
        foreach (var (columnName, columnWidth) in _columnsToDisplay)
        {
            entityDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = columnName,
                DataPropertyName = columnName,
                HeaderText = columnName,
                Width = columnWidth,
            });
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
            AutoSize = true,
        };

        filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.RowHeadersWidth));
        filterRowPanel.Controls.Add(new Label { Width = entityDataGridView.RowHeadersWidth });

        for (var i = 0; i < entityDataGridView.Columns.Count; i++)
        {
            filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.Columns[i].Width));
            var textBox = new ThemedTextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Tag = entityDataGridView.Columns[i].Name,
            };
            textBox.TextChanged += OnColumnFilterTextChanged;
            filterRowPanel.Controls.Add(textBox);
            _filterTextBoxes[entityDataGridView.Columns[i].Name] = textBox;
        }

        filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        filterRowPanel.Controls.Add(new Label { Width = 20 });

        // Add after entityDataGridView so filterRowPanel has higher z-order,
        // causing it to dock Top first while the grid fills the remaining space.
        gridPanel.Controls.Add(filterRowPanel);
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

        if (_entities == null)
        {
            return;
        }

        foreach (var entity in _entities)
        {
            var row = _dataTable.NewRow();
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                row[columnName] = entity.ContainsKey(columnName) ? entity.GetProperty(columnName).Value ?? "" : "";
            }
            _dataTable.Rows.Add(row);
        }

        entityDataGridView.AutoGenerateColumns = false;
        entityDataGridView.DataSource = _dataTable;
    }

    private void OnColumnFilterTextChanged(object? sender, EventArgs e)
    {
        var filterExpression = string.Empty;
        foreach (var textBox in _filterTextBoxes.Values.Where(tb => !string.IsNullOrEmpty(tb.Text)))
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

    private static string EscapeFilterValue(string filterValue)
    {
        const string lb = "~~LB~~";
        const string rb = "~~RB~~";
        filterValue = filterValue.Replace("[", lb).Replace("]", rb)
            .Replace("*", "[*]").Replace("%", "[%]").Replace("'", "''");
        filterValue = filterValue.Replace(lb, "[[]").Replace(rb, "[]]");
        return filterValue;
    }

    private void OnCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        var columnIndex = entityDataGridView.Columns["hammeruniqueid"]?.Index ?? -1;
        if (columnIndex < 0)
        {
            return;
        }

        var hammerUniqueId = entityDataGridView.Rows[e.RowIndex].Cells[columnIndex].Value?.ToString();
        if (!string.IsNullOrEmpty(hammerUniqueId))
        {
            OnEntityClicked?.Invoke(this, hammerUniqueId);
        }
    }

    private void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            OnEntityDoubleClicked?.Invoke(this, string.Empty);
        }
    }

    private void OnFilterPanelTextChanged(object? sender, EventArgs e)
    {
        var keyFilter = keyTextBox.Text;
        var valueFilter = valueTextBox.Text;
        var isExactMatch = exactMatchCheckBox.Checked;
        var outFilter = outputTextBox.Text;
        var tgtFilter = targetTextBox.Text;
        var inFilter = inputTextBox.Text;

        UpdateTableColumns(keyFilter, valueFilter, outFilter, tgtFilter, inFilter);

        var filteredEntities = FilterEntities(keyFilter, valueFilter, isExactMatch, outFilter, tgtFilter, inFilter);
        UpdateDataTable(filteredEntities, keyFilter, valueFilter, isExactMatch, outFilter, tgtFilter, inFilter);
    }

    private void UpdateTableColumns(string keyFilter, string valueFilter, string outputFilter, string targetFilter, string inputFilter)
    {
        var dynamicColumns = new List<(string Name, int Width, bool Show)>
        {
            ("Key", 150, !string.IsNullOrEmpty(keyFilter)),
            ("Value", 150, !string.IsNullOrEmpty(valueFilter)),
            ("Output", 150, !string.IsNullOrEmpty(outputFilter)),
            ("Target", 150, !string.IsNullOrEmpty(targetFilter)),
            ("Input", 150, !string.IsNullOrEmpty(inputFilter)),
        };

        var dynamicNames = dynamicColumns.Select(d => d.Name).ToHashSet();
        var staticColumns = _columnsToDisplay.Where(x => !dynamicNames.Contains(x.ColumnName)).ToList();
        var visibleDynamic = dynamicColumns.Where(x => x.Show).Select(x => (x.Name, x.Width)).ToList();

        _columnsToDisplay = [];

        var targetnameIndex = staticColumns.FindIndex(x => x.ColumnName == "targetname");
        if (targetnameIndex >= 0)
        {
            for (var i = 0; i <= targetnameIndex; i++)
            {
                _columnsToDisplay.Add(staticColumns[i]);
            }
            _columnsToDisplay.AddRange(visibleDynamic);
            for (var i = targetnameIndex + 1; i < staticColumns.Count; i++)
            {
                _columnsToDisplay.Add(staticColumns[i]);
            }
        }
        else
        {
            _columnsToDisplay.AddRange(staticColumns);
            _columnsToDisplay.AddRange(visibleDynamic);
        }

        entityDataGridView.Columns.Clear();
        foreach (var (columnName, columnWidth) in _columnsToDisplay)
        {
            entityDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = columnName,
                DataPropertyName = columnName,
                HeaderText = columnName,
                Width = columnWidth,
            });
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
                FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                    entity.Properties.Properties.Any(p =>
                    {
                        var keyMatches = isExactMatch
                            ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                            : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);
                        if (!keyMatches) return false;
                        return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                            isExactMatch
                                ? (p.Value.Value?.ToString() ?? "").Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                                : (p.Value.Value?.ToString() ?? "").Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
                    })));
        }
        else if (!string.IsNullOrEmpty(keyFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                    isExactMatch
                        ? entity.ContainsKey(keyTerm)
                        : entity.Properties.Properties.Any(p => p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase))));
        }
        else if (!string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                    entity.Properties.Properties.Any(p =>
                        isExactMatch
                            ? (p.Value.Value?.ToString() ?? "").Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                            : (p.Value.Value?.ToString() ?? "").Contains(valueTerm, StringComparison.OrdinalIgnoreCase))));
        }

        if (!string.IsNullOrEmpty(outputFilter) || !string.IsNullOrEmpty(targetFilter) || !string.IsNullOrEmpty(inputFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                entity.Connections?.Any(connection =>
                    (string.IsNullOrEmpty(outputFilter) || FilterExpressionParser.MatchesExpression(outputFilter, t =>
                        connection.GetStringProperty("m_outputName").Contains(t, StringComparison.OrdinalIgnoreCase))) &&
                    (string.IsNullOrEmpty(targetFilter) || FilterExpressionParser.MatchesExpression(targetFilter, t =>
                        connection.GetStringProperty("m_targetName").Contains(t, StringComparison.OrdinalIgnoreCase))) &&
                    (string.IsNullOrEmpty(inputFilter) || FilterExpressionParser.MatchesExpression(inputFilter, t =>
                        connection.GetStringProperty("m_inputName").Contains(t, StringComparison.OrdinalIgnoreCase)))
                ) == true);
        }

        return filteredEntities.ToList();
    }

    private void UpdateDataTable(List<EntityLump.Entity> filteredEntities, string keyFilter, string valueFilter,
        bool isExactMatch, string outputFilter, string targetFilter, string inputFilter)
    {
        _dataTable?.Clear();

        foreach (var entity in filteredEntities)
        {
            var row = _dataTable!.NewRow();
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                row[columnName] = columnName switch
                {
                    "Key" => GetMatchedKey(entity, keyFilter, valueFilter, isExactMatch),
                    "Value" => GetMatchedValue(entity, keyFilter, valueFilter, isExactMatch),
                    "Output" => FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter)?.GetStringProperty("m_outputName") ?? "",
                    "Target" => FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter)?.GetStringProperty("m_targetName") ?? "",
                    "Input" => FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter)?.GetStringProperty("m_inputName") ?? "",
                    _ => entity.ContainsKey(columnName) ? entity.GetProperty(columnName).Value ?? "" : "",
                };
            }
            _dataTable.Rows.Add(row);
        }

        entityDataGridView.DataSource = _dataTable;
    }

    private static string GetMatchedKey(EntityLump.Entity entity, string keyFilter, string valueFilter, bool isExactMatch)
    {
        if (string.IsNullOrEmpty(keyFilter))
        {
            return "";
        }

        var match = entity.Properties.Properties.FirstOrDefault(p =>
            FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
            {
                var keyMatches = isExactMatch
                    ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                    : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);
                if (!keyMatches || string.IsNullOrEmpty(valueFilter)) return keyMatches;
                return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                    isExactMatch
                        ? (p.Value.Value?.ToString() ?? "").Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                        : (p.Value.Value?.ToString() ?? "").Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
            }));
        return match.Key ?? "";
    }

    private static string GetMatchedValue(EntityLump.Entity entity, string keyFilter, string valueFilter, bool isExactMatch)
    {
        if (string.IsNullOrEmpty(keyFilter) && string.IsNullOrEmpty(valueFilter))
        {
            return "";
        }

        var match = entity.Properties.Properties.FirstOrDefault(p =>
        {
            if (!string.IsNullOrEmpty(keyFilter))
            {
                return FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                {
                    var keyMatches = isExactMatch
                        ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                        : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);
                    if (!keyMatches) return false;
                    if (string.IsNullOrEmpty(valueFilter)) return true;
                    return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                        isExactMatch
                            ? (p.Value.Value?.ToString() ?? "").Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                            : (p.Value.Value?.ToString() ?? "").Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
                });
            }
            return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                isExactMatch
                    ? (p.Value.Value?.ToString() ?? "").Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                    : (p.Value.Value?.ToString() ?? "").Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
        });
        return match.Value.Value?.ToString() ?? "";
    }

    private static KVObject? FindMatchedConnection(EntityLump.Entity entity, string outputFilter, string targetFilter, string inputFilter)
    {
        return entity.Connections?.FirstOrDefault(connection =>
            (string.IsNullOrEmpty(outputFilter) || FilterExpressionParser.MatchesExpression(outputFilter, t =>
                connection.GetStringProperty("m_outputName").Contains(t, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrEmpty(targetFilter) || FilterExpressionParser.MatchesExpression(targetFilter, t =>
                connection.GetStringProperty("m_targetName").Contains(t, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrEmpty(inputFilter) || FilterExpressionParser.MatchesExpression(inputFilter, t =>
                connection.GetStringProperty("m_inputName").Contains(t, StringComparison.OrdinalIgnoreCase))));
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        try
        {
            var filteredEntities = GetCurrentFilteredEntities();

            if (filteredEntities.Count == 0)
            {
                MessageBox.Show("No entities to export.", "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new PropertySelectionDialog(filteredEntities, _customExportProperties, _entities);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _customExportProperties = dialog.SelectedProperties;

            using var saveDialog = new SaveFileDialog
            {
                Title = "Choose save location",
                FileName = "entities.json",
                InitialDirectory = Settings.Config.SaveDirectory,
                DefaultExt = "json",
                Filter = "JSON files|*.json",
                AddToRecent = true,
            };

            if (saveDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var directory = Path.GetDirectoryName(saveDialog.FileName);
            if (directory != null)
            {
                Settings.Config.SaveDirectory = directory;
            }

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
        if (_entities == null || _dataTable == null)
        {
            return [];
        }

        var keyFilter = keyTextBox.Text;
        var valueFilter = valueTextBox.Text;
        var isExactMatch = exactMatchCheckBox.Checked;
        var outFilter = outputTextBox.Text;
        var tgtFilter = targetTextBox.Text;
        var inFilter = inputTextBox.Text;

        if (string.IsNullOrEmpty(keyFilter) && string.IsNullOrEmpty(valueFilter) &&
            string.IsNullOrEmpty(outFilter) && string.IsNullOrEmpty(tgtFilter) &&
            string.IsNullOrEmpty(inFilter))
        {
            var hasColumnFilters = _filterTextBoxes.Values.Any(tb => !string.IsNullOrEmpty(tb.Text));
            if (!hasColumnFilters)
            {
                return [.. _entities];
            }
        }

        return FilterEntities(keyFilter, valueFilter, isExactMatch, outFilter, tgtFilter, inFilter);
    }
}

/// <summary>
/// Filter expression parser supporting AND/OR/NOT logic.
/// Syntax: comma(,)=OR, plus(+)=AND, exclamation(!)=NOT.
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
                part = part[1..].Trim();
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
