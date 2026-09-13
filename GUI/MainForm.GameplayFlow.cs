using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using ResourceViewer = GUI.Types.Viewers.Resource;

namespace GUI;

partial class MainForm
{
    private void InitializeGameplayFlowCloseConfirmation()
    {
        var confirmClose = mainTabs.ConfirmClose;
        mainTabs.ConfirmClose = tab =>
            ConfirmCloseGameplayFlow(tab)
            && (confirmClose?.Invoke(tab) ?? true);
    }

    private static bool ConfirmCloseGameplayFlow(TabPage tab)
        => tab.Tag is not ExportData { DisposableContents: ResourceViewer { GLViewer: GLWorldViewer world } }
            || world.TryCloseGameplayFlow();

    private bool ConfirmCloseGameplayFlows()
    {
        foreach (TabPage tab in mainTabs.TabPages)
        {
            if (!ConfirmCloseGameplayFlow(tab))
            {
                return false;
            }
        }

        return true;
    }
}
