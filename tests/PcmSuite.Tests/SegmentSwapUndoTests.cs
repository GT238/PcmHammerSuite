using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Undo segment swap must step back through swaps one at a time, newest first.</summary>
public class SegmentSwapUndoTests
{
    private static SegmentInfo Segment(string name, uint start, uint end) => new() { Index = 1, Name = name, StartAddress = start, EndAddress = end };

    private static byte[] Filled(byte value) => Enumerable.Repeat(value, 4096).ToArray();

    [Fact]
    public void TwoSwaps_AreUndoneOneAtATime_NewestFirst()
    {
        byte[] bin = Filled(0x11);
        byte[] donor = Filled(0xAA);
        SwapHistory history = new();

        history.Apply(bin, donor, new[] { Segment("Engine Calibration", 0x100, 0x1FF) });
        history.Apply(bin, donor, new[] { Segment("System", 0x400, 0x4FF) });
        Assert.Equal(0xAA, bin[0x150]);
        Assert.Equal(0xAA, bin[0x450]);
        Assert.Equal(2, history.Count);

        Assert.Equal("System", history.Undo(bin));            // the System swap goes back...
        Assert.Equal(0x11, bin[0x450]);
        Assert.Equal(0xAA, bin[0x150]);                       // ...and the earlier one is still in place
        Assert.Equal(1, history.Count);                       // so Undo is still available

        Assert.Equal("Engine Calibration", history.Undo(bin));
        Assert.Equal(0, history.Count);
        Assert.All(bin, b => Assert.Equal(0x11, b));          // the BIN is back to what it was
    }

    [Fact]
    public void UndoWithNothingToUndo_ChangesNothing()
    {
        byte[] bin = Filled(0x22);
        Assert.Null(new SwapHistory().Undo(bin));
        Assert.All(bin, b => Assert.Equal(0x22, b));
    }

    [Fact]
    public void UndoOnlyRestoresTheSwappedRange_NotEditsMadeElsewhere()
    {
        byte[] bin = Filled(0x33);
        SwapHistory history = new();
        history.Apply(bin, Filled(0xBB), new[] { Segment("Fuel System", 0x200, 0x2FF) });

        bin[0x800] = 0x77;                                    // an edit outside the swapped range
        history.Undo(bin);

        Assert.Equal(0x33, bin[0x250]);
        Assert.Equal(0x77, bin[0x800]);
    }

    [Fact]
    public void OverlappingSwaps_UnwindCorrectly()
    {
        byte[] bin = Filled(0x01);
        SwapHistory history = new();
        history.Apply(bin, Filled(0x02), new[] { Segment("A", 0x100, 0x2FF) });
        history.Apply(bin, Filled(0x03), new[] { Segment("B", 0x200, 0x3FF) });   // overlaps the first

        history.Undo(bin);
        Assert.Equal(0x02, bin[0x250]);                       // what the first swap left there
        Assert.Equal(0x01, bin[0x350]);                       // never touched by the first

        history.Undo(bin);
        Assert.All(bin, b => Assert.Equal(0x01, b));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        byte[] bin = Filled(0x44);
        SwapHistory history = new();
        history.Apply(bin, Filled(0x55), new[] { Segment("A", 0x100, 0x1FF) });

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Null(history.Undo(bin));
        Assert.Equal(0x55, bin[0x150]);                       // the swap stays; there is just nothing left to undo it with
    }
}
