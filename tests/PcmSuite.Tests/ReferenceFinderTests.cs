using System.Text;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using PcmSuite.Tuning.Core.Services;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Picking, from the BINs the user already has, the best reference for a BIN that has no XDF.</summary>
public class ReferenceFinderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pcmsuite-refs-" + Guid.NewGuid().ToString("N"));

    public ReferenceFinderTests() => Directory.CreateDirectory(this.root);

    public void Dispose()
    {
        try { Directory.Delete(this.root, true); } catch { }
    }

    private const int Size = 0x2000;

    private static byte[] Random(int seed, int length = Size)
    {
        byte[] b = new byte[length];
        new System.Random(seed).NextBytes(b);
        return b;
    }

    private static void Id(byte[] bin, string id, int at = 0x1F00)
    {
        Array.Clear(bin, at, 24);
        Encoding.ASCII.GetBytes(id).CopyTo(bin, at);
    }

    private string Save(string project, string name, byte[] data)
    {
        string folder = Path.Combine(this.root, project);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private void WriteXdf(string id, bool readable = true)
    {
        string folder = Path.Combine(this.root, "XDFs");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, id + " - definition.xdf");
        if (!readable)
        {
            File.WriteAllBytes(path, new byte[] { 0x05, 0x22, 0x97, 0x01, 0xDE, 0xAD, 0xBE, 0xEF });
            return;
        }

        List<TableDefinition> tables = new();
        // Two tables before the point where the target gained eight bytes and two after it, so each shift has two tables behind it.
        int[] at = { 0x100, 0x1A0, 0x400, 0x700 };
        for (int i = 0; i < at.Length; i++)
        {
            tables.Add(new TableDefinition { Name = "T" + i, Category = "Airflow", Address = (uint)at[i], Rows = 4, Columns = 4, ElementBits = 16 });
        }

        XdfWriter.Write(path, tables, "OS" + id, "test", Size);
    }

    private IReadOnlyList<string> FindXdfs(string path, byte[] data) =>
        OsIdDetector.FindMatchingXdfs(data, Path.Combine(this.root, "XDFs")).Select(n => Path.Combine(this.root, "XDFs", n)).ToList();

    /// <summary>The same calibration with eight bytes inserted at 0x200 (what is after moves up by 8), under a different part number.</summary>
    private static byte[] Moved(byte[] source, string id)
    {
        byte[] b = new byte[source.Length];
        Array.Copy(source, 0, b, 0, 0x200);
        new System.Random(3).NextBytes(b.AsSpan(0x200, 8));
        Array.Copy(source, 0x200, b, 0x208, source.Length - 0x208);
        Id(b, id);
        return b;
    }

    [Fact]
    public void TheClosestBin_ComesFirst_AndEverythingUnsuitableIsLeftOut()
    {
        byte[] close = Random(1);
        Id(close, "12111111");
        byte[] target = Moved(close, "12999999");
        byte[] unrelated = Random(2);
        Id(unrelated, "12222222");
        WriteXdf("12111111");
        WriteXdf("12222222");

        List<(string, string)> candidates = new()
        {
            (this.Save("Truck", "close.bin", close), "Truck"),
            (this.Save("Car", "unrelated.bin", unrelated), "Car"),
            (this.Save("Car", "other size.bin", Random(1, Size * 2)), "Car"),
            (this.Save("Truck", "the target itself.bin", target), "Truck"),
            (this.Save("Elsewhere", "copy of close.bin", (byte[])close.Clone()), "Elsewhere"),
        };

        IReadOnlyList<ReferenceCandidate> ranked = ReferenceFinder.Rank(target, Path.Combine(this.root, "Truck", "the target itself.bin"), candidates, this.FindXdfs);

        Assert.Equal("close.bin", ranked[0].FileName);
        Assert.Equal("Truck", ranked[0].Origin);
        Assert.Equal(4, ranked[0].Content);
        Assert.Equal("unrelated.bin", ranked[1].FileName);
        Assert.Equal(0, ranked[1].Content);
        Assert.Equal(2, ranked.Count);                       // the other size, the target and the exact copy are not listed
        Assert.True(ranked[0].Similarity > ranked[1].Similarity);
    }

    [Fact]
    public void ATunedCopyThatGivesTheSameResult_IsNotListedTwice()
    {
        byte[] close = Random(1);
        Id(close, "12111111");
        byte[] tuned = (byte[])close.Clone();
        tuned[0x1000] ^= 0x01;                                // one cell changed
        byte[] target = Moved(close, "12999999");
        WriteXdf("12111111");

        List<(string, string)> candidates = new()
        {
            (this.Save("Car", "v1.bin", close), "Car"),
            (this.Save("Car", "v2.bin", tuned), "Car"),
        };

        IReadOnlyList<ReferenceCandidate> ranked = ReferenceFinder.Rank(target, null, candidates, this.FindXdfs);

        Assert.Single(ranked);
    }

    [Fact]
    public void ABinWhoseXdfCannotBeRead_IsSkipped_ButOneWithoutAnXdfIsToo()
    {
        byte[] protectedBin = Random(1);
        Id(protectedBin, "12333333");
        byte[] noXdf = Random(4);
        Id(noXdf, "12444444");
        byte[] target = Moved(protectedBin, "12999999");
        WriteXdf("12333333", readable: false);

        List<(string, string)> candidates = new()
        {
            (this.Save("Car", "protected.bin", protectedBin), "Car"),
            (this.Save("Car", "no xdf.bin", noXdf), "Car"),
        };

        Assert.Empty(ReferenceFinder.Rank(target, null, candidates, this.FindXdfs));
    }

    [Fact]
    public void Gather_ListsBinsFromEveryProject_WithTheirFolder_AndSkipsTheXdfLibrary()
    {
        this.Save("Truck", "a.bin", Random(1));
        this.Save(Path.Combine("Truck", "reads"), "b.bin", Random(2));
        this.Save("Reference BINs", "c.bin", Random(3));
        this.Save("XDFs", "inside the library.bin", Random(4));
        this.Save("Car", "notes.txt", Random(5));

        IReadOnlyList<(string Path, string Origin)> found = ReferenceFinder.Gather(this.root, Path.Combine(this.root, "XDFs"));

        Assert.Equal(new[] { "a.bin", "b.bin", "c.bin" }, found.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n).ToArray());
        Assert.Equal("Truck", found.Single(f => f.Path.EndsWith("b.bin")).Origin);
        Assert.Equal("Reference BINs", found.Single(f => f.Path.EndsWith("c.bin")).Origin);
    }

    [Fact]
    public void Similarity_IsHighForTheSameCalibration_AndLowForUnrelatedFiles()
    {
        byte[] a = Random(1);
        HashSet<ulong> windows = ReferenceFinder.Windows(Moved(a, "12999999"));

        Assert.True(ReferenceFinder.Similarity(a, windows) > 0.9);
        Assert.True(ReferenceFinder.Similarity(Random(2), windows) < 0.05);
        Assert.Equal(0, ReferenceFinder.Similarity(new byte[Size], windows));   // erased flash says nothing
    }

    // ── With the real files: for the Truck BIN, the Tahoe is a closer reference than the Impala ──

    [Fact]
    public void RealFiles_ForTheTruckBin_TheTahoeRanksFirst()
    {
        string projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcmSuite Projects");
        string library = Path.Combine(projects, "XDFs");
        string? truck = Directory.Exists(projects)
            ? Directory.GetFiles(projects, "pcm_read_20260408_140451.bin", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (truck == null || !File.Exists(Path.Combine(library, "12212156 - 2002 512k V1.xdf")))
        {
            return;   // the real files are only on the developer's machine
        }

        IReadOnlyList<(string Path, string Origin)> all = ReferenceFinder.Gather(projects, library);
        IReadOnlyList<ReferenceCandidate> ranked = ReferenceFinder.Rank(File.ReadAllBytes(truck), truck, all,
            (path, data) => OsIdDetector.FindMatchingXdfs(data, library).Select(n => Path.Combine(library, n)).ToList());

        Assert.NotEmpty(ranked);
        Assert.Contains("Tahoe", ranked[0].FileName);
        Assert.True(ranked[0].Content > 50);
    }
}
