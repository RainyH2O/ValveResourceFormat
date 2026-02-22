using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

public class EntityListForm : ThemedForm
{
    private const string EntityColumn = "__Entity";

    private static readonly List<(string ColumnName, int ColumnWidth)> DefaultColumns =
    [
        ("classname", 150),
        ("targetname", 150),
        ("spawnflags", 105),
        ("origin", 165),
        ("hammeruniqueid", 130)
    ];

    private readonly DataGridView entityDataGridView;
    private readonly Panel gridPanel;
    private readonly Dictionary<string, TextBox> _filterTextBoxes = [];
    private TableLayoutPanel? _filterRowPanel;
    private readonly TextBox keyTextBox;
    private readonly TextBox valueTextBox;
    private readonly CheckBox exactMatchCheckBox;
    private readonly TextBox outputTextBox;
    private readonly TextBox targetTextBox;
    private readonly TextBox inputTextBox;
    private readonly CheckBox syncMapSelectionCheckBox;
    private readonly string DefaultExportFileName;
    private readonly Predicate<EntityLump.Entity>? canExportEntity;

    private List<(string ColumnName, int ColumnWidth)> _columnsToDisplay = [.. DefaultColumns];
    private HashSet<string> _customExportProperties = [];
    private DataTable? _dataTable;
    private List<EntityLump.Entity>? _entities;
    private bool synchronizingSelection;

    public string ExportPath { get; private set; }

    public event EventHandler<IReadOnlyList<EntityLump.Entity>>? OnEntitySelectionChanged;
    public event EventHandler<EntityLump.Entity>? OnEntityDoubleClicked;
    public event EventHandler<EntityLump.Entity>? OnEntityInfoRequested;
    public event EventHandler? OnMapSelectionSyncRequested;

#pragma warning disable CA2000 // Controls are transferred to their parent container's Controls collection and disposed with it
    public EntityListForm(List<EntityLump.Entity>? entities, string defaultExportFileName, string exportPath, Predicate<EntityLump.Entity>? canExportEntity = null)
    {
        DefaultExportFileName = defaultExportFileName;
        ExportPath = exportPath;
        this.canExportEntity = canExportEntity;
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
            MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        entityDataGridView.CellDoubleClick += OnCellDoubleClick;
        entityDataGridView.KeyDown += OnGridKeyDown;
        entityDataGridView.SelectionChanged += OnSelectionChanged;

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
        syncMapSelectionCheckBox = new CheckBox
        {
            Text = "Sync MAP selection",
            AutoSize = true,
            Margin = new Padding(8, 4, 4, 0),
        };
        syncMapSelectionCheckBox.CheckedChanged += OnSyncMapSelectionCheckedChanged;
        buttonPanel.Controls.Add(exportButton);
        buttonPanel.Controls.Add(syncMapSelectionCheckBox);

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
        entityDataGridView.ClearSelection();

        ResumeLayout(true);
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataTable?.Dispose();
            _filterRowPanel?.Dispose();
            entityDataGridView.CellDoubleClick -= OnCellDoubleClick;
            entityDataGridView.ColumnWidthChanged -= OnGridColumnWidthChanged;
            entityDataGridView.KeyDown -= OnGridKeyDown;
            entityDataGridView.SelectionChanged -= OnSelectionChanged;
            entityDataGridView.Dispose();
            gridPanel.Dispose();
            keyTextBox.Dispose();
            valueTextBox.Dispose();
            exactMatchCheckBox.Dispose();
            outputTextBox.Dispose();
            targetTextBox.Dispose();
            inputTextBox.Dispose();
            syncMapSelectionCheckBox.CheckedChanged -= OnSyncMapSelectionCheckedChanged;
            syncMapSelectionCheckBox.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Return) && entityDataGridView.CurrentRow is { } row && GetEntityAtRow(row.Index) is { } entity)
        {
            OnEntityInfoRequested?.Invoke(this, entity);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void InitDataTable(List<EntityLump.Entity>? entities)
    {
        foreach (var (columnName, _) in _columnsToDisplay)
        {
            entityDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = columnName,
                DataPropertyName = columnName,
                HeaderText = columnName,
                MinimumWidth = 50,
            });
        }

        AddFilterRowControls();
        BindData(entities);
    }

    private void AddFilterRowControls()
    {
        _filterTextBoxes.Clear();

        _filterRowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = entityDataGridView.Columns.Count + 2,
            RowCount = 1,
            AutoSize = true,
        };

        _filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.RowHeadersWidth));
        _filterRowPanel.Controls.Add(new Label { Width = entityDataGridView.RowHeadersWidth });

        for (var i = 0; i < entityDataGridView.Columns.Count; i++)
        {
            _filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, entityDataGridView.Columns[i].Width));
            var textBox = new ThemedTextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Tag = entityDataGridView.Columns[i].Name,
            };
            textBox.TextChanged += OnColumnFilterTextChanged;
            _filterRowPanel.Controls.Add(textBox);
            _filterTextBoxes[entityDataGridView.Columns[i].Name] = textBox;
        }

        _filterRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        _filterRowPanel.Controls.Add(new Label { Width = 20 });

        // Add after entityDataGridView so _filterRowPanel has higher z-order,
        // causing it to dock Top first while the grid fills the remaining space.
        gridPanel.Controls.Add(_filterRowPanel);

        entityDataGridView.ColumnWidthChanged += OnGridColumnWidthChanged;
    }

    private void OnGridColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
    {
        if (_filterRowPanel == null)
        {
            return;
        }

        var styleIndex = e.Column.Index + 1; // index 0 is the RowHeader placeholder
        if (styleIndex < _filterRowPanel.ColumnStyles.Count)
        {
            _filterRowPanel.ColumnStyles[styleIndex] = new ColumnStyle(SizeType.Absolute, e.Column.Width);
        }
    }

    private void SyncFilterRowToColumnWidths()
    {
        if (_filterRowPanel == null)
        {
            return;
        }

        for (var i = 0; i < entityDataGridView.Columns.Count; i++)
        {
            var styleIndex = i + 1; // index 0 is the RowHeader placeholder
            if (styleIndex < _filterRowPanel.ColumnStyles.Count)
            {
                _filterRowPanel.ColumnStyles[styleIndex] =
                    new ColumnStyle(SizeType.Absolute, entityDataGridView.Columns[i].Width);
            }
        }
    }

    private void BindData(List<EntityLump.Entity>? entities)
    {
        _entities = entities;
        _dataTable = new DataTable();
        _dataTable.Columns.Add(EntityColumn, typeof(EntityLump.Entity));

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
            row[EntityColumn] = entity;
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                row[columnName] = entity.ContainsKey(columnName) ? FormatEntityValue(entity[columnName]) : "";
            }
            _dataTable.Rows.Add(row);
        }

        entityDataGridView.AutoGenerateColumns = false;
        entityDataGridView.DataSource = _dataTable;
        entityDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
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

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.A)
        {
            return;
        }

        entityDataGridView.SelectAll();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (synchronizingSelection)
        {
            return;
        }

        var selectedEntities = entityDataGridView.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => GetEntityAtRow(row.Index))
            .OfType<EntityLump.Entity>()
            .ToArray();

        OnEntitySelectionChanged?.Invoke(this, selectedEntities);
    }

    public void SelectEntities(IReadOnlyCollection<EntityLump.Entity> entities)
    {
        if (!syncMapSelectionCheckBox.Checked)
        {
            return;
        }

        var selectedEntities = entities.ToHashSet();

        synchronizingSelection = true;
        try
        {
            entityDataGridView.ClearSelection();
            var firstSelectedRowIndex = -1;
            DataGridViewRow? firstSelectedRow = null;
            var rowsToSelect = new List<DataGridViewRow>();

            foreach (DataGridViewRow row in entityDataGridView.Rows)
            {
                if (GetEntityAtRow(row.Index) is { } entity && selectedEntities.Contains(entity))
                {
                    rowsToSelect.Add(row);
                    firstSelectedRowIndex = firstSelectedRowIndex < 0 ? row.Index : firstSelectedRowIndex;
                    firstSelectedRow ??= row;
                }
            }

            if (firstSelectedRow != null)
            {
                entityDataGridView.CurrentCell = firstSelectedRow.Cells[0];

                foreach (var row in rowsToSelect)
                {
                    row.Selected = true;
                }

                entityDataGridView.FirstDisplayedScrollingRowIndex = firstSelectedRowIndex;
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    public void SetEntities(List<EntityLump.Entity> entities)
    {
        var selectedEntities = entityDataGridView.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => GetEntityAtRow(row.Index))
            .OfType<EntityLump.Entity>()
            .ToHashSet();
        var columnFilters = _filterTextBoxes.ToDictionary(static item => item.Key, static item => item.Value.Text);

        synchronizingSelection = true;
        try
        {
            _entities = entities;
            OnFilterPanelTextChanged(this, EventArgs.Empty);

            foreach (var (columnName, filter) in columnFilters)
            {
                if (_filterTextBoxes.TryGetValue(columnName, out var textBox))
                {
                    textBox.Text = filter;
                }
            }

            OnColumnFilterTextChanged(this, EventArgs.Empty);
            entityDataGridView.ClearSelection();
            foreach (DataGridViewRow row in entityDataGridView.Rows)
            {
                if (GetEntityAtRow(row.Index) is { } entity && selectedEntities.Contains(entity))
                {
                    row.Selected = true;
                }
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    private void OnSyncMapSelectionCheckedChanged(object? sender, EventArgs e)
    {
        if (syncMapSelectionCheckBox.Checked)
        {
            OnMapSelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && GetEntityAtRow(e.RowIndex) is { } entity)
        {
            OnEntityDoubleClicked?.Invoke(this, entity);
        }
    }

    private EntityLump.Entity? GetEntityAtRow(int rowIndex)
    {
        if (_entities == null || entityDataGridView.Rows[rowIndex].DataBoundItem is not DataRowView rowView)
        {
            return null;
        }

        return rowView.Row.Field<EntityLump.Entity>(EntityColumn);
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

        entityDataGridView.ColumnWidthChanged -= OnGridColumnWidthChanged;
        entityDataGridView.Columns.Clear();
        foreach (var (columnName, _) in _columnsToDisplay)
        {
            entityDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = columnName,
                DataPropertyName = columnName,
                HeaderText = columnName,
                MinimumWidth = 50,
            });
        }

        foreach (var (columnName, _) in _columnsToDisplay)
        {
            if (!_dataTable!.Columns.Contains(columnName))
            {
                _dataTable.Columns.Add(columnName);
            }
        }

        // Rebuild the filter row since column count may have changed
        if (_filterRowPanel != null)
        {
            gridPanel.Controls.Remove(_filterRowPanel);
            _filterRowPanel.Dispose();
            _filterRowPanel = null;
        }

        AddFilterRowControls();
    }

    private List<EntityLump.Entity> FilterEntities(string keyFilter, string valueFilter, bool isExactMatch,
        string outputFilter, string targetFilter, string inputFilter)
    {
        var filteredEntities = _entities!.AsEnumerable();

        if (!string.IsNullOrEmpty(keyFilter) && !string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                    entity.Children.Any(p =>
                    {
                        var keyMatches = isExactMatch
                            ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                            : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);
                        if (!keyMatches) return false;
                        return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                            isExactMatch
                                ? FormatEntityValue(p.Value).Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                                : FormatEntityValue(p.Value).Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
                    })));
        }
        else if (!string.IsNullOrEmpty(keyFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
                    isExactMatch
                        ? entity.ContainsKey(keyTerm)
                        : entity.Children.Any(p => p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase))));
        }
        else if (!string.IsNullOrEmpty(valueFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                    entity.Children.Any(p =>
                        isExactMatch
                            ? FormatEntityValue(p.Value).Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                            : FormatEntityValue(p.Value).Contains(valueTerm, StringComparison.OrdinalIgnoreCase))));
        }

        if (!string.IsNullOrEmpty(outputFilter) || !string.IsNullOrEmpty(targetFilter) || !string.IsNullOrEmpty(inputFilter))
        {
            filteredEntities = filteredEntities.Where(entity =>
                entity.Connections?.Any(connection =>
                    (string.IsNullOrEmpty(outputFilter) || FilterExpressionParser.MatchesExpression(outputFilter, t =>
                        connection.OutputName.Contains(t, StringComparison.OrdinalIgnoreCase))) &&
                    (string.IsNullOrEmpty(targetFilter) || FilterExpressionParser.MatchesExpression(targetFilter, t =>
                        connection.TargetName.Contains(t, StringComparison.OrdinalIgnoreCase))) &&
                    (string.IsNullOrEmpty(inputFilter) || FilterExpressionParser.MatchesExpression(inputFilter, t =>
                        connection.InputName.Contains(t, StringComparison.OrdinalIgnoreCase)))
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
            row[EntityColumn] = entity;
            foreach (var (columnName, _) in _columnsToDisplay)
            {
                row[columnName] = columnName switch
                {
                    "Key" => GetMatchedKey(entity, keyFilter, valueFilter, isExactMatch),
                    "Value" => GetMatchedValue(entity, keyFilter, valueFilter, isExactMatch),
                    "Output" => FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter)?.OutputName ?? "",
                    "Target" => FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter)?.TargetName ?? "",
                    "Input" => FindMatchedConnection(entity, outputFilter, targetFilter, inputFilter)?.InputName ?? "",
                    _ => entity.ContainsKey(columnName) ? FormatEntityValue(entity[columnName]) : "",
                };
            }
            _dataTable.Rows.Add(row);
        }

        entityDataGridView.DataSource = _dataTable;
        entityDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
    }

    private static string GetMatchedKey(EntityLump.Entity entity, string keyFilter, string valueFilter, bool isExactMatch)
    {
        if (string.IsNullOrEmpty(keyFilter))
        {
            return "";
        }

        var match = entity.Children.FirstOrDefault(p =>
            FilterExpressionParser.MatchesExpression(keyFilter, keyTerm =>
            {
                var keyMatches = isExactMatch
                    ? p.Key.Equals(keyTerm, StringComparison.OrdinalIgnoreCase)
                    : p.Key.Contains(keyTerm, StringComparison.OrdinalIgnoreCase);
                if (!keyMatches || string.IsNullOrEmpty(valueFilter)) return keyMatches;
                return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                    isExactMatch
                        ? FormatEntityValue(p.Value).Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                        : FormatEntityValue(p.Value).Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
            }));
        return match.Key ?? "";
    }

    private static string GetMatchedValue(EntityLump.Entity entity, string keyFilter, string valueFilter, bool isExactMatch)
    {
        if (string.IsNullOrEmpty(keyFilter) && string.IsNullOrEmpty(valueFilter))
        {
            return "";
        }

        var match = entity.Children.FirstOrDefault(p =>
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
                            ? FormatEntityValue(p.Value).Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                            : FormatEntityValue(p.Value).Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
                });
            }
            return FilterExpressionParser.MatchesExpression(valueFilter, valueTerm =>
                isExactMatch
                    ? FormatEntityValue(p.Value).Equals(valueTerm, StringComparison.OrdinalIgnoreCase)
                    : FormatEntityValue(p.Value).Contains(valueTerm, StringComparison.OrdinalIgnoreCase));
        });
        return match.Value == null ? "" : FormatEntityValue(match.Value);
    }

    private static string FormatEntityValue(KVObject value)
    {
        return value.IsArray
            ? string.Join(" ", value.Values.Select(FormatEntityValue))
            : value.ToString();
    }

    private static EntityLump.Connection? FindMatchedConnection(EntityLump.Entity entity, string outputFilter, string targetFilter, string inputFilter)
    {
        return entity.Connections?.FirstOrDefault(connection =>
            (string.IsNullOrEmpty(outputFilter) || FilterExpressionParser.MatchesExpression(outputFilter, t =>
                connection.OutputName.Contains(t, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrEmpty(targetFilter) || FilterExpressionParser.MatchesExpression(targetFilter, t =>
                connection.TargetName.Contains(t, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrEmpty(inputFilter) || FilterExpressionParser.MatchesExpression(inputFilter, t =>
                connection.InputName.Contains(t, StringComparison.OrdinalIgnoreCase))));
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        try
        {
            var filteredEntities = GetCurrentFilteredEntities();
            if (canExportEntity != null)
            {
                filteredEntities.RemoveAll(entity => !canExportEntity(entity));
            }

            if (filteredEntities.Count == 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync("No entities to export.", "Export Failed", MessageIcon.Warning);
                return;
            }

            var allExportableEntities = canExportEntity == null ? _entities : _entities?.Where(entity => canExportEntity(entity)).ToList();
            using var dialog = new PropertySelectionDialog(filteredEntities, _customExportProperties, allExportableEntities);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _customExportProperties = dialog.SelectedProperties;

            using var previewForm = new JsonPreviewForm(dialog.ExportedJson, dialog.FinalEntityList.Count, DefaultExportFileName, ExportPath);
            previewForm.ShowDialog(this);
            ExportPath = previewForm.ExportPath;
        }
        catch (Exception ex)
        {
            _ = AppMessageDialogs.ShowMessageAsync($"Export failed: {ex.Message}", "Error", MessageIcon.Error);
        }
    }

    private List<EntityLump.Entity> GetCurrentFilteredEntities()
    {
        if (_entities == null || _dataTable == null)
        {
            return [];
        }

        return _dataTable.DefaultView
            .Cast<DataRowView>()
            .Select(row => row.Row.Field<EntityLump.Entity>(EntityColumn)!)
            .ToList();
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
