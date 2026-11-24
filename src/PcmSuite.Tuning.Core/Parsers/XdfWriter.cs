using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tuning.Core.Parsers
{
    /// <summary>
    /// Writes tables out as a TunerPro XDF, the reverse of <see cref="XdfParser"/>: what is written here reads back as the same tables.
    /// This is how the Tuner keeps its own definition for an OS: anything it can load (or build) can be saved as a plain,
    /// unprotected XDF that TunerPro and the Tuner both open.
    /// Patches are not written (only tables, single values and flags).
    /// </summary>
    public static class XdfWriter
    {
        // Names TunerPro lets an equation use for a linked entry. X is the value itself, so it is never used here.
        private const string LinkIds = "YZABCDEFGHIJKLMNOPQRSTUVW";

        /// <summary>Saves the tables to a new XDF file. Overwrites the path if it exists.</summary>
        /// <param name="binSize">Size of the BIN the definitions are for (the XDF records it as its region).</param>
        public static void Write(string path, IReadOnlyList<TableDefinition> tables, string title, string description, int binSize)
        {
            XDocument doc = Build(tables, title, description, binSize);
            XmlWriterSettings settings = new()
            {
                Indent = true,
                Encoding = new UTF8Encoding(false),
                NewLineHandling = NewLineHandling.Entitize,   // a line break inside a description is kept as &#xD;&#xA;
            };

            using XmlWriter writer = XmlWriter.Create(path, settings);
            doc.Save(writer);
        }

        /// <summary>The XDF as an in-memory document (what <see cref="Write"/> saves).</summary>
        public static XDocument Build(IReadOnlyList<TableDefinition> tables, string title, string description, int binSize)
        {
            // One number per entry, so equations can point at other entries.
            Dictionary<TableDefinition, int> ids = new();
            Dictionary<string, TableDefinition> byName = new(StringComparer.OrdinalIgnoreCase);
            int next = 1;
            foreach (TableDefinition t in tables)
            {
                ids[t] = next++;
                byName[t.Name] = t;
            }

            // Category names, in the order they first appear.
            List<string> categories = new();
            foreach (TableDefinition t in tables)
            {
                foreach (string name in Names(t))
                {
                    if (!categories.Contains(name))
                    {
                        categories.Add(name);
                    }
                }
            }

            XElement header = new("XDFHEADER",
                new XElement("flags", "0x1"),
                new XElement("fileversion", "2016-11"),
                new XElement("deftitle", title),
                new XElement("description", description),
                new XElement("BASEOFFSET", new XAttribute("offset", "0"), new XAttribute("subtract", "0")),
                new XElement("DEFAULTS", new XAttribute("datasizeinbits", "16"), new XAttribute("sigdigits", "2"),
                    new XAttribute("outputtype", "1"), new XAttribute("signed", "0"), new XAttribute("lsbfirst", "0"), new XAttribute("float", "0")),
                new XElement("REGION", new XAttribute("type", "0xFFFFFFFF"), new XAttribute("startaddress", "0x0"),
                    new XAttribute("size", "0x" + Math.Max(0, binSize - 1).ToString("X")), new XAttribute("regionflags", "0x0"),
                    new XAttribute("name", "Binary File"), new XAttribute("desc", "This region describes the bin file edited by this XDF")));
            for (int i = 0; i < categories.Count; i++)
            {
                header.Add(new XElement("CATEGORY", new XAttribute("index", "0x" + i.ToString("X")), new XAttribute("name", categories[i])));
            }

            XElement root = new("XDFFORMAT", new XAttribute("version", "1.60"), header);
            foreach (TableDefinition t in tables)
            {
                root.Add(Entry(t, ids, byName, categories));
            }

            return new XDocument(new XComment(" Written by PCM Tuner "), root);
        }

        private static IEnumerable<string> Names(TableDefinition t)
        {
            if (!string.IsNullOrEmpty(t.Category))
            {
                yield return t.Category;
                if (!string.IsNullOrEmpty(t.SubCategory))
                {
                    yield return t.SubCategory;
                    if (!string.IsNullOrEmpty(t.SubSubCategory))
                    {
                        yield return t.SubSubCategory;
                    }
                }
            }
        }

        private static XElement Entry(TableDefinition t, Dictionary<TableDefinition, int> ids, Dictionary<string, TableDefinition> byName, List<string> categories)
        {
            bool isFlag = t.Mask != 0;
            bool isScalar = !isFlag && t.Rows <= 1 && t.Columns <= 1 && t.XLabels.Count <= 1 && t.YLabels.Count <= 1
                            && t.XAxis == null && t.YAxis == null && t.RowAddressOverrides == null;

            XElement e = new(isFlag ? "XDFFLAG" : isScalar ? "XDFCONSTANT" : "XDFTABLE",
                new XAttribute("uniqueid", "0x" + ids[t].ToString("X")));
            if (!isFlag)
            {
                e.Add(new XAttribute("flags", isScalar ? "0xC" : "0x30"));
            }

            e.Add(new XElement("title", t.Name), new XElement("description", t.Description));

            int level = 0;
            foreach (string name in Names(t))
            {
                e.Add(new XElement("CATEGORYMEM", new XAttribute("index", level++), new XAttribute("category", categories.IndexOf(name) + 1)));
            }

            if (isFlag || isScalar)
            {
                e.Add(Embedded(t.Address, t.ElementBits, t, includeCounts: false));
                if (isFlag)
                {
                    e.Add(new XElement("mask", "0x" + t.Mask.ToString("X2")));
                    return e;
                }

                AddValueFields(e, t, ids, byName);
                return e;
            }

            e.Add(Axis("x", t.XAxis, t.XUnits, t.XLabels, t.XLabelsAreReal, t.Columns));
            e.Add(Axis("y", t.YAxis, t.YUnits, t.YLabels, t.YLabelsAreReal, t.Rows));

            XElement z = new("XDFAXIS", new XAttribute("id", "z"), Embedded(t.Address, t.ElementBits, t, includeCounts: true));
            AddValueFields(z, t, ids, byName);
            if (t.RowAddressOverrides != null)
            {
                // Each row lives at its own address (rows are numbered from 1 in the file).
                foreach (KeyValuePair<int, uint> row in t.RowAddressOverrides.OrderBy(r => r.Key))
                {
                    z.Add(new XElement("MATH", new XAttribute("row", row.Key + 1), new XAttribute("col", 1), new XAttribute("equation", "X"),
                        new XElement("VAR", new XAttribute("id", "X"), new XAttribute("type", "address"),
                            new XAttribute("address", "0x" + row.Value.ToString("X")), new XAttribute("sizeinbits", t.ElementBits))));
                }
            }

            e.Add(z);
            return e;
        }

        private static XElement Embedded(uint address, int bits, TableDefinition t, bool includeCounts)
        {
            XElement embed = new("EMBEDDEDDATA");
            if (address != 0)
            {
                embed.Add(new XAttribute("mmedaddress", "0x" + address.ToString("X")));
            }

            embed.Add(new XAttribute("mmedelementsizebits", bits));
            if (includeCounts)
            {
                embed.Add(new XAttribute("mmedrowcount", t.Rows), new XAttribute("mmedcolcount", t.Columns));
            }

            embed.Add(new XAttribute("mmedmajorstridebits", Math.Max(0, t.MajorStrideBits)),
                      new XAttribute("mmedminorstridebits", Math.Max(0, t.MinorStrideBits)));
            int flags = (t.IsSigned ? 0x01 : 0) | (t.IsBigEndian ? 0 : 0x10);
            if (flags != 0)
            {
                embed.Add(new XAttribute("mmedtypeflags", "0x" + flags.ToString("X")));
            }

            return embed;
        }

        private static void AddValueFields(XElement parent, TableDefinition t, Dictionary<TableDefinition, int> ids, Dictionary<string, TableDefinition> byName)
        {
            if (!string.IsNullOrEmpty(t.ZUnits))
            {
                parent.Add(new XElement("units", t.ZUnits));
            }

            parent.Add(new XElement("decimalpl", t.DecimalPlaces),
                       new XElement("outputtype", t.OutputType),
                       new XElement("datatype", t.IsSigned ? 1 : 0));
            parent.Add(MathElement(t.ScalingEquation, ids, byName));
        }

        private static XElement Axis(string id, AxisSource? source, string units, List<string> labels, bool labelsAreReal, int count)
        {
            XElement axis = new("XDFAXIS", new XAttribute("id", id), new XAttribute("uniqueid", "0x0"));

            if (source != null)
            {
                // The headings are stored in the BIN.
                XElement embed = new("EMBEDDEDDATA", new XAttribute("mmedaddress", "0x" + source.Address.ToString("X")),
                    new XAttribute("mmedelementsizebits", source.ElementBits),
                    new XAttribute("mmedmajorstridebits", source.StrideBits > 0 ? source.StrideBits : -32),
                    new XAttribute("mmedminorstridebits", 0));
                int flags = (source.IsSigned ? 0x01 : 0) | (source.IsBigEndian ? 0 : 0x10);
                if (flags != 0)
                {
                    embed.Add(new XAttribute("mmedtypeflags", "0x" + flags.ToString("X")));
                }

                axis.Add(embed);
                if (!string.IsNullOrEmpty(units)) axis.Add(new XElement("units", units));
                axis.Add(new XElement("indexcount", Math.Max(1, source.Count)),
                         new XElement("decimalpl", source.DecimalPlaces),
                         MathElement(source.ScalingEquation, null, null));
                return axis;
            }

            axis.Add(new XElement("EMBEDDEDDATA", new XAttribute("mmedelementsizebits", 16),
                new XAttribute("mmedmajorstridebits", -32), new XAttribute("mmedminorstridebits", 0)));
            if (!string.IsNullOrEmpty(units)) axis.Add(new XElement("units", units));
            axis.Add(new XElement("indexcount", Math.Max(1, labelsAreReal ? labels.Count : Math.Max(count, labels.Count))));
            if (labelsAreReal)
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    axis.Add(new XElement("LABEL", new XAttribute("index", i), new XAttribute("value", labels[i])));
                }
            }

            axis.Add(MathElement("X", null, null));
            return axis;
        }

        /// <summary>
        /// The MATH element for an equation. References to other entries (ref:'name') become TunerPro links, which is how the parser reads them back.
        /// A reference to an entry that is not among the tables being written cannot be resolved, so it becomes 1, as it does when the table is shown.
        /// </summary>
        private static XElement MathElement(string? equation, Dictionary<TableDefinition, int>? ids, Dictionary<string, TableDefinition>? byName)
        {
            string eq = string.IsNullOrWhiteSpace(equation) ? "X" : equation.Trim();
            List<XElement> vars = new() { new XElement("VAR", new XAttribute("id", "X")) };

            if (ids != null && byName != null)
            {
                Dictionary<string, string> assigned = new(StringComparer.OrdinalIgnoreCase);
                eq = Regex.Replace(eq, @"(?<![A-Za-z0-9_])(?:ref|table)\s*:\s*'([^']*)'", m =>
                {
                    string name = m.Groups[1].Value;
                    if (!byName.TryGetValue(name, out TableDefinition? target))
                    {
                        return "1.0";
                    }

                    if (!assigned.TryGetValue(name, out string? varId))
                    {
                        varId = assigned.Count < LinkIds.Length ? LinkIds[assigned.Count].ToString() : "Q" + assigned.Count;
                        assigned[name] = varId;
                        vars.Add(new XElement("VAR", new XAttribute("id", varId), new XAttribute("type", "link"),
                            new XAttribute("linkid", "0x" + ids[target].ToString("X"))));
                    }

                    return varId;
                }, RegexOptions.IgnoreCase);
            }
            else
            {
                eq = Regex.Replace(eq, @"(?<![A-Za-z0-9_])(?:ref|table)\s*:\s*'[^']*'", "1.0", RegexOptions.IgnoreCase);
            }

            XElement math = new("MATH", new XAttribute("equation", eq));
            math.Add(vars.Cast<object>().ToArray());
            return math;
        }
    }
}
