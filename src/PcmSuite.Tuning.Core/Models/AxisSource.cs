using System;
using System.Globalization;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// Where one axis of a table (its column or row headings, such as RPM or MAP) is stored in the BIN.
    /// A table without one of these has fixed labels written in its definition instead.
    /// Reading the axis from the BIN means the headings follow the file: if the axis values are edited, the labels are right.
    /// </summary>
    public sealed class AxisSource
    {
        /// <summary>Byte offset of the first axis value.</summary>
        public uint Address { get; set; }

        /// <summary>How many values the axis has (columns for the X axis, rows for the Y axis).</summary>
        public int Count { get; set; }

        public int ElementBits { get; set; } = 16;

        public bool IsSigned { get; set; }

        public bool IsBigEndian { get; set; } = true;

        /// <summary>Distance between values in bits. 0 means they sit one after another.</summary>
        public int StrideBits { get; set; }

        public string ScalingEquation { get; set; } = "X";

        public int DecimalPlaces { get; set; }

        public AxisSource Clone() => (AxisSource)MemberwiseClone();

        public int SizeInBytes => Math.Max(1, this.ElementBits / 8);

        public int ElementOffset(int index) => (int)this.Address + index * (this.StrideBits > 0 ? this.StrideBits : this.ElementBits) / 8;

        /// <summary>The stored (unscaled) number for one axis value. Zero if it lies outside the image.</summary>
        public double ReadRaw(byte[] bin, int index)
        {
            int offset = this.ElementOffset(index);
            int size = this.SizeInBytes;
            if (index < 0 || offset < 0 || offset + size > bin.Length)
            {
                return 0;
            }

            ulong value = 0;
            for (int i = 0; i < size; i++)
            {
                int b = this.IsBigEndian ? i : size - 1 - i;
                value = (value << 8) | bin[offset + b];
            }

            if (!this.IsSigned)
            {
                return value;
            }

            return size switch
            {
                1 => (sbyte)value,
                2 => (short)value,
                _ => (int)(uint)value,
            };
        }

        /// <summary>The heading text for one axis value, scaled and rounded the way the axis asks.</summary>
        public string Label(byte[] bin, int index, Func<string, double>? crossRef = null)
        {
            double scaled = TableDefinition.ScaleWith(this.ScalingEquation, this.ReadRaw(bin, index), crossRef);
            return scaled.ToString($"F{Math.Max(0, this.DecimalPlaces)}", CultureInfo.InvariantCulture);
        }
    }
}
