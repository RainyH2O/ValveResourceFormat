using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Forms;

public enum GoToType
{
    EntityName,
    Coordinate,
    HammerId
}

public class GoToForm : ThemedForm
{
    private readonly ComboBox typeComboBox;
    private readonly TextBox inputTextBox;
    private readonly Label exampleLabel;

    public string InputText => inputTextBox.Text.Trim();
    public GoToType SelectedGoToType => (GoToType)typeComboBox.SelectedIndex;

    private static GoToType lastSelectedType;

#pragma warning disable CA2000 // Controls are transferred to their parent container's Controls collection and disposed with it
    public GoToForm()
    {
        Text = "Go To";
        ClientSize = new System.Drawing.Size(380, 200); // height will be corrected after layout
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        SuspendLayout();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // label column: fits content
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // input column: fills remaining
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // type
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // input
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // example
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

        var typeLabel = new Label
        {
            Text = "Type:",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 2),
        };

        typeComboBox = new ThemedComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 2),
        };
        typeComboBox.Items.AddRange(["Entity Name", "Coordinate", "Hammer ID"]);
        typeComboBox.SelectedIndex = (int)lastSelectedType;
        typeComboBox.SelectedIndexChanged += (_, _) => UpdateExample();

        var inputLabel = new Label
        {
            Text = "Input:",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 2),
        };

        inputTextBox = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 2),
        };

        exampleLabel = new Label
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            ForeColor = System.Drawing.SystemColors.GrayText,
            TextAlign = System.Drawing.ContentAlignment.TopLeft,
            Margin = new Padding(0, 2, 0, 6),
            AutoSize = true,
        };

        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = Padding.Empty,
        };
        var cancelButton = new ThemedButton
        {
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(4, 3, 0, 3),
        };
        var goButton = new ThemedButton
        {
            Text = "Go",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Margin = new Padding(0, 3, 0, 3),
        };
        buttonFlow.Controls.AddRange([cancelButton, goButton]);

        AcceptButton = goButton;
        CancelButton = cancelButton;

        layout.Controls.Add(typeLabel, 0, 0);
        layout.Controls.Add(typeComboBox, 1, 0);
        layout.Controls.Add(inputLabel, 0, 1);
        layout.Controls.Add(inputTextBox, 1, 1);
        layout.Controls.Add(exampleLabel, 0, 2);
        layout.SetColumnSpan(exampleLabel, 2);
        layout.Controls.Add(buttonFlow, 0, 3);
        layout.SetColumnSpan(buttonFlow, 2);

        Controls.Add(layout);

        ActiveControl = inputTextBox;

        // Set example text before ResumeLayout so the auto-size row accounts for it
        UpdateExample();

        ResumeLayout(true);

        // Correct form height to exactly fit the auto-sized layout content
        ClientSize = new System.Drawing.Size(ClientSize.Width, layout.Height);
    }
#pragma warning restore CA2000

    private void UpdateExample()
    {
        exampleLabel.Text = (GoToType)typeComboBox.SelectedIndex switch
        {
            GoToType.EntityName => "e.g. my_entity_name",
            GoToType.Coordinate => "e.g. 100 200 300",
            GoToType.HammerId => "e.g. 12345",
            _ => string.Empty,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            typeComboBox.Dispose();
            inputTextBox.Dispose();
            exampleLabel.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        lastSelectedType = SelectedGoToType;
        base.OnFormClosing(e);
    }
}
