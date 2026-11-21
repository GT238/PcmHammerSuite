using System.Text;
using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Matching a BIN to its XDF by OS ID, and the Help tab's search.</summary>
public class OsIdAndHelpTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pcmsuite-tests-" + Guid.NewGuid().ToString("N"));

    public OsIdAndHelpTests() => Directory.CreateDirectory(this.folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.folder, true);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] BinWith(params (int At, string Text)[] strings)
    {
        byte[] bin = new byte[4096];
        foreach ((int at, string text) in strings)
        {
            Encoding.ASCII.GetBytes(text).CopyTo(bin, at);
        }

        return bin;
    }

    private void Xdf(string name) => File.WriteAllText(Path.Combine(this.folder, name), "<XDFFORMAT/>");

    [Fact]
    public void ScanForOsIds_FindsEightDigitPartNumbers_InOrder_WithoutDuplicates()
    {
        byte[] bin = BinWith((100, "12212156"), (300, "12587603"), (500, "12212156"));
        Assert.Equal(new[] { "12212156", "12587603" }, OsIdDetector.ScanForOsIds(bin).ToArray());
    }

    [Fact]
    public void ScanForOsIds_IgnoresLongerDigitRuns_AndNumbersNotStartingWith12To19()
    {
        byte[] bin = BinWith((100, "122121561"), (300, "20212156"), (500, "11212156"));
        Assert.Empty(OsIdDetector.ScanForOsIds(bin));
    }

    [Fact]
    public void FindMatchingXdfs_RanksByHowOftenTheIdAppears()
    {
        this.Xdf("OS 12212156 2002.xdf");
        this.Xdf("OS 12587603 latest.xdf");
        this.Xdf("OS 99999999 other.xdf");
        byte[] bin = BinWith((100, "12212156"), (300, "12587603"), (500, "12587603"), (700, "12587603"));

        IReadOnlyList<string> matches = OsIdDetector.FindMatchingXdfs(bin, this.folder);

        Assert.Equal(new[] { "OS 12587603 latest.xdf", "OS 12212156 2002.xdf" }, matches);
        Assert.Equal("OS 12587603 latest.xdf", OsIdDetector.FindMatchingXdf(bin, this.folder));
    }

    [Fact]
    public void NoMatch_ReturnsNothing_AndMissingFolderIsFine()
    {
        this.Xdf("OS 99999999 other.xdf");
        byte[] bin = BinWith((100, "12212156"));

        Assert.Empty(OsIdDetector.FindMatchingXdfs(bin, this.folder));
        Assert.Null(OsIdDetector.FindMatchingXdf(bin, this.folder));
        Assert.Empty(OsIdDetector.FindMatchingXdfs(bin, Path.Combine(this.folder, "nope")));
        Assert.Empty(OsIdDetector.FindMatchingXdfs(new byte[100], this.folder));
    }

    // ── Help search ──────────────────────────────────────────────────────────────────────────

    private static HelpSearch Index()
    {
        HelpSearch index = new();
        index.AddPage("tuner", "The Tuner", @"
            <h1 id='intro'>The Tuner</h1><p>Edit calibration tables.</p>
            <h3 id='saving'>Saving and the checksums</h3><p>Save As writes a new file and corrects the checksums so the PCM accepts it.</p>
            <h3 id='undo'>Undo and redo</h3><p>Every change can be undone and redone from the history list.</p>
            <h3 id='problems'>Protected XDF files</h3><p>Some XDF files are protected and cannot be read.</p>");
        index.AddPage("welcome", "Welcome", @"
            <h1 id='welcome'>Welcome</h1><h3 id='kernels'>Kernel files</h3><p>Reading the PCM needs the kernel files.</p>");
        return index;
    }

    [Theory]
    [InlineData("how do I save the file", "saving")]
    [InlineData("fix checksum", "saving")]
    [InlineData("revert a change", "undo")]
    [InlineData("protected xdf", "problems")]
    [InlineData("where are the kernel files", "kernels")]
    public void Search_FindsTheRightSection_ForAnEverydayQuestion(string question, string anchor)
    {
        IReadOnlyList<HelpHit> hits = Index().Search(question);
        Assert.NotEmpty(hits);
        Assert.Equal(anchor, hits[0].Section.Anchor);
    }

    [Fact]
    public void Search_NothingRelevant_ReturnsNothing()
    {
        Assert.Empty(Index().Search("xyzzy plugh"));
        Assert.Empty(Index().Search("   "));
    }
}
