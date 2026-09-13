using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using GUI.Controls;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

internal sealed class EntityMoveForm : ThemedForm
{
    private readonly Label TargetLabel;
    private readonly Label CurrentOriginLabel;
    private readonly Label DescendantsLabel;
    private readonly Label HistoryLabel;
    private readonly Label ErrorLabel;
    private readonly ThemedTextBox OriginTextBox;
    private readonly CheckBox MoveDescendantsCheckBox;
    private readonly ThemedButton MoveButton;
    private readonly ThemedButton UndoButton;
    private readonly ThemedButton RedoButton;
    private readonly ThemedButton FocusButton;
    private bool HasTarget;

#pragma warning disable CA2000 // Controls are owned by their parent containers.
    public EntityMoveForm(Action useSelection, Func<string, bool, string?> move, Func<bool, string?> restore, Action focus)
    {
        Text = "Move Entity";
        ClientSize = new Size(620, 320);
        MinimumSize = new Size(520, 330);
        StartPosition = FormStartPosition.CenterParent;
        AutoScroll = true;
        ShowInTaskbar = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        TargetLabel = new Label { AutoSize = true, Dock = DockStyle.Fill };
        CurrentOriginLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        DescendantsLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        HistoryLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        ErrorLabel = new Label { AutoSize = true, ForeColor = Color.Firebrick, Dock = DockStyle.Fill };
        OriginTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "X Y Z", AccessibleName = "Destination origin" };
        OriginTextBox.TextChanged += (_, _) => ValidateInput();
        MoveDescendantsCheckBox = new CheckBox
        {
            Text = "Include child entities",
            Checked = true,
            AutoSize = true,
            AccessibleDescription = "Moves all child entities linked to this entity, including nested ones. When unchecked, they stay in place. Editing pauses entity simulation.",
        };

        var targetButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var selectionButton = new ThemedButton { Text = "Use selection", AutoSize = true };
        selectionButton.Click += (_, _) => useSelection();
        FocusButton = new ThemedButton { Text = "Focus entity", AutoSize = true };
        FocusButton.Click += (_, _) => focus();
        targetButtons.Controls.AddRange([selectionButton, FocusButton]);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        MoveButton = new ThemedButton { Text = "Move", AutoSize = true };
        MoveButton.Click += (_, _) => ShowError(move(OriginTextBox.Text, MoveDescendantsCheckBox.Checked));
        UndoButton = new ThemedButton { Text = "Undo", AutoSize = true };
        UndoButton.Click += (_, _) => ShowError(restore(false));
        RedoButton = new ThemedButton { Text = "Redo", AutoSize = true };
        RedoButton.Click += (_, _) => ShowError(restore(true));
        actions.Controls.AddRange([MoveButton, UndoButton, RedoButton]);
        AcceptButton = MoveButton;

        layout.Controls.Add(new Label { Text = "Entity", AutoSize = true }, 0, 0);
        layout.Controls.Add(TargetLabel, 1, 0);
        layout.Controls.Add(targetButtons, 1, 1);
        layout.Controls.Add(new Label { Text = "Current origin", AutoSize = true }, 0, 2);
        layout.Controls.Add(CurrentOriginLabel, 1, 2);
        layout.Controls.Add(new Label { Text = "Destination", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(OriginTextBox, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "Same coordinate formats as Go To.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
        }, 1, 4);
        layout.Controls.Add(MoveDescendantsCheckBox, 1, 5);
        layout.Controls.Add(DescendantsLabel, 1, 6);
        layout.Controls.Add(actions, 1, 7);
        layout.Controls.Add(HistoryLabel, 1, 8);
        layout.Controls.Add(ErrorLabel, 0, 9);
        layout.SetColumnSpan(ErrorLabel, 2);
        Controls.Add(layout);
        Shown += (_, _) =>
        {
            ClientSize = new Size(ClientSize.Width, layout.PreferredSize.Height);
            MinimumSize = new Size(MinimumSize.Width, Height);
        };

        UpdateState(null, null, 0, 0, 0);
    }
#pragma warning restore CA2000

    public void UpdateState(string? description, Vector3? origin, int descendants, int historyPosition, int historyCount)
    {
        HasTarget = origin.HasValue;
        TargetLabel.Text = description ?? "Choose an entity in MAP or Entity List.";
        CurrentOriginLabel.Text = origin is { } position ? FormatOrigin(position) : "-";
        OriginTextBox.Text = origin is { } destination ? FormatOrigin(destination) : string.Empty;
        DescendantsLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{descendants} child entities (nested included).");
        HistoryLabel.Text = string.Create(CultureInfo.InvariantCulture, $"History: {historyPosition}/{historyCount}. Camera unchanged.");
        UndoButton.Enabled = historyPosition > 0;
        RedoButton.Enabled = historyPosition < historyCount;
        FocusButton.Enabled = HasTarget;
        ValidateInput();
    }

    public void ShowError(string? error)
    {
        ErrorLabel.Text = error ?? string.Empty;
    }

    private void ValidateInput()
    {
        var valid = EntityTransformHelper.TryParseCoordinates(OriginTextBox.Text, out _);
        MoveButton.Enabled = HasTarget && valid;
        ErrorLabel.Text = HasTarget && !valid ? "Enter three finite numbers: X Y Z." : string.Empty;
    }

    private static string FormatOrigin(Vector3 origin)
        => string.Create(CultureInfo.InvariantCulture, $"{origin.X:G9} {origin.Y:G9} {origin.Z:G9}");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TargetLabel.Dispose();
            CurrentOriginLabel.Dispose();
            DescendantsLabel.Dispose();
            HistoryLabel.Dispose();
            ErrorLabel.Dispose();
            OriginTextBox.Dispose();
            MoveDescendantsCheckBox.Dispose();
            MoveButton.Dispose();
            UndoButton.Dispose();
            RedoButton.Dispose();
            FocusButton.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }
}
