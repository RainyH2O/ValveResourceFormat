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

    public GoToForm()
    {
        Text = "Go To";
        Width = 400;
        Height = 180;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = null;
        KeyPreview = true;

        var typeLabel = new Label
        {
            Text = "Type:",
            Location = new System.Drawing.Point(12, 15),
            AutoSize = true,
        };

        typeComboBox = new ThemedComboBox
        {
            Location = new System.Drawing.Point(60, 12),
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        typeComboBox.Items.AddRange(["Entity Name", "Coordinate", "Hammer ID"]);
        typeComboBox.SelectedIndex = (int)lastSelectedType;
        typeComboBox.SelectedIndexChanged += (_, _) => UpdateExample();

        var inputLabel = new Label
        {
            Text = "Input:",
            Location = new System.Drawing.Point(12, 48),
            AutoSize = true,
        };

        inputTextBox = new ThemedTextBox
        {
            Location = new System.Drawing.Point(60, 45),
            Width = 310,
        };

        exampleLabel = new Label
        {
            Location = new System.Drawing.Point(60, 72),
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
        };

        var goButton = new ThemedButton
        {
            Text = "Go",
            Location = new System.Drawing.Point(214, 100),
            Width = 75,
            DialogResult = DialogResult.OK,
        };

        var cancelButton = new ThemedButton
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(295, 100),
            Width = 75,
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = goButton;
        CancelButton = cancelButton;

        Controls.AddRange([typeLabel, typeComboBox, inputLabel, inputTextBox, exampleLabel, goButton, cancelButton]);

        UpdateExample();
    }

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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        lastSelectedType = SelectedGoToType;
        base.OnFormClosing(e);
    }
}
