using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using GUI.Forms;

namespace GUI.Utils;

/// <summary>
/// Compares DataGridView rows by multiple columns with numeric-aware comparison.
/// </summary>
internal sealed class MultiColumnNumericStringComparer(ListSortDirection direction, string[] columnNames) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not DataGridViewRow rowX || y is not DataGridViewRow rowY)
        {
            return 0;
        }

        var result = 0;

        foreach (var columnName in columnNames)
        {
            var cellX = rowX.Cells[columnName]?.Value;
            var cellY = rowY.Cells[columnName]?.Value;

            if (cellX is float numX && cellY is float numY)
            {
                result = numX.CompareTo(numY);
            }
            else
            {
                result = SearchForm.NumericComparer.Compare(cellX?.ToString() ?? string.Empty, cellY?.ToString() ?? string.Empty);
            }

            if (result != 0)
            {
                break;
            }
        }

        return direction == ListSortDirection.Descending ? -result : result;
    }
}
