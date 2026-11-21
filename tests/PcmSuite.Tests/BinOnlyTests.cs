using PcmSuite.Tune;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>A BIN with no matching XDF still opens, so the hex view, checksums and segment swap work on it.</summary>
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
}
