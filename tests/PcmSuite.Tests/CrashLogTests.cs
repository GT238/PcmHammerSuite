using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

// These tests point the app's data folder at a scratch folder through an environment variable, which the whole process shares.
[Collection("ScratchEnvironment")]
public class CrashLogTests
{
    private static string Scratch() => Path.Combine(Path.GetTempPath(), "pcmsuite-crash-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ACrash_LeavesAFileWithTheErrorAndTheBuildDetails()
    {
        string scratch = Scratch();
        string? old = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        try
        {
            string? path = CrashLog.Write("TestApp", new InvalidOperationException("boom"), "unit test");

            Assert.NotNull(path);
            Assert.StartsWith(Path.Combine(scratch, "crashes"), path);
            string text = File.ReadAllText(path!);
            Assert.Contains("TestApp", text);
            Assert.Contains("unit test", text);
            Assert.Contains("boom", text);
            Assert.Contains("PCM Hammer commit:", text);
            Assert.Single(CrashLog.Files());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", old);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [Fact]
    public void OnlyTheNewestCrashFilesAreKept()
    {
        string scratch = Scratch();
        string? old = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        try
        {
            for (int i = 0; i < 25; i++)
            {
                Assert.NotNull(CrashLog.Write("TestApp", new Exception("crash " + i), "unit test"));
                Thread.Sleep(2);        // file names carry milliseconds, so keep them distinct
            }

            string[] files = CrashLog.Files();
            Assert.Equal(20, files.Length);
            Assert.Contains("crash 24", File.ReadAllText(files[0]));        // the newest comes first
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", old);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [Fact]
    public void WritingTheLog_NeverThrows_EvenWhenTheFolderCannotBeMade()
    {
        string scratch = Scratch();
        string? old = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        Directory.CreateDirectory(scratch);
        string blocker = Path.Combine(scratch, "file-not-folder");
        File.WriteAllText(blocker, "x");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", blocker);       // "crashes" cannot be created inside a file
        try
        {
            Assert.Null(CrashLog.Write("TestApp", new Exception("boom"), "unit test"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", old);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [Fact]
    public void BuildInfo_SaysWhatItDoesNotKnow()
    {
        // Under a test host there is no stamped entry assembly, so the details read "unknown" rather than failing.
        string text = BuildInfo.Describe("TestApp");
        Assert.Contains("TestApp", text);
        Assert.Contains("PcmSuite commit:", text);
        Assert.Contains("PCM Hammer commit:", text);
    }
}
