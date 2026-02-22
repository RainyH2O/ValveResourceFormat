using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms;

internal sealed class CoordinateMarkerForm : ThemedForm
{
    private readonly CoordinateMarkerSession session;
    private readonly Func<string, string, string?> addMarkers;
    private readonly Action<CoordinateMarkerSession.Marker, string> renameMarker;
    private readonly Action<IReadOnlyCollection<CoordinateMarkerSession.Marker>> removeMarkers;
    private readonly Action clearMarkers;
    private readonly ThemedTextBox nameTextBox;
    private readonly ThemedTextBox coordinatesTextBox;
    private readonly ThemedTextBox renameTextBox;
    private readonly DataGridView markerGrid;

#pragma warning disable CA2000 // Controls are transferred to parent containers and disposed with the form
    public CoordinateMarkerForm(
        CoordinateMarkerSession session,
        Func<string, string, string?> addMarkers,
        Action<CoordinateMarkerSession.Marker, string> renameMarker,
        Action<IReadOnlyCollection<CoordinateMarkerSession.Marker>> removeMarkers,
        Action clearMarkers)
    {
        this.session = session;
        this.addMarkers = addMarkers;
        this.renameMarker = renameMarker;
        this.removeMarkers = removeMarkers;
        this.clearMarkers = clearMarkers;

        Text = "Coordinate Markers";
        ClientSize = new Size(620, 560);
        MinimumSize = new Size(520, 440);
        StartPosition = FormStartPosition.CenterParent;

        var inputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(8),
        };
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        inputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        inputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        nameTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Optional name" };
        coordinatesTextBox = new ThemedTextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            PlaceholderText = "One coordinate per line: X Y Z",
        };
        var addButton = new ThemedButton { Text = "Add", AutoSize = true, Anchor = AnchorStyles.Right };
        addButton.Click += OnAddClick;

        inputLayout.Controls.Add(new Label { Text = "Name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        inputLayout.Controls.Add(nameTextBox, 1, 0);
        inputLayout.Controls.Add(new Label { Text = "Coordinates", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top }, 0, 1);
        inputLayout.Controls.Add(coordinatesTextBox, 1, 1);
        inputLayout.Controls.Add(addButton, 1, 2);

        markerGrid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Dock = DockStyle.Fill,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        markerGrid.Columns.Add("Name", "Name");
        markerGrid.Columns.Add("X", "X");
        markerGrid.Columns.Add("Y", "Y");
        markerGrid.Columns.Add("Z", "Z");
        markerGrid.SelectionChanged += OnMarkerSelectionChanged;

        var manageBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4),
            WrapContents = false,
        };
        renameTextBox = new ThemedTextBox { Width = 190, PlaceholderText = "New name" };
        var renameButton = new ThemedButton { Text = "Rename", AutoSize = true };
        renameButton.Click += OnRenameClick;
        var deleteButton = new ThemedButton { Text = "Delete selected", AutoSize = true };
        deleteButton.Click += OnDeleteClick;
        var clearButton = new ThemedButton { Text = "Clear all", AutoSize = true };
        clearButton.Click += OnClearClick;
        manageBar.Controls.Add(renameTextBox);
        manageBar.Controls.Add(renameButton);
        manageBar.Controls.Add(deleteButton);
        manageBar.Controls.Add(clearButton);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 210,
        };
        split.Panel1.Controls.Add(inputLayout);

        var lowerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        lowerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        lowerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        lowerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lowerLayout.Controls.Add(markerGrid, 0, 0);
        lowerLayout.Controls.Add(manageBar, 0, 1);
        split.Panel2.Controls.Add(lowerLayout);
        Controls.Add(split);

        RefreshMarkers();
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            nameTextBox.Dispose();
            coordinatesTextBox.Dispose();
            renameTextBox.Dispose();
            markerGrid.Dispose();
        }

        base.Dispose(disposing);
    }

    public void RefreshMarkers()
    {
        var selected = GetSelectedMarkers().ToHashSet();

        markerGrid.Rows.Clear();
        foreach (var marker in session.Markers)
        {
            var rowIndex = markerGrid.Rows.Add(
                marker.Entity.GetStringProperty("targetname"),
                marker.Position.X.ToString("R", CultureInfo.InvariantCulture),
                marker.Position.Y.ToString("R", CultureInfo.InvariantCulture),
                marker.Position.Z.ToString("R", CultureInfo.InvariantCulture));
            var row = markerGrid.Rows[rowIndex];
            row.Tag = marker;
        }

        markerGrid.ClearSelection();
        foreach (DataGridViewRow row in markerGrid.Rows)
        {
            row.Selected = row.Tag is CoordinateMarkerSession.Marker marker && selected.Contains(marker);
        }
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var error = addMarkers(nameTextBox.Text, coordinatesTextBox.Text);
        if (error != null)
        {
            _ = AppMessageDialogs.ShowMessageAsync(error, "Invalid Coordinates", MessageIcon.Warning);
            return;
        }

        coordinatesTextBox.Clear();
        RefreshMarkers();
    }

    private void OnMarkerSelectionChanged(object? sender, EventArgs e)
    {
        if (GetSelectedMarkers() is [var marker])
        {
            renameTextBox.Text = marker.Entity.GetStringProperty("targetname");
        }
    }

    private void OnRenameClick(object? sender, EventArgs e)
    {
        if (GetSelectedMarkers() is not [var marker])
        {
            _ = AppMessageDialogs.ShowMessageAsync("Select exactly one marker to rename.", "Rename Marker", MessageIcon.Warning);
            return;
        }

        renameMarker(marker, renameTextBox.Text);
        RefreshMarkers();
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedMarkers();
        if (selected.Count == 0)
        {
            return;
        }

        removeMarkers(selected);
        RefreshMarkers();
    }

    private async void OnClearClick(object? sender, EventArgs e)
    {
        if (session.Markers.Count == 0
            || !await AppMessageDialogs.ConfirmAsync("Remove all coordinate markers?", "Clear Coordinate Markers", buttons: ConfirmButtons.YesNo).ConfigureAwait(true))
        {
            return;
        }

        clearMarkers();
        RefreshMarkers();
    }

    private List<CoordinateMarkerSession.Marker> GetSelectedMarkers()
        => markerGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(static row => row.Tag)
            .OfType<CoordinateMarkerSession.Marker>()
            .ToList();
}
