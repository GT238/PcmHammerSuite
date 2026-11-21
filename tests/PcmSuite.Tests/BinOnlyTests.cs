using PcmSuite.Tune;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>A BIN with no matching XDF still opens, so the hex view, checksums and segment swap work on it.</summary>
// These tests point the Tuner at a scratch folder through environment variables, which are shared by the whole process, so they must not run at the same time.
[Collection("ScratchEnvironment")]
public class BinOnlyTests
{
    [Fact]
    public async Task BinWithNoXdf_IsStillOpened()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            string bin = Path.Combine(scratch, "mystery.bin");
            byte[] bytes = new byte[8192];
            bytes[100] = 0x42;
            await File.WriteAllBytesAsync(bin, bytes);

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);

            Assert.NotNull(tune.BinData);
            Assert.Equal(8192, tune.BinData!.Length);
            Assert.Equal(0x42, tune.BinData[100]);
            Assert.True(tune.IsBinOnly);
            Assert.False(tune.IsDirty);
            Assert.Contains("hex view", tune.Status);
            Assert.Null(tune.TableNameAt(100));      // no tables, so the hex view just shows bytes
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [Fact]
    public async Task SaveAs_WritesTheFile_AndKeepsTheSameOpenImage()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            string bin = Path.Combine(scratch, "in.bin");
            await File.WriteAllBytesAsync(bin, new byte[512 * 1024]);
            string saved = Path.Combine(scratch, "out.bin");

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);
            byte[] open = tune.BinData!;
            open[0x9000] = 0x42;

            await tune.SaveAsAsync(saved);

            Assert.Equal(512 * 1024, new FileInfo(saved).Length);
            Assert.Equal(0x42, (await File.ReadAllBytesAsync(saved))[0x9000]);   // the edit is in the file
            Assert.Same(open, tune.BinData);                                     // still the same array the editors point at
            Assert.False(tune.IsDirty);
            Assert.Contains("Saved as out.bin", tune.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}
