using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Table headings (RPM, MAP and so on) read from the BIN when the definition says where the axis is stored.</summary>
public class AxisFromBinTests
{
    private static byte[] Image()
    {
        byte[] bin = new byte[256];

        // X axis at 0x10: four 16-bit big-endian values; the tests scale them by X*0.25, giving 400, 800, 1200 and 1600.
        ushort[] x = { 1600, 3200, 4800, 6400 };
        for (int i = 0; i < x.Length; i++)
        {
            bin[0x10 + i * 2] = (byte)(x[i] >> 8);
            bin[0x11 + i * 2] = (byte)x[i];
        }

        // Y axis at 0x20: three 8-bit values.
        bin[0x20] = 15;
        bin[0x21] = 40;
        bin[0x22] = 105;

        // Data at 0x40: 3 rows x 4 columns of 8-bit cells.
        for (int i = 0; i < 12; i++)
        {
            bin[0x40 + i] = (byte)(50 + i);
        }

        return bin;
    }

    private static TableDefinition Table() => new()
    {
        Name = "Test table",
        Address = 0x40,
        Rows = 3,
        Columns = 4,
        ElementBits = 8,
        XAxis = new AxisSource { Address = 0x10, Count = 4, ElementBits = 16, ScalingEquation = "X*0.25" },
        YAxis = new AxisSource { Address = 0x20, Count = 3, ElementBits = 8 },
    };

    [Fact]
    public void Labels_AreReadFromTheBin_AndScaled()
    {
        ValueEditorViewModel editor = new(Table(), Image(), null);

        Assert.Equal("400", editor.ColumnLabel(0));
        Assert.Equal("1600", editor.ColumnLabel(3));
        Assert.Equal("15", editor.RowLabel(0));
        Assert.Equal("105", editor.RowLabel(2));
        Assert.True(editor.ColumnLabelsAreReal);
        Assert.True(editor.RowLabelsAreReal);
        Assert.True(editor.HasColumnLabels);
    }

    [Fact]
    public void Labels_FollowTheBin_WhenTheAxisIsEdited()
    {
        byte[] bin = Image();
        ValueEditorViewModel editor = new(Table(), bin, null);

        bin[0x11] = 0xE0; // first column value 1600 -> 1760 => 440

        Assert.Equal("440", editor.ColumnLabel(0));
    }

    [Fact]
    public void FixedLabels_StillUsed_WhenThereIsNoAxisAddress()
    {
        TableDefinition table = Table();
        table.XAxis = null;
        table.XLabels = new List<string> { "a", "b", "c", "d" };
        ValueEditorViewModel editor = new(table, Image(), null);

        Assert.Equal("c", editor.ColumnLabel(2));
    }

    [Fact]
    public void AxisSource_HandlesSignedLittleEndianAndStride()
    {
        byte[] bin = new byte[32];
        // 16-bit little-endian signed, one value every 32 bits: -2 then 5.
        bin[4] = 0xFE; bin[5] = 0xFF;
        bin[8] = 0x05; bin[9] = 0x00;
        AxisSource axis = new() { Address = 4, Count = 2, ElementBits = 16, IsSigned = true, IsBigEndian = false, StrideBits = 32, DecimalPlaces = 1 };

        Assert.Equal(-2, axis.ReadRaw(bin, 0));
        Assert.Equal(5, axis.ReadRaw(bin, 1));
        Assert.Equal("-2.0", axis.Label(bin, 0));
    }

    [Fact]
    public void AxisSource_PastTheEndOfTheImage_ReadsZero_NotACrash()
    {
        AxisSource axis = new() { Address = 0xFFFF00, Count = 3, ElementBits = 16 };

        Assert.Equal(0, axis.ReadRaw(new byte[16], 0));
        Assert.Equal("0", axis.Label(new byte[16], 2));
    }

    // ── The XDF side ──────────────────────────────────────────────────────

    private const string Xdf = """
        <?xml version="1.0"?>
        <XDFFORMAT version="1.60">
          <XDFHEADER><flags>0x1</flags><deftitle>test</deftitle></XDFHEADER>
          <XDFTABLE uniqueid="0x1" flags="0x30">
            <title>Axes in the BIN</title>
            <XDFAXIS id="x" uniqueid="0x0">
              <EMBEDDEDDATA mmedaddress="0x10" mmedelementsizebits="16" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <units>RPM</units>
              <indexcount>4</indexcount>
              <decimalpl>0</decimalpl>
              <MATH equation="X*0.25"><VAR id="X" /></MATH>
            </XDFAXIS>
            <XDFAXIS id="y" uniqueid="0x0">
              <EMBEDDEDDATA mmedaddress="0x20" mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <units>kPa</units>
              <indexcount>3</indexcount>
              <decimalpl>0</decimalpl>
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
            <XDFAXIS id="z">
              <EMBEDDEDDATA mmedaddress="0x40" mmedelementsizebits="8" mmedrowcount="3" mmedcolcount="4" mmedmajorstridebits="0" mmedminorstridebits="0" />
              <units>%</units>
              <decimalpl>0</decimalpl>
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
          </XDFTABLE>
          <XDFTABLE uniqueid="0x2" flags="0x30">
            <title>Fixed labels</title>
            <XDFAXIS id="x" uniqueid="0x0">
              <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <indexcount>2</indexcount>
              <LABEL index="0" value="10" /><LABEL index="1" value="20" />
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
            <XDFAXIS id="y" uniqueid="0x0">
              <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <indexcount>1</indexcount>
              <LABEL index="0" value="1" />
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
            <XDFAXIS id="z">
              <EMBEDDEDDATA mmedaddress="0x60" mmedelementsizebits="8" mmedrowcount="1" mmedcolcount="2" mmedmajorstridebits="0" mmedminorstridebits="0" />
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
          </XDFTABLE>
        </XDFFORMAT>
        """;

    [Fact]
    public void Xdf_AxisWithAnAddress_BecomesAnAxisSource_AndFixedLabelsAreUntouched()
    {
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-axis-" + Guid.NewGuid().ToString("N") + ".xdf");
        try
        {
            File.WriteAllText(path, Xdf);
            List<TableDefinition> tables = XdfParser.Parse(path);

            TableDefinition bound = tables.Single(t => t.Name == "Axes in the BIN");
            Assert.NotNull(bound.XAxis);
            Assert.Equal(0x10u, bound.XAxis!.Address);
            Assert.Equal(16, bound.XAxis.ElementBits);
            Assert.Equal(4, bound.XAxis.Count);
            Assert.Equal(0, bound.XAxis.StrideBits); // a negative stride means "unused"
            Assert.Equal(0x20u, bound.YAxis!.Address);
            Assert.Equal(3, bound.YAxis.Count);

            ValueEditorViewModel editor = new(bound, Image(), null);
            Assert.Equal("800", editor.ColumnLabel(1));
            Assert.Equal("40", editor.RowLabel(1));

            TableDefinition fixedLabels = tables.Single(t => t.Name == "Fixed labels");
            Assert.Null(fixedLabels.XAxis);
            Assert.Null(fixedLabels.YAxis);
            Assert.Equal(new[] { "10", "20" }, fixedLabels.XLabels);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
