using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>What the writer saves must read back as the same tables: that is what makes a saved definition trustworthy.</summary>
public class XdfWriterTests
{
    private static List<TableDefinition> RoundTrip(IReadOnlyList<TableDefinition> tables, int binSize = 0x80000)
    {
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-write-" + Guid.NewGuid().ToString("N") + ".xdf");
        try
        {
            XdfWriter.Write(path, tables, "OS12345678 test", "A test definition.\r\nSecond line.", binSize);
            return XdfParser.Parse(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertSame(TableDefinition a, TableDefinition b)
    {
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Description.Replace("\r\n", "\n"), b.Description.Replace("\r\n", "\n"));
        Assert.Equal(a.Category, b.Category);
        Assert.Equal(a.SubCategory, b.SubCategory);
        Assert.Equal(a.Address, b.Address);
        Assert.Equal(a.Rows, b.Rows);
        Assert.Equal(a.Columns, b.Columns);
        Assert.Equal(a.ElementBits, b.ElementBits);
        Assert.Equal(a.IsSigned, b.IsSigned);
        Assert.Equal(a.ZUnits, b.ZUnits);
        Assert.Equal(a.DecimalPlaces, b.DecimalPlaces);
        Assert.Equal(a.OutputType, b.OutputType);
        Assert.Equal(a.ScalingEquation, b.ScalingEquation);
        Assert.Equal(a.Mask, b.Mask);
        Assert.Equal(a.MinorStrideBits, b.MinorStrideBits);
        Assert.Equal(a.MajorStrideBits, b.MajorStrideBits);
    }

    private static TableDefinition Ve() => new()
    {
        Name = "B0101 - Main VE Table",
        Description = "Volumetric efficiency.\r\nRPM across, MAP down.",
        Category = "Airflow",
        SubCategory = "Fuel",
        Address = 0x8360,
        Rows = 3,
        Columns = 4,
        ElementBits = 16,
        ZUnits = "g*K/kPa",
        DecimalPlaces = 4,
        ScalingEquation = "X*0.0001953125",
        XUnits = "RPM",
        XLabels = new List<string> { "400", "800", "1200", "1600" },
        XLabelsAreReal = true,
        YUnits = "MAP (kPa)",
        YLabels = new List<string> { "15", "20", "25" },
        YLabelsAreReal = true,
    };

    [Fact]
    public void Table_WithFixedLabels_ReadsBackTheSame()
    {
        TableDefinition original = Ve();
        TableDefinition back = RoundTrip(new[] { original }).Single();

        AssertSame(original, back);
        Assert.Equal(original.XLabels, back.XLabels);
        Assert.Equal(original.YLabels, back.YLabels);
        Assert.Equal("RPM", back.XUnits);
        Assert.Equal("MAP (kPa)", back.YUnits);
        Assert.True(back.XLabelsAreReal);
        Assert.Null(back.XAxis);
    }

    [Fact]
    public void Table_WithAxesStoredInTheBin_KeepsTheirAddresses()
    {
        TableDefinition original = Ve();
        original.XLabels = new List<string>();
        original.XLabelsAreReal = false;
        original.YLabels = new List<string>();
        original.YLabelsAreReal = false;
        original.XAxis = new AxisSource { Address = 0x8300, Count = 4, ElementBits = 16, ScalingEquation = "X*0.25", DecimalPlaces = 0 };
        original.YAxis = new AxisSource { Address = 0x8340, Count = 3, ElementBits = 8, IsSigned = true, IsBigEndian = false, DecimalPlaces = 1 };

        TableDefinition back = RoundTrip(new[] { original }).Single();

        AssertSame(original, back);
        Assert.Equal(0x8300u, back.XAxis!.Address);
        Assert.Equal(4, back.XAxis.Count);
        Assert.Equal(16, back.XAxis.ElementBits);
        Assert.Equal("X*0.25", back.XAxis.ScalingEquation);
        Assert.Equal(0x8340u, back.YAxis!.Address);
        Assert.Equal(3, back.YAxis.Count);
        Assert.Equal(8, back.YAxis.ElementBits);
        Assert.True(back.YAxis.IsSigned);
        Assert.False(back.YAxis.IsBigEndian);
        Assert.Equal(1, back.YAxis.DecimalPlaces);
    }

    [Fact]
    public void SingleValue_AndFlag_ReadBackTheSame()
    {
        TableDefinition value = new()
        {
            Name = "Rev limit", Category = "Rev Limiter", Address = 0x9278, ElementBits = 16, ZUnits = "RPM",
            DecimalPlaces = 0, ScalingEquation = "X", IsSigned = true,
        };
        TableDefinition flag = new()
        {
            Name = "Cat protection enable", Category = "Abuse Management", Address = 0x8D84, ElementBits = 8, Mask = 0x01,
        };
        TableDefinition wideFlag = new()
        {
            Name = "Wide flag", Category = "System", Address = 0x9000, ElementBits = 16, Mask = 0x0100,
        };

        List<TableDefinition> back = RoundTrip(new[] { value, flag, wideFlag });

        AssertSame(value, back.Single(t => t.Name == value.Name));
        AssertSame(flag, back.Single(t => t.Name == flag.Name));
        AssertSame(wideFlag, back.Single(t => t.Name == wideFlag.Name));
    }

    [Fact]
    public void LittleEndianAndStride_ReadBackTheSame()
    {
        TableDefinition original = Ve();
        original.IsBigEndian = false;
        original.IsSigned = true;
        original.MajorStrideBits = 64;

        TableDefinition back = RoundTrip(new[] { original }).Single();

        AssertSame(original, back);
        Assert.False(back.IsBigEndian);
        Assert.True(back.IsSigned);
    }

    [Fact]
    public void RowsAtSeparateAddresses_ReadBackTheSame()
    {
        TableDefinition original = new()
        {
            Name = "Segment info", Category = "System", Address = 0x504, Rows = 3, Columns = 1, ElementBits = 32,
            RowAddressOverrides = new Dictionary<int, uint> { [0] = 0x504, [1] = 0x508, [2] = 0x7FF00 },
        };

        TableDefinition back = RoundTrip(new[] { original }).Single();

        Assert.Equal(original.RowAddressOverrides, back.RowAddressOverrides);
        Assert.Equal(32, back.ElementBits);
    }

    [Fact]
    public void ReferenceToAnotherEntry_BecomesALink_AndComesBack()
    {
        TableDefinition volume = new() { Name = "B0104 - Cylinder Volume", Category = "Airflow", Address = 0x8100, ElementBits = 16, ScalingEquation = "X*0.01" };
        TableDefinition ve = Ve();
        ve.ScalingEquation = "(X*0.000195312500)*(28.9647/ref:'B0104 - Cylinder Volume')";

        List<TableDefinition> back = RoundTrip(new[] { ve, volume });

        Assert.Equal(ve.ScalingEquation.Replace("ref:'B0104 - Cylinder Volume'", "ref:'B0104 - Cylinder Volume'"), back.Single(t => t.Name == ve.Name).ScalingEquation);
    }

    [Fact]
    public void ReferenceToAMissingEntry_BecomesOne_NotABrokenEquation()
    {
        TableDefinition ve = Ve();
        ve.ScalingEquation = "X*ref:'Not there'";

        TableDefinition back = RoundTrip(new[] { ve }).Single();

        Assert.Equal("X*1.0", back.ScalingEquation);
    }

    [Fact]
    public void TextWithMarkupCharacters_IsEscaped()
    {
        TableDefinition original = Ve();
        original.Name = "Spark <advance> & \"limits\"";
        original.Description = "a < b & c > d";
        original.XLabels = new List<string> { "<1", "2&3", "\"4\"", "5" };

        TableDefinition back = RoundTrip(new[] { original }).Single();

        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.Description, back.Description);
        Assert.Equal(original.XLabels, back.XLabels);
    }

    [Fact]
    public void RegionSize_FollowsTheBin()
    {
        string xml = XdfWriter.Build(new[] { Ve() }, "t", "d", 0x100000).ToString();

        Assert.Contains("size=\"0xFFFFF\"", xml);
    }

    // ── With a real XDF: nothing is lost on the way through ──────────────

    [RealXdfFact("12212156 - 2002 512k V1.xdf")]
    public void RealP01Xdf_WrittenAndReadBack_HasEveryTableUnchanged()
    {
        List<TableDefinition> original = XdfParser.Parse(TestData.Xdf("12212156 - 2002 512k V1.xdf")!);
        List<TableDefinition> back = RoundTrip(original);

        Assert.Equal(original.Count, back.Count);
        foreach (TableDefinition a in original)
        {
            TableDefinition b = back.First(t => t.Name == a.Name && t.Address == a.Address);
            AssertSame(a, b);
            Assert.Equal(a.XLabels, b.XLabels);
            Assert.Equal(a.YLabels, b.YLabels);
            Assert.Equal(a.RowAddressOverrides, b.RowAddressOverrides);
        }
    }

    [RealXdfFact("12587603 -Latest Release.xdf")]
    public void RealP59Xdf_WrittenAndReadBack_HasEveryTableUnchanged()
    {
        List<TableDefinition> original = XdfParser.Parse(TestData.Xdf("12587603 -Latest Release.xdf")!);
        List<TableDefinition> back = RoundTrip(original);

        Assert.Equal(original.Count, back.Count);
        foreach (TableDefinition a in original)
        {
            TableDefinition b = back.First(t => t.Name == a.Name && t.Address == a.Address);
            AssertSame(a, b);
            Assert.Equal(a.XLabels, b.XLabels);
            Assert.Equal(a.YLabels, b.YLabels);
        }
    }
}
