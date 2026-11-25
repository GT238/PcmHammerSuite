using System.Globalization;
using PcmHacking;
using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Placing logged samples in a table's cells, and the VE wizard that relies on it.</summary>
public class LogBinningTests
{
    private static readonly double[] Rpm = { 1000, 2000, 3000 };
    private static readonly double[] Kpa = { 40, 60, 80 };

    /// <summary>A 3 x 3 table: engine speed across the columns, load down the rows, every cell holding 100.</summary>
    private static ValueEditorViewModel Table(out byte[] bin)
    {
        TableDefinition table = new()
        {
            Name = "Test VE",
            Address = 0,
            Rows = 3,
            Columns = 3,
            ElementBits = 8,
            ScalingEquation = "X",
            XLabels = Rpm.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList(),
            YLabels = Kpa.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList(),
            XLabelsAreReal = true,
            YLabelsAreReal = true,
            YUnits = "kPa",
        };

        bin = Enumerable.Repeat((byte)100, 64).ToArray();
        return new ValueEditorViewModel(table, bin, null);
    }

    private static LoggedData Log(params double[][] samples) => new(
        new[]
        {
            LogFileReader.ParseHeading("PID 000C: Engine Speed (RPM)"),
            LogFileReader.ParseHeading("PID 000B: Manifold Absolute Pressure (kPa)"),
            LogFileReader.ParseHeading("PID 0034: Air/Fuel Ratio (AFR)"),
        },
        samples.Select((_, i) => DateTime.MinValue.AddSeconds(i)).ToList(),
        samples,
        0);

    [Theory]
    [InlineData(1000, 0)]
    [InlineData(1400, 0)]
    [InlineData(1600, 1)]
    [InlineData(3000, 2)]
    [InlineData(3400, 2)]    // within half a step beyond the last cell
    [InlineData(4200, -1)]   // clearly outside the table
    [InlineData(-200, -1)]
    public void Nearest_PicksTheClosestAxisValue(double x, int expected) =>
        Assert.Equal(expected, LogBinning.Nearest(Rpm, x));

    [Fact]
    public void Nearest_OfAnEmptyAxis_IsOutside() => Assert.Equal(-1, LogBinning.Nearest(Array.Empty<double>(), 5));

    [Fact]
    public void Axes_AreReadFromTheTable()
    {
        ValueEditorViewModel editor = Table(out _);
        Assert.True(LogBinning.TryReadAxes(editor, out double[] cols, out double[] rows, out _));
        Assert.Equal(Rpm, cols);
        Assert.Equal(Kpa, rows);
    }

    [Fact]
    public void Axes_AreRefused_WhenTheXdfDoesNotListThem()
    {
        ValueEditorViewModel editor = Table(out _);
        editor.Table.XLabelsAreReal = false;
        Assert.False(LogBinning.TryReadAxes(editor, out _, out _, out string error));
        Assert.Contains("axis values", error);
    }

    [Fact]
    public void Bin_CountsAverageMinMax_PerCell_AndCountsStrays()
    {
        LoggedData log = Log(
            new double[] { 2000, 60, 14.0 },
            new double[] { 2100, 58, 15.0 },
            new double[] { 1900, 62, 16.0 },
            new double[] { 1000, 40, 12.0 },
            new double[] { 9000, 60, 14.0 },     // beyond the table
            new double[] { 0, 60, 14.0 },        // engine not running
            new[] { 2000, 60, double.NaN });     // no reading

        CellStats stats = LogBinning.Bin(log, 0, 1, s => s[2], Rpm, Kpa, rpmOnColumns: true);

        Assert.Equal(3, stats.Count[1, 1]);
        Assert.Equal(15.0, stats.Average(1, 1), 6);
        Assert.Equal(14.0, stats.Min[1, 1]);
        Assert.Equal(16.0, stats.Max[1, 1]);
        Assert.Equal(1, stats.Count[0, 0]);
        Assert.Equal(4, stats.Used);
        Assert.Equal(1, stats.Outside);
        Assert.True(double.IsNaN(stats.Value(CellStat.Average, 2, 2)));
        Assert.Equal(3, stats.MaxCount());
    }

    [Fact]
    public void Bin_PutsRpmOnTheRows_WhenTheTableIsTheOtherWayRound()
    {
        // Same axes swapped: rows are engine speed, columns are load.
        LoggedData log = Log(new double[] { 3000, 40, 14.0 });
        CellStats stats = LogBinning.Bin(log, 0, 1, s => s[2], Kpa, Rpm, rpmOnColumns: false);
        Assert.Equal(1, stats.Count[2, 0]);
    }

    [Fact]
    public void Bin_WithoutAMeasurement_JustCounts()
    {
        LoggedData log = Log(new double[] { 2000, 60, 14.0 }, new double[] { 2000, 60, 99.0 });
        CellStats stats = LogBinning.Bin(log, 0, 1, _ => 1, Rpm, Kpa, true);
        Assert.Equal(2, stats.Count[1, 1]);
        Assert.Equal(2.0, stats.Value(CellStat.Count, 1, 1));
    }

    [Fact]
    public async Task VeWizard_ProposesTheAverageCorrection_AndIgnoresThinCells()
    {
        ValueEditorViewModel editor = Table(out _);
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-bin-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            List<string> lines = new() { "Time, Elapsed, PID 000C: Engine Speed (RPM), PID 000B: Manifold Absolute Pressure (kPa), PID 0034: Air/Fuel Ratio (AFR)" };
            for (int i = 0; i < 6; i++)
            {
                lines.Add($"2026-01-01 00:00:{i:00}Z, {i}, 2000, 60, 16.17");     // 10% lean in the middle cell
            }

            lines.Add("2026-01-01 00:00:07Z, 7, 1000, 40, 14.7");                 // only two samples: below the minimum
            lines.Add("2026-01-01 00:00:08Z, 8, 1000, 40, 14.7");
            lines.Add("2026-01-01 00:00:09Z, 9, 9000, 60, 14.7");                 // outside the table
            await File.WriteAllLinesAsync(path, lines);

            VeWizardViewModel wizard = new(editor);
            await wizard.LoadAsync(path);
            Assert.Equal(0, wizard.RpmIndex);
            Assert.Equal(1, wizard.LoadIndex);
            Assert.Equal(2, wizard.ValueIndex);

            wizard.Calculate();

            VeCellResult cell = Assert.Single(wizard.Results);
            Assert.Equal(1, cell.Row);
            Assert.Equal(1, cell.Column);
            Assert.Equal(6, cell.Samples);
            Assert.Equal(110.0, cell.NewScaled, 1);
            Assert.Contains("8 samples used", wizard.Summary);
            Assert.Contains("1 fell outside", wizard.Summary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string FilterHeader =
        "Clock, Elapsed, PID 000C: Engine Speed (RPM), PID 000B: MAP Sensor (kPa), PID 0005: ECT Sensor (°F), PID 0011: Throttle Position (%), PID 125A: Injector PWM (ms), PID 0034: Air/Fuel Ratio (AFR)";

    [Fact]
    public async Task VeWizard_LeavesOutColdAndFuelCutSamples_AndSaysSo()
    {
        ValueEditorViewModel editor = Table(out _);
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-filter-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            List<string> lines = new() { FilterHeader };
            int n = 0;
            for (int i = 0; i < 6; i++)
            {
                lines.Add($"2026-01-01 00:00:00Z, {n++ * 0.1:0.0}, 2000, 60, 190, 20, 3.0, 16.17");   // 10% lean, warm, fuelled
            }

            for (int i = 0; i < 3; i++)
            {
                lines.Add($"2026-01-01 00:00:00Z, {n++ * 0.1:0.0}, 2000, 60, 100, 20, 3.0, 22.0");    // cold: would drag the average up
            }

            for (int i = 0; i < 2; i++)
            {
                lines.Add($"2026-01-01 00:00:00Z, {n++ * 0.1:0.0}, 2000, 60, 190, 20, 0.0, 30.0");    // fuel cut: lean by definition
            }

            await File.WriteAllLinesAsync(path, lines);
            VeWizardViewModel wizard = new(editor);
            await wizard.LoadAsync(path);

            // Picked up from the log's own columns.
            Assert.True(wizard.CoolantIndex > 0);
            Assert.True(wizard.ThrottleIndex > 0);
            Assert.True(wizard.InjectorIndex > 0);
            Assert.Equal(160, wizard.MinCoolant);       // a Fahrenheit column
            Assert.True(wizard.HasTiming);

            wizard.Calculate();

            VeCellResult cell = Assert.Single(wizard.Results);
            Assert.Equal(6, cell.Samples);
            Assert.Equal(110.0, cell.NewScaled, 1);
            Assert.Contains("6 samples used", wizard.Summary);
            Assert.Contains("Left out: 3 engine cold, 2 fuel cut", wizard.Summary);

            // With the rules switched off the unusable samples would have been averaged in.
            wizard.CoolantIndex = 0;
            wizard.InjectorIndex = 0;
            wizard.MaxChange = 50;
            wizard.Calculate();
            Assert.True(Math.Abs(Assert.Single(wizard.Results).Percent - 10) > 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VeWizard_WidebandDelay_PairsEachSampleWithALaterReading()
    {
        ValueEditorViewModel editor = Table(out _);
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-delay-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            // The engine is lean from the sixth reading on; the wideband sees that half a second after it happens.
            List<string> lines = new() { FilterHeader };
            for (int i = 0; i < 10; i++)
            {
                double afr = i < 5 ? 14.7 : 16.17;
                lines.Add($"2026-01-01 00:00:00Z, {i * 0.1:0.0}, 2000, 60, 190, 20, 3.0, {afr}");
            }

            await File.WriteAllLinesAsync(path, lines);
            VeWizardViewModel wizard = new(editor);
            await wizard.LoadAsync(path);
            wizard.MinSamples = 5;

            wizard.Calculate();
            Assert.Equal(5.0, Assert.Single(wizard.Results).Percent, 1);      // no delay: half the samples are on each side

            wizard.DelayMs = 500;
            wizard.Calculate();
            VeCellResult cell = Assert.Single(wizard.Results);
            Assert.Equal(5, cell.Samples);                                   // the last five have no later reading
            Assert.Equal(10.0, cell.Percent, 1);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Real files written by the Logger (engine-off logs from the Truck): read the same way the log map and VE wizard read them.</summary>
public class RealLoggerFileTests
{
    [Fact]
    public void RealLoggerCsvFiles_AreRead_AndAnEngineOffLogPlacesNothingInATable()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] files = Directory.Exists(docs) ? Directory.GetFiles(docs, "20261104_*.csv") : Array.Empty<string>();
        string? binPath = TestData.Bin("Impala_LS2_P1_Modified_v3.bin");
        string? xdfPath = TestData.Xdf("12212156 - 2002 512k V1.xdf");
        if (files.Length == 0 || binPath == null || xdfPath == null)
        {
            return;   // the real files are only on the developer's machine
        }

        byte[] bin = File.ReadAllBytes(binPath);
        TableDefinition ve = PcmSuite.Tuning.Core.Parsers.XdfParser.Parse(xdfPath).Single(t => t.Name.StartsWith("B0101 - Main VE Table"));
        ValueEditorViewModel editor = new(ve, bin, null);
        Assert.True(LogBinning.TryReadAxes(editor, out double[] columns, out double[] rows, out string error), error);

        foreach (string file in files)
        {
            using StreamReader reader = new(file);
            LoggedData log = LogFileReader.Read(reader);

            int rpm = LogBinning.FindColumn(log, "engine speed", "rpm");
            int map = LogBinning.FindColumn(log, "manifold", "map", "load");
            int stft = LogBinning.FindColumn(log, "short term");
            Assert.True(rpm >= 0 && map >= 0 && stft >= 0, file);
            Assert.True(log.Samples.Count > 50, file);

            CellStats stats = LogBinning.Bin(log, rpm, map, s => s[stft], columns, rows, rpmOnColumns: true);

            // The engine was off (0 rpm throughout): nothing counts, and nothing fails.
            Assert.Equal(0, stats.Used);
        }
    }
}
