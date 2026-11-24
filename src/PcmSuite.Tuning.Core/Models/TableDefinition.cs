using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DynamicExpresso;

namespace PcmSuite.Tuning.Core.Models
{
    public class TableDefinition
    {
        // ── Identity ────────────────────────────────────────────────
        public string Name        { get; set; } = "Unknown";
        public string Description { get; set; } = "";
        public string Category       { get; set; } = "Other";
        public string SubCategory    { get; set; } = "";
        public string SubSubCategory { get; set; } = "";

        // ── Z axis (the data) ────────────────────────────────────────
        public uint   Address         { get; set; }
        public int    Rows            { get; set; } = 1;
        public int    Columns         { get; set; } = 1;
        public int    ElementBits     { get; set; } = 16;  // bits per value (8 or 16)
        public int    SizeInBytes     => ElementBits / 8;
        public bool   IsSigned        { get; set; } = false;
        public bool   IsBigEndian     { get; set; } = true;
        public string ZUnits          { get; set; } = "";
        public int    DecimalPlaces   { get; set; } = 2;
        public string ScalingEquation { get; set; } = "X";

        // Display format from XDF outputtype field (TunerPro convention):
        //   1 = decimal, 2 = decimal (the XDF default), 3 = hex, 4 = ASCII character
        public int OutputType { get; set; } = 2;

        // Stride: how far apart elements are in memory (bits).
        // 0 means "compute automatically" (= element size for minor, = cols*colStride for major).
        public int MinorStrideBits { get; set; } = 0;  // between columns
        public int MajorStrideBits { get; set; } = 0;  // between rows

        // ── Flag mask (XDFFLAG only) ─────────────────────────────────
        // Non-zero when this entry is a bit flag: value = (element & Mask) != 0 ? 1 : 0, where the element is
        // ElementBits wide and read in the table's byte order, so a mask can be wider than 8 bits (0x0100 on a 16-bit word).
        public uint Mask { get; set; } = 0;

        /// <summary>The unsigned value of the element of <see cref="SizeInBytes"/> bytes at this offset, in the table's byte order. Zero if it is outside the image.</summary>
        public uint ReadElement(byte[] bin, int offset)
        {
            int sz = SizeInBytes;
            if (offset < 0 || offset + sz > bin.Length) return 0;
            uint value = 0;
            for (int i = 0; i < sz; i++)
            {
                int b = IsBigEndian ? i : sz - 1 - i;
                value = (value << 8) | bin[offset + b];
            }
            return value;
        }

        /// <summary>Write an unsigned element of <see cref="SizeInBytes"/> bytes at this offset, in the table's byte order.</summary>
        public void WriteElement(byte[] bin, int offset, uint value)
        {
            int sz = SizeInBytes;
            if (offset < 0 || offset + sz > bin.Length) return;
            for (int i = sz - 1; i >= 0; i--)
            {
                int b = IsBigEndian ? i : sz - 1 - i;
                bin[offset + b] = (byte)value;
                value >>= 8;
            }
        }

        // ── Per-row address overrides ─────────────────────────────────
        // Some XDF tables (e.g. Calibration Segment ID) store each row at a
        // completely different address. Key = 0-based row index, Value = address.
        public Dictionary<int, uint>? RowAddressOverrides { get; set; }

        // ── X axis (column headers) ───────────────────────────────────
        public string       XUnits  { get; set; } = "";
        public List<string> XLabels { get; set; } = new();

        /// <summary>
        /// True when the XDF gave real axis values. False when XLabels only holds the placeholders 1..N
        /// (the axis is stored in the BIN, or has no values), which must not be read as numbers.
        /// </summary>
        public bool XLabelsAreReal { get; set; }

        // ── Y axis (row headers) ──────────────────────────────────────
        public string       YUnits  { get; set; } = "";
        public List<string> YLabels { get; set; } = new();

        public bool YLabelsAreReal { get; set; }

        /// <summary>When set, the column headings are read from the BIN at this place instead of coming from <see cref="XLabels"/>.</summary>
        public AxisSource? XAxis { get; set; }

        /// <summary>When set, the row headings are read from the BIN at this place instead of coming from <see cref="YLabels"/>.</summary>
        public AxisSource? YAxis { get; set; }

        /// <summary>The byte offset in the BIN of the cell at this row and column (the same rule reading and writing use).</summary>
        public int CellOffset(int row, int col)
        {
            int sz = SizeInBytes;

            // Per-row address override (e.g. Calibration Segment ID stores each
            // row at a completely different non-contiguous address in the BIN).
            if (RowAddressOverrides != null && RowAddressOverrides.TryGetValue(row, out uint rowAddr))
            {
                return (int)rowAddr + col * sz;
            }

            if (MinorStrideBits > 0)
            {
                // Column-major storage with minor-stride alignment.
                int colStrideBytes = (int)Math.Ceiling((double)(Rows * ElementBits) / MinorStrideBits)
                                     * MinorStrideBits / 8;
                return (int)Address + col * colStrideBytes + row * sz;
            }

            // Row-major storage (standard).
            int colStride = ElementBits;
            int rowStride = MajorStrideBits > 0 ? MajorStrideBits : Columns * colStride;
            return (int)Address + (row * rowStride + col * colStride) / 8;
        }

        // ── Read raw value from bin ───────────────────────────────────
        public double GetRawValue(byte[] bin, int row, int col)
        {
            int sz = SizeInBytes;
            int offsetBytes = CellOffset(row, col);

            if (offsetBytes < 0 || offsetBytes + sz > bin.Length) return 0;

            // A flag is one bit (or a few) of the element, whatever the element's width.
            if (Mask != 0) return (ReadElement(bin, offsetBytes) & Mask) != 0 ? 1.0 : 0.0;

            if (sz == 1)
            {
                byte b = bin[offsetBytes];
                return IsSigned ? (sbyte)b : b;
            }
            else if (sz == 2)
            {
                int raw = IsBigEndian
                    ? (bin[offsetBytes] << 8) | bin[offsetBytes + 1]
                    : bin[offsetBytes] | (bin[offsetBytes + 1] << 8);
                return IsSigned ? (short)raw : (ushort)raw;
            }
            else // sz == 4 (32-bit)
            {
                uint raw = IsBigEndian
                    ? ((uint)bin[offsetBytes]     << 24) | ((uint)bin[offsetBytes + 1] << 16)
                    | ((uint)bin[offsetBytes + 2] <<  8) |  bin[offsetBytes + 3]
                    : bin[offsetBytes] | ((uint)bin[offsetBytes + 1] << 8)
                    | ((uint)bin[offsetBytes + 2] << 16) | ((uint)bin[offsetBytes + 3] << 24);
                return IsSigned ? (double)(int)raw : raw;
            }
        }

        // ── Write raw value to bin ────────────────────────────────────
        public void SetRawValue(byte[] bin, int row, int col, double rawValue)
        {
            int sz = SizeInBytes;
            int offsetBytes = CellOffset(row, col);

            if (offsetBytes < 0 || offsetBytes + sz > bin.Length) return;

            if (sz == 1)
            {
                bin[offsetBytes] = (byte)((long)Math.Round(rawValue) & 0xFF);
            }
            else if (sz == 2)
            {
                int raw = (int)Math.Round(rawValue);
                if (IsBigEndian)
                {
                    bin[offsetBytes]     = (byte)((raw >> 8) & 0xFF);
                    bin[offsetBytes + 1] = (byte)(raw & 0xFF);
                }
                else
                {
                    bin[offsetBytes]     = (byte)(raw & 0xFF);
                    bin[offsetBytes + 1] = (byte)((raw >> 8) & 0xFF);
                }
            }
            else // sz == 4
            {
                uint raw = (uint)(long)Math.Round(rawValue);
                if (IsBigEndian)
                {
                    bin[offsetBytes]     = (byte)((raw >> 24) & 0xFF);
                    bin[offsetBytes + 1] = (byte)((raw >> 16) & 0xFF);
                    bin[offsetBytes + 2] = (byte)((raw >>  8) & 0xFF);
                    bin[offsetBytes + 3] = (byte)(raw & 0xFF);
                }
                else
                {
                    bin[offsetBytes]     = (byte)(raw & 0xFF);
                    bin[offsetBytes + 1] = (byte)((raw >>  8) & 0xFF);
                    bin[offsetBytes + 2] = (byte)((raw >> 16) & 0xFF);
                    bin[offsetBytes + 3] = (byte)((raw >> 24) & 0xFF);
                }
            }
        }

        // ── Inverse scaling (for cell editing) ────────────────────────
        /// <summary>
        /// Finds the raw integer whose scaled result is closest to <paramref name="scaledValue"/>.
        /// Assumes the equation is monotone (true for all standard XDF linear equations).
        /// </summary>
        public double InverseScale(double scaledValue,
            Func<string, double>? crossRefResolver = null)
        {
            string eq = ScalingEquation?.Trim() ?? "X";
            if (string.IsNullOrEmpty(eq) || eq == "X" || eq == "x") return scaledValue;

            long rawMin = IsSigned
                ? (ElementBits == 8 ? -128L : ElementBits == 16 ? -32768L : int.MinValue)
                : 0L;
            long rawMax = ElementBits == 8
                ? (IsSigned ? 127L : 255L)
                : ElementBits == 16
                    ? (IsSigned ? 32767L : 65535L)
                    : (IsSigned ? int.MaxValue : uint.MaxValue);

            double atMin = ApplyScaling(rawMin, crossRefResolver);
            double atMax = ApplyScaling(rawMax, crossRefResolver);
            bool increasing = atMax >= atMin;

            long lo = rawMin, hi = rawMax;
            for (int i = 0; i < 64; i++)
            {
                if (lo >= hi) break;
                long mid = lo + (hi - lo) / 2;
                double v = ApplyScaling(mid, crossRefResolver);
                if (increasing ? v < scaledValue : v > scaledValue)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            // The search lands on the first raw value at or past the target. The previous one may be closer.
            long best = Math.Clamp(lo, rawMin, rawMax);
            if (best > rawMin)
            {
                double here = Math.Abs(ApplyScaling(best, crossRefResolver) - scaledValue);
                double before = Math.Abs(ApplyScaling(best - 1, crossRefResolver) - scaledValue);
                if (before < here)
                    best--;
            }

            return (double)best;
        }

        // ── Apply scaling equation ────────────────────────────────────

        /// <summary>
        /// Returns the scaled (display) value for a cell.
        /// <paramref name="crossRefResolver"/> is optional: when supplied it resolves
        /// "ref:'name'" cross-references in the equation by returning the named
        /// table's scalar value from the loaded BIN.
        /// </summary>
        public double GetScaledValue(byte[] bin, int row, int col,
            Func<string, double>? crossRefResolver = null)
        {
            double raw = GetRawValue(bin, row, col);
            return ApplyScaling(raw, crossRefResolver);
        }

        public double ApplyScaling(double rawX,
            Func<string, double>? crossRefResolver = null) => ScaleWith(this.ScalingEquation, rawX, crossRefResolver);

        /// <summary>Applies an equation in X (as written in an XDF) to a stored number. Used for cell values and for axis values.</summary>
        internal static double ScaleWith(string? equation, double rawX,
            Func<string, double>? crossRefResolver = null)
        {
            string eq = equation?.Trim() ?? "X";
            if (string.IsNullOrEmpty(eq) || eq == "X" || eq == "x") return rawX;

            // Resolve cross-references:  ref:'some_name'  →  numeric value from BIN.
            // "table:'name'" is also accepted, because XDFs exported by Universal Patcher write it that way.
            string safeEq = eq;
            if (eq.Contains("ref", StringComparison.OrdinalIgnoreCase) || eq.Contains("table", StringComparison.OrdinalIgnoreCase))
            {
                safeEq = Regex.Replace(
                    eq,
                    @"(?<![A-Za-z0-9_])(?:ref|table)\s*:\s*'([^']*)'",
                    m =>
                    {
                        string refName = m.Groups[1].Value;
                        if (crossRefResolver != null)
                        {
                            try { return crossRefResolver(refName).ToString("R", CultureInfo.InvariantCulture); }
                            catch { }
                        }
                        return "1.0";
                    },
                    RegexOptions.IgnoreCase);
            }

            // The same equation text is evaluated for every cell, so work it out once.
            if (EvaluatorCache.Count > 5000)
                EvaluatorCache.Clear(); // equations with substituted constants can keep producing new texts
            Evaluator ev = EvaluatorCache.GetOrAdd(safeEq, BuildEvaluator);
            return ev.Apply(rawX);
        }

        // ── Cached evaluators ─────────────────────────────────────────
        // Parsing an equation (regexes, or building a DynamicExpresso interpreter) is far costlier than
        // evaluating it, and a table edit re-reads every cell several times.

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Evaluator> EvaluatorCache = new();

        private sealed class Evaluator
        {
            private readonly bool affine;
            private readonly double slope;
            private readonly double offset;
            private readonly string equation;
            private readonly Func<double, double, double>? compiled;

            public Evaluator(string equation, bool affine, double slope, double offset, Func<double, double, double>? compiled)
            {
                this.equation = equation;
                this.affine = affine;
                this.slope = slope;
                this.offset = offset;
                this.compiled = compiled;
            }

            public double Apply(double x)
            {
                if (this.affine)
                    return this.slope * x + this.offset;

                if (this.compiled != null)
                {
                    try { return this.compiled(x, x); }
                    catch { return x; }
                }

                // Pattern the fast path knows but that is not a straight line: keep evaluating it directly.
                return TryFastEval(this.equation, x) ?? x;
            }
        }

        private static Evaluator BuildEvaluator(string equation)
        {
            // Every pattern the fast path recognises is a straight line in X, so two probes give the line.
            // Use it only if it reproduces the direct evaluation at a few more points.
            double? f0 = TryFastEval(equation, 0);
            double? f1 = TryFastEval(equation, 1);
            if (f0.HasValue && f1.HasValue)
            {
                double slope = f1.Value - f0.Value;
                double offset = f0.Value;
                bool exact = true;
                foreach (double probe in new[] { 7.0, 255.0, 4096.0, 65535.0, -3.0 })
                {
                    double direct = TryFastEval(equation, probe) ?? double.NaN;
                    double line = slope * probe + offset;
                    if (double.IsNaN(direct) || Math.Abs(direct - line) > 1e-9 * Math.Max(1.0, Math.Abs(direct)))
                    {
                        exact = false;
                        break;
                    }
                }

                return exact
                    ? new Evaluator(equation, true, slope, offset, null)
                    : new Evaluator(equation, false, 0, 0, null);
            }

            if (f0.HasValue || f1.HasValue)
                return new Evaluator(equation, false, 0, 0, null);

            // Non-linear or complex expression: compile once with DynamicExpresso.
            try
            {
                var interp = new Interpreter();
                Func<double, double, double> compiled =
                    interp.ParseAsDelegate<Func<double, double, double>>(equation, "X", "x");
                return new Evaluator(equation, false, 0, 0, compiled);
            }
            catch
            {
                return new Evaluator(equation, false, 0, 0, (x, _) => x);
            }
        }

        /// <summary>
        /// Directly evaluates the most common XDF linear equation forms without
        /// DynamicExpresso, using invariant-culture double parsing.
        /// Supports: X*a  X*a+b  X*a-b  X/a  X/a+b  X/a-b  (X+a)*b  (X-a)*b
        /// Returns null if the pattern is not recognised.
        /// </summary>
        private static double? TryFastEval(string eq, double x)
        {
            // Normalise: collapse spaces, unify variable to X
            string e = Regex.Replace(eq.Trim(), @"\s+", "");
            e = Regex.Replace(e, @"(?<![A-Za-z0-9_])x(?![A-Za-z0-9_])", "X");

            double a, b;

            // X*a+b  or  X*a-b
            var m = Regex.Match(e, @"^X\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)([+-][0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                return x * a + b;

            // X*a
            m = Regex.Match(e, @"^X\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                return x * a;

            // X/a+b  or  X/a-b
            m = Regex.Match(e, @"^X/([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)([+-][0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b) &&
                a != 0)
                return x / a + b;

            // X/a
            m = Regex.Match(e, @"^X/([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                a != 0)
                return x / a;

            // (X+a)*b  or  (X-a)*b
            m = Regex.Match(e, @"^\(X([+-][0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)\)\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                return (x + a) * b;

            // (X*a+b)*c  — three-term compound
            m = Regex.Match(e, @"^\(X\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)([+-][0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)\)\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b) &&
                double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double c))
                return (x * a + b) * c;

            // (X*a)*b  →  X*(a*b)
            m = Regex.Match(e, @"^\(X\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)\)\*([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                return x * a * b;

            // (X/a)/b  →  X/(a*b)
            m = Regex.Match(e, @"^\(X/([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)\)/([+-]?[0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b) &&
                a != 0 && b != 0)
                return x / a / b;

            // (X*a)*(b/c) — the P59 VE % pattern after cross-ref substitution:
            // (X*0.000195312500)*(28.9647/0.746)
            m = Regex.Match(e, @"^\(X\*([0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)\)\*\(([0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)/([0-9]*\.?[0-9]+(?:[Ee][+-]?[0-9]+)?)\)$");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b) &&
                double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out c) &&
                c != 0)
                return x * a * (b / c);

            return null;
        }

        // ── Format a scaled value for display ────────────────────────
        public string FormatValue(double scaled)
        {
            long integer = (long)Math.Round(scaled);
            return OutputType switch
            {
                3 => integer.ToString("X"),           // hex, no prefix: D2E8
                4 => integer is >= 32 and <= 126      // printable ASCII character
                     ? ((char)integer).ToString()
                     : "--",                          // unprogrammed / out-of-range byte
                _ => scaled.ToString($"F{Math.Max(0, DecimalPlaces)}"),   // always the table's decimals, so columns line up
            };
        }

        // ── Reference comparison ──────────────────────────────────────
        public bool CellChanged(byte[] current, byte[] reference, int row, int col)
        {
            if (reference == null || reference.Length != current.Length) return false;
            return GetRawValue(current, row, col) != GetRawValue(reference, row, col);
        }

        public bool HasAnyChanges(byte[] current, byte[] reference)
        {
            if (reference == null || reference.Length != current.Length) return false;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                    if (CellChanged(current, reference, r, c)) return true;
            return false;
        }

        public int CountChanges(byte[] current, byte[] reference)
        {
            if (reference == null || reference.Length != current.Length) return 0;
            int count = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                    if (CellChanged(current, reference, r, c)) count++;
            return count;
        }
    }
}
