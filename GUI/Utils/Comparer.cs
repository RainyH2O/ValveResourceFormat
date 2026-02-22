using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;

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
            var cellX = rowX.Cells[columnName]?.Value?.ToString() ?? string.Empty;
            var cellY = rowY.Cells[columnName]?.Value?.ToString() ?? string.Empty;

            if (int.TryParse(cellX, out var numX) && int.TryParse(cellY, out var numY))
            {
                result = numX.CompareTo(numY);
            }
            else
            {
                result = string.Compare(cellX, cellY, StringComparison.Ordinal);
            }

            if (result != 0)
            {
                break;
            }
        }

        return direction == ListSortDirection.Descending ? -result : result;
    }
}
