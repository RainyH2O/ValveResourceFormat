using System.Linq;
using System.Windows.Forms;

namespace GUI.Controls;

partial class GLViewerTreeSelectionControl
{
    public Predicate<string>? BulkSelectionPredicate { get; set; }

    private void InitializeBulkSelection()
    {
        treeView.KeyDown += (_, e) =>
        {
            if (e.KeyData != (Keys.Control | Keys.A))
            {
                return;
            }

            var nodes = AllBelow(treeView.Nodes).ToArray();
            bool CanCheck(TreeNode node) => BulkSelectionPredicate?.Invoke(node.Name) ?? true;
            var check = nodes.Any(node => CanCheck(node) && !node.Checked);

            treeView.BeginUpdate();
            try
            {
                foreach (var node in nodes)
                {
                    node.Checked = check && CanCheck(node);
                }
            }
            finally
            {
                treeView.EndUpdate();
            }

            CheckedPathsChanged?.Invoke([.. nodes.Where(static node => node.Checked).Select(static node => node.Name)]);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
    }
}
