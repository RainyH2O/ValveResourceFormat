using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;

namespace GUI;

partial class MainForm
{
    private void InitializeEntityImportMenu()
    {
#pragma warning disable CA2000 // Ownership is transferred to the File menu.
        var importItem = new ThemedToolStripMenuItem
        {
            Name = "createEntityMapFromJsonToolStripMenuItem",
            Text = "Create entity map from JSON...",
        };
        fileToolStripMenuItem.DropDownItems.Insert(
            fileToolStripMenuItem.DropDownItems.IndexOf(createVpkToolStripMenuItem), importItem);
#pragma warning restore CA2000
        importItem.Click += OnCreateEntityMapFromJson;
    }

    private async void OnCreateEntityMapFromJson(object? sender, EventArgs e)
    {
        using var dialog = new EntityMapImportForm();
        await dialog.ShowDialogAsync(this).ConfigureAwait(true);
    }
}
