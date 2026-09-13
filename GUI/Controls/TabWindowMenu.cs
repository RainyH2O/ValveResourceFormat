using System.Linq;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

internal static class TabWindowMenu
{
#pragma warning disable CA2000 // The tab's Disposed handler owns the context menu for the tab lifetime
    public static void Register(TabControl tabControl, TabPage targetPage, Action openWindow)
    {
        var contextMenu = new ThemedContextMenuStrip();
        contextMenu.Items.Add("Open in New Window", null, (_, _) => openWindow());

        void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var page = tabControl.TabPages.Cast<TabPage>()
                .Where((_, index) => tabControl.GetTabRect(index).Contains(e.Location))
                .FirstOrDefault();

            if (page == targetPage)
            {
                contextMenu.Show(tabControl, e.Location);
            }
        }

        void OnDisposed(object? sender, EventArgs e)
        {
            tabControl.MouseUp -= OnMouseUp;
            tabControl.Disposed -= OnDisposed;
            contextMenu.Dispose();
        }

        tabControl.MouseUp += OnMouseUp;
        tabControl.Disposed += OnDisposed;
    }
#pragma warning restore CA2000
}
