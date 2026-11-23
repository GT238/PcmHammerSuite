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
    private double[]? elapsed;

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

    // Samples to leave out. Each column choice is in the optional list (0 = none, so rule off).
    [ObservableProperty] private int _coolantIndex;
    [ObservableProperty] private double _minCoolant = 160;
    [ObservableProperty] private int _throttleIndex;
    [ObservableProperty] private double _maxThrottleRate = 50;
    [ObservableProperty] private int _injectorIndex;
    [ObservableProperty] private double _steadyMs;
    [ObservableProperty] private double _delayMs;
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

    /// <summary>The log has readable elapsed times, so the throttle, steady and delay settings can work.</summary>
    [ObservableProperty] private bool _hasTiming;

    // A coolant reading in Fahrenheit and one in Celsius need very different limits, so picking the column sets a sensible one.
    partial void OnCoolantIndexChanged(int value)
    {
        if (this.log != null && value > 0)
        {
            string units = this.log.Columns[value - 1].Units;
            this.MinCoolant = units.Contains('F', StringComparison.OrdinalIgnoreCase) ? 160 : 70;
        }
    }

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
            this.elapsed = await Task.Run(() => LogTiming.ReadElapsedSeconds(path, data.Samples.Count));
            this.HasTiming = this.elapsed != null;
            this.LogName = Path.GetFileName(path);
            this.Headings.Clear();
            this.HeadingsOptional.Clear();
            this.HeadingsOptional.Add("(none)");
            foreach (LoggedColumn c in data.Columns)
            {
                this.Headings.Add(c.Heading);
                this.HeadingsOptional.Add(c.Heading);
            }

            this.RpmIndex = LogBinning.FindColumn(data, "engine speed", "rpm");
            this.LoadIndex = LogBinning.FindColumn(data, "manifold", "map", "load");
            this.ValueIndex = LogBinning.FindColumn(data, "air/fuel", "afr", "lambda", "wideband", "short term", "stft");
            int trim = LogBinning.FindColumn(data, "long term", "ltft");
            this.TrimIndex = trim < 0 ? 0 : trim + 1;
            this.Mode = this.ValueIndex >= 0 && data.Columns[this.ValueIndex].Name.Contains("term", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            // The rules that keep the numbers honest: on whenever the log has the column for them.
            int coolant = LogBinning.FindColumn(data, "coolant", "ect");
            this.CoolantIndex = coolant < 0 ? 0 : coolant + 1;
            int throttle = LogBinning.FindColumn(data, "throttle", "tps");
            this.ThrottleIndex = throttle < 0 ? 0 : throttle + 1;
            int injector = LogBinning.FindColumn(data, "injector pwm", "idc", "injector");
            this.InjectorIndex = injector < 0 ? 0 : injector + 1;
            this.SteadyMs = 0;
            this.DelayMs = 0;

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

        if (!LogBinning.TryReadAxes(this.editor, out double[] colAxis, out double[] rowAxis, out string axisError))
        {
            this.Status = axisError;
            return;
        }

        int rows = this.editor.Rows;
        int cols = this.editor.Columns;
        int trimColumn = this.TrimIndex - 1;
        int valueColumn = this.ValueIndex;
        bool afr = this.Mode == 0;
        double target = this.Target;

        LoggedData data = this.log;
        // Leave out the samples that cannot say what the table should hold.
        double[] loadAxis = this.RpmOnColumns ? rowAxis : colAxis;
        double loadSpan = loadAxis.Length > 1 ? loadAxis.Max() - loadAxis.Min() : 0;
        SampleFilterSettings rules = new()
        {
            CoolantIndex = this.CoolantIndex - 1,
            MinCoolant = this.MinCoolant,
            ThrottleIndex = this.ThrottleIndex - 1,
            MaxThrottleRate = this.MaxThrottleRate,
            InjectorIndex = this.InjectorIndex - 1,
            FuelCutBelow = this.InjectorIndex > 0 && data.Columns[this.InjectorIndex - 1].Units.Contains('%') ? 1.0 : 0.3,
            SteadyMs = this.SteadyMs,
            LoadTolerance = Math.Max(1, loadSpan * 0.05),
        };
        SampleFilterResult filtered = SampleFilter.Evaluate(data, this.elapsed, rules, this.RpmIndex, this.LoadIndex);

        // A wideband reads the exhaust a moment after the burn, so for air/fuel the reading that belongs to a sample is a later one.
        int[]? later = afr && this.DelayMs > 0 && this.elapsed != null ? SampleFilter.DelayedIndex(this.elapsed, this.DelayMs) : null;

        // Each sample becomes a fuel correction factor; one the wizard cannot use (no reading, or a factor of zero or less) returns NaN and is skipped.
        CellStats stats = LogBinning.Bin(
            this.log,
            this.RpmIndex,
            this.LoadIndex,
            (index, sample) =>
            {
                double[] reading = sample;
                if (later != null)
                {
                    if (later[index] < 0)
                    {
                        return double.NaN;
                    }

                    reading = data.Samples[later[index]];
                }

                double value = reading[valueColumn];
                double trim = trimColumn >= 0 ? sample[trimColumn] : 0;
                if (double.IsNaN(value) || double.IsNaN(trim))
                {
                    return double.NaN;
                }

                double factor = afr ? value / target : 1 + (value + trim) / 100.0;
                return factor <= 0 ? double.NaN : factor;
            },
            colAxis,
            rowAxis,
            this.RpmOnColumns,
            filtered.Accepted);
        int used = stats.Used;
        int outside = stats.Outside;
        int[,] count = stats.Count;
        double[,] sum = stats.Sum;

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
        if (filtered.TotalRejected > 0)
        {
            this.Summary += $" Left out: {filtered.Describe()}.";
        }

        if (filtered.SkippedForWantOfTime)
        {
            this.Summary += " This log has no readable elapsed times, so the throttle and steady rules were skipped.";
        }

        this.Status = this.Summary;
    }

    /// <summary>Put the proposed values into the open table as one undoable edit.</summary>
    public int Apply() =>
        this.editor.ApplyBatch(this.Results.Select(r => (r.Row, r.Column, r.NewScaled)), "VE wizard corrections");
}
