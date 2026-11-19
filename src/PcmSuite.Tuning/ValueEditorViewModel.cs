using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>
/// Reads and edits the values of one table (or single value / flag) in the loaded BIN.
/// Edits change the BIN's bytes in memory; nothing reaches disk until the BIN is saved.
/// </summary>
public sealed partial class ValueEditorViewModel : ObservableObject
{
    private readonly TableDefinition table;
    private readonly byte[] bin;
    private readonly Func<string, double>? crossRef;
    private readonly Stack<Action> undoStack = new();

    public ValueEditorViewModel(TableDefinition table, byte[] bin, Func<string, double>? crossRef)
    {
        this.table = table;
        this.bin = bin;
        this.crossRef = crossRef;
    }

    /// <summary>Raised after any change to the BIN's bytes.</summary>
    public event Action? DataChanged;

    public TableDefinition Table => this.table;

    public string Name => this.table.Name;

    public string Units => this.table.ZUnits;

    public int Rows => Math.Max(1, this.table.Rows);

    public int Columns => Math.Max(1, this.table.Columns);

    /// <summary>True when the table has several rows and columns, so a 3D surface makes sense.</summary>
    public bool Is2D => this.table.Mask == 0 && this.table.Rows > 1 && this.table.Columns > 1;

    public bool IsFlag => this.table.Mask != 0;

    public bool IsSingleValue => this.Rows == 1 && this.Columns == 1;

    public bool HasColumnLabels => this.Columns > 1 || this.table.XLabels.Count > 0 && this.Rows > 1;

    public bool HasRowLabels => this.Rows > 1;

    public bool CanUndo => this.undoStack.Count > 0;

    public string XAxisTitle => this.table.XUnits;

    public string YAxisTitle => this.table.YUnits;

    public string ColumnLabel(int column) =>
        column < this.table.XLabels.Count ? this.table.XLabels[column] : (column + 1).ToString();

    public string RowLabel(int row) =>
        row < this.table.YLabels.Count ? this.table.YLabels[row] : (row + 1).ToString();

    public double Scaled(int row, int column) =>
        this.table.GetScaledValue(this.bin, row, column, this.crossRef);

    public string CellText(int row, int column) => this.table.FormatValue(this.Scaled(row, column));

    /// <summary>Lowest and highest scaled value in the table, for the colour scale.</summary>
    public (double Min, double Max) Range()
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        for (int r = 0; r < this.Rows; r++)
        {
            for (int c = 0; c < this.Columns; c++)
            {
                double v = this.Scaled(r, c);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        return min > max ? (0, 0) : (min, max);
    }

    /// <summary>Applies text typed into a cell. Returns true when the BIN changed.</summary>
    public bool TryCommit(int row, int column, string text, out string shown)
    {
        shown = this.CellText(row, column);
        if (!this.TryParse(text.Trim(), out double scaled))
        {
            return false;
        }

        double raw = Math.Clamp(this.table.InverseScale(scaled, this.crossRef), this.RawMin, this.RawMax);
        double oldRaw = this.table.GetRawValue(this.bin, row, column);
        if (raw == oldRaw)
        {
            return false;
        }

        this.table.SetRawValue(this.bin, row, column, raw);

        // A cell whose address lies outside this BIN cannot be written (the XDF may not match the file).
        if (this.table.GetRawValue(this.bin, row, column) != raw)
        {
            shown = this.CellText(row, column);
            return false;
        }

        this.undoStack.Push(() => this.table.SetRawValue(this.bin, row, column, oldRaw));
        shown = this.CellText(row, column);
        this.Changed();
        return true;
    }

    /// <summary>The BIN to compare against (same size as the loaded one), or null.</summary>
    public byte[]? Reference { get; set; }

    public bool IsChanged(int row, int column) =>
        this.Reference != null
        && this.table.GetRawValue(this.bin, row, column) != this.table.GetRawValue(this.Reference, row, column);

    public string? ReferenceText(int row, int column) =>
        this.Reference == null
            ? null
            : this.table.FormatValue(this.table.GetScaledValue(this.Reference, row, column, this.crossRef));

    public bool DiffersFromReference() => DiffersFromReference(this.table, this.bin, this.Reference);

    public static bool DiffersFromReference(TableDefinition table, byte[] bin, byte[]? reference)
    {
        if (reference == null)
        {
            return false;
        }

        int rows = Math.Max(1, table.Rows);
        int columns = Math.Max(1, table.Columns);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (table.GetRawValue(bin, r, c) != table.GetRawValue(reference, r, c))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The table as tab-separated text with its axis labels, ready to paste into a spreadsheet.</summary>
    public string ToTabText()
    {
        System.Text.StringBuilder text = new();
        bool columnLabels = this.HasColumnLabels;
        bool rowLabels = this.HasRowLabels;
        if (columnLabels)
        {
            text.Append(rowLabels ? this.YAxisTitle + "\t" : string.Empty);
            text.AppendLine(string.Join("\t", Enumerable.Range(0, this.Columns).Select(this.ColumnLabel)));
        }

        for (int r = 0; r < this.Rows; r++)
        {
            if (rowLabels)
            {
                text.Append(this.RowLabel(r)).Append('\t');
            }

            text.AppendLine(string.Join("\t", Enumerable.Range(0, this.Columns).Select(c => this.CellText(r, c))));
        }

        return text.ToString();
    }

    /// <summary>
    /// Sets many cells at once from scaled values (for example the VE wizard's corrections), as ONE undo step.
    /// Returns how many cells changed.
    /// </summary>
    public int ApplyBatch(IEnumerable<(int Row, int Column, double Scaled)> edits)
    {
        List<(int Row, int Column, double Old)> before = new();
        foreach ((int row, int column, double scaled) in edits)
        {
            double raw = Math.Clamp(this.table.InverseScale(scaled, this.crossRef), this.RawMin, this.RawMax);
            double old = this.table.GetRawValue(this.bin, row, column);
            if (raw == old)
            {
                continue;
            }

            this.table.SetRawValue(this.bin, row, column, raw);
            if (this.table.GetRawValue(this.bin, row, column) != raw)
            {
                continue;
            }

            before.Add((row, column, old));
        }

        if (before.Count == 0)
        {
            return 0;
        }

        this.undoStack.Push(() =>
        {
            for (int i = before.Count - 1; i >= 0; i--)
            {
                this.table.SetRawValue(this.bin, before[i].Row, before[i].Column, before[i].Old);
            }
        });
        this.Changed();
        return before.Count;
    }

    public bool GetFlag() => this.table.GetRawValue(this.bin, 0, 0) != 0;

    /// <summary>
    /// Sets or clears the flag's bit only. (The table's own raw writer replaces the whole byte,
    /// which would clobber the other flags stored in it.)
    /// </summary>
    public void SetFlag(bool on)
    {
        int offset = (int)this.table.Address;
        if (offset < 0 || offset >= this.bin.Length)
        {
            return;
        }

        byte before = this.bin[offset];
        byte after = on ? (byte)(before | this.table.Mask) : (byte)(before & ~this.table.Mask);
        if (after == before)
        {
            return;
        }

        this.bin[offset] = after;
        this.undoStack.Push(() => this.bin[offset] = before);
        this.Changed();
    }

    public bool Undo()
    {
        if (this.undoStack.Count == 0)
        {
            return false;
        }

        this.undoStack.Pop().Invoke();
        this.Changed();
        return true;
    }

    private long RawMin => this.table.IsSigned ? (this.table.ElementBits == 8 ? -128 : this.table.ElementBits == 16 ? -32768 : int.MinValue) : 0;

    private long RawMax => this.table.ElementBits == 8
        ? (this.table.IsSigned ? 127 : 255)
        : this.table.ElementBits == 16
            ? (this.table.IsSigned ? 32767 : 65535)
            : (this.table.IsSigned ? int.MaxValue : uint.MaxValue);

    private bool TryParse(string text, out double value)
    {
        value = 0;
        if (text.Length == 0)
        {
            return false;
        }

        switch (this.table.OutputType)
        {
            case 3: // hex
                if (long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex))
                {
                    value = hex;
                    return true;
                }

                return false;

            case 4: // ASCII character
                if (text.Length == 1)
                {
                    value = text[0];
                    return true;
                }

                return false;

            default:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || double.TryParse(text, out value);
        }
    }

    private void Changed()
    {
        this.OnPropertyChanged(nameof(this.CanUndo));
        this.DataChanged?.Invoke();
    }
}
