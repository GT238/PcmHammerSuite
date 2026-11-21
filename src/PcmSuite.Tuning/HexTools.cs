using System.Globalization;
using System.Text;

namespace PcmSuite.Tune;

/// <summary>A run of bytes that differ between two images: <see cref="Start"/> up to but not including <see cref="End"/>.</summary>
public readonly record struct DiffRange(int Start, int End)
{
    public int Length => this.End - this.Start;
}

/// <summary>The pure logic behind the hex view: address and search parsing, searching, and finding differences between two images.</summary>
public static class HexTools
{
    /// <summary>Accepts "8360", "0x8360" or "$8360" (always hexadecimal, as in a hex editor).</summary>
    public static bool TryParseAddress(string? text, out int address)
    {
        address = 0;
        string s = (text ?? string.Empty).Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }
        else if (s.StartsWith('$'))
        {
            s = s[1..];
        }

        return s.Length > 0 && int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }

    /// <summary>
    /// Turns what was typed into the bytes to look for. Text in double quotes is ASCII ("12212156");
    /// anything else is hex bytes, with or without spaces (FF 8A 00, or FF8A00).
    /// </summary>
    public static bool TryParsePattern(string? text, out byte[] pattern)
    {
        pattern = Array.Empty<byte>();
        string s = (text ?? string.Empty).Trim();
        if (s.Length >= 3 && s[0] == '"' && s[^1] == '"')
        {
            pattern = Encoding.ASCII.GetBytes(s[1..^1]);
            return true;
        }

        string hex = new(s.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        if (hex.Length == 0 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        pattern = new byte[hex.Length / 2];
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return true;
    }

    /// <summary>The offset of the next match at or after <paramref name="from"/> (or at or before it, searching backwards), wrapping round the ends. -1 if there is none.</summary>
    public static int Find(byte[] data, byte[] pattern, int from, bool forward = true)
    {
        if (pattern.Length == 0 || data.Length < pattern.Length)
        {
            return -1;
        }

        int last = data.Length - pattern.Length;
        from = Math.Clamp(from, 0, last);
        for (int n = 0; n <= last; n++)
        {
            int at = forward ? (from + n) % (last + 1) : ((from - n) % (last + 1) + last + 1) % (last + 1);
            if (data.AsSpan(at, pattern.Length).SequenceEqual(pattern))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>Every run of differing bytes, in address order. Images of different sizes are compared over the shorter length.</summary>
    public static List<DiffRange> Differences(byte[] a, byte[] b)
    {
        List<DiffRange> ranges = new();
        int length = Math.Min(a.Length, b.Length);
        int start = -1;
        for (int i = 0; i < length; i++)
        {
            if (a[i] != b[i])
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                ranges.Add(new DiffRange(start, i));
                start = -1;
            }
        }

        if (start >= 0)
        {
            ranges.Add(new DiffRange(start, length));
        }

        return ranges;
    }

    /// <summary>The value at an offset as the editor would read it: one byte, and the 16 and 32 bit big-endian values (the GM byte order).</summary>
    public static string Describe(byte[] data, int at)
    {
        if (at < 0 || at >= data.Length)
        {
            return string.Empty;
        }

        StringBuilder text = new();
        text.Append($"${at:X6}: {data[at]} (0x{data[at]:X2})");
        if (at + 1 < data.Length)
        {
            text.Append($"   16-bit {(data[at] << 8) | data[at + 1]}");
        }

        if (at + 3 < data.Length)
        {
            uint v = ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];
            text.Append($"   32-bit {v}");
        }

        return text.ToString();
    }

    /// <summary>The printable character for the ASCII column, or a dot.</summary>
    public static char AsciiChar(byte b) => b is >= 0x20 and < 0x7F ? (char)b : '.';
}
