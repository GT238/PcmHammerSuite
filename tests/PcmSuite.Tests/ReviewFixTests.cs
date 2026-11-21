using PcmHacking;
using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Regression tests for problems found in the code review.</summary>
// These tests point the Tuner at a scratch folder through environment variables, which are shared by the whole process, so they must not run at the same time.
[Collection("ScratchEnvironment")]
public class ReviewFixTests
{
    // ---- folder containment ----

    [Theory]
    [InlineData(@"C:\Projects\Car\reads\a.bin", @"C:\Projects\Car", true)]
    [InlineData(@"C:\Projects\Car\reads\a.bin", @"C:\Projects\Car\", true)]
    [InlineData(@"C:\Projects\Car_2\reads\a.bin", @"C:\Projects\Car", false)]     // a sibling that starts with the same letters
    [InlineData(@"C:\Projects\car\reads\a.bin", @"C:\Projects\CAR", true)]
    [InlineData(@"C:\Elsewhere\a.bin", @"C:\Projects\Car", false)]
    [InlineData(null, @"C:\Projects\Car", false)]
    [InlineData(@"C:\Projects\Car\a.bin", null, false)]
    public void IsInside_NeedsAWholeFolderName(string? path, string? folder, bool expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;   // the sample paths are Windows paths
        }

        Assert.Equal(expected, AppPaths.IsInside(path, folder));
    }

    // ---- choosing log columns ----

    private static LoggedData Log(params string[] headings) => new(
        headings.Select(h => LogFileReader.ParseHeading(h)).ToList(),
        new List<DateTime>(),
        new List<double[]>(),
        0);

    [Fact]
    public void FindColumn_PrefersTheFirstKeyword_NotTheFirstColumn()
    {
        LoggedData log = Log("PID 0004: Calculated Load (%)", "PID 000B: Manifold Absolute Pressure (kPa)", "PID 000C: Engine Speed (RPM)");

        // Load comes first in the file, but "manifold" is the better word and must win.
        Assert.Equal(1, LogBinning.FindColumn(log, "manifold", "map", "load"));
        Assert.Equal(2, LogBinning.FindColumn(log, "engine speed", "rpm"));
    }

    [Fact]
    public void FindColumn_FallsBackToLaterKeywords_AndReportsNone()
    {
        LoggedData log = Log("PID 0004: Calculated Load (%)", "PID 000C: Engine Speed (RPM)");
        Assert.Equal(0, LogBinning.FindColumn(log, "manifold", "map", "load"));
        Assert.Equal(-1, LogBinning.FindColumn(log, "wideband"));
    }

    // ---- a bad XDF must not wreck the open session ----

    [RealXdfFact("12212156 - 2002 512k V1.xdf")]
    public async Task AnEmptyXdf_LeavesTheOpenBinAndTablesAlone()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            string bin = Path.Combine(scratch, "blank.bin");
            await File.WriteAllBytesAsync(bin, new byte[512 * 1024]);
            string empty = Path.Combine(scratch, "empty.xdf");
            await File.WriteAllTextAsync(empty, "<?xml version=\"1.0\"?><XDFFORMAT version=\"1.60\"><XDFHEADER><deftitle>nothing</deftitle></XDFHEADER></XDFFORMAT>");

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);
            await tune.OpenXdfAsync(TestData.Xdf("12212156 - 2002 512k V1.xdf")!);
            Assert.True(tune.Categories.Count > 0, "the real XDF should have loaded");

            byte[] before = tune.BinData!;
            before[0x8360] = 0x55;                       // an unsaved edit
            int categories = tune.Categories.Count;
            string? xdf = tune.XdfPath;

            await tune.OpenXdfAsync(empty);

            Assert.Same(before, tune.BinData);           // not replaced by a fresh read from disk
            Assert.Equal(0x55, tune.BinData![0x8360]);   // the edit is still there
            Assert.Equal(categories, tune.Categories.Count);
            Assert.Equal(xdf, tune.XdfPath);
            Assert.Contains("no tables", tune.Status);
            Assert.Contains("Still using", tune.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}

/// <summary>Choosing an XDF by hand adds it to the shared library, but never overwrites a different XDF that is already there.</summary>
// These tests point the Tuner at a scratch folder through environment variables, which are shared by the whole process, so they must not run at the same time.
[Collection("ScratchEnvironment")]
public class XdfLibraryCopyTests
{
    private const string Name = "12212156 - 2002 512k V1.xdf";

    private static async Task Run(Func<string, string, string, TuneViewModel, Task> body)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            string bin = Path.Combine(scratch, "blank.bin");
            await File.WriteAllBytesAsync(bin, new byte[512 * 1024]);
            string chosenFolder = Path.Combine(scratch, "downloads");
            Directory.CreateDirectory(chosenFolder);
            string chosen = Path.Combine(chosenFolder, Name);
            File.Copy(TestData.Xdf(Name)!, chosen);

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);
            await body(scratch, chosen, Path.Combine(ProjectInfo.XdfLibraryFolder, Name), tune);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [RealXdfFact(Name)]
    public Task NewXdf_IsCopiedIntoTheLibrary() => Run(async (_, chosen, library, tune) =>
    {
        await tune.OpenXdfAsync(chosen);

        Assert.True(File.Exists(library));
        Assert.Equal(File.ReadAllBytes(chosen), File.ReadAllBytes(library));
        Assert.Equal(library, tune.XdfPath);
        Assert.Contains("added to the XDF library", tune.Status);
    });

    [RealXdfFact(Name)]
    public Task ADifferentXdfWithTheSameName_IsNotOverwritten() => Run(async (_, chosen, library, tune) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        await File.WriteAllTextAsync(library, "somebody else's file");

        await tune.OpenXdfAsync(chosen);

        Assert.Equal("somebody else's file", await File.ReadAllTextAsync(library));   // untouched
        Assert.Equal(chosen, tune.XdfPath);                                            // the chosen file is the one in use
        Assert.Contains("not copied", tune.Status);
        Assert.True(tune.Categories.Count > 0);
    });

    [RealXdfFact(Name)]
    public Task TheSameXdfAlreadyInTheLibrary_IsLeftAlone() => Run(async (_, chosen, library, tune) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.Copy(chosen, library);

        await tune.OpenXdfAsync(chosen);

        Assert.Equal(library, tune.XdfPath);
        Assert.DoesNotContain("not copied", tune.Status);
    });
}
