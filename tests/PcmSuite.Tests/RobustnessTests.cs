using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Damaged input must cost one entry, one segment or one retry, never the whole load.</summary>
public class XdfRobustnessTests
{
    private const string Xdf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <XDFFORMAT version="1.60">
          <XDFHEADER><CATEGORY index="0x0" name="Fuel"/></XDFHEADER>
          <XDFTABLE uniqueid="0x1" flags="0x0">
            <title>Good table</title>
            <CATEGORYMEM index="0" category="1"/>
            <XDFAXIS id="z">
              <EMBEDDEDDATA mmedaddress="0x100" mmedelementsizebits="16" mmedrowcount="2" mmedcolcount="2" mmedtypeflags="0x0"/>
              <decimalpl>2</decimalpl><outputtype>1</outputtype>
              <MATH equation="X"><VAR id="X"/></MATH>
            </XDFAXIS>
          </XDFTABLE>
          <XDFTABLE uniqueid="0x2" flags="0x0">
            <title>Address is not hex</title>
            <XDFAXIS id="z"><EMBEDDEDDATA mmedaddress="0xNOTHEX" mmedelementsizebits="16" mmedrowcount="2" mmedcolcount="2"/><MATH equation="X"><VAR id="X"/></MATH></XDFAXIS>
          </XDFTABLE>
          <XDFTABLE uniqueid="0x3" flags="0x0">
            <title>Address too big</title>
            <XDFAXIS id="z"><EMBEDDEDDATA mmedaddress="0x1FFFFFFFF" mmedelementsizebits="16" mmedrowcount="2" mmedcolcount="2"/><MATH equation="X"><VAR id="X"/></MATH></XDFAXIS>
          </XDFTABLE>
          <XDFCONSTANT uniqueid="0x4" flags="0x0">
            <title>Constant with a bad address</title>
            <EMBEDDEDDATA mmedaddress="zzz" mmedelementsizebits="8"/>
            <MATH equation="X"><VAR id="X"/></MATH>
          </XDFCONSTANT>
          <XDFCONSTANT uniqueid="0x5" flags="0x0">
            <title>Good constant</title>
            <EMBEDDEDDATA mmedaddress="0x200" mmedelementsizebits="8"/>
            <MATH equation="X"><VAR id="X"/></MATH>
          </XDFCONSTANT>
          <XDFPATCH uniqueid="0x6" flags="0x0">
            <title>Patch with damaged entries</title>
            <CATEGORYMEM index="0" category="0x1"/>
            <XDFPATCHENTRY name="bad address" address="0xQQ" patchdata="4E71"/>
            <XDFPATCHENTRY name="bad data" address="0x10" patchdata="XYZ1"/>
            <XDFPATCHENTRY name="good" address="0x20" patchdata="4E71"/>
          </XDFPATCH>
        </XDFFORMAT>
        """;

    private static string Write()
    {
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-damaged-" + Guid.NewGuid().ToString("N") + ".xdf");
        File.WriteAllText(path, Xdf);
        return path;
    }

    [Fact]
    public void OneBadEntry_IsSkipped_AndTheRestOfTheXdfLoads()
    {
        string path = Write();
        try
        {
            List<TableDefinition> tables = XdfParser.Parse(path);
            Assert.Equal(new[] { "Good constant", "Good table" }, tables.Select(t => t.Name).OrderBy(n => n).ToArray());
            Assert.Equal(0x100u, tables.Single(t => t.Name == "Good table").Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DamagedPatchEntries_AreSkipped_AndAHexCategoryIsUnderstood()
    {
        string path = Write();
        try
        {
            PatchDefinition patch = Assert.Single(XdfParser.ParsePatches(path));
            PatchEntry entry = Assert.Single(patch.Entries);
            Assert.Equal("good", entry.Name);
            Assert.Equal(0x20u, entry.Address);
            Assert.Equal("Fuel", patch.Category);       // category="0x1" is read as hex, as it is for tables
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0x1F", true, 0x1Fu)]
    [InlineData("1f", true, 0x1Fu)]
    [InlineData("  0X8360 ", true, 0x8360u)]
    [InlineData("", false, 0u)]
    [InlineData("0x", false, 0u)]
    [InlineData("0xGG", false, 0u)]
    [InlineData("0x1FFFFFFFF", false, 0u)]
    public void TryParseHexUInt_AcceptsHexAndRefusesEverythingElse(string text, bool ok, uint expected)
    {
        Assert.Equal(ok, XdfParser.TryParseHexUInt(text, out uint value));
        Assert.Equal(expected, value);
    }
}

public class SegmentTableRobustnessTests
{
    private static void Write(byte[] image, int at, uint value)
    {
        image[at] = (byte)(value >> 24);
        image[at + 1] = (byte)(value >> 16);
        image[at + 2] = (byte)(value >> 8);
        image[at + 3] = (byte)value;
    }

    [Fact]
    public void ErasedSegmentTable_IsReportedOutOfRange_NotACrash()
    {
        // Erased flash reads 0xFF. A start of 0xFFFFFFFF used to wrap to 1 in 32-bit arithmetic and pass the range check.
        byte[] image = new byte[0x1000];
        Array.Fill(image, (byte)0xFF);
        byte[] before = (byte[])image.Clone();

        Assert.Empty(ChecksumCalculator.ReadSegments(image, requireP01Signature: false));

        IReadOnlyList<string> messages = ChecksumCalculator.CorrectSegmentTable(image);
        Assert.Equal(8, messages.Count);
        Assert.All(messages, m => Assert.Contains("out of range", m));
        Assert.Equal(before, image);                    // nothing was written
    }

    [Fact]
    public void EntryAtTheLastByte_IsSkipped_AndTheOthersStillWork()
    {
        byte[] image = new byte[0x1000];
        for (int s = 0; s < 8; s++)
        {
            Write(image, 0x50C + s * 8, s == 0 ? 0u : 0x600u + (uint)s * 0x10);
            Write(image, 0x50C + s * 8 + 4, 0x600u + (uint)s * 0x10 + 0xF);
        }

        Write(image, 0x50C + 3 * 8, (uint)image.Length - 1);          // segment 3 starts on the very last byte
        Write(image, 0x50C + 3 * 8 + 4, (uint)image.Length - 1);

        IReadOnlyList<SegmentInfo> segments = ChecksumCalculator.ReadSegments(image, requireP01Signature: false);
        Assert.Equal(7, segments.Count);
        Assert.DoesNotContain(segments, s => s.Index == 3);

        IReadOnlyList<string> messages = ChecksumCalculator.CorrectSegmentTable(image);
        Assert.Contains("out of range", messages[3]);
        Assert.DoesNotContain("out of range", messages[2]);
    }

    [Theory]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0xFFFFFFFEu)]
    public void StartThatWrapsRoundIn32Bits_IsSkipped_NotACrash(uint start)
    {
        // start + 2 wraps to 1 or 0 in 32-bit arithmetic, and a believable end address makes the old range check pass.
        byte[] image = new byte[0x1000];
        Write(image, 0x50C + 8, start);
        Write(image, 0x50C + 8 + 4, 0x7FFu);

        Assert.DoesNotContain(ChecksumCalculator.ReadSegments(image, requireP01Signature: false), s => s.Index == 1);
        Assert.Contains("out of range", ChecksumCalculator.CorrectSegmentTable(image)[1]);
    }

    [Fact]
    public void ImageTooShortForTheTable_IsReportedOutOfRange_NotACrash()
    {
        byte[] image = new byte[0x520];                 // ends in the middle of the segment table
        IReadOnlyList<string> messages = ChecksumCalculator.CorrectSegmentTable(image);
        Assert.Equal(8, messages.Count);

        // The first two rows of the table still fit in the image; the rest run off its end.
        Assert.All(messages.Skip(2), m => Assert.Contains("out of range", m));
        Assert.Empty(ChecksumCalculator.ReadSegments(image, requireP01Signature: false));
    }
}

// These tests point the Tuner at a scratch folder through environment variables, which are shared by the whole process.
[Collection("ScratchEnvironment")]
public class LegacyMigrationTests
{
    [Fact]
    public void ACopyThatStopsHalfWay_IsRetriedNextTime_NotMistakenForDone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;     // relies on Windows refusing to copy a file another process holds open
        }

        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-migrate-" + Guid.NewGuid().ToString("N"));
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            string legacy = ProjectInfo.LegacyProjectsRoot;
            Directory.CreateDirectory(Path.Combine(legacy, "Car"));
            File.WriteAllText(Path.Combine(legacy, "first.txt"), "one");
            string locked = Path.Combine(legacy, "Car", "project.json");
            File.WriteAllText(locked, "two");

            // The second file cannot be copied while this handle is open, so the copy fails after the first one went across.
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(ProjectInfo.MigrateLegacyRoot());
                Assert.False(Directory.Exists(ProjectInfo.DefaultProjectsRoot));          // no half-filled folder left to fool the next start
                Assert.False(Directory.Exists(ProjectInfo.DefaultProjectsRoot + ".migrating"));
            }

            // The next start tries again and finishes the job.
            Assert.True(ProjectInfo.MigrateLegacyRoot());
            Assert.Equal("one", File.ReadAllText(Path.Combine(ProjectInfo.DefaultProjectsRoot, "first.txt")));
            Assert.Equal("two", File.ReadAllText(Path.Combine(ProjectInfo.DefaultProjectsRoot, "Car", "project.json")));
            Assert.True(Directory.Exists(legacy));                                          // the old folder is left alone
            Assert.False(ProjectInfo.MigrateLegacyRoot());                                  // and it is only ever done once
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}
