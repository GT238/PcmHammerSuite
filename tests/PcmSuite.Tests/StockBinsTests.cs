using System.Text;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

public class StockBinsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pcmsuite-stock-" + Guid.NewGuid().ToString("N"));

    public StockBinsTests() => Directory.CreateDirectory(this.folder);

    public void Dispose()
    {
        try { Directory.Delete(this.folder, true); } catch { }
    }

    // A small fake BIN: zeros with GM part numbers written at fixed places, and a marker byte so two files can differ.
    private static byte[] Bin(int size, byte marker, params string[] osIds)
    {
        byte[] data = new byte[size];
        data[size - 1] = marker;
        for (int i = 0; i < osIds.Length; i++)
        {
            Encoding.ASCII.GetBytes(osIds[i]).CopyTo(data, 0x100 + i * 0x20);
        }

        return data;
    }

    private string Save(string name, byte[] data, string? subFolder = null)
    {
        string dir = subFolder == null ? this.folder : Path.Combine(this.folder, subFolder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void FindsTheFileWithTheSamePartNumber()
    {
        this.Save("other-car.bin", Bin(4096, 1, "12299999"));
        string stock = this.Save("stock.bin", Bin(4096, 2, "12212156", "12200000"));

        Assert.Equal(stock, StockBins.FindMatch(Bin(4096, 9, "12212156"), null, this.folder));
    }

    [Fact]
    public void PrefersTheFileSharingTheMostPartNumbers()
    {
        this.Save("one-shared.bin", Bin(4096, 1, "12212156"));
        string both = this.Save("two-shared.bin", Bin(4096, 2, "12212156", "12200000"));

        Assert.Equal(both, StockBins.FindMatch(Bin(4096, 9, "12212156", "12200000"), null, this.folder));
    }

    [Fact]
    public void IgnoresADifferentSizeTheFileItselfAndAnExactCopy()
    {
        byte[] open = Bin(4096, 9, "12212156");
        string own = this.Save("mine.bin", open);

        this.Save("copy.bin", (byte[])open.Clone());                         // identical: nothing to compare
        this.Save("bigger.bin", Bin(8192, 1, "12212156"));                    // a compare needs the same size

        Assert.Null(StockBins.FindMatch(open, own, this.folder));
    }

    [Fact]
    public void SearchesFoldersInsideAndSurvivesAnUnreadableFile()
    {
        string nested = this.Save("stock.bin", Bin(4096, 2, "12212156"), "Impala");
        this.Save("not-a-bin.bin", Array.Empty<byte>());                       // empty file: different size, skipped

        Assert.Equal(nested, StockBins.FindMatch(Bin(4096, 9, "12212156"), null, this.folder));
    }

    [Fact]
    public void ReturnsNullWithoutPartNumbersOrWithoutTheFolder()
    {
        this.Save("stock.bin", Bin(4096, 2, "12212156"));

        Assert.Null(StockBins.FindMatch(new byte[4096], null, this.folder));                       // no part number in the open BIN
        Assert.Null(StockBins.FindMatch(Bin(4096, 9, "12212156"), null, Path.Combine(this.folder, "missing")));
    }
}
