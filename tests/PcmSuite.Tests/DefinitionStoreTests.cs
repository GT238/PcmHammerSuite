using System.Text;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Keeping the tables on screen as the Tuner's own definition for an OS, and finding it again for the next BIN.</summary>
public class DefinitionStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pcmsuite-defs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
        {
            Directory.Delete(this.folder, true);
        }
    }

    private static byte[] BinWith(params string[] ids)
    {
        byte[] bin = new byte[0x1000];
        int at = 0x100;
        foreach (string id in ids)
        {
            Encoding.ASCII.GetBytes(id).CopyTo(bin, at);
            at += 0x20;
        }

        return bin;
    }

    private static List<TableDefinition> Tables() => new()
    {
        new TableDefinition { Name = "Rev limit", Category = "Rev Limiter", Address = 0x200, ElementBits = 16, ScalingEquation = "X", ZUnits = "RPM", DecimalPlaces = 0 },
    };

    [Fact]
    public void PrimaryId_IsTheNumberThatAppearsMostOften()
    {
        // The calibration's own number is repeated; the others are only mentioned.
        byte[] bin = BinWith("12212156", "12345678", "12212156", "12212156");

        Assert.Equal("12212156", OsIdDetector.PrimaryId(bin));
    }

    [Fact]
    public void PrimaryId_IsNull_WhenTheBinHasNoPartNumber()
    {
        Assert.Null(OsIdDetector.PrimaryId(new byte[0x400]));
    }

    [Fact]
    public void Save_WritesAnXdfNamedForTheOs_ThatAutoDetectFindsAndTheParserReads()
    {
        byte[] bin = BinWith("12212156", "12212156");

        string path = DefinitionStore.Save(Tables(), bin, "some source.xdf", this.folder);

        Assert.Equal("12212156 - my definition.xdf", Path.GetFileName(path));
        Assert.Equal(new[] { "12212156 - my definition.xdf" }, OsIdDetector.FindMatchingXdfs(bin, this.folder));

        TableDefinition back = XdfParser.Parse(path).Single();
        Assert.Equal("Rev limit", back.Name);
        Assert.Equal(0x200u, back.Address);
        Assert.Contains("some source.xdf", File.ReadAllText(path));
    }

    [Fact]
    public void Save_NamesTheFileAfterTheOsThatTheSourceXdfCarries_NotJustTheFirstNumber()
    {
        // Three part numbers, each once (the OS and two calibrations); the first one found is not the OS.
        byte[] bin = BinWith("16264444", "16264634", "12212156");

        string path = DefinitionStore.Save(Tables(), bin, "12212156 - 2002 512k V1.xdf", this.folder);

        Assert.Equal("12212156 - my definition.xdf", Path.GetFileName(path));
    }

    [Fact]
    public void Save_FromAnEarlierDefinition_KeepsThatOs()
    {
        byte[] bin = BinWith("16264444", "12212156");

        string path = DefinitionStore.Save(Tables(), bin, "12212156 - my definition.xdf", this.folder);

        Assert.Equal("12212156 - my definition.xdf", Path.GetFileName(path));
    }

    [Fact]
    public void Save_WhenTheSourceNameHasNoPartNumber_FallsBackToTheMostCommonOne()
    {
        byte[] bin = BinWith("12212156", "12212156", "16264444");

        string path = DefinitionStore.Save(Tables(), bin, "my own file.xdf", this.folder);

        Assert.Equal("12212156 - my definition.xdf", Path.GetFileName(path));
    }

    [Fact]
    public void Save_NeverReplacesAnEarlierDefinition()
    {
        byte[] bin = BinWith("12212156");

        string first = DefinitionStore.Save(Tables(), bin, "a", this.folder);
        string second = DefinitionStore.Save(Tables(), bin, "b", this.folder);
        string third = DefinitionStore.Save(Tables(), bin, "c", this.folder);

        Assert.Equal("12212156 - my definition.xdf", Path.GetFileName(first));
        Assert.Equal("12212156 - my definition (2).xdf", Path.GetFileName(second));
        Assert.Equal("12212156 - my definition (3).xdf", Path.GetFileName(third));
        Assert.Contains("from a.", File.ReadAllText(first));
    }

    [Fact]
    public void Save_WithNoOsIdInTheBin_SaysSo_AndWritesNothing()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => DefinitionStore.Save(Tables(), new byte[0x400], "x", this.folder));

        Assert.Contains("No OS ID", error.Message);
        Assert.False(Directory.Exists(this.folder) && Directory.GetFiles(this.folder).Length > 0);
    }

    [Fact]
    public void Save_WithNoTables_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DefinitionStore.Save(new List<TableDefinition>(), BinWith("12212156"), "x", this.folder));
    }
}
