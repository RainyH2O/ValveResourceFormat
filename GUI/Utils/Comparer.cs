using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;

namespace GUI.Utils
{
    internal class MultiColumnNumericStringComparer(ListSortDirection direction, string[] columnNames) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not DataGridViewRow row1 || y is not DataGridViewRow row2)
            {
                return 0;
            }

            foreach (var columnName in columnNames)
            {
                var xText = row1.Cells[columnName].Value as string;
                var yText = row2.Cells[columnName].Value as string;

                if (int.TryParse(xText, out var xNum) && int.TryParse(yText, out var yNum))
                {
                    var result = xNum.CompareTo(yNum);
                    if (result != 0)
                    {
                        return direction == ListSortDirection.Ascending ? result : -result;
                    }
                }
                else
                {
                    var result = string.Compare(xText, yText, StringComparison.Ordinal);
                    if (result != 0)
                    {
                        return direction == ListSortDirection.Ascending ? result : -result;
                    }
                }
            }

            return 0;
        }
    }
}
