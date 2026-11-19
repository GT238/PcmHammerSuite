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
        // Non-zero when this entry is a bit flag: value = (byte & Mask) != 0 ? 1 : 0
        public byte Mask { get; set; } = 0;

        // ── Per-row address overrides ─────────────────────────────────
        // Some XDF tables (e.g. Calibration Segment ID) store each row at a
        // completely different address. Key = 0-based row index, Value = address.
        public Dictionary<int, uint>? RowAddressOverrides { get; set; }

        // ── X axis (column headers) ───────────────────────────────────
        public string       XUnits  { get; set; } = "";
        public List<string> XLabels { get; set; } = new();

        // ── Y axis (row headers) ──────────────────────────────────────
        public string       YUnits  { get; set; } = "";
        public List<string> YLabels { get; set; } = new();

        // ── Read raw value from bin ───────────────────────────────────
        public double GetRawValue(byte[] bin, int row, int col)
        {
            int offsetBytes;
            int sz = SizeInBytes;

            // Per-row address override (e.g. Calibration Segment ID stores each
            // row at a completely different non-contiguous address in the BIN).
            if (RowAddressOverrides != null && RowAddressOverrides.TryGetValue(row, out uint rowAddr))
            {
                offsetBytes = (int)rowAddr + col * sz;
            }
            else if (MinorStrideBits > 0)
            {
                // Column-major storage with minor-stride alignment.
                int colStrideBytes = (int)Math.Ceiling((double)(Rows * ElementBits) / MinorStrideBits)
                                     * MinorStrideBits / 8;
                offsetBytes = (int)Address + col * colStrideBytes + row * sz;
            }
            else
            {
                // Row-major storage (standard).
                int colStride = ElementBits;
                int rowStride = MajorStrideBits > 0 ? MajorStrideBits : Columns * colStride;
                offsetBytes = (int)Address + (row * rowStride + col * colStride) / 8;
            }

            if (offsetBytes < 0 || offsetBytes + sz > bin.Length) return 0;

            if (sz == 1)
            {
                byte b = bin[offsetBytes];
                if (Mask != 0) return (b & Mask) != 0 ? 1.0 : 0.0;
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
            int offsetBytes;
            int sz = SizeInBytes;

            if (RowAddressOverrides != null && RowAddressOverrides.TryGetValue(row, out uint rowAddr))
            {
                offsetBytes = (int)rowAddr + col * sz;
            }
            else if (MinorStrideBits > 0)
            {
                int colStrideBytes = (int)Math.Ceiling((double)(Rows * ElementBits) / MinorStrideBits)
                                     * MinorStrideBits / 8;
                offsetBytes = (int)Address + col * colStrideBytes + row * sz;
            }
            else
            {
                int colStride = ElementBits;
                int rowStride = MajorStrideBits > 0 ? MajorStrideBits : Columns * colStride;
                offsetBytes = (int)Address + (row * rowStride + col * colStride) / 8;
            }

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
            return (double)Math.Clamp(lo, rawMin, rawMax);
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
            Func<string, double>? crossRefResolver = null)
        {
            string eq = ScalingEquation?.Trim() ?? "X";
            if (string.IsNullOrEmpty(eq) || eq == "X" || eq == "x") return rawX;

            // Resolve cross-references:  ref:'some_name'  →  numeric value from BIN.
            // "table:'name'" is also accepted, because XDFs exported by Universal Patcher write it that way.
            string safeEq = Regex.Replace(
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

            // ── Fast path: handle the common XDF linear equation patterns directly
            // without DynamicExpresso (avoids locale/version issues).
            double? fast = TryFastEval(safeEq, rawX);
            if (fast.HasValue) return fast.Value;

            // ── Fallback: DynamicExpresso for non-linear / complex expressions
            try
            {
                var interp = new Interpreter();
                return (double)interp.Eval(safeEq,
                    new Parameter("X", typeof(double), rawX),
                    new Parameter("x", typeof(double), rawX));
            }
            catch { return rawX; }
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
                _ => scaled % 1 == 0 && DecimalPlaces > 0              // whole number — drop the ".0"
                     ? ((long)scaled).ToString()
                     : scaled.ToString($"F{Math.Max(0, DecimalPlaces)}"),
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
