using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PcmSuite.Tune;

/// <summary>
/// A read-only hex dump of a BIN: address, 16 bytes per row, and an ASCII column. Only the rows on screen are drawn,
/// so a 1 MB image costs the same as a small one. Bytes that differ from a reference image and the bytes of the open table are shaded.
/// </summary>
public sealed class HexControl : Control
{
    private const int BytesPerRow = 16;

    private static readonly Typeface Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
    private const double FontSize = 12.5;

    private byte[] data = Array.Empty<byte>();
    private byte[]? reference;
    private int selected;
    private int topRow;
    private (int Start, int End) highlight;
    private double charWidth;
    private double rowHeight;

    public HexControl()
    {
        this.Focusable = true;
        this.ClipToBounds = true;
        this.MeasureText();
    }

    /// <summary>Raised when the scroll position or the selected byte changes.</summary>
    public event EventHandler? ViewChanged;

    public byte[] Data
    {
        get => this.data;
        set
        {
            this.data = value;
            this.selected = Math.Clamp(this.selected, 0, Math.Max(0, value.Length - 1));
            this.topRow = Math.Clamp(this.topRow, 0, this.MaxTopRow);
            this.Changed();
        }
    }

    /// <summary>The image to compare with, or null. Bytes that differ are shaded red.</summary>
    public byte[]? Reference
    {
        get => this.reference;
        set
        {
            this.reference = value;
            this.InvalidateVisual();
        }
    }

    public int RowCount => (this.data.Length + BytesPerRow - 1) / BytesPerRow;

    public int VisibleRows => Math.Max(1, (int)(this.Bounds.Height / this.rowHeight));

    public int MaxTopRow => Math.Max(0, this.RowCount - this.VisibleRows);

    public int TopRow
    {
        get => this.topRow;
        set
        {
            int clamped = Math.Clamp(value, 0, this.MaxTopRow);
            if (clamped != this.topRow)
            {
                this.topRow = clamped;
                this.Changed();
            }
        }
    }

    public int SelectedIndex => this.selected;

    /// <summary>Shade a range of bytes (for example the open table). End is exclusive; an empty range clears it.</summary>
    public void SetHighlight(int start, int end)
    {
        this.highlight = (start, end);
        this.InvalidateVisual();
    }

    /// <summary>Select a byte and scroll so it is visible (centred if it was off screen).</summary>
    public void Select(int index, bool center = false)
    {
        if (this.data.Length == 0)
        {
            return;
        }

        this.selected = Math.Clamp(index, 0, this.data.Length - 1);
        int row = this.selected / BytesPerRow;
        if (center || row < this.topRow || row >= this.topRow + this.VisibleRows)
        {
            this.topRow = Math.Clamp(row - this.VisibleRows / 2, 0, this.MaxTopRow);
        }

        this.Changed();
    }

    private void Changed()
    {
        this.InvalidateVisual();
        this.ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MeasureText()
    {
        FormattedText probe = new("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, Brushes.Black);
        this.charWidth = probe.Width / 10;
        this.rowHeight = Math.Ceiling(probe.Height) + 2;
    }

    /// <summary>The width the dump needs: address, two groups of eight bytes, and the ASCII column.</summary>
    public double DumpWidth => (this.HexX + 16 * 3 + 1 + 2 + BytesPerRow) * this.charWidth + 8;

    private double HexX => 8;

    private double ByteX(int column) => (this.HexX + column * 3 + (column >= 8 ? 1 : 0)) * this.charWidth + 4;

    private double AsciiX => (this.HexX + 16 * 3 + 1 + 2) * this.charWidth + 4;

    protected override Size MeasureOverride(Size availableSize) =>
        new(this.DumpWidth, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        this.topRow = Math.Clamp(this.topRow, 0, this.MaxTopRow);
        this.Changed();
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, this.ActualThemeVariant, out object? found) && found is IBrush b ? b : fallback;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(this.Bounds.Size));
        if (this.data.Length == 0)
        {
            return;
        }

        IBrush text = this.Brush("TextFillColorPrimaryBrush", Brushes.Gray);
        IBrush dim = this.Brush("TextFillColorTertiaryBrush", Brushes.DimGray);
        IBrush differs = new SolidColorBrush(Color.FromArgb(110, 244, 67, 54));
        IBrush tableShade = new SolidColorBrush(Color.FromArgb(70, 59, 130, 246));
        IBrush selectedShade = new SolidColorBrush(Color.FromArgb(150, 232, 168, 56));

        int rows = Math.Min(this.VisibleRows + 1, this.RowCount - this.topRow);
        for (int r = 0; r < rows; r++)
        {
            int rowStart = (this.topRow + r) * BytesPerRow;
            double y = r * this.rowHeight;

            context.DrawText(this.Line(rowStart.ToString("X6", CultureInfo.InvariantCulture), dim), new Point(4, y + 1));

            char[] ascii = new char[BytesPerRow];
            int count = Math.Min(BytesPerRow, this.data.Length - rowStart);
            for (int c = 0; c < count; c++)
            {
                int at = rowStart + c;
                byte b = this.data[at];
                ascii[c] = HexTools.AsciiChar(b);

                IBrush? shade = at == this.selected ? selectedShade
                    : this.reference != null && at < this.reference.Length && this.reference[at] != b ? differs
                    : at >= this.highlight.Start && at < this.highlight.End ? tableShade
                    : null;
                double x = this.ByteX(c);
                if (shade != null)
                {
                    context.FillRectangle(shade, new Rect(x - 1, y, 2 * this.charWidth + 2, this.rowHeight));
                    context.FillRectangle(shade, new Rect(this.AsciiX + c * this.charWidth, y, this.charWidth, this.rowHeight));
                }

                context.DrawText(this.Line(b.ToString("X2", CultureInfo.InvariantCulture), b is 0x00 or 0xFF ? dim : text), new Point(x, y + 1));
            }

            context.DrawText(this.Line(new string(ascii, 0, count), text), new Point(this.AsciiX, y + 1));
        }
    }

    private FormattedText Line(string s, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, brush);

    private int IndexAt(Point p)
    {
        int row = this.topRow + (int)(p.Y / this.rowHeight);
        int column;
        if (p.X >= this.AsciiX)
        {
            column = (int)((p.X - this.AsciiX) / this.charWidth);
        }
        else
        {
            column = 0;
            for (int c = BytesPerRow - 1; c >= 0; c--)
            {
                if (p.X >= this.ByteX(c) - this.charWidth * 0.5)
                {
                    column = c;
                    break;
                }
            }
        }

        return Math.Clamp(row * BytesPerRow + Math.Clamp(column, 0, BytesPerRow - 1), 0, Math.Max(0, this.data.Length - 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        this.Focus();
        this.Select(this.IndexAt(e.GetPosition(this)));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        this.TopRow -= (int)Math.Round(e.Delta.Y * 3);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int page = this.VisibleRows * BytesPerRow;
        int? target = e.Key switch
        {
            Key.Left => this.selected - 1,
            Key.Right => this.selected + 1,
            Key.Up => this.selected - BytesPerRow,
            Key.Down => this.selected + BytesPerRow,
            Key.PageUp => this.selected - page,
            Key.PageDown => this.selected + page,
            Key.Home => 0,
            Key.End => this.data.Length - 1,
            _ => null,
        };

        if (target != null)
        {
            this.Select(target.Value);
            e.Handled = true;
        }
    }
}
