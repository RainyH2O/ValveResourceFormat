namespace GUI.Controls
{
    partial class ExplorerControl
    {
        private void InitializeMapOpenMenu()
        {
#pragma warning disable CA2000 // Ownership is transferred to fileContextMenuStrip.
            var openItem = new ThemedToolStripMenuItem
            {
                Text = "Find and open map",
                Name = "findAndOpenMapToolStripMenuItem",
            };
            fileContextMenuStrip.Items.Insert(0, openItem);
#pragma warning restore CA2000

            var opening = false;
            fileContextMenuStrip.Opening += (_, _) =>
            {
                openItem.Visible = treeView.SelectedNode?.Tag is string path && path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase);
                openItem.Enabled = !opening;
            };
            openItem.Click += async (_, _) =>
            {
                if (opening || treeView.SelectedNode?.Tag is not string path)
                {
                    return;
                }

                opening = true;
                openItem.Enabled = false;
                try
                {
                    await Program.MainForm.FindAndOpenMapAsync(path).ConfigureAwait(true);
                }
                finally
                {
                    opening = false;
                    if (!IsDisposed)
                    {
                        openItem.Enabled = true;
                    }
                }
            };
        }
    }
}
