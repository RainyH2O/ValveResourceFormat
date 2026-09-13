using GUI.Controls;
using GUI.Utils;

namespace GUI
{
    partial class MainForm
    {
        private void InitializeEntitySimulationOpenMenu()
        {
#pragma warning disable CA2000 // Ownership is transferred to vpkContextMenu.
            var openItem = new ThemedToolStripMenuItem
            {
                Text = "Open without entity simulation",
                Name = "openWithoutEntitySimulationToolStripMenuItem",
            };
            vpkContextMenu.Items.Insert(vpkContextMenu.Items.IndexOf(openWithoutViewerToolStripMenuItem), openItem);
#pragma warning restore CA2000

            vpkContextMenu.Opening += (_, _) =>
            {
                var (_, selectedNode) = GetSingleSelectedNode(openItem);
                openItem.Visible = selectedNode is { IsFolder: false, PackageEntry.TypeName: "vmap_c" or "vwrld_c" };
            };

            openItem.Click += (_, _) =>
            {
                var (guiContext, selectedNode) = GetSingleSelectedNode(openItem);
                if (guiContext == null || selectedNode?.PackageEntry == null)
                {
                    return;
                }

                var newContext = new VrfGuiContext(selectedNode.PackageEntry.GetFullPath(), guiContext)
                {
                    DisableEntitySimulationOnOpen = true,
                };
                OpenFile(newContext, selectedNode.PackageEntry);
            };
        }
    }
}
