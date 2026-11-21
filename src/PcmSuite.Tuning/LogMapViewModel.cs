using System.Collections.ObjectModel;
using PcmHacking;

namespace PcmSuite.Tune;

/// <summary>
/// Shows where a logged drive went on the open table: each sample is placed in a cell by the table's own axes, and the cell
/// shows how many samples landed there, or the average, lowest or highest of a logged channel. Nothing is changed in the BIN.
/// </summary>
public sealed class LogMapViewModel
{
    private readonly ValueEditorViewModel editor;
    private LoggedData? log;

    public LogMapViewModel(ValueEditorViewModel editor)
    {
        this.editor = editor;
        this.RpmOnColumns = !(editor.YAxisTitle ?? string.Empty).Contains("rpm", StringComparison.OrdinalIgnoreCase);
    }

    public ValueEditorViewModel Editor => this.editor;

    public string TableName => this.editor.Name;

    public string LogName { get; private set; } = "No log loaded";

    public string Status { get; private set; } = "Choose a log file written by the Logger.";

    public IReadOnlyList<string> Headings { get; private set; } = Array.Empty<string>();

    public int RpmIndex { get; set; } = -1;

    public int LoadIndex { get; set; } = -1;

    /// <summary>The logged channel to show in each cell, or -1 to just count samples.</summary>
    public int ValueIndex { get; set; } = -1;

    public CellStat Stat { get; set; } = CellStat.Average;

    public bool RpmOnColumns { get; set; }

    /// <summary>The result of the last <see cref="Calculate"/>, or null.</summary>
    public CellStats? Stats { get; private set; }

    public double[] ColumnAxis { get; private set; } = Array.Empty<double>();

    public double[] RowAxis { get; private set; } = Array.Empty<double>();

    public string Units => this.ValueIndex >= 0 && this.log != null ? this.log.Columns[this.ValueIndex].Units : string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public async Task<bool> LoadAsync(string path)
    {
        try
        {
            LoggedData data = await Task.Run(() =>
            {
                using StreamReader reader = new(path);
                return LogFileReader.Read(reader);
            });

            this.log = data;
            this.LogName = Path.GetFileName(path);
            this.Headings = data.Columns.Select(c => c.Heading).ToList();
            this.RpmIndex = LogBinning.FindColumn(data, "engine speed", "rpm");
            this.LoadIndex = LogBinning.FindColumn(data, "manifold", "map", "load");
            this.ValueIndex = -1;
            this.Stats = null;
            this.Summary = string.Empty;
            this.Status = $"{data.Samples.Count:N0} samples, {data.Columns.Count} columns" +
                          (data.MalformedRows > 0 ? $" ({data.MalformedRows} unreadable rows skipped)" : string.Empty) + ".";
            return true;
        }
        catch (Exception ex)
        {
            this.Status = "Could not read the log: " + ex.Message;
            return false;
        }
    }

    /// <summary>Bin the loaded log into the table. Returns false (with the reason in <see cref="Status"/>) when it cannot.</summary>
    public bool Calculate()
    {
        this.Stats = null;
        this.Summary = string.Empty;
        if (this.log == null || this.RpmIndex < 0 || this.LoadIndex < 0)
        {
            this.Status = "Pick the engine speed and load columns first.";
            return false;
        }

        if (!LogBinning.TryReadAxes(this.editor, out double[] cols, out double[] rows, out string error))
        {
            this.Status = error;
            return false;
        }

        this.ColumnAxis = cols;
        this.RowAxis = rows;
        int valueColumn = this.ValueIndex;
        this.Stats = LogBinning.Bin(
            this.log,
            this.RpmIndex,
            this.LoadIndex,
            sample => valueColumn < 0 ? 1 : sample[valueColumn],
            cols,
            rows,
            this.RpmOnColumns);

        int visited = 0;
        foreach (int c in this.Stats.Count)
        {
            if (c > 0)
            {
                visited++;
            }
        }

        int cells = this.Stats.Rows * this.Stats.Columns;
        this.Summary = $"{this.Stats.Used:N0} samples placed in {visited} of {cells} cells" +
                       (this.Stats.Outside > 0 ? $"; {this.Stats.Outside:N0} fell outside the table" : string.Empty) + ".";
        this.Status = this.Summary;
        return true;
    }

    /// <summary>The figure to print in a cell (empty when no sample landed there).</summary>
    public string CellText(int row, int column)
    {
        CellStats? stats = this.Stats;
        if (stats == null || stats.Count[row, column] == 0)
        {
            return string.Empty;
        }

        if (this.ValueIndex < 0 || this.Stat == CellStat.Count)
        {
            return stats.Count[row, column].ToString();
        }

        return FormatValue(stats.Value(this.Stat, row, column));
    }

    /// <summary>The detail shown when the pointer rests on a cell.</summary>
    public string CellTip(int row, int column)
    {
        CellStats? stats = this.Stats;
        string where = $"{this.editor.ColumnLabel(column)}  x  {this.editor.RowLabel(row)}";
        if (stats == null || stats.Count[row, column] == 0)
        {
            return where + "\nNo samples";
        }

        string text = where + $"\n{stats.Count[row, column]:N0} samples";
        if (this.ValueIndex >= 0)
        {
            text += $"\naverage {FormatValue(stats.Average(row, column))}, lowest {FormatValue(stats.Min[row, column])}, highest {FormatValue(stats.Max[row, column])} {this.Units}";
        }

        return text + "\nTable value " + this.editor.CellText(row, column);
    }

    /// <summary>The cell's position on the colour scale, 0 to 1, or NaN when it has no samples.</summary>
    public double Fraction(int row, int column)
    {
        CellStats? stats = this.Stats;
        if (stats == null || stats.Count[row, column] == 0)
        {
            return double.NaN;
        }

        if (this.ValueIndex < 0 || this.Stat == CellStat.Count)
        {
            int max = Math.Max(1, stats.MaxCount());
            return Math.Sqrt(stats.Count[row, column] / (double)max);   // square root so a few hot cells do not wash out the rest
        }

        double lo = double.MaxValue;
        double hi = double.MinValue;
        for (int r = 0; r < stats.Rows; r++)
        {
            for (int c = 0; c < stats.Columns; c++)
            {
                double v = stats.Value(this.Stat, r, c);
                if (!double.IsNaN(v))
                {
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                }
            }
        }

        return hi > lo ? (stats.Value(this.Stat, row, column) - lo) / (hi - lo) : 0.5;
    }

    private static string FormatValue(double v) =>
        Math.Abs(v) >= 1000 ? v.ToString("0") : Math.Abs(v) >= 100 ? v.ToString("0.#") : v.ToString("0.##");
}
