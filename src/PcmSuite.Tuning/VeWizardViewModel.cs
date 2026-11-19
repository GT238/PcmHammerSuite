using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmHacking;

namespace PcmSuite.Tune;

/// <summary>One cell of the table with a proposed new value.</summary>
public sealed class VeCellResult
{
    public int Row { get; init; }

    public int Column { get; init; }

    public string Where { get; init; } = string.Empty;

    public int Samples { get; init; }

    public double Percent { get; init; }

    public double NewScaled { get; init; }

    public string Change { get; init; } = string.Empty;
}

/// <summary>
/// Suggests corrections for the open table (for example the main VE table) from a log file written by
/// the Logger. Each logged sample is placed in the nearest cell of the table, using the table's own
/// axes; the cell's correction is the average over its samples. Nothing changes until Apply.
/// </summary>
public sealed partial class VeWizardViewModel : ObservableObject
{
    private readonly ValueEditorViewModel editor;
    private LoggedData? log;

    [ObservableProperty] private string _logName = "No log loaded";
    [ObservableProperty] private string _status = "Choose a log file written by the Logger.";
    [ObservableProperty] private int _rpmIndex = -1;
    [ObservableProperty] private int _loadIndex = -1;
    [ObservableProperty] private int _valueIndex = -1;
    [ObservableProperty] private int _trimIndex;       // 0 = none (in the optional list)
    [ObservableProperty] private int _mode;            // 0 = air/fuel or lambda, 1 = fuel trims
    [ObservableProperty] private double _target = 14.7;
    [ObservableProperty] private int _minSamples = 5;
    [ObservableProperty] private double _maxChange = 15;
    [ObservableProperty] private bool _rpmOnColumns;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _summary = string.Empty;

    public VeWizardViewModel(ValueEditorViewModel editor)
    {
        this.editor = editor;
        this.RpmOnColumns = !(editor.YAxisTitle ?? string.Empty).Contains("rpm", StringComparison.OrdinalIgnoreCase);
    }

    public string TableName => this.editor.Name;

    public ObservableCollection<string> Headings { get; } = new();

    public ObservableCollection<string> HeadingsOptional { get; } = new();

    public ObservableCollection<VeCellResult> Results { get; } = new();

    public bool IsAfrMode => this.Mode == 0;

    partial void OnModeChanged(int value) => this.OnPropertyChanged(nameof(this.IsAfrMode));

    public async Task LoadAsync(string path)
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
            this.Headings.Clear();
            this.HeadingsOptional.Clear();
            this.HeadingsOptional.Add("(none)");
            foreach (LoggedColumn c in data.Columns)
            {
                this.Headings.Add(c.Heading);
                this.HeadingsOptional.Add(c.Heading);
            }

            this.RpmIndex = Find(data, "engine speed", "rpm");
            this.LoadIndex = Find(data, "manifold", "map", "load");
            this.ValueIndex = Find(data, "air/fuel", "afr", "lambda", "wideband", "short term", "stft");
            int trim = Find(data, "long term", "ltft");
            this.TrimIndex = trim < 0 ? 0 : trim + 1;
            this.Mode = this.ValueIndex >= 0 && data.Columns[this.ValueIndex].Name.Contains("term", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            this.Results.Clear();
            this.HasResults = false;
            this.Status = $"{data.Samples.Count:N0} samples, {data.Columns.Count} columns" +
                          (data.MalformedRows > 0 ? $" ({data.MalformedRows} unreadable rows skipped)" : string.Empty) +
                          ". Check the column choices, then press Calculate.";
        }
        catch (Exception ex)
        {
            this.Status = "Could not read the log: " + ex.Message;
        }
    }

    public void Calculate()
    {
        this.Results.Clear();
        this.HasResults = false;
        if (this.log == null || this.RpmIndex < 0 || this.LoadIndex < 0 || this.ValueIndex < 0)
        {
            this.Status = "Pick the RPM, load and measurement columns first.";
            return;
        }

        if (this.Mode == 0 && this.Target <= 0)
        {
            this.Status = "Enter a target above zero (14.7 for air/fuel, 1.0 for lambda).";
            return;
        }

        int rows = this.editor.Rows;
        int cols = this.editor.Columns;
        double[] colAxis = new double[cols];
        double[] rowAxis = new double[rows];
        for (int c = 0; c < cols; c++)
        {
            if (!double.TryParse(this.editor.ColumnLabel(c), NumberStyles.Float, CultureInfo.InvariantCulture, out colAxis[c]))
            {
                this.Status = "This table's column labels are not numbers, so samples cannot be placed in it.";
                return;
            }
        }

        for (int r = 0; r < rows; r++)
        {
            if (!double.TryParse(this.editor.RowLabel(r), NumberStyles.Float, CultureInfo.InvariantCulture, out rowAxis[r]))
            {
                this.Status = "This table's row labels are not numbers, so samples cannot be placed in it.";
                return;
            }
        }

        double[] rpmAxis = this.RpmOnColumns ? colAxis : rowAxis;
        double[] loadAxis = this.RpmOnColumns ? rowAxis : colAxis;

        double[,] sum = new double[rows, cols];
        int[,] count = new int[rows, cols];
        int used = 0;
        int outside = 0;
        int trimColumn = this.TrimIndex - 1;

        foreach (double[] sample in this.log.Samples)
        {
            double rpm = sample[this.RpmIndex];
            double load = sample[this.LoadIndex];
            double value = sample[this.ValueIndex];
            double trim = trimColumn >= 0 ? sample[trimColumn] : 0;
            if (double.IsNaN(rpm) || double.IsNaN(load) || double.IsNaN(value) || double.IsNaN(trim) || rpm <= 0)
            {
                continue;
            }

            int rpmCell = Nearest(rpmAxis, rpm);
            int loadCell = Nearest(loadAxis, load);
            if (rpmCell < 0 || loadCell < 0)
            {
                outside++;
                continue;
            }

            double factor = this.Mode == 0 ? value / this.Target : 1 + (value + trim) / 100.0;
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
            {
                continue;
            }

            int row = this.RpmOnColumns ? loadCell : rpmCell;
            int col = this.RpmOnColumns ? rpmCell : loadCell;
            sum[row, col] += factor;
            count[row, col]++;
            used++;
        }

        double limit = this.MaxChange / 100.0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (count[r, c] < Math.Max(1, this.MinSamples))
                {
                    continue;
                }

                double factor = Math.Clamp(sum[r, c] / count[r, c], 1 - limit, 1 + limit);
                double old = this.editor.Scaled(r, c);
                double next = old * factor;
                double percent = (factor - 1) * 100;
                this.Results.Add(new VeCellResult
                {
                    Row = r,
                    Column = c,
                    Where = $"{this.editor.ColumnLabel(c)}  x  {this.editor.RowLabel(r)}",
                    Samples = count[r, c],
                    Percent = percent,
                    NewScaled = next,
                    Change = $"{old:0.##} → {next:0.##}   ({(percent >= 0 ? "+" : string.Empty)}{percent:0.0}%)",
                });
            }
        }

        this.HasResults = this.Results.Count > 0;
        this.Summary = this.HasResults
            ? $"{this.Results.Count} cell(s) have at least {this.MinSamples} samples. {used:N0} samples used" +
              (outside > 0 ? $", {outside:N0} fell outside the table" : string.Empty) + "."
            : "No cell has enough samples. Lower the minimum, or log more driving in the table's range.";
        this.Status = this.Summary;
    }

    /// <summary>Put the proposed values into the open table as one undoable edit.</summary>
    public int Apply() =>
        this.editor.ApplyBatch(this.Results.Select(r => (r.Row, r.Column, r.NewScaled)));

    private static int Find(LoggedData data, params string[] keywords)
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
    private static int Nearest(double[] axis, double x)
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
