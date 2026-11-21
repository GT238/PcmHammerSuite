using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Reading, writing and scaling table cells: the code every edit goes through.</summary>
public class TableDefinitionTests
{
    private static TableDefinition Table(int rows, int cols, int bits = 16, string equation = "X", int decimals = 2) => new()
    {
        Address = 0x100,
        Rows = rows,
        Columns = cols,
        ElementBits = bits,
        ScalingEquation = equation,
        DecimalPlaces = decimals,
    };

    [Fact]
    public void RawValues_RoundTrip_16BitBigEndian()
    {
        byte[] bin = new byte[0x400];
        TableDefinition table = Table(3, 4);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                table.SetRawValue(bin, r, c, r * 1000 + c * 7 + 300);
            }
        }

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                Assert.Equal(r * 1000 + c * 7 + 300, table.GetRawValue(bin, r, c));
            }
        }

        // Row-major, big endian: cell (1,2) is the 7th word.
        int offset = 0x100 + (1 * 4 + 2) * 2;
        Assert.Equal((byte)((1000 + 14 + 300) >> 8), bin[offset]);
        Assert.Equal((byte)((1000 + 14 + 300) & 0xFF), bin[offset + 1]);
    }

    [Fact]
    public void LittleEndian_And_Signed_AreHonoured()
    {
        byte[] bin = new byte[0x200];
        TableDefinition table = Table(1, 1);
        table.IsBigEndian = false;
        table.IsSigned = true;
        table.SetRawValue(bin, 0, 0, -2);

        Assert.Equal(0xFE, bin[0x100]);
        Assert.Equal(0xFF, bin[0x101]);
        Assert.Equal(-2, table.GetRawValue(bin, 0, 0));
    }

    [Fact]
    public void EightBit_And_ThirtyTwoBit_RoundTrip()
    {
        byte[] bin = new byte[0x200];
        TableDefinition eight = Table(1, 3, 8);
        eight.SetRawValue(bin, 0, 2, 200);
        Assert.Equal(200, eight.GetRawValue(bin, 0, 2));
        Assert.Equal(200, bin[0x102]);

        TableDefinition thirtyTwo = Table(1, 2, 32);
        thirtyTwo.SetRawValue(bin, 0, 1, 0x12345678);
        Assert.Equal(0x12345678, thirtyTwo.GetRawValue(bin, 0, 1));
        Assert.Equal(0x12, bin[0x104]);
        Assert.Equal(0x78, bin[0x107]);
    }

    [Fact]
    public void OutsideTheImage_ReadsZero_AndWritesNothing()
    {
        byte[] bin = new byte[0x80];
        TableDefinition table = Table(2, 2);
        Assert.Equal(0, table.GetRawValue(bin, 1, 1));
        table.SetRawValue(bin, 1, 1, 123);
        Assert.All(bin, b => Assert.Equal(0, b));
    }

    [Fact]
    public void WriteOfOneCell_LeavesNeighboursAlone()
    {
        byte[] bin = Enumerable.Range(0, 0x400).Select(i => (byte)(i & 0xFF)).ToArray();
        byte[] before = (byte[])bin.Clone();
        TableDefinition table = Table(4, 4);
        table.SetRawValue(bin, 2, 2, 0xABCD);

        int offset = 0x100 + (2 * 4 + 2) * 2;
        for (int i = 0; i < bin.Length; i++)
        {
            if (i != offset && i != offset + 1)
            {
                Assert.Equal(before[i], bin[i]);
            }
        }
    }

    [Theory]
    [InlineData("X*0.390625", 128, 50.0)]
    [InlineData("X/256", 512, 2.0)]
    [InlineData("X*0.1-40", 800, 40.0)]
    [InlineData("X", 77, 77.0)]
    public void Scaling_Equations_Evaluate(string equation, double raw, double expected)
    {
        TableDefinition table = Table(1, 1, 16, equation);
        Assert.Equal(expected, table.ApplyScaling(raw), 6);
    }

    [Theory]
    [InlineData("X*0.390625", 50.0)]
    [InlineData("X/256", 2.0)]
    [InlineData("X*0.1-40", 12.5)]
    public void InverseScale_FindsTheRawValueThatDisplaysClosest(string equation, double wanted)
    {
        TableDefinition table = Table(1, 1, 16, equation);
        double raw = table.InverseScale(wanted);
        double shown = table.ApplyScaling(raw);
        double step = Math.Abs(table.ApplyScaling(raw + 1) - shown);
        Assert.True(Math.Abs(shown - wanted) <= step / 2 + 1e-9, $"raw {raw} shows {shown}, wanted {wanted}");
    }

    [Fact]
    public void CrossReferences_AreResolvedThroughTheCallback()
    {
        TableDefinition table = Table(1, 1, 16, "X*ref:'Gain'");
        Assert.Equal(30.0, table.ApplyScaling(10, name => name == "Gain" ? 3.0 : 0.0), 6);

        TableDefinition universalPatcherStyle = Table(1, 1, 16, "X*table:'Gain'");
        Assert.Equal(30.0, universalPatcherStyle.ApplyScaling(10, name => name == "Gain" ? 3.0 : 0.0), 6);
    }

    [Theory]
    [InlineData(2.0, 4, "2.0000")]       // whole numbers keep their decimals so a column lines up
    [InlineData(1.9701, 4, "1.9701")]
    [InlineData(97.0, 3, "97.000")]
    [InlineData(1500.0, 0, "1500")]
    [InlineData(-3.5, 1, "-3.5")]
    public void FormatValue_AlwaysShowsTheTableDecimals(double value, int decimals, string expected)
    {
        Assert.Equal(expected, Table(1, 1, 16, "X", decimals).FormatValue(value));
    }

    [Fact]
    public void CellChanged_And_CountChanges_ComparePerCell()
    {
        byte[] a = new byte[0x400];
        byte[] b = new byte[0x400];
        TableDefinition table = Table(3, 3);
        Assert.False(table.HasAnyChanges(a, b));

        table.SetRawValue(b, 1, 1, 500);
        table.SetRawValue(b, 2, 0, 9);

        Assert.True(table.HasAnyChanges(a, b));
        Assert.Equal(2, table.CountChanges(a, b));
        Assert.True(table.CellChanged(a, b, 1, 1));
        Assert.False(table.CellChanged(a, b, 0, 0));
    }
}
