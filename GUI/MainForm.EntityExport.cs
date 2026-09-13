using GUI.Types.Exporter;
using GUI.Utils;

namespace GUI;

partial class MainForm
{
    private void InitializeEntityExportMenus()
    {
        vpkContextMenu.Opening += (_, _) =>
        {
            var (_, items) = GetSelectedItems(exportEntitiesToolStripMenuItem);
            exportEntitiesToolStripMenuItem.Visible = items is [{ PackageEntry.TypeName: "vmap_c" or "vwrld_c" }];
        };
    }

    private static bool IsEntityExportResource(string fileName)
        => fileName.EndsWith(".vmap_c", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".vwrld_c", StringComparison.OrdinalIgnoreCase);

    private void ExportEntitiesToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var (guiContext, selectedNode) = GetSingleSelectedNode(sender);
        if (guiContext == null || selectedNode == null)
        {
            return;
        }

        try
        {
            ExportFile.ExportEntitiesFromTreeNode(selectedNode, guiContext);
        }
        catch (Exception exception)
        {
            ErrorReport.ShowError(exception, selectedNode.PackageEntry?.GetFullPath());
        }
    }

    private void ExportEntitiesTabToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var tabPage = FetchToolstripTabContext(sender);
        if (tabPage.Tag is not ExportData exportData)
        {
            return;
        }

        try
        {
            ExportFile.ExportEntitiesFromTabPage(exportData);
        }
        catch (Exception exception)
        {
            ErrorReport.ShowError(exception, exportData.VrfGuiContext.FileName);
        }
    }
}
