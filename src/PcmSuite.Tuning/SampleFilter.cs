using System.Globalization;
using PcmHacking;

namespace PcmSuite.Tune;

/// <summary>
/// Which logged samples the VE wizard should leave out, and by what rule. A column index of -1 turns that rule off.
/// </summary>
public sealed class SampleFilterSettings
{
    /// <summary>Coolant temperature column, and the lowest reading (in that column's own units) that still counts. A cold engine runs warm-up enrichment.</summary>
    public int CoolantIndex { get; set; } = -1;

    public double MinCoolant { get; set; }

    /// <summary>Throttle column, and the fastest it may move, in percent per second. Needs the log's elapsed times.</summary>
    public int ThrottleIndex { get; set; } = -1;

    public double MaxThrottleRate { get; set; } = 50;

    /// <summary>Injector pulse (ms) or duty (%) column; a reading at or below <see cref="FuelCutBelow"/> means the fuel was cut (deceleration).</summary>
    public int InjectorIndex { get; set; } = -1;

    public double FuelCutBelow { get; set; }

    /// <summary>Engine speed and load must have stayed within the tolerances for this long before a sample counts. 0 turns it off. Needs the log's elapsed times.</summary>
    public double SteadyMs { get; set; }

    public double RpmTolerance { get; set; } = 100;

    public double LoadTolerance { get; set; } = 3;
}

/// <summary>What <see cref="SampleFilter.Evaluate"/> decided.</summary>
public sealed class SampleFilterResult
{
    public SampleFilterResult(bool[] accepted, IReadOnlyList<(string Reason, int Count)> rejected, bool skippedForWantOfTime)
    {
        this.Accepted = accepted;
        this.Rejected = rejected;
        this.SkippedForWantOfTime = skippedForWantOfTime;
    }

    /// <summary>One flag per sample; false means a rule left it out.</summary>
    public bool[] Accepted { get; }

    /// <summary>Why samples were left out, in the order the rules are applied, with how many each rule removed. Rules that removed nothing are not listed.</summary>
    public IReadOnlyList<(string Reason, int Count)> Rejected { get; }

    /// <summary>True when a rule that needs the elapsed times (throttle rate, steady hold) was switched on but the log had none to read.</summary>
    public bool SkippedForWantOfTime { get; }

    public int TotalRejected => this.Rejected.Sum(r => r.Count);

    /// <summary>"412 engine cold, 230 throttle moving", or an empty string.</summary>
    public string Describe() => string.Join(", ", this.Rejected.Select(r => $"{r.Count:N0} {r.Reason}"));
}

/// <summary>
/// The sample rules behind the VE wizard. A sample taken while the engine is cold, the fuel is cut, the throttle is moving or the
/// engine has not settled does not say what the VE table should hold, so it is left out rather than averaged in.
/// </summary>
public static class SampleFilter
{
    public const string Cold = "engine cold";
    public const string FuelCut = "fuel cut";
    public const string ThrottleMoving = "throttle moving";
    public const string NotSteady = "not steady";

    /// <summary>
    /// Decide every sample. <paramref name="elapsed"/> is the seconds since the log began for each sample (see <see cref="LogTiming"/>),
    /// or null when the log has none; the two rules that need it are then skipped.
    /// </summary>
    public static SampleFilterResult Evaluate(LoggedData log, double[]? elapsed, SampleFilterSettings settings, int rpmIndex, int loadIndex)
    {
        int count = log.Samples.Count;
        bool[] accepted = new bool[count];
        int cold = 0, cut = 0, throttle = 0, steady = 0;
        bool wantsTime = (settings.ThrottleIndex >= 0 || settings.SteadyMs > 0);
        bool haveTime = elapsed != null && elapsed.Length == count;

        for (int i = 0; i < count; i++)
        {
            double[] sample = log.Samples[i];
            accepted[i] = true;

            if (settings.CoolantIndex >= 0 && Below(sample[settings.CoolantIndex], settings.MinCoolant))
            {
                accepted[i] = false;
                cold++;
                continue;
            }

            if (settings.InjectorIndex >= 0 && AtOrBelow(sample[settings.InjectorIndex], settings.FuelCutBelow))
            {
                accepted[i] = false;
                cut++;
                continue;
            }

            if (haveTime && settings.ThrottleIndex >= 0 && i > 0 && ThrottleRate(log, elapsed!, settings.ThrottleIndex, i) > settings.MaxThrottleRate)
            {
                accepted[i] = false;
                throttle++;
                continue;
            }

            if (haveTime && settings.SteadyMs > 0 && !IsSteady(log, elapsed!, settings, rpmIndex, loadIndex, i))
            {
                accepted[i] = false;
                steady++;
            }
        }

        List<(string, int)> rejected = new();
        if (cold > 0)
        {
            rejected.Add((Cold, cold));
        }

        if (cut > 0)
        {
            rejected.Add((FuelCut, cut));
        }

        if (throttle > 0)
        {
            rejected.Add((ThrottleMoving, throttle));
        }

        if (steady > 0)
        {
            rejected.Add((NotSteady, steady));
        }

        return new SampleFilterResult(accepted, rejected, wantsTime && !haveTime);
    }

    /// <summary>
    /// For each sample, the index of the first sample taken at least <paramref name="delayMs"/> later, or -1 when the log ends first.
    /// A wideband reading describes the exhaust gas that left the cylinder a moment before, so the reading to pair with a sample is the later one.
    /// </summary>
    public static int[] DelayedIndex(double[] elapsed, double delayMs)
    {
        int[] result = new int[elapsed.Length];
        double delay = delayMs / 1000.0;
        int j = 0;
        for (int i = 0; i < elapsed.Length; i++)
        {
            if (j < i)
            {
                j = i;
            }

            while (j < elapsed.Length && elapsed[j] < elapsed[i] + delay)
            {
                j++;
            }

            result[i] = j < elapsed.Length ? j : -1;
        }

        return result;
    }

    private static bool Below(double value, double limit) => !double.IsNaN(value) && value < limit;

    private static bool AtOrBelow(double value, double limit) => !double.IsNaN(value) && value <= limit;

    private static double ThrottleRate(LoggedData log, double[] elapsed, int column, int i)
    {
        double now = log.Samples[i][column];
        double before = log.Samples[i - 1][column];
        double dt = elapsed[i] - elapsed[i - 1];
        if (double.IsNaN(now) || double.IsNaN(before) || dt <= 0)
        {
            return 0;
        }

        return Math.Abs(now - before) / dt;
    }

    private static bool IsSteady(LoggedData log, double[] elapsed, SampleFilterSettings settings, int rpmIndex, int loadIndex, int i)
    {
        double window = settings.SteadyMs / 1000.0;

        // Until the log has run for the whole window there is no way to know the engine was steady.
        if (elapsed[i] - elapsed[0] < window)
        {
            return false;
        }

        double rpm = log.Samples[i][rpmIndex];
        double load = log.Samples[i][loadIndex];
        for (int j = i - 1; j >= 0 && elapsed[j] >= elapsed[i] - window; j--)
        {
            double r = log.Samples[j][rpmIndex];
            double l = log.Samples[j][loadIndex];
            if ((!double.IsNaN(r) && !double.IsNaN(rpm) && Math.Abs(r - rpm) > settings.RpmTolerance)
                || (!double.IsNaN(l) && !double.IsNaN(load) && Math.Abs(l - load) > settings.LoadTolerance))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Reads the "Elapsed Time" column of a Logger file. PCM Hammer's reader keeps only the whole-second clock, which is too coarse for
/// the filters that work in fractions of a second, so the finer column is read here.
/// </summary>
public static class LogTiming
{
    /// <summary>
    /// Seconds since the start of the log for each sample, in the same order as the samples PCM Hammer's reader returns, or null when the
    /// file has no readable elapsed times.
    /// </summary>
    public static double[]? ReadElapsedSeconds(string path, int expectedSamples)
    {
        try
        {
            using StreamReader reader = new(path);
            return ReadElapsedSeconds(reader, expectedSamples);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static double[]? ReadElapsedSeconds(TextReader reader, int expectedSamples)
    {
        string? header = reader.ReadLine();
        if (header == null)
        {
            return null;
        }

        int fields = header.Split(',').Length;
        List<double> seconds = new();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split(',');

            // The same rule PCM Hammer's reader uses, so row i here is sample i there.
            if (parts.Length != fields)
            {
                continue;
            }

            if (!TryParse(parts[1].Trim(), out double value))
            {
                return null;
            }

            seconds.Add(value);
        }

        return seconds.Count == expectedSamples && seconds.Count > 1 ? seconds.ToArray() : null;
    }

    private static bool TryParse(string text, out double seconds)
    {
        if (text.Contains(':') && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan span))
        {
            seconds = span.TotalSeconds;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }
}
