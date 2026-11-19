using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PcmSuite.Tune;

/// <summary>
/// Oblique 3D surface of a two-dimensional table (ported from LS-Studio's Surface3DView).
/// Columns run to the right, rows run back into the scene, values run up.
/// </summary>
public sealed class SurfaceView : Control
{
    // Per-unit steps of the projection.
    private const double CX = 1.00, CY = 0.00;     // one column
    private const double RX = -0.40, RY = -0.45;   // one row (up and to the left)
    private const double ZH = 2.00;                // height of the value axis

    private ValueEditorViewModel? editor;
    private double[,] values = new double[0, 0];
    private double min;
    private double max;

    public ValueEditorViewModel? Editor
    {
        get => this.editor;
        set
        {
            if (this.editor != null)
            {
                this.editor.DataChanged -= this.Snapshot;
            }

            this.editor = value;
            if (this.editor != null)
            {
                this.editor.DataChanged += this.Snapshot;
            }

            this.Snapshot();
        }
    }

    private void Snapshot()
    {
        ValueEditorViewModel? e = this.editor;
        if (e == null || e.Rows < 2 || e.Columns < 2)
        {
            this.values = new double[0, 0];
        }
        else
        {
            this.values = new double[e.Rows, e.Columns];
            for (int r = 0; r < e.Rows; r++)
            {
                for (int c = 0; c < e.Columns; c++)
                {
                    this.values[r, c] = e.Scaled(r, c);
                }
            }

            (this.min, this.max) = e.Range();
        }

        this.InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        ValueEditorViewModel? e = this.editor;
        int rows = this.values.GetLength(0);
        int cols = this.values.GetLength(1);
        if (e == null || rows < 2 || cols < 2)
        {
            Text(ctx, "3D view needs a table with at least 2 rows and 2 columns", new Point(this.Bounds.Width / 2, this.Bounds.Height / 2), 13, center: true);
            return;
        }

        const double margin = 55;
        double availW = this.Bounds.Width - 2 * margin;
        double availH = this.Bounds.Height - 2 * margin;
        if (availW < 20 || availH < 20)
        {
            return;
        }

        double s = Math.Min(availW / (cols * CX + rows * Math.Abs(RX)), availH / (rows * Math.Abs(RY) + ZH));
        Vector xStep = new(CX * s, CY * s);
        Vector yStep = new(RX * s, RY * s);
        Vector zStep = new(0, -ZH * s);
        Point origin = new(margin + rows * Math.Abs(RX) * s, this.Bounds.Height - margin);

        int lastRow = rows - 1;
        int lastCol = cols - 1;

        // Floor plate.
        FillQuad(ctx, origin, origin + lastCol * xStep, origin + lastCol * xStep + lastRow * yStep, origin + lastRow * yStep,
            new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(50, 200, 200, 200)), 0.5));

        // Surface: farthest row first so nearer quads paint over it.
        for (int r = lastRow - 1; r >= 0; r--)
        {
            for (int c = 0; c < lastCol; c++)
            {
                double n00 = this.Norm(this.values[r, c]);
                double n10 = this.Norm(this.values[r, c + 1]);
                double n11 = this.Norm(this.values[r + 1, c + 1]);
                double n01 = this.Norm(this.values[r + 1, c]);
                FillQuad(
                    ctx,
                    Proj(c, r, n00, origin, xStep, yStep, zStep),
                    Proj(c + 1, r, n10, origin, xStep, yStep, zStep),
                    Proj(c + 1, r + 1, n11, origin, xStep, yStep, zStep),
                    Proj(c, r + 1, n01, origin, xStep, yStep, zStep),
                    new SolidColorBrush(Heat((n00 + n10 + n11 + n01) / 4)),
                    new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 0.4));
            }
        }

        // Axes.
        Pen axis = new(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        ctx.DrawLine(axis, origin, origin + lastCol * xStep);
        ctx.DrawLine(axis, origin, origin + lastRow * yStep);
        ctx.DrawLine(axis, origin, origin + zStep);

        // Labels.
        int xSkip = Math.Max(1, lastCol / 8);
        for (int c = 0; c <= lastCol; c += xSkip)
        {
            Text(ctx, e.ColumnLabel(c), origin + c * xStep + new Vector(0, 15), 9, center: true);
        }

        Text(ctx, e.XAxisTitle, origin + lastCol / 2.0 * xStep + new Vector(0, 27), 9, center: true);

        int ySkip = Math.Max(1, lastRow / 8);
        for (int r = 0; r <= lastRow; r += ySkip)
        {
            Text(ctx, e.RowLabel(r), origin + r * yStep + new Vector(-8, 0), 9, right: true);
        }

        Text(ctx, e.YAxisTitle, origin + lastRow / 2.0 * yStep + new Vector(-8, -12), 9, right: true);

        Text(ctx, this.min.ToString("F1", CultureInfo.InvariantCulture), origin + new Vector(-6, 0), 9, right: true);
        Text(ctx, this.max.ToString("F1", CultureInfo.InvariantCulture), origin + zStep + new Vector(-6, 0), 9, right: true);
        if (!string.IsNullOrEmpty(e.Units))
        {
            Text(ctx, "[" + e.Units + "]", origin + zStep + new Vector(0, -14), 9, center: true);
        }

        // Colour legend.
        const double lx = 16, lw = 12, lh = 100, ly = margin;
        const int steps = 40;
        for (int i = 0; i < steps; i++)
        {
            ctx.FillRectangle(new SolidColorBrush(Heat(1.0 - (double)i / steps)),
                new Rect(lx, ly + i * lh / steps, lw, lh / steps + 1));
        }

        Text(ctx, this.max.ToString("F1", CultureInfo.InvariantCulture), new Point(lx + lw + 4, ly + 4), 9);
        Text(ctx, this.min.ToString("F1", CultureInfo.InvariantCulture), new Point(lx + lw + 4, ly + lh - 4), 9);
    }

    private double Norm(double v) =>
        Math.Abs(this.max - this.min) < 1e-10 ? 0.5 : Math.Clamp((v - this.min) / (this.max - this.min), 0, 1);

    private static Point Proj(double c, double r, double n, Point origin, Vector xStep, Vector yStep, Vector zStep) =>
        new(origin.X + c * xStep.X + r * yStep.X + n * zStep.X, origin.Y + c * xStep.Y + r * yStep.Y + n * zStep.Y);

    private static void FillQuad(DrawingContext ctx, Point a, Point b, Point c, Point d, IBrush fill, IPen? pen)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext gc = geometry.Open())
        {
            gc.BeginFigure(a, true);
            gc.LineTo(b);
            gc.LineTo(c);
            gc.LineTo(d);
            gc.EndFigure(true);
        }

        ctx.DrawGeometry(fill, pen, geometry);
    }

    // Blue, cyan, green, yellow, red.
    private static Color Heat(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t switch
        {
            < 0.25 => Lerp(Color.FromRgb(30, 80, 220), Color.FromRgb(0, 210, 255), t * 4),
            < 0.50 => Lerp(Color.FromRgb(0, 210, 255), Color.FromRgb(50, 220, 50), (t - 0.25) * 4),
            < 0.75 => Lerp(Color.FromRgb(50, 220, 50), Color.FromRgb(255, 220, 0), (t - 0.50) * 4),
            _ => Lerp(Color.FromRgb(255, 220, 0), Color.FromRgb(220, 30, 30), (t - 0.75) * 4),
        };
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static void Text(DrawingContext ctx, string? text, Point at, double size, bool center = false, bool right = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        FormattedText formatted = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            size,
            new SolidColorBrush(Color.FromRgb(150, 150, 150)));
        double x = right ? at.X - formatted.Width : center ? at.X - formatted.Width / 2 : at.X;
        ctx.DrawText(formatted, new Point(x, at.Y - formatted.Height / 2));
    }
}
