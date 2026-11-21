using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>
/// Copies segments from a donor into a BIN and remembers what each copy overwrote, so swaps can be undone one at a time,
/// newest first. Only the swapped address ranges are put back, so edits made elsewhere survive.
/// </summary>
public sealed class SwapHistory
{
    private readonly Stack<(string Description, List<(uint Start, byte[] Bytes)> Ranges)> swaps = new();

    /// <summary>How many swaps can still be undone.</summary>
    public int Count => this.swaps.Count;

    /// <summary>Copy the segments from the donor into the target (in memory) and remember what they replaced.</summary>
    public void Apply(byte[] target, byte[] donor, IReadOnlyList<SegmentInfo> segments)
    {
        List<(uint, byte[])> backup = new();
        foreach (SegmentInfo s in segments)
        {
            byte[] before = new byte[s.ByteCount];
            Array.Copy(target, s.StartAddress, before, 0, s.ByteCount);
            backup.Add((s.StartAddress, before));
            Array.Copy(donor, s.StartAddress, target, s.StartAddress, s.ByteCount);
        }

        this.swaps.Push((string.Join(", ", segments.Select(s => s.Name)), backup));
    }

    /// <summary>Put back what the newest swap overwrote. Returns the names of the segments restored, or null if there was nothing to undo.</summary>
    public string? Undo(byte[] target)
    {
        if (this.swaps.Count == 0)
        {
            return null;
        }

        (string description, List<(uint Start, byte[] Bytes)> ranges) = this.swaps.Pop();
        foreach ((uint start, byte[] bytes) in ranges)
        {
            Array.Copy(bytes, 0, target, start, bytes.Length);
        }

        return description;
    }

    /// <summary>Forget everything (a different BIN was opened).</summary>
    public void Clear() => this.swaps.Clear();
}
