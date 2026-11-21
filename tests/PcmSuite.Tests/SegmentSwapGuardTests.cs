using System.Text;
using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Segment swap guardrails: a calibration must not move between operating systems without an explicit yes.</summary>
public class SegmentSwapGuardTests
{
    /// <summary>A 512 KB image with a segment table (whole-image entry, a calibration segment, a diagnostics segment), a calibration part number inside the calibration segment, and the OS part number in the rest.</summary>
    private static byte[] Image(string osId, string calId)
    {
        byte[] bin = new byte[512 * 1024];
        (uint Start, uint End)[] segments = { (0, 0x7FFFD), (0x8002, 0x13FFF), (0x14002, 0x16DFF) };
        for (int s = 0; s < segments.Length; s++)
        {
            WriteUInt32(bin, 0x50C + s * 8, segments[s].Start);
            WriteUInt32(bin, 0x50C + s * 8 + 4, segments[s].End);
        }

        Encoding.ASCII.GetBytes(calId).CopyTo(bin, 0x9000);    // inside the calibration segment
        Encoding.ASCII.GetBytes(osId).CopyTo(bin, 0x70000);    // in the operating system
        return bin;
    }

    private static void WriteUInt32(byte[] bin, int at, uint value)
    {
        bin[at] = (byte)(value >> 24);
        bin[at + 1] = (byte)(value >> 16);
        bin[at + 2] = (byte)(value >> 8);
        bin[at + 3] = (byte)value;
    }

    private static SegmentInfo Segment(string name, bool risky = false) => new() { Index = 1, Name = name, IsRisky = risky };

    [Fact]
    public void OperatingSystemId_IgnoresCalibrationPartNumbers()
    {
        byte[] bin = Image("12212156", "15151515");
        Assert.Equal("12212156", SegmentSwapGuard.OperatingSystemId(bin));
    }

    [Fact]
    public void OperatingSystemId_IsNullWhenThereIsNone()
    {
        Assert.Null(SegmentSwapGuard.OperatingSystemId(new byte[512 * 1024]));
        Assert.Null(SegmentSwapGuard.OperatingSystemId(new byte[100]));
    }

    [Fact]
    public void TwoCalibrationsOnTheSameOs_AreTheSameOs_EvenWithDifferentCalNumbers()
    {
        string? a = SegmentSwapGuard.OperatingSystemId(Image("12212156", "15151515"));
        string? b = SegmentSwapGuard.OperatingSystemId(Image("12212156", "12582302"));
        Assert.Equal(OsMatch.Same, SegmentSwapGuard.Compare(a, b));
    }

    [Theory]
    [InlineData("12212156", "12212156", OsMatch.Same)]
    [InlineData("12212156", "12587603", OsMatch.Different)]
    [InlineData(null, "12587603", OsMatch.Unknown)]
    [InlineData("12212156", null, OsMatch.Unknown)]
    [InlineData("", "", OsMatch.Unknown)]
    public void Compare_ClassifiesThePair(string? target, string? donor, OsMatch expected)
    {
        Assert.Equal(expected, SegmentSwapGuard.Compare(target, donor));
    }

    [Fact]
    public void SameOs_PlainSegment_NeedsNoQuestion()
    {
        Assert.Null(SegmentSwapGuard.Review(OsMatch.Same, "12212156", "12212156", new[] { Segment("Engine Calibration") }));
    }

    [Fact]
    public void DifferentOs_IsADangerousSwap_AndNamesBothSystemsAndTheSegments()
    {
        SwapReview? review = SegmentSwapGuard.Review(OsMatch.Different, "12212156", "12587603", new[] { Segment("Engine Calibration") });

        Assert.NotNull(review);
        Assert.True(review!.Danger);
        Assert.Contains("12212156", review.Message);
        Assert.Contains("12587603", review.Message);
        Assert.Contains("Engine Calibration", review.Message);
    }

    [Fact]
    public void UnknownOs_AsksButIsNotCalledDangerous()
    {
        SwapReview? review = SegmentSwapGuard.Review(OsMatch.Unknown, null, "12587603", new[] { Segment("Engine Calibration") });

        Assert.NotNull(review);
        Assert.False(review!.Danger);
        Assert.Contains("could not be identified", review.Message);
    }

    [Fact]
    public void SameOs_ButIdentitySegment_StillAsks()
    {
        SwapReview? review = SegmentSwapGuard.Review(OsMatch.Same, "12212156", "12212156", new[] { Segment("System", risky: true) });

        Assert.NotNull(review);
        Assert.False(review!.Danger);
        Assert.Contains("System", review.Message);
        Assert.Contains("VIN", review.Message);
    }

    [Fact]
    public void DifferentOs_AndIdentitySegment_MentionsBothProblems()
    {
        SwapReview? review = SegmentSwapGuard.Review(OsMatch.Different, "12212156", "12587603",
            new[] { Segment("Engine Calibration"), Segment("System", risky: true) });

        Assert.True(review!.Danger);
        Assert.Contains("12587603", review.Message);
        Assert.Contains("VIN", review.Message);
    }

    [Fact]
    public void Verdict_SaysSomethingForEveryCase()
    {
        Assert.Contains("Same operating system", SegmentSwapGuard.Verdict(OsMatch.Same, "1", "1"));
        Assert.Contains("DIFFERENT", SegmentSwapGuard.Verdict(OsMatch.Different, "1", "2"));
        Assert.Contains("could not be identified", SegmentSwapGuard.Verdict(OsMatch.Unknown, null, null));
    }

    [RealBinFact("Impala_LS2_P1_Modified_v3.bin")]
    public void RealBins_OnTheSameOs_AreRecognisedAsCompatible()
    {
        byte[] impala = File.ReadAllBytes(TestData.Bin("Impala_LS2_P1_Modified_v3.bin")!);
        Assert.Equal("12212156", SegmentSwapGuard.OperatingSystemId(impala));

        string? tahoePath = TestData.Bin("Bradley*.bin");
        if (tahoePath != null)
        {
            byte[] tahoe = File.ReadAllBytes(tahoePath);
            Assert.Equal(
                OsMatch.Same,
                SegmentSwapGuard.Compare(SegmentSwapGuard.OperatingSystemId(impala), SegmentSwapGuard.OperatingSystemId(tahoe)));
        }
    }
}
