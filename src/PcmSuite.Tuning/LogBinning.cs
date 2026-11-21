using System.Globalization;
using PcmHacking;

namespace PcmSuite.Tune;

/// <summary>What to show in each cell of a log map.</summary>
public enum CellStat
{
    Count,
    Average,
    Minimum,
    Maximum,
}

/// <summary>What the samples of a log added up to in each cell of a table.</summary>
public sealed class CellStats
{
    public CellStats(int rows, int columns)
    {
        this.Rows = rows;
        this.Columns = columns;
        this.Count = new int[rows, columns];
        this.Sum = new double[rows, columns];
        this.Min = new double[rows, columns];
        this.Max = new double[rows, columns];
    }

    public int Rows { get; }

    public int Columns { get; }

    public int[,] Count { get; }

    public double[,] Sum { get; }

    public double[,] Min { get; }

    public double[,] Max { get; }

    /// <summary>Samples that were placed in a cell.</summary>
    public int Used { get; set; }

    /// <summary>Samples with an engine speed and load that fell outside the table's axes.</summary>
    public int Outside { get; set; }

    public double Average(int row, int column) => this.Count[row, column] == 0 ? double.NaN : this.Sum[row, column] / this.Count[row, column];

    /// <summary>The chosen figure for a cell, or NaN when no sample landed there.</summary>
    public double Value(CellStat stat, int row, int column)
    {
        if (this.Count[row, column] == 0)
        {
            return double.NaN;
        }

        return stat switch
        {
            CellStat.Count => this.Count[row, column],
            CellStat.Average => this.Average(row, column),
            CellStat.Minimum => this.Min[row, column],
            _ => this.Max[row, column],
        };
    }

    public int MaxCount()
    {
        int max = 0;
        foreach (int c in this.Count)
        {
            max = Math.Max(max, c);
        }

        return max;
    }
}

/// <summary>
/// Places logged samples in the cells of a table by the table's own axes. Shared by the VE wizard and the log map
/// so both put a sample in the same cell.
/// </summary>
public static class LogBinning
{
    /// <summary>The numbers on the table's two axes. False (with the reason) when the XDF does not list real axis values.</summary>
    public static bool TryReadAxes(ValueEditorViewModel editor, out double[] columnAxis, out double[] rowAxis, out string error)
    {
        columnAxis = new double[editor.Columns];
        rowAxis = new double[editor.Rows];
        error = string.Empty;

        if (!editor.ColumnLabelsAreReal || !editor.RowLabelsAreReal)
        {
            error = "This table's axis values are not listed in the XDF (they are stored in the BIN), so samples cannot be placed " +
                    "in it. Choose a table whose axes show real values, such as RPM and kPa.";
            return false;
        }

        for (int c = 0; c < columnAxis.Length; c++)
        {
            if (!double.TryParse(editor.ColumnLabel(c), NumberStyles.Float, CultureInfo.InvariantCulture, out columnAxis[c]))
            {
                error = "This table's column labels are not numbers, so samples cannot be placed in it.";
                return false;
            }
        }

        for (int r = 0; r < rowAxis.Length; r++)
        {
            if (!double.TryParse(editor.RowLabel(r), NumberStyles.Float, CultureInfo.InvariantCulture, out rowAxis[r]))
            {
                error = "This table's row labels are not numbers, so samples cannot be placed in it.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Count, sum, minimum and maximum of <paramref name="value"/> in each cell. A sample needs a positive engine speed and a load;
    /// <paramref name="value"/> returns NaN for a sample that should not count.
    /// </summary>
    public static CellStats Bin(
        LoggedData log,
        int rpmIndex,
        int loadIndex,
        Func<double[], double> value,
        double[] columnAxis,
        double[] rowAxis,
        bool rpmOnColumns)
    {
        double[] rpmAxis = rpmOnColumns ? columnAxis : rowAxis;
        double[] loadAxis = rpmOnColumns ? rowAxis : columnAxis;
        CellStats stats = new(rowAxis.Length, columnAxis.Length);

        foreach (double[] sample in log.Samples)
        {
            double rpm = sample[rpmIndex];
            double load = sample[loadIndex];
            if (double.IsNaN(rpm) || double.IsNaN(load) || rpm <= 0)
            {
                continue;
            }

            double v = value(sample);
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                continue;
            }

            int rpmCell = Nearest(rpmAxis, rpm);
            int loadCell = Nearest(loadAxis, load);
            if (rpmCell < 0 || loadCell < 0)
            {
                stats.Outside++;
                continue;
            }

            int row = rpmOnColumns ? loadCell : rpmCell;
            int col = rpmOnColumns ? rpmCell : loadCell;
            if (stats.Count[row, col] == 0)
            {
                stats.Min[row, col] = v;
                stats.Max[row, col] = v;
            }
            else
            {
                stats.Min[row, col] = Math.Min(stats.Min[row, col], v);
                stats.Max[row, col] = Math.Max(stats.Max[row, col], v);
            }

            stats.Sum[row, col] += v;
            stats.Count[row, col]++;
            stats.Used++;
        }

        return stats;
    }

    /// <summary>Index of the first column whose heading contains any of the words (tried in order), or -1.</summary>
    public static int FindColumn(LoggedData data, params string[] keywords)
    {
        for (int i = 0; i < data.Columns.Count; i++)
        {
            foreach (string keyword in keywords)
            {
                if (data.Columns[i].Heading.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Index of the axis value closest to x, or -1 when x is clearly outside the axis.</summary>
    public static int Nearest(double[] axis, double x)
    {
        if (axis.Length == 0)
        {
            return -1;
        }

        double min = axis.Min();
        double max = axis.Max();
        double edge = axis.Length > 1 ? (max - min) / (axis.Length - 1) : 0;
        if (x < min - edge || x > max + edge)
        {
            return -1;
        }

        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < axis.Length; i++)
        {
            double d = Math.Abs(axis[i] - x);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }
}
