using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>One cell's value before and after an edit, in the table's units.</summary>
public sealed record CellChange(int Row, int Column, double Old, double New);

/// <summary>What one edit, undo or redo did: its name and every cell it changed.</summary>
public sealed record EditRecord(string Kind, string Label, IReadOnlyList<CellChange> Cells);

/// <summary>
/// Reads and edits the values of one table (or single value / flag) in the loaded BIN.
/// Edits change the BIN's bytes in memory; nothing reaches disk until the BIN is saved.
/// </summary>
public sealed partial class ValueEditorViewModel : ObservableObject
{
    private readonly TableDefinition table;
    private readonly byte[] bin;
    private readonly Func<string, double>? crossRef;
    private readonly List<EditStep> undoStack = new();
    private readonly List<EditStep> redoStack = new();

    /// <summary>One undoable change: what it was called, how to take it back and how to do it again.</summary>
    private sealed record EditStep(string Label, Action Undo, Action Redo, IReadOnlyList<CellChange> Cells);

    /// <summary>Raised after every edit, undo and redo, so the Tuner can keep a change log.</summary>
    public event Action<EditRecord>? Edited;

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

    /// <summary>A single column of values with a trouble code on each row. The XDF gives its one column a placeholder label ("0.00"), which is not worth showing.</summary>
    public bool IsTroubleCodeList => this.Columns == 1 && this.Rows > 1 && DtcCatalog.IsCode(this.RowLabel(0));

    public bool HasColumnLabels => !this.IsTroubleCodeList && (this.Columns > 1 || (this.table.XLabels.Count > 0 || this.table.XAxis != null) && this.Rows > 1);

    public bool HasRowLabels => this.Rows > 1;

    public bool CanUndo => this.undoStack.Count > 0;

    public bool CanRedo => this.redoStack.Count > 0;

    /// <summary>Recent edits, newest first, for the history list.</summary>
    public IReadOnlyList<string> History => this.undoStack.AsEnumerable().Reverse().Take(30).Select(s => s.Label).ToList();

    /// <summary>False when the column labels are only the placeholders 1..N, not real axis values.</summary>
    public bool ColumnLabelsAreReal => this.table.XLabelsAreReal || this.table.XAxis != null;

    public bool RowLabelsAreReal => this.table.YLabelsAreReal || this.table.YAxis != null;

    public string XAxisTitle => this.table.XUnits;

    public string YAxisTitle => this.table.YUnits;

    /// <summary>The column heading: read from the BIN when the table says where its axis is stored, otherwise the label written in the definition.</summary>
    public string ColumnLabel(int column) =>
        this.table.XAxis != null && column < this.table.XAxis.Count ? this.table.XAxis.Label(this.bin, column, this.crossRef) :
        column < this.table.XLabels.Count ? this.table.XLabels[column] : (column + 1).ToString();

    public string RowLabel(int row) =>
        this.table.YAxis != null && row < this.table.YAxis.Count ? this.table.YAxis.Label(this.bin, row, this.crossRef) :
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

        bool changed = this.ApplyRaw(new[] { (row, column, this.table.InverseScale(scaled, this.crossRef)) }, "Typed value") > 0;
        shown = this.CellText(row, column);
        return changed;
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
    public int ApplyBatch(IEnumerable<(int Row, int Column, double Scaled)> edits, string label = "Batch edit") =>
        this.ApplyRaw(edits.Select(e => (e.Row, e.Column, this.table.InverseScale(e.Scaled, this.crossRef))).ToList(), label);

    /// <summary>The one place cells change: rounds and clamps each raw value, writes it, and records a single undo step.</summary>
    private int ApplyRaw(IEnumerable<(int Row, int Column, double Raw)> edits, string label)
    {
        List<(int Row, int Column, double Old, double New)> done = new();
        List<CellChange> changes = new();
        foreach ((int row, int column, double wanted) in edits)
        {
            double raw = Math.Round(Math.Clamp(wanted, this.RawMin, this.RawMax));
            double old = this.table.GetRawValue(this.bin, row, column);
            if (raw == old)
            {
                continue;
            }

            double oldScaled = this.Scaled(row, column);
            this.table.SetRawValue(this.bin, row, column, raw);

            // A cell whose address lies outside this BIN cannot be written (the XDF may not match the file).
            if (this.table.GetRawValue(this.bin, row, column) != raw)
            {
                continue;
            }

            done.Add((row, column, old, raw));
            changes.Add(new CellChange(row, column, oldScaled, this.Scaled(row, column)));
        }

        if (done.Count == 0)
        {
            return 0;
        }

        this.Push(new EditStep(
            label,
            () =>
            {
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    this.table.SetRawValue(this.bin, done[i].Row, done[i].Column, done[i].Old);
                }
            },
            () =>
            {
                foreach ((int row, int column, _, double next) in done)
                {
                    this.table.SetRawValue(this.bin, row, column, next);
                }
            },
            changes));
        this.Changed();
        this.Edited?.Invoke(new EditRecord("Edit", label, changes));
        return done.Count;
    }

    private void Push(EditStep step)
    {
        this.undoStack.Add(step);
        this.redoStack.Clear();
    }

    public bool GetFlag() => this.table.GetRawValue(this.bin, 0, 0) != 0;

    /// <summary>
    /// Sets or clears the flag's bits only, in the element's full width and byte order. (The table's own raw writer replaces the
    /// whole element, which would clobber the other flags stored in it.)
    /// </summary>
    public void SetFlag(bool on)
    {
        int offset = (int)this.table.Address;
        if (offset < 0 || offset + this.table.SizeInBytes > this.bin.Length)
        {
            return;
        }

        uint before = this.table.ReadElement(this.bin, offset);
        uint after = on ? before | this.table.Mask : before & ~this.table.Mask;
        if (after == before)
        {
            return;
        }

        this.table.WriteElement(this.bin, offset, after);
        this.Push(new EditStep(
            "Flag " + this.Name,
            () => this.table.WriteElement(this.bin, offset, before),
            () => this.table.WriteElement(this.bin, offset, after),
            Array.Empty<CellChange>()));
        this.Changed();
        this.Edited?.Invoke(new EditRecord("Edit", (on ? "Set flag " : "Clear flag ") + this.Name, Array.Empty<CellChange>()));
    }

    public bool Undo()
    {
        if (this.undoStack.Count == 0)
        {
            return false;
        }

        EditStep step = this.undoStack[^1];
        this.undoStack.RemoveAt(this.undoStack.Count - 1);
        step.Undo();
        this.redoStack.Add(step);
        this.Changed();
        this.Edited?.Invoke(new EditRecord("Undo", step.Label, step.Cells.Select(c => new CellChange(c.Row, c.Column, c.New, c.Old)).ToList()));
        return true;
    }

    public bool Redo()
    {
        if (this.redoStack.Count == 0)
        {
            return false;
        }

        EditStep step = this.redoStack[^1];
        this.redoStack.RemoveAt(this.redoStack.Count - 1);
        step.Redo();
        this.undoStack.Add(step);
        this.Changed();
        this.Edited?.Invoke(new EditRecord("Redo", step.Label, step.Cells));
        return true;
    }

    /// <summary>Undo several edits at once (jump back in the history list).</summary>
    public int UndoSteps(int steps)
    {
        int undone = 0;
        while (undone < steps && this.Undo())
        {
            undone++;
        }

        return undone;
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
        this.OnPropertyChanged(nameof(this.CanRedo));
        this.DataChanged?.Invoke();
    }
}
