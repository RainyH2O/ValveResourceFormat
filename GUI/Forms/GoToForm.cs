#nullable disable

using System.Windows.Forms;

namespace GUI.Forms;

internal partial class GoToForm : Form
{
    public enum GoToType
    {
        EntityName,
        Coordinate,
        HammerId
    }

    // Remember last selected type for user convenience
    private static GoToType lastSelectedType;

    private static readonly GoToTypeItem[] GoToTypes =
    [
        new("Entity Name", GoToType.EntityName, "e.g. player_spawn_01"),
        new("Coordinate", GoToType.Coordinate, "e.g. 100 200 300"),
        new("Hammer ID", GoToType.HammerId, "e.g. 123456")
    ];

    public GoToForm()
    {
        InitializeComponent();
        InitializeComboBox();
    }

    /// <summary>
    ///     Gets whatever text was entered by the user in the input textbox.
    /// </summary>
    public string InputText
    {
        get => inputTextBox.Text;
    }

    /// <summary>
    ///     Gets whatever options was selected by the user in the type combobox.
    /// </summary>
    public GoToType SelectedGoToType
    {
        get => ((GoToTypeItem)typeComboBox.SelectedItem).Type;
    }

    private void InitializeComboBox()
    {
        typeComboBox.ValueMember = nameof(GoToTypeItem.Type);
        typeComboBox.DisplayMember = nameof(GoToTypeItem.Name);
        typeComboBox.Items.AddRange(GoToTypes);

        // Set default selection to last used type
        var lastSelectedIndex = Array.FindIndex(GoToTypes, item => item.Type == lastSelectedType);
        typeComboBox.SelectedIndex = lastSelectedIndex >= 0 ? lastSelectedIndex : 0;
    }

    /// <summary>
    ///     On form load, set up the initial state and set the textbox as the focused control.
    /// </summary>
    /// <param name="sender">Object which raised event.</param>
    /// <param name="e">Event data.</param>
    private void GoToForm_Load(object sender, EventArgs e)
    {
        ActiveControl = inputTextBox;
        UpdateExampleText();
    }

    /// <summary>
    ///     Update the example text when the type changes.
    /// </summary>
    /// <param name="sender">Object which raised event.</param>
    /// <param name="e">Event data.</param>
    private void TypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        UpdateExampleText();

        // Save selection immediately when user changes it
        if (typeComboBox.SelectedItem is GoToTypeItem selectedItem)
        {
            lastSelectedType = selectedItem.Type;
        }
    }

    private void UpdateExampleText()
    {
        if (typeComboBox.SelectedItem is GoToTypeItem selectedItem)
        {
            exampleLabel.Text = selectedItem.Example;
        }
    }

    private class GoToTypeItem(string name, GoToType type, string example)
    {
        public string Name { get; } = name;
        public GoToType Type { get; } = type;
        public string Example { get; } = example;
    }
}
