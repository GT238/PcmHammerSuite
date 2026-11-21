using PcmSuite.Tune;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>The logic behind the hex view: typed addresses and search patterns, searching, and listing differences.</summary>
public class HexToolsTests
{
    [Theory]
    [InlineData("8360", 0x8360)]
    [InlineData("0x8360", 0x8360)]
    [InlineData("$8360", 0x8360)]
    [InlineData("  ff8000 ", 0xFF8000)]
    public void Address_IsAlwaysHex(string text, int expected)
    {
        Assert.True(HexTools.TryParseAddress(text, out int address));
        Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("0x")]
    [InlineData(null)]
    public void Address_RejectsNonsense(string? text) => Assert.False(HexTools.TryParseAddress(text, out _));

    [Fact]
    public void Pattern_HexWithOrWithoutSpaces()
    {
        Assert.True(HexTools.TryParsePattern("FF 8a 00", out byte[] a));
        Assert.Equal(new byte[] { 0xFF, 0x8A, 0x00 }, a);
        Assert.True(HexTools.TryParsePattern("FF8A00", out byte[] b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pattern_QuotedTextIsAscii()
    {
        Assert.True(HexTools.TryParsePattern("\"1221\"", out byte[] p));
        Assert.Equal(new byte[] { 0x31, 0x32, 0x32, 0x31 }, p);
    }

    [Theory]
    [InlineData("F")]       // odd number of digits
    [InlineData("GG")]
    [InlineData("")]
    public void Pattern_RejectsNonsense(string text) => Assert.False(HexTools.TryParsePattern(text, out _));

    [Fact]
    public void Find_Forward_Backward_AndWraps()
    {
        byte[] data = new byte[64];
        data[10] = 0xAB; data[11] = 0xCD;
        data[40] = 0xAB; data[41] = 0xCD;
        byte[] pattern = { 0xAB, 0xCD };

        Assert.Equal(10, HexTools.Find(data, pattern, 0));
        Assert.Equal(40, HexTools.Find(data, pattern, 11));
        Assert.Equal(10, HexTools.Find(data, pattern, 41));              // wraps to the start
        Assert.Equal(10, HexTools.Find(data, pattern, 39, forward: false));
        Assert.Equal(40, HexTools.Find(data, pattern, 9, forward: false)); // wraps to the end
        Assert.Equal(-1, HexTools.Find(data, new byte[] { 1, 2, 3 }, 0));
    }

    [Fact]
    public void Find_PatternAtTheVeryEnd_IsFound()
    {
        byte[] data = new byte[16];
        data[14] = 0x12; data[15] = 0x34;
        Assert.Equal(14, HexTools.Find(data, new byte[] { 0x12, 0x34 }, 0));
    }

    [Fact]
    public void Differences_GroupsRuns_IncludingAtBothEnds()
    {
        byte[] a = new byte[20];
        byte[] b = new byte[20];
        b[0] = 1;                       // run at the very start
        b[5] = 1; b[6] = 1; b[7] = 1;   // a run of three
        b[19] = 1;                      // run at the very end

        List<DiffRange> ranges = HexTools.Differences(a, b);

        Assert.Equal(new[] { new DiffRange(0, 1), new DiffRange(5, 8), new DiffRange(19, 20) }, ranges);
        Assert.Equal(3, ranges[1].Length);
    }

    [Fact]
    public void Differences_IdenticalImages_HaveNone() =>
        Assert.Empty(HexTools.Differences(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }));

    [Fact]
    public void Describe_ReadsBigEndian()
    {
        string text = HexTools.Describe(new byte[] { 0x01, 0x02, 0x03, 0x04 }, 0);
        Assert.Contains("16-bit 258", text);
        Assert.Contains("32-bit 16909060", text);
        Assert.Equal(string.Empty, HexTools.Describe(new byte[2], 5));
    }

    [Fact]
    public void AsciiChar_ShowsPrintableOnly()
    {
        Assert.Equal('A', HexTools.AsciiChar(0x41));
        Assert.Equal('.', HexTools.AsciiChar(0x00));
        Assert.Equal('.', HexTools.AsciiChar(0xFF));
    }
}
