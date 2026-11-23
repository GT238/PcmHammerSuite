using PcmHacking;
using PcmSuite.Tune;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>The rules that leave unusable samples out of the VE wizard, the delay shift, and the elapsed-time reader.</summary>
public class SampleFilterTests
{
    // Columns: 0 engine speed, 1 load, 2 coolant (F), 3 throttle, 4 injector pulse (ms), 5 air/fuel.
    private static LoggedData Log(params double[][] rows)
    {
        LoggedColumn[] columns =
        {
            LogFileReader.ParseHeading("PID 000C: Engine Speed (RPM)"),
            LogFileReader.ParseHeading("PID 000B: MAP Sensor (kPa)"),
            LogFileReader.ParseHeading("PID 0005: ECT Sensor (°F)"),
            LogFileReader.ParseHeading("PID 0011: Throttle Position (%)"),
            LogFileReader.ParseHeading("PID 125A: Injector PWM (ms)"),
            LogFileReader.ParseHeading("PID 0034: Air/Fuel Ratio (AFR)"),
        };
        return new LoggedData(columns, rows.Select(_ => DateTime.MinValue).ToList(), rows, 0);
    }

    private static double[] Row(double rpm = 2000, double map = 60, double coolant = 190, double tps = 20, double pulse = 3, double afr = 14.7)
        => new[] { rpm, map, coolant, tps, pulse, afr };

    private static SampleFilterSettings Rules() => new()
    {
        CoolantIndex = 2,
        MinCoolant = 160,
        ThrottleIndex = 3,
        MaxThrottleRate = 50,
        InjectorIndex = 4,
        FuelCutBelow = 0.3,
    };

    [Fact]
    public void ColdEngine_IsLeftOut()
    {
        LoggedData log = Log(Row(coolant: 100), Row(coolant: 159), Row(coolant: 160), Row(coolant: 200));
        SampleFilterResult result = SampleFilter.Evaluate(log, null, Rules(), 0, 1);
        Assert.Equal(new[] { false, false, true, true }, result.Accepted);
        Assert.Equal("2 engine cold", result.Describe());
    }

    [Fact]
    public void FuelCut_IsLeftOut()
    {
        LoggedData log = Log(Row(pulse: 0), Row(pulse: 0.3), Row(pulse: 0.31), Row(pulse: 3));
        SampleFilterResult result = SampleFilter.Evaluate(log, null, Rules(), 0, 1);
        Assert.Equal(new[] { false, false, true, true }, result.Accepted);
        Assert.Equal("2 fuel cut", result.Describe());
    }

    [Fact]
    public void ThrottleMovingFast_IsLeftOut_NeedsTimes()
    {
        // 10 samples a second. 20% to 30% in a tenth of a second is 100 %/s.
        LoggedData log = Log(Row(tps: 20), Row(tps: 20.2), Row(tps: 30), Row(tps: 30.1));
        double[] elapsed = { 0, 0.1, 0.2, 0.3 };

        SampleFilterResult result = SampleFilter.Evaluate(log, elapsed, Rules(), 0, 1);
        Assert.Equal(new[] { true, true, false, true }, result.Accepted);
        Assert.Equal("1 throttle moving", result.Describe());
        Assert.False(result.SkippedForWantOfTime);

        // Without elapsed times the rule cannot run, and says so.
        SampleFilterResult noTimes = SampleFilter.Evaluate(log, null, Rules(), 0, 1);
        Assert.All(noTimes.Accepted, Assert.True);
        Assert.True(noTimes.SkippedForWantOfTime);
    }

    [Fact]
    public void SteadyHold_NeedsTheWholeWindowToBeQuiet()
    {
        // Half-second steps; hold for 1 s. Engine speed jumps at the fifth sample.
        double[] rpms = { 2000, 2010, 2000, 2005, 2000, 3000, 3000, 3005, 3000, 3000 };
        LoggedData log = Log(rpms.Select(r => Row(rpm: r)).ToArray());
        double[] elapsed = Enumerable.Range(0, rpms.Length).Select(i => i * 0.5).ToArray();

        SampleFilterSettings rules = new() { SteadyMs = 1000, RpmTolerance = 100, LoadTolerance = 5 };
        SampleFilterResult result = SampleFilter.Evaluate(log, elapsed, rules, 0, 1);

        // The first two samples have not yet run a full second. The jump at 2.5 s spoils that sample and the next (their windows still reach back to 2000 rpm);
        // from 3.5 s on the whole window is at 3000 again.
        Assert.Equal(new[] { false, false, true, true, true, false, false, true, true, true }, result.Accepted);
    }

    [Fact]
    public void Rules_AreAppliedInOrder_AndEachSampleIsCountedOnce()
    {
        // Cold AND fuel cut: counted as cold only.
        LoggedData log = Log(Row(coolant: 80, pulse: 0));
        SampleFilterResult result = SampleFilter.Evaluate(log, null, Rules(), 0, 1);
        Assert.Equal("1 engine cold", result.Describe());
        Assert.Equal(1, result.TotalRejected);
    }

    [Fact]
    public void RuleOff_WhenItsColumnIsNotChosen()
    {
        LoggedData log = Log(Row(coolant: 10, pulse: 0));
        SampleFilterResult result = SampleFilter.Evaluate(log, null, new SampleFilterSettings(), 0, 1);
        Assert.True(result.Accepted[0]);
        Assert.Equal(0, result.TotalRejected);
        Assert.Equal(string.Empty, result.Describe());
    }

    [Fact]
    public void DelayedIndex_PointsAtTheFirstSampleThatIsLateEnough()
    {
        double[] elapsed = { 0, 0.1, 0.2, 0.3, 0.4, 0.5 };
        int[] later = SampleFilter.DelayedIndex(elapsed, 250);
        Assert.Equal(new[] { 3, 4, 5, -1, -1, -1 }, later);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, SampleFilter.DelayedIndex(elapsed, 0));
    }

    [Fact]
    public void ElapsedTimes_ReadFineTimesAndPlainSeconds_AndLineUpWithThePcmHammerReader()
    {
        string fine =
            "Clock Time, Elapsed Time, PID 000C: Engine Speed (RPM)\n" +
            "2026-11-05 09:51:28Z, 00:00:00.0883533, 1558\n" +
            "2026-11-05 09:51:28Z, 00:00:00.1829997, 1697\n" +
            "this row, is short\n" +
            "2026-11-05 09:51:29Z, 00:00:01.2500000, 1700\n";
        double[]? times = LogTiming.ReadElapsedSeconds(new StringReader(fine), 3);
        Assert.NotNull(times);
        Assert.Equal(0.0883533, times![0], 6);
        Assert.Equal(1.25, times[2], 6);

        // The short row is skipped by both readers, so the counts agree.
        LoggedData data = LogFileReader.Read(new StringReader(fine));
        Assert.Equal(data.Samples.Count, times.Length);

        string plain = "Clock, Elapsed, PID 000C: Engine Speed (RPM)\n2026-01-01 00:00:00Z, 0, 1000\n2026-01-01 00:00:01Z, 5, 1000\n";
        Assert.Equal(new[] { 0.0, 5.0 }, LogTiming.ReadElapsedSeconds(new StringReader(plain), 2));
    }

    [Fact]
    public void ElapsedTimes_AreNull_WhenTheyCannotBeTrusted()
    {
        string bad = "Clock, Elapsed, PID 000C: Engine Speed (RPM)\n2026-01-01 00:00:00Z, soon, 1000\n2026-01-01 00:00:01Z, later, 1000\n";
        Assert.Null(LogTiming.ReadElapsedSeconds(new StringReader(bad), 2));

        string good = "Clock, Elapsed, PID 000C: Engine Speed (RPM)\n2026-01-01 00:00:00Z, 0, 1000\n2026-01-01 00:00:01Z, 1, 1000\n";
        Assert.Null(LogTiming.ReadElapsedSeconds(new StringReader(good), 5));    // count does not match the samples
    }
}
