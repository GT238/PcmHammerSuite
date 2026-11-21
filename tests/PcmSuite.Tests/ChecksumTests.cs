using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>
/// The checksum fixer is the code where a silent mistake gets flashed into a PCM. Synthetic images check its arithmetic;
/// the real-BIN tests check it against PCM Hammer's own validator, which is what accepts the file at flash time.
/// </summary>
public class ChecksumTests
{
    /// <summary>A 512 KB image with the P01/P59 signature and a valid-looking 8-segment table, filled with a pattern.</summary>
    private static byte[] SyntheticP01()
    {
        byte[] image = new byte[512 * 1024];
        for (int i = 0; i < image.Length; i++)
        {
            image[i] = (byte)((i * 31 + (i >> 8)) & 0xFF);
        }

        image[0x1FFFE] = 0x4A; image[0x1FFFF] = 0xFC;
        image[0x7FFFE] = 0x4A; image[0x7FFFF] = 0xFC;

        // Segment 0 starts at 0 (its checksum is at 0x500); the others start at their checksum word.
        (uint Start, uint End)[] segments =
        {
            (0x00000, 0x1FFFF), (0x20000, 0x2FFFF), (0x30000, 0x37FFF), (0x38000, 0x3FFFF),
            (0x40000, 0x47FFF), (0x48000, 0x4FFFF), (0x50000, 0x5FFFF), (0x60000, 0x7FFFF),
        };
        for (int s = 0; s < segments.Length; s++)
        {
            WriteUInt32(image, 0x50C + s * 8, segments[s].Start);
            WriteUInt32(image, 0x50C + s * 8 + 4, segments[s].End);
        }

        return image;
    }

    private static void WriteUInt32(byte[] image, int at, uint value)
    {
        image[at] = (byte)(value >> 24);
        image[at + 1] = (byte)(value >> 16);
        image[at + 2] = (byte)(value >> 8);
        image[at + 3] = (byte)value;
    }

    /// <summary>The rule PCM Hammer checks: all 16-bit words of the segment, the checksum word included, add up to zero.</summary>
    private static ushort SegmentWordSum(byte[] image, SegmentInfo segment)
    {
        ushort sum = 0;
        uint start = segment.Index == 0 ? 0 : segment.ChecksumAddress;
        for (uint addr = start; addr <= segment.EndAddress; addr += 2)
        {
            if (addr == 0x4000)
            {
                addr = 0x20000 - 2;   // the factory area is skipped
                continue;
            }

            sum += (ushort)((image[addr] << 8) | image[addr + 1]);
        }

        return sum;
    }

    [Fact]
    public void SyntheticImage_IsRecognisedAsP01P59()
    {
        Assert.True(ChecksumCalculator.IsP01P59(SyntheticP01()));
        Assert.Equal(8, ChecksumCalculator.ReadSegments(SyntheticP01()).Count);
    }

    [Fact]
    public void WrongSizeOrSignature_IsNotP01P59_AndIsLeftAlone()
    {
        byte[] small = new byte[256 * 1024];
        byte[] copy = (byte[])small.Clone();
        Assert.False(ChecksumCalculator.IsP01P59(small));
        IReadOnlyList<string> messages = ChecksumCalculator.CorrectChecksums(small);
        Assert.Single(messages);
        Assert.Equal(copy, small);

        byte[] unsigned = new byte[512 * 1024];
        Assert.False(ChecksumCalculator.IsP01P59(unsigned));
    }

    [Fact]
    public void Correct_MakesEverySegmentSumToZero()
    {
        byte[] image = SyntheticP01();
        ChecksumCalculator.CorrectSegmentTable(image);

        foreach (SegmentInfo segment in ChecksumCalculator.ReadSegments(image))
        {
            Assert.True(SegmentWordSum(image, segment) == 0, $"segment {segment.Index} does not sum to zero");
        }
    }

    [Fact]
    public void Correct_ChangesOnlyTheEightChecksumWords()
    {
        byte[] image = SyntheticP01();
        byte[] before = (byte[])image.Clone();
        ChecksumCalculator.CorrectSegmentTable(image);

        HashSet<int> allowed = new();
        foreach (SegmentInfo segment in ChecksumCalculator.ReadSegments(image))
        {
            allowed.Add((int)segment.ChecksumAddress);
            allowed.Add((int)segment.ChecksumAddress + 1);
        }

        for (int i = 0; i < image.Length; i++)
        {
            if (image[i] != before[i])
            {
                Assert.True(allowed.Contains(i), $"byte 0x{i:X} changed but is not a checksum word");
            }
        }
    }

    [Fact]
    public void Correct_IsIdempotent_AndReportsNoFurtherChange()
    {
        byte[] image = SyntheticP01();
        ChecksumCalculator.CorrectSegmentTable(image);
        byte[] once = (byte[])image.Clone();

        IReadOnlyList<string> second = ChecksumCalculator.CorrectSegmentTable(image);

        Assert.Equal(once, image);
        Assert.All(second, line => Assert.Contains("unchanged", line));
    }

    [Fact]
    public void EditingACalibrationByte_IsFixedByCorrect()
    {
        byte[] image = SyntheticP01();
        ChecksumCalculator.CorrectSegmentTable(image);

        SegmentInfo calibration = ChecksumCalculator.ReadSegments(image)[1];
        image[calibration.StartAddress + 100] ^= 0x5A;
        Assert.NotEqual(0, SegmentWordSum(image, calibration));

        IReadOnlyList<string> messages = ChecksumCalculator.CorrectSegmentTable(image);

        Assert.Equal(0, SegmentWordSum(image, calibration));
        Assert.Single(messages, m => m.Contains("fixed"));   // only the segment that was edited
    }

    [Fact]
    public void SegmentTableOutsideTheImage_IsSkippedNotCrashed()
    {
        byte[] image = SyntheticP01();
        WriteUInt32(image, 0x50C + 7 * 8 + 4, 0xFFFFFFF0);   // last segment ends far beyond the file

        IReadOnlyList<string> messages = ChecksumCalculator.CorrectSegmentTable(image);

        Assert.Contains(messages, m => m.Contains("out of range"));
    }

    // ── Against PCM Hammer's own validator, with real files ──────────────────────────────────

    [RealBinFact("*.bin")]
    public void RealBins_AfterCorrection_AreAcceptedByPcmHammer()
    {
        foreach (string path in Directory.GetFiles(TestData.BinFolder, "*.bin"))
        {
            byte[] image = File.ReadAllBytes(path);
            ChecksumReport report = ChecksumService.CorrectAndValidate(image);
            if (image.Length == 512 * 1024 && ChecksumCalculator.IsP01P59(image))
            {
                Assert.True(report.Valid, $"{Path.GetFileName(path)}: {report.Summary}\n{report.Details}");
            }
        }
    }

    [RealBinFact("*.bin")]
    public void RealBins_CorruptedCalibration_FailsThenIsFixed()
    {
        string path = Directory.GetFiles(TestData.BinFolder, "*.bin").First(f => ChecksumCalculator.IsP01P59(File.ReadAllBytes(f)));
        byte[] image = File.ReadAllBytes(path);
        ChecksumService.CorrectAndValidate(image);                       // start from a good image
        Assert.True(ChecksumService.Validate(image).Valid);

        SegmentInfo calibration = ChecksumCalculator.ReadSegments(image)[1];
        image[calibration.StartAddress + 50] ^= 0xFF;                    // an edit with no checksum fix
        Assert.False(ChecksumService.Validate(image).Valid);             // PCM Hammer must reject it

        ChecksumReport fixedReport = ChecksumService.CorrectAndValidate(image);
        Assert.True(fixedReport.Valid, fixedReport.Summary);
        Assert.True(fixedReport.Corrected);
    }

    [RealBinFact("*.bin")]
    public void RealBins_CorrectionTouchesOnlyChecksumWords()
    {
        string path = Directory.GetFiles(TestData.BinFolder, "*.bin").First(f => ChecksumCalculator.IsP01P59(File.ReadAllBytes(f)));
        byte[] image = File.ReadAllBytes(path);
        byte[] before = (byte[])image.Clone();
        SegmentInfo calibration = ChecksumCalculator.ReadSegments(image)[1];
        image[calibration.StartAddress + 50] ^= 0xFF;
        byte[] edited = (byte[])image.Clone();

        ChecksumService.CorrectAndValidate(image);

        HashSet<int> checksumBytes = ChecksumCalculator.ReadSegments(image)
            .SelectMany(s => new[] { (int)s.ChecksumAddress, (int)s.ChecksumAddress + 1 })
            .ToHashSet();
        for (int i = 0; i < image.Length; i++)
        {
            if (image[i] != edited[i])
            {
                Assert.Contains(i, checksumBytes);
            }
        }

        Assert.Equal(before.Length, image.Length);
    }
}
