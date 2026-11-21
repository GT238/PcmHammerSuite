using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>One table that holds different values in the two BINs.</summary>
public sealed record TableDiff(TableDefinition Table, int CellsChanged, int CellsTotal);

/// <summary>The result of comparing two BINs through an XDF: the tables that differ, and the bytes no table accounts for.</summary>
public sealed record BinComparison(IReadOnlyList<TableDiff> Tables, long DifferingBytes, long UnmappedBytes);

/// <summary>The pure logic behind the compare workbench: which tables differ, and copying whole tables from one BIN into another.</summary>
public static class BinCompare
{
    /// <summary>Compare two images of the same size. Tables are listed by category then name; tables with no difference are left out.</summary>
    public static BinComparison Compare(IEnumerable<TableDefinition> tables, byte[] open, byte[] other)
    {
        List<TableDiff> diffs = new();
        bool[] covered = new bool[Math.Min(open.Length, other.Length)];
        foreach (TableDefinition table in tables)
        {
            int rows = Math.Max(1, table.Rows);
            int columns = Math.Max(1, table.Columns);
            int size = Math.Max(1, table.SizeInBytes);
            int changed = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int at = table.CellOffset(r, c);
                    for (int i = 0; i < size && at >= 0 && at + i < covered.Length; i++)
                    {
                        covered[at + i] = true;
                    }

                    if (table.GetRawValue(open, r, c) != table.GetRawValue(other, r, c))
                    {
                        changed++;
                    }
                }
            }

            if (changed > 0)
            {
                diffs.Add(new TableDiff(table, changed, rows * columns));
            }
        }

        long differing = 0;
        long unmapped = 0;
        for (int i = 0; i < covered.Length; i++)
        {
            if (open[i] != other[i])
            {
                differing++;
                if (!covered[i])
                {
                    unmapped++;
                }
            }
        }

        diffs.Sort((x, y) =>
        {
            int byCategory = string.Compare(x.Table.Category, y.Table.Category, StringComparison.OrdinalIgnoreCase);
            return byCategory != 0 ? byCategory : string.Compare(x.Table.Name, y.Table.Name, StringComparison.OrdinalIgnoreCase);
        });
        return new BinComparison(diffs, differing, unmapped);
    }

    /// <summary>
    /// Copy every cell of a table from <paramref name="source"/> into <paramref name="target"/>. A flag keeps the other
    /// bits of its word. Returns what was overwritten, so the copy can be put back.
    /// </summary>
    public static List<(int Offset, byte[] Bytes)> CopyTable(TableDefinition table, byte[] target, byte[] source)
    {
        List<(int, byte[])> backup = new();
        int rows = Math.Max(1, table.Rows);
        int columns = Math.Max(1, table.Columns);
        int size = Math.Max(1, table.SizeInBytes);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int at = table.CellOffset(r, c);
                if (at < 0 || at + size > target.Length || at + size > source.Length)
                {
                    continue;
                }

                backup.Add((at, target.AsSpan(at, size).ToArray()));
                if (table.Mask != 0)
                {
                    uint value = (table.ReadElement(target, at) & ~table.Mask) | (table.ReadElement(source, at) & table.Mask);
                    table.WriteElement(target, at, value);
                }
                else
                {
                    Array.Copy(source, at, target, at, size);
                }
            }
        }

        return backup;
    }
}

/// <summary>Remembers each copy-back so they can be undone one at a time, newest first.</summary>
public sealed class CopyBackHistory
{
    private readonly Stack<(string Description, List<(int Offset, byte[] Bytes)> Backup)> copies = new();

    public int Count => this.copies.Count;

    /// <summary>Copy the tables from the source into the target and remember what they replaced.</summary>
    public void Apply(byte[] target, byte[] source, IReadOnlyList<TableDefinition> tables)
    {
        List<(int, byte[])> all = new();
        foreach (TableDefinition table in tables)
        {
            all.AddRange(BinCompare.CopyTable(table, target, source));
        }

        this.copies.Push((string.Join(", ", tables.Select(t => t.Name)), all));
    }

    /// <summary>Put back what the newest copy replaced. Returns its description, or null if there was nothing to undo.</summary>
    public string? Undo(byte[] target)
    {
        if (this.copies.Count == 0)
        {
            return null;
        }

        (string description, List<(int Offset, byte[] Bytes)> backup) = this.copies.Pop();
        for (int i = backup.Count - 1; i >= 0; i--)
        {
            Array.Copy(backup[i].Bytes, 0, target, backup[i].Offset, backup[i].Bytes.Length);
        }

        return description;
    }

    public void Clear() => this.copies.Clear();
}
