using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>The compare workbench's logic: which tables differ, what no table accounts for, and copying tables with undo.</summary>
public class BinCompareTests
{
    private static TableDefinition Table(string name, uint address, int rows, int cols, int bits = 16) => new()
    {
        Name = name,
        Category = "Test",
        Address = address,
        Rows = rows,
        Columns = cols,
        ElementBits = bits,
    };

    [Fact]
    public void CellOffset_MatchesWhereValuesAreReadAndWritten()
    {
        foreach (TableDefinition table in new[]
        {
            Table("plain", 0x100, 3, 4),
            new TableDefinition { Name = "byte", Address = 0x80, Rows = 2, Columns = 5, ElementBits = 8 },
            new TableDefinition { Name = "strided", Address = 0x200, Rows = 2, Columns = 3, ElementBits = 16, MajorStrideBits = 64 },
            new TableDefinition { Name = "override", Address = 0x10, Rows = 2, Columns = 2, ElementBits = 16, RowAddressOverrides = new() { [0] = 0x300, [1] = 0x340 } },
        })
        {
            for (int r = 0; r < table.Rows; r++)
            {
                for (int c = 0; c < table.Columns; c++)
                {
                    byte[] bin = new byte[0x500];
                    table.SetRawValue(bin, r, c, table.SizeInBytes == 1 ? 77 : 0x1234);
                    int at = table.CellOffset(r, c);
                    Assert.True(bin[at] != 0, $"{table.Name} ({r},{c}) offset {at:X}");
                    Assert.Equal(bin.Count(b => b != 0), table.SizeInBytes == 1 ? 1 : 2);
                }
            }
        }
    }

    [Fact]
    public void Compare_ListsOnlyTablesThatDiffer_AndCountsBytesOutsideTables()
    {
        TableDefinition a = Table("A", 0x100, 2, 2);
        TableDefinition b = Table("B", 0x200, 1, 4);
        byte[] open = new byte[0x400];
        byte[] other = new byte[0x400];
        a.SetRawValue(other, 1, 0, 500);       // one cell of A differs
        other[0x300] = 9;                       // a byte no table covers
        other[0x301] = 9;

        BinComparison result = BinCompare.Compare(new[] { a, b }, open, other);

        TableDiff only = Assert.Single(result.Tables);
        Assert.Same(a, only.Table);
        Assert.Equal(1, only.CellsChanged);
        Assert.Equal(4, only.CellsTotal);
        Assert.Equal(2, result.UnmappedBytes);
        Assert.True(result.DifferingBytes >= 4);   // the cell's changed bytes plus the two outside
    }

    [Fact]
    public void Compare_IdenticalImages_HasNothing()
    {
        byte[] bin = new byte[0x400];
        BinComparison result = BinCompare.Compare(new[] { Table("A", 0x100, 2, 2) }, bin, (byte[])bin.Clone());
        Assert.Empty(result.Tables);
        Assert.Equal(0, result.DifferingBytes);
    }

    [Fact]
    public void CopyTable_CopiesOnlyThatTable_AndUndoRestoresIt()
    {
        TableDefinition a = Table("A", 0x100, 2, 2);
        TableDefinition b = Table("B", 0x200, 1, 4);
        byte[] open = new byte[0x400];
        byte[] other = new byte[0x400];
        for (int i = 0; i < other.Length; i++)
        {
            other[i] = (byte)(i % 251 + 1);
            open[i] = (byte)(i % 13);
        }

        byte[] before = (byte[])open.Clone();
        CopyBackHistory history = new();
        history.Apply(open, other, new[] { a });

        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 2; c++)
            {
                Assert.Equal(a.GetRawValue(other, r, c), a.GetRawValue(open, r, c));
            }
        }

        // Everything outside A is untouched, including table B.
        for (int i = 0; i < open.Length; i++)
        {
            if (i is < 0x100 or >= 0x108)
            {
                Assert.Equal(before[i], open[i]);
            }
        }

        Assert.Equal("A", history.Undo(open));
        Assert.Equal(before, open);
        Assert.Null(history.Undo(open));
        _ = b;
    }

    [Fact]
    public void CopyTable_Flag_KeepsTheOtherBitsOfTheWord()
    {
        TableDefinition flag = new() { Name = "F", Address = 0x10, Rows = 1, Columns = 1, ElementBits = 16, Mask = 0x0100 };
        byte[] open = new byte[0x40];
        byte[] other = new byte[0x40];
        open[0x10] = 0x00; open[0x11] = 0xAB;       // flag clear, low byte 0xAB
        other[0x10] = 0x01; other[0x11] = 0x55;      // flag set, low byte 0x55 (not part of the flag)

        BinCompare.CopyTable(flag, open, other);

        Assert.Equal(0x01, open[0x10]);
        Assert.Equal(0xAB, open[0x11]);              // the other bits are still ours
    }

    [Fact]
    public void Undo_PutsBackNewestFirst_AndSkipsNothing()
    {
        TableDefinition a = Table("A", 0x100, 1, 2);
        byte[] open = new byte[0x200];
        byte[] one = Enumerable.Repeat((byte)1, 0x200).ToArray();
        byte[] two = Enumerable.Repeat((byte)2, 0x200).ToArray();
        CopyBackHistory history = new();

        history.Apply(open, one, new[] { a });
        history.Apply(open, two, new[] { a });
        Assert.Equal(2, history.Count);
        Assert.Equal(2, open[0x100]);

        history.Undo(open);
        Assert.Equal(1, open[0x100]);
        history.Undo(open);
        Assert.Equal(0, open[0x100]);
        Assert.Equal(0, history.Count);
    }
}
