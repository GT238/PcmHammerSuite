using System.Globalization;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tune;

/// <summary>How a table's headings (RPM across, MAP down) are known.</summary>
public enum AxisMode
{
    /// <summary>Not known: shown as 1, 2, 3...</summary>
    Numbers,

    /// <summary>Fixed numbers or names typed into the definition.</summary>
    Labels,

    /// <summary>Stored in the BIN, read from there (so they follow the file).</summary>
    FromBin,
}

/// <summary>The editable form of one axis. Everything is text, as typed; <see cref="DefinitionDraft.TryBuild"/> checks it.</summary>
public sealed class AxisDraft
{
    public AxisMode Mode { get; set; } = AxisMode.Numbers;

    public string Units { get; set; } = string.Empty;

    /// <summary>For <see cref="AxisMode.Labels"/>: the headings, separated by commas or spaces.</summary>
    public string Labels { get; set; } = string.Empty;

    /// <summary>For <see cref="AxisMode.FromBin"/>: the hex address of the first heading.</summary>
    public string Address { get; set; } = string.Empty;

    public int Bits { get; set; } = 16;

    public bool IsSigned { get; set; }

    public bool IsLittleEndian { get; set; }

    public string Equation { get; set; } = "X";

    public string Decimals { get; set; } = "0";

    internal static AxisDraft From(AxisSource? source, List<string> labels, bool labelsAreReal, string units)
    {
        if (source != null)
        {
            return new AxisDraft
            {
                Mode = AxisMode.FromBin,
                Units = units,
                Address = "0x" + source.Address.ToString("X"),
                Bits = source.ElementBits,
                IsSigned = source.IsSigned,
                IsLittleEndian = !source.IsBigEndian,
                Equation = source.ScalingEquation,
                Decimals = source.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
            };
        }

        return new AxisDraft
        {
            Mode = labelsAreReal ? AxisMode.Labels : AxisMode.Numbers,
            Units = units,
            Labels = labelsAreReal ? string.Join(", ", labels) : string.Empty,
        };
    }
}

/// <summary>What the definition editor shows for one table, and the checks that turn it back into a <see cref="TableDefinition"/>.</summary>
public sealed class DefinitionDraft
{
    private TableDefinition? original;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = "Other";

    public string SubCategory { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Hex address of the first value.</summary>
    public string Address { get; set; } = string.Empty;

    public string Rows { get; set; } = "1";

    public string Columns { get; set; } = "1";

    public int ElementBits { get; set; } = 16;

    public bool IsSigned { get; set; }

    public bool IsLittleEndian { get; set; }

    public string Units { get; set; } = string.Empty;

    public string Decimals { get; set; } = "2";

    /// <summary>The scaling, written with X standing for the number stored in the BIN (for example X*0.25).</summary>
    public string Equation { get; set; } = "X";

    /// <summary>Hex bit mask, only for a single on/off flag; empty for an ordinary table or value.</summary>
    public string Mask { get; set; } = string.Empty;

    public AxisDraft X { get; } = new();

    public AxisDraft Y { get; } = new();

    /// <summary>True when the table being edited keeps each row at its own address or uses a stride: those parts are carried over unchanged.</summary>
    public bool HasSpecialLayout => this.original != null
        && (this.original.RowAddressOverrides != null || this.original.MajorStrideBits > 0 || this.original.MinorStrideBits > 0);

    /// <summary>The table this draft was made from (null for a new one).</summary>
    public TableDefinition? Original => this.original;

    public static DefinitionDraft From(TableDefinition t)
    {
        DefinitionDraft d = new()
        {
            original = t,
            Name = t.Name,
            Category = t.Category,
            SubCategory = t.SubCategory,
            Description = t.Description,
            Address = "0x" + t.Address.ToString("X"),
            Rows = t.Rows.ToString(CultureInfo.InvariantCulture),
            Columns = t.Columns.ToString(CultureInfo.InvariantCulture),
            ElementBits = t.ElementBits,
            IsSigned = t.IsSigned,
            IsLittleEndian = !t.IsBigEndian,
            Units = t.ZUnits,
            Decimals = t.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
            Equation = t.ScalingEquation,
            Mask = t.Mask != 0 ? "0x" + t.Mask.ToString("X") : string.Empty,
        };

        CopyAxis(AxisDraft.From(t.XAxis, t.XLabels, t.XLabelsAreReal, t.XUnits), d.X);
        CopyAxis(AxisDraft.From(t.YAxis, t.YLabels, t.YLabelsAreReal, t.YUnits), d.Y);
        return d;
    }

    /// <summary>An empty definition to fill in, starting at the given address.</summary>
    public static DefinitionDraft Blank(string category = "Other", uint address = 0) => new()
    {
        Category = string.IsNullOrWhiteSpace(category) ? "Other" : category,
        Address = address == 0 ? string.Empty : "0x" + address.ToString("X"),
    };

    private static void CopyAxis(AxisDraft from, AxisDraft to)
    {
        to.Mode = from.Mode;
        to.Units = from.Units;
        to.Labels = from.Labels;
        to.Address = from.Address;
        to.Bits = from.Bits;
        to.IsSigned = from.IsSigned;
        to.IsLittleEndian = from.IsLittleEndian;
        to.Equation = from.Equation;
        to.Decimals = from.Decimals;
    }

    /// <summary>
    /// Checks the fields and builds the table. False with <paramref name="errors"/> when something must be fixed first.
    /// <paramref name="warnings"/> are things worth a look that do not stop the table being used (it reaches past the end of the BIN, or overlaps another table).
    /// </summary>
    /// <param name="others">The other tables in the definition, to catch a repeated name and an overlap.</param>
    public bool TryBuild(byte[] bin, IEnumerable<TableDefinition> others, out TableDefinition? table, out List<string> errors, out List<string> warnings)
    {
        errors = new List<string>();
        warnings = new List<string>();
        table = null;

        string name = this.Name.Trim();
        List<TableDefinition> rest = others.Where(o => !ReferenceEquals(o, this.original)).ToList();
        if (name.Length == 0)
        {
            errors.Add("Give the table a name.");
        }
        else if (rest.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Another table is already called \"" + name + "\".");
        }

        uint address = 0;
        if (!XdfParser.TryParseHexUInt(this.Address, out address) || address == 0)
        {
            errors.Add("The address must be a hex number above 0, such as 0x8360.");
        }

        int rows = ReadCount(this.Rows, "Rows", errors);
        int columns = ReadCount(this.Columns, "Columns", errors);
        if (rows > 0 && columns > 0 && (long)rows * columns > 65536)
        {
            errors.Add("That is more than 65,536 values in one table.");
        }

        if (this.ElementBits is not (8 or 16 or 32))
        {
            errors.Add("The value size must be 8, 16 or 32 bits.");
        }

        int decimals = ReadDecimals(this.Decimals, "Decimals", errors);

        uint mask = 0;
        if (!string.IsNullOrWhiteSpace(this.Mask))
        {
            if (!XdfParser.TryParseHexUInt(this.Mask, out mask) || mask == 0)
            {
                errors.Add("The flag mask must be a hex number such as 0x01.");
            }
            else if (rows > 1 || columns > 1)
            {
                errors.Add("A flag is a single value: set rows and columns to 1, or clear the mask.");
            }
            else if (this.ElementBits is 8 or 16 && mask >= 1u << this.ElementBits)
            {
                errors.Add("The mask is wider than a " + this.ElementBits + "-bit value.");
            }
        }

        if (!TableDefinition.CheckEquation(this.Equation, out string equationError))
        {
            errors.Add("The scaling equation does not work: " + equationError);
        }

        AxisSource? xSource = this.BuildAxis(this.X, "Column headings", columns, errors, out List<string> xLabels, out bool xReal);
        AxisSource? ySource = this.BuildAxis(this.Y, "Row headings", rows, errors, out List<string> yLabels, out bool yReal);

        if (errors.Count > 0)
        {
            return false;
        }

        TableDefinition t = new()
        {
            Name = name,
            Description = this.Description,
            Category = string.IsNullOrWhiteSpace(this.Category) ? "Other" : this.Category.Trim(),
            SubCategory = this.SubCategory.Trim(),
            SubSubCategory = this.original?.SubSubCategory ?? string.Empty,
            Address = address,
            Rows = rows,
            Columns = columns,
            ElementBits = this.ElementBits,
            IsSigned = this.IsSigned,
            IsBigEndian = !this.IsLittleEndian,
            ZUnits = this.Units.Trim(),
            DecimalPlaces = decimals,
            ScalingEquation = string.IsNullOrWhiteSpace(this.Equation) ? "X" : this.Equation.Trim(),
            Mask = mask,
            OutputType = this.original?.OutputType ?? 2,
            MajorStrideBits = this.original?.MajorStrideBits ?? 0,
            MinorStrideBits = this.original?.MinorStrideBits ?? 0,
            RowAddressOverrides = this.original?.RowAddressOverrides == null ? null : new Dictionary<int, uint>(this.original.RowAddressOverrides),
            XUnits = this.X.Units.Trim(),
            XLabels = xLabels,
            XLabelsAreReal = xReal,
            XAxis = xSource,
            YUnits = this.Y.Units.Trim(),
            YLabels = yLabels,
            YLabelsAreReal = yReal,
            YAxis = ySource,
        };

        (int start, int end) = TuneViewModel.Extent(t);
        if (end > bin.Length)
        {
            warnings.Add($"The values run from 0x{start:X} to 0x{end - 1:X}, past the end of this BIN (0x{bin.Length - 1:X}).");
        }

        foreach (TableDefinition other in rest)
        {
            (int otherStart, int otherEnd) = TuneViewModel.Extent(other);
            if (start < otherEnd && otherStart < end && !(other.Address == t.Address && other.Rows * other.Columns == t.Rows * t.Columns))
            {
                warnings.Add("The values overlap \"" + other.Name + "\" (0x" + otherStart.ToString("X") + " to 0x" + (otherEnd - 1).ToString("X") + ").");
                break;
            }
        }

        foreach ((AxisSource? source, string label) in new[] { (xSource, "Column"), (ySource, "Row") })
        {
            if (source != null && source.ElementOffset(source.Count - 1) + source.SizeInBytes > bin.Length)
            {
                warnings.Add(label + " headings run past the end of this BIN.");
            }
        }

        table = t;
        return true;
    }

    private AxisSource? BuildAxis(AxisDraft a, string title, int count, List<string> errors, out List<string> labels, out bool real)
    {
        labels = new List<string>();
        real = false;
        if (a.Mode == AxisMode.Numbers)
        {
            for (int i = 1; i <= Math.Max(0, count); i++)
            {
                labels.Add(i.ToString(CultureInfo.InvariantCulture));
            }

            return null;
        }

        if (a.Mode == AxisMode.Labels)
        {
            labels = a.Labels.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            real = true;
            if (count > 0 && labels.Count != count)
            {
                errors.Add($"{title}: this table has {count}, and {labels.Count} {(labels.Count == 1 ? "was" : "were")} typed.");
            }

            return null;
        }

        // Stored in the BIN.
        if (!XdfParser.TryParseHexUInt(a.Address, out uint address) || address == 0)
        {
            errors.Add(title + ": the address must be a hex number above 0.");
        }

        if (a.Bits is not (8 or 16 or 32))
        {
            errors.Add(title + ": the size must be 8, 16 or 32 bits.");
        }

        int decimals = ReadDecimals(a.Decimals, title + " decimals", errors);
        if (!TableDefinition.CheckEquation(a.Equation, out string error))
        {
            errors.Add(title + ": the equation does not work: " + error);
        }

        real = true;
        return new AxisSource
        {
            Address = address,
            Count = Math.Max(1, count),
            ElementBits = a.Bits,
            IsSigned = a.IsSigned,
            IsBigEndian = !a.IsLittleEndian,
            ScalingEquation = string.IsNullOrWhiteSpace(a.Equation) ? "X" : a.Equation.Trim(),
            DecimalPlaces = decimals,
        };
    }

    private static int ReadCount(string text, string what, List<string> errors)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > 4096)
        {
            errors.Add(what + " must be a whole number from 1 to 4096.");
            return 0;
        }

        return n;
    }

    private static int ReadDecimals(string text, string what, List<string> errors)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n > 8)
        {
            errors.Add(what + " must be a whole number from 0 to 8.");
            return 0;
        }

        return n;
    }
}
