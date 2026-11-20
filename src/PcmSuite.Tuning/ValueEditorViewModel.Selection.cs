using System.Globalization;
using System.Text;

namespace PcmSuite.Tune;

/// <summary>A rectangle of cells, both ends included.</summary>
public readonly record struct CellRange(int Row0, int Col0, int Row1, int Col1)
{
    public int Rows => this.Row1 - this.Row0 + 1;

    public int Columns => this.Col1 - this.Col0 + 1;

    public int Count => this.Rows * this.Columns;

    public bool Contains(int row, int column) =>
        row >= this.Row0 && row <= this.Row1 && column >= this.Col0 && column <= this.Col1;
}

/// <summary>Selecting cells and the spreadsheet-style operations on a selection: nudge, fill, paste, scale, interpolate, smooth.</summary>
public sealed partial class ValueEditorViewModel
{
    private (int Row, int Column)? anchor;
    private (int Row, int Column) end;

    /// <summary>Raised when the selected rectangle changes.</summary>
    public event Action? SelectionChanged;

    public bool HasSelection => this.anchor != null;

    /// <summary>The selected rectangle, or null when nothing is selected.</summary>
    public CellRange? Selection => this.anchor is { } a
        ? new CellRange(Math.Min(a.Row, this.end.Row), Math.Min(a.Column, this.end.Column), Math.Max(a.Row, this.end.Row), Math.Max(a.Column, this.end.Column))
        : null;

    /// <summary>The cell the selection started from (where typing and paste begin).</summary>
    public (int Row, int Column)? Anchor => this.anchor;

    /// <summary>The cell at the moving end of the selection (where a drag or Shift+arrow is now).</summary>
    public (int Row, int Column)? Extent => this.anchor == null ? null : this.end;

    public void Select(int row, int column, bool extend = false)
    {
        row = Math.Clamp(row, 0, this.Rows - 1);
        column = Math.Clamp(column, 0, this.Columns - 1);
        if (extend && this.anchor != null)
        {
            this.end = (row, column);
        }
        else
        {
            this.anchor = (row, column);
            this.end = (row, column);
        }

        this.SelectionChanged?.Invoke();
    }

    public void SelectColumn(int column) => this.SelectRange(0, column, this.Rows - 1, column);

    public void SelectRow(int row) => this.SelectRange(row, 0, row, this.Columns - 1);

    public void SelectAll() => this.SelectRange(0, 0, this.Rows - 1, this.Columns - 1);

    public void SelectRange(int row0, int col0, int row1, int col1)
    {
        this.anchor = (Math.Clamp(row0, 0, this.Rows - 1), Math.Clamp(col0, 0, this.Columns - 1));
        this.end = (Math.Clamp(row1, 0, this.Rows - 1), Math.Clamp(col1, 0, this.Columns - 1));
        this.SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        this.anchor = null;
        this.SelectionChanged?.Invoke();
    }

    /// <summary>Arrow keys: move the selection by one cell, or grow it when extend is set.</summary>
    public void Move(int dRow, int dColumn, bool extend)
    {
        if (this.anchor == null)
        {
            this.Select(0, 0);
            return;
        }

        (int row, int column) = extend ? this.end : this.anchor.Value;
        this.Select(row + dRow, column + dColumn, extend);
    }

    /// <summary>"12 cells · min 40.9 · max 97.9 · avg 70.1" for the selection, or empty.</summary>
    public string SelectionStats()
    {
        if (this.Selection is not { } range)
        {
            return string.Empty;
        }

        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                double v = this.Scaled(r, c);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                sum += v;
            }
        }

        string units = string.IsNullOrEmpty(this.Units) ? string.Empty : " " + this.Units;
        return range.Count == 1
            ? this.table.FormatValue(min) + units
            : $"{range.Count} cells · min {this.table.FormatValue(min)}{units} · max {this.table.FormatValue(max)}{units} · avg {this.table.FormatValue(sum / range.Count)}{units}";
    }

    // ---- Operations on the selection (each is ONE undo step) ----

    /// <summary>Move every selected cell up or down by raw steps (the smallest change the BIN can hold).</summary>
    public int Nudge(int rawSteps)
    {
        if (this.Selection is not { } range || rawSteps == 0)
        {
            return 0;
        }

        List<(int, int, double)> edits = new();
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                edits.Add((r, c, this.table.GetRawValue(this.bin, r, c) + rawSteps));
            }
        }

        return this.ApplyRaw(edits, $"Nudge {(rawSteps > 0 ? "+" : string.Empty)}{rawSteps} ({range.Rows}x{range.Columns} cells)");
    }

    /// <summary>Put one value (as typed, in the table's units) into every selected cell.</summary>
    public int Fill(double scaled)
    {
        if (this.Selection is not { } range)
        {
            return 0;
        }

        List<(int, int, double)> edits = new();
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                edits.Add((r, c, scaled));
            }
        }

        return this.ApplyBatch(edits, $"Fill {this.table.FormatValue(scaled)} ({range.Rows}x{range.Columns} cells)");
    }

    /// <summary>Multiply by a factor: the selected cells when several are selected, otherwise the whole table.</summary>
    public int Scale(double factor)
    {
        if (Math.Abs(factor) < 1e-10)
        {
            return 0;
        }

        CellRange range = this.Selection is { Count: > 1 } sel ? sel : new CellRange(0, 0, this.Rows - 1, this.Columns - 1);
        List<(int, int, double)> edits = new();
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                edits.Add((r, c, this.Scaled(r, c) * factor));
            }
        }

        string where = range.Count == this.Rows * this.Columns ? "whole table" : $"{range.Rows}x{range.Columns} cells";
        return this.ApplyBatch(edits, $"Scale x{factor:G4} ({where})");
    }

    /// <summary>Fill the selection by blending between its four corners (the corners stay as they are).</summary>
    public int Interpolate()
    {
        if (this.Selection is not { Count: > 1 } range)
        {
            return 0;
        }

        double v00 = this.Scaled(range.Row0, range.Col0);
        double v01 = this.Scaled(range.Row0, range.Col1);
        double v10 = this.Scaled(range.Row1, range.Col0);
        double v11 = this.Scaled(range.Row1, range.Col1);
        List<(int, int, double)> edits = new();
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            double tr = range.Row1 > range.Row0 ? (double)(r - range.Row0) / (range.Row1 - range.Row0) : 0.0;
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                bool corner = (r == range.Row0 || r == range.Row1) && (c == range.Col0 || c == range.Col1);
                if (corner)
                {
                    continue;
                }

                double tc = range.Col1 > range.Col0 ? (double)(c - range.Col0) / (range.Col1 - range.Col0) : 0.0;
                double value = (1 - tr) * ((1 - tc) * v00 + tc * v01) + tr * ((1 - tc) * v10 + tc * v11);
                edits.Add((r, c, value));
            }
        }

        return this.ApplyBatch(edits, $"Interpolate ({range.Rows}x{range.Columns} cells)");
    }

    /// <summary>Average each selected cell with its neighbours inside the selection (the four corners stay fixed).</summary>
    public int Smooth()
    {
        if (this.Selection is not { } range || range.Rows < 2 || range.Columns < 2)
        {
            return 0;
        }

        double[,] snapshot = new double[range.Rows, range.Columns];
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                snapshot[r - range.Row0, c - range.Col0] = this.Scaled(r, c);
            }
        }

        int[] dr = { -1, 0, 1, 0, 0 };
        int[] dc = { 0, -1, 0, 1, 0 };
        List<(int, int, double)> edits = new();
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                bool corner = (r == range.Row0 || r == range.Row1) && (c == range.Col0 || c == range.Col1);
                if (corner)
                {
                    continue;
                }

                double sum = 0;
                int count = 0;
                for (int k = 0; k < dr.Length; k++)
                {
                    int nr = r + dr[k];
                    int nc = c + dc[k];
                    if (nr < range.Row0 || nr > range.Row1 || nc < range.Col0 || nc > range.Col1)
                    {
                        continue;
                    }

                    sum += snapshot[nr - range.Row0, nc - range.Col0];
                    count++;
                }

                edits.Add((r, c, sum / count));
            }
        }

        return this.ApplyBatch(edits, $"Smooth ({range.Rows}x{range.Columns} cells)");
    }

    /// <summary>Parse typed text and put it into every selected cell. Returns how many cells changed.</summary>
    public int FillText(string text) => this.TryParse(text.Trim(), out double value) ? this.Fill(value) : 0;

    // ---- Clipboard ----

    /// <summary>The selected cells as tab-separated text, ready for a spreadsheet. Null when nothing is selected.</summary>
    public string? SelectionText()
    {
        if (this.Selection is not { } range)
        {
            return null;
        }

        StringBuilder text = new();
        for (int r = range.Row0; r <= range.Row1; r++)
        {
            for (int c = range.Col0; c <= range.Col1; c++)
            {
                if (c > range.Col0)
                {
                    text.Append('\t');
                }

                text.Append(this.CellText(r, c));
            }

            if (r < range.Row1)
            {
                text.AppendLine();
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Paste tab/newline separated numbers starting at the selection's top-left cell. A single pasted value fills the
    /// whole selection. Returns how many cells changed.
    /// </summary>
    public int Paste(string text)
    {
        if (this.Selection is not { } range)
        {
            return 0;
        }

        List<List<string>> grid = text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t').ToList())
            .ToList();
        if (grid.Count == 0)
        {
            return 0;
        }

        bool single = grid.Count == 1 && grid[0].Count == 1;
        int rowCount = single ? range.Rows : grid.Count;
        List<(int, int, double)> edits = new();
        for (int dr = 0; dr < rowCount; dr++)
        {
            int row = range.Row0 + dr;
            if (row >= this.Rows)
            {
                break;
            }

            List<string> source = single ? grid[0] : grid[dr];
            int colCount = single ? range.Columns : source.Count;
            for (int dc = 0; dc < colCount; dc++)
            {
                int column = range.Col0 + dc;
                if (column >= this.Columns)
                {
                    break;
                }

                string cell = single ? source[0] : source[dc];
                if (this.TryParse(cell.Trim(), out double scaled))
                {
                    edits.Add((row, column, scaled));
                }
            }
        }

        int changed = this.ApplyBatch(edits, $"Paste ({edits.Count} cells)");

        // Show what was pasted as the selection.
        if (edits.Count > 0)
        {
            int lastRow = Math.Min(range.Row0 + rowCount - 1, this.Rows - 1);
            int lastColumn = Math.Min(range.Col0 + (single ? range.Columns : grid.Max(r => r.Count)) - 1, this.Columns - 1);
            this.SelectRange(range.Row0, range.Col0, lastRow, lastColumn);
        }

        return changed;
    }
}
