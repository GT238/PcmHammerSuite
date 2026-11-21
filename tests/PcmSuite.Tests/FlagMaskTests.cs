using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Bit flags: the mask applies to the whole element, so it can be wider than a byte and respects byte order.</summary>
public class FlagMaskTests
{
    private static TableDefinition Flag(uint mask, int bits, bool bigEndian = true) => new()
    {
        Name = "Test flag",
        Address = 4,
        Rows = 1,
        Columns = 1,
        ElementBits = bits,
        Mask = mask,
        IsBigEndian = bigEndian,
    };

    [Fact]
    public void EightBitFlag_StillWorks()
    {
        byte[] bin = new byte[16];
        bin[4] = 0b0000_0100;
        ValueEditorViewModel editor = new(Flag(0x04, 8), bin, null);

        Assert.True(editor.GetFlag());
        editor.SetFlag(false);
        Assert.Equal(0, bin[4]);
        Assert.False(editor.GetFlag());
    }

    [Fact]
    public void SixteenBitMaskAboveAByte_IsReadAndWrittenWithoutTouchingOtherBits()
    {
        byte[] bin = new byte[16];
        bin[4] = 0x01;     // 0x0123 big-endian
        bin[5] = 0x23;
        ValueEditorViewModel editor = new(Flag(0x0100, 16), bin, null);

        Assert.True(editor.GetFlag());
        editor.SetFlag(false);
        Assert.Equal(new byte[] { 0x00, 0x23 }, new[] { bin[4], bin[5] });   // the other bits survive
        Assert.False(editor.GetFlag());

        editor.SetFlag(true);
        Assert.Equal(new byte[] { 0x01, 0x23 }, new[] { bin[4], bin[5] });
    }

    [Fact]
    public void LittleEndianElement_IsReadInItsOwnOrder()
    {
        byte[] bin = new byte[16];
        bin[4] = 0x23;     // 0x0123 little-endian
        bin[5] = 0x01;
        ValueEditorViewModel editor = new(Flag(0x0100, 16, bigEndian: false), bin, null);

        Assert.True(editor.GetFlag());
        editor.SetFlag(false);
        Assert.Equal(new byte[] { 0x23, 0x00 }, new[] { bin[4], bin[5] });
    }

    [Fact]
    public void ByteMaskOnASixteenBitElement_MeansTheLowByteOfBigEndian()
    {
        byte[] bin = new byte[16];
        bin[5] = 0x80;     // 0x0080: the mask 0x80 applies to the whole 16-bit value
        ValueEditorViewModel editor = new(Flag(0x80, 16), bin, null);

        Assert.True(editor.GetFlag());
        bin[5] = 0x00;
        bin[4] = 0x80;     // 0x8000: the mask no longer matches
        Assert.False(editor.GetFlag());
    }

    [Fact]
    public void Undo_PutsTheWholeElementBack()
    {
        byte[] bin = new byte[16];
        bin[4] = 0xFF;
        bin[5] = 0xFF;
        ValueEditorViewModel editor = new(Flag(0x8000, 16), bin, null);

        editor.SetFlag(false);
        Assert.Equal(new byte[] { 0x7F, 0xFF }, new[] { bin[4], bin[5] });
        editor.Undo();
        Assert.Equal(new byte[] { 0xFF, 0xFF }, new[] { bin[4], bin[5] });
        editor.Redo();
        Assert.Equal(new byte[] { 0x7F, 0xFF }, new[] { bin[4], bin[5] });
    }

    [Fact]
    public void XdfFlagWithAWideMask_IsParsedAsAFlag()
    {
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-flag-" + Guid.NewGuid().ToString("N") + ".xdf");
        try
        {
            File.WriteAllText(path,
                "<?xml version=\"1.0\"?><XDFFORMAT version=\"1.60\"><XDFHEADER><deftitle>t</deftitle></XDFHEADER>" +
                "<XDFFLAG uniqueid=\"0x10\" flags=\"0x0\"><title>Wide flag</title>" +
                "<EMBEDDEDDATA mmedaddress=\"0x104\" mmedelementsizebits=\"16\" mmedmajorstridebits=\"0\" mmedminorstridebits=\"0\" />" +
                "<mask>0x0100</mask></XDFFLAG></XDFFORMAT>");

            List<TableDefinition> tables = XdfParser.Parse(path);

            TableDefinition flag = Assert.Single(tables);
            Assert.Equal(0x0100u, flag.Mask);
            Assert.Equal(16, flag.ElementBits);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
