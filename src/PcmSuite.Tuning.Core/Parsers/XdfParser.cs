using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tuning.Core.Parsers
{
    /// <summary>
    /// Reads TunerPro XDF definitions.
    /// Credit: Universal Patcher (https://universalpatcher.net/, GPLv3, by Joukoy and contributors)
    /// is a widely used XDF exporter. This parser tolerates the quirks of its files (duplicate
    /// stub entries, odd CATEGORYMEM indexes, "eq" attributes, empty percent equations, and
    /// table:'name' references), and the table-reference idea in equations follows its convention.
    /// No Universal Patcher source code is used here.
    /// </summary>
    public static class XdfParser
    {
        /// <summary>
        /// Strip TunerPro protection flags and binary markers from an XDF file and save
        /// a clean UTF-8 XML copy that TunerPro RT can open and re-save normally.
        /// Returns null on success, or an error message if something went wrong.
        /// </summary>
        public static string? ConvertToCleanXdf(string inputPath, string outputPath)
        {
            try
            {
                // ── Strategy A: already clean XML ────────────────────
                XDocument? doc = null;
                try { doc = XDocument.Load(inputPath); } catch { }
                if (doc != null)
                {
                    StripProtectionFromDoc(doc);
                    doc.Save(outputPath);
                    return null;
                }

                // ── Strategy B: use MSXML (same engine as TunerPro) ──
                // MSXML6 handles the v4.x binary-wrapped format natively.
                // Its .xml property serialises the loaded DOM back to clean XML.
                string? msxmlError = TryConvertViaMsxml(inputPath, outputPath);
                if (msxmlError == null) return null;   // success

                // ── Strategy C: best-effort text clean + write ───────
                // If MSXML is not available (Linux, etc.) write the cleaned
                // text so the user can at least inspect it.
                return BestEffortClean(inputPath, outputPath);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string? TryConvertViaMsxml(string inputPath, string outputPath)
        {
            try
            {
                var msxmlType = Type.GetTypeFromProgID("Msxml2.DOMDocument.6.0");
                if (msxmlType == null) return "MSXML6 not available";

                dynamic dom = System.Activator.CreateInstance(msxmlType)!;
                dom.async            = false;
                dom.validateOnParse  = false;
                dom.resolveExternals = false;

                bool ok = (bool)dom.load(inputPath);
                if (!ok)
                {
                    string parseErr = (string)dom.parseError.reason;
                    return $"MSXML load failed: {parseErr}";
                }

                // Get clean serialised XML from the DOM
                string xmlText = (string)dom.xml;

                // Parse with XDocument so we can strip the protection flag cleanly
                var doc = XDocument.Parse(xmlText);
                StripProtectionFromDoc(doc);
                doc.Save(outputPath);
                return null;   // success
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string? BestEffortClean(string inputPath, string outputPath)
        {
            byte[] raw = File.ReadAllBytes(inputPath);
            int xmlStart = Array.IndexOf(raw, (byte)'<');
            if (xmlStart < 0) return "No XML content found in file.";

            var cleanBytes = new byte[raw.Length - xmlStart];
            int len = 0;
            for (int i = xmlStart; i < raw.Length; i++)
            {
                byte b = raw[i];
                if (b == 0x09 || b == 0x0A || b == 0x0D ||
                    (b >= 0x20 && b <= 0x7E) || b >= 0xC0)
                    cleanBytes[len++] = b;
            }
            string text = Encoding.Latin1.GetString(cleanBytes, 0, len);

            // Escape bare < > in attribute values
            text = System.Text.RegularExpressions.Regex.Replace(
                text, "=\"([^\"]*)\"",
                m => "=\"" + m.Groups[1].Value.Replace("<", "&lt;").Replace(">", "&gt;") + "\"");

            // Clear protection flag
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"<flags>\s*0x[0-9a-fA-F]+\s*</flags>",
                "<flags>0x0</flags>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Remove encoding declaration so we save as UTF-8
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\bencoding\s*=\s*""[^""]*""", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            File.WriteAllText(outputPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return null;
        }

        private static void StripProtectionFromDoc(XDocument doc)
        {
            // Remove or zero the <flags> element inside XDFHEADER
            foreach (var flags in doc.Descendants()
                         .Where(x => x.Name.LocalName == "flags"))
            {
                string val = flags.Value.Trim();
                // Parse the flag value and clear the "locked" bit (bit 0)
                try
                {
                    long v = val.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToInt64(val, 16) : long.Parse(val);
                    v &= ~1L;   // clear bit 0 = protected
                    flags.Value = $"0x{v:X}";
                }
                catch { flags.Value = "0x0"; }
            }

            // Also handle flags as an attribute on <XDFFORMAT> or <XDFHEADER>
            foreach (var attr in doc.Descendants()
                         .SelectMany(x => x.Attributes())
                         .Where(a => a.Name.LocalName == "flags"))
            {
                try
                {
                    long v = attr.Value.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToInt64(attr.Value.Trim(), 16) : long.Parse(attr.Value.Trim());
                    v &= ~1L;
                    attr.Value = $"0x{v:X}";
                }
                catch { attr.Value = "0x0"; }
            }
        }

        /// <summary>
        /// Load an XDocument from a file, tolerating the binary control-character markers
        /// that many XDF authoring tools embed in otherwise-XML files.
        /// </summary>
        private static XDocument LoadXdf(string xdfPath)
        {
            // Strategy 1: clean XML — fast path, no processing needed.
            try { return XDocument.Load(xdfPath); } catch { }

            // Strategy 2: byte-clean into a temp file, then load with MSXML.
            // TunerPro uses MSXML 3.0 (very lenient).  We combine our binary-marker
            // stripping with MSXML's tolerance for non-standard XML quirks.
            string? msxmlDiag = null;
            string? cleanTmp  = null;
            try
            {
                // Write a byte-cleaned temp file that MSXML can tolerate
                cleanTmp = WriteByteCleaned(xdfPath);

                // The cleaned text is often valid XML already. Load it with .NET's own parser first, which works
                // on every platform; MSXML below exists only on Windows.
                try { return XDocument.Load(cleanTmp); }
                catch (Exception ex) { msxmlDiag = "cleaned text: " + ex.Message; }

                // Try MSXML versions from most lenient (3.0) to strictest (6.0)
                string[] progIds = {
                    "Msxml2.DOMDocument.3.0",
                    "Msxml2.DOMDocument.6.0",
                    "Msxml2.DOMDocument",
                    "Microsoft.XMLDOM"
                };

                foreach (var progId in progIds)
                {
                    try
                    {
                        var t = Type.GetTypeFromProgID(progId);
                        if (t == null) continue;

                        dynamic dom = System.Activator.CreateInstance(t)!;
                        dom.async            = false;
                        dom.validateOnParse  = false;
                        dom.resolveExternals = false;

                        bool loaded = (bool)dom.load(cleanTmp);
                        if (!loaded) { msxmlDiag = $"{progId}: {(string)dom.parseError.reason}"; continue; }

                        string outTmp = System.IO.Path.GetTempFileName();
                        try   { dom.save(outTmp); return XDocument.Load(outTmp); }
                        finally { try { System.IO.File.Delete(outTmp); } catch { } }
                    }
                    catch (Exception ex) { msxmlDiag = $"{progId}: {ex.Message}"; }
                }
            }
            finally
            {
                if (cleanTmp != null) try { System.IO.File.Delete(cleanTmp); } catch { }
            }

            throw new System.Xml.XmlException(
                $"Could not parse XDF — all strategies failed. Last: {msxmlDiag}");
        }

        /// <summary>
        /// Write a byte-cleaned copy of the XDF to a temp file so MSXML can load it.
        /// Strips binary control-character markers and escapes bare &lt;/&gt; in attribute values.
        /// Returns the temp file path (caller must delete it).
        /// </summary>
        private static string WriteByteCleaned(string xdfPath)
        {
            byte[] raw = System.IO.File.ReadAllBytes(xdfPath);

            int xmlStart = Array.IndexOf(raw, (byte)'<');
            if (xmlStart < 0) throw new System.Xml.XmlException("No XML content found in XDF file.");

            // Strip bytes that are invalid XML 1.0 content
            var cleanBytes = new byte[raw.Length - xmlStart];
            int len = 0;
            for (int i = xmlStart; i < raw.Length; i++)
            {
                byte b = raw[i];
                if (b == 0x09 || b == 0x0A || b == 0x0D ||
                    (b >= 0x20 && b <= 0x7E) || b >= 0xC0)
                    cleanBytes[len++] = b;
            }

            string text = Encoding.Latin1.GetString(cleanBytes, 0, len);

            // Encode ALL '<' as '&lt;', then restore only those that look like valid
            // XML tag starts.  Any binary 0x3C marker in an attribute or between
            // attributes stays safely escaped; real tags get their '<' back.
            text = text.Replace("<", "&lt;");

            // Restore: &lt; followed by optional '/' or '!' or '?', optional junk,
            // then a valid NameStartChar  →  real tag start
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"&lt;(/?)([^A-Za-z_:?!/\s>]*)([A-Za-z_:?!])",
                m => "<" + m.Groups[1].Value + m.Groups[3].Value);

            // Remove encoding declaration — we write as UTF-8
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\bencoding\s*=\s*""[^""]*""", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            string tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return tmp;
        }

        public static List<TableDefinition> Parse(string xdfPath)
        {
            var doc = LoadXdf(xdfPath);

            // ── 1. Build category index ──────────────────────────────
            var categories = BuildCategoryDict(doc);

            // ── 1b. Build uniqueid → title map for VAR link resolution ──
            // TunerPro equations use <VAR id="Y" type="link" linkid="0x1234"/>
            // to reference another table's value. We convert those to our
            // ref:'title' syntax so the cross-ref resolver can handle them.
            var uniqueIdToTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in doc.Descendants())
            {
                string localName = el.Name.LocalName;
                if (localName != "XDFTABLE" && localName != "XDFCONSTANT" && localName != "XDFFLAG") continue;
                string? uid = el.Attribute("uniqueid")?.Value?.Trim();
                string? title = el.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value?.Trim();
                if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(title))
                    uniqueIdToTitle[uid] = title;
            }

            // ── 2. Parse tables, constants, and flags ───────────────
            var tables = new List<TableDefinition>();

            foreach (var xTable in doc.Descendants().Where(x => x.Name.LocalName == "XDFTABLE"))
            {
              try
              {
                var table = new TableDefinition();

                table.Name        = xTable.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value ?? "Unnamed";
                table.Description = xTable.Elements().FirstOrDefault(x => x.Name.LocalName == "description")?.Value ?? "";

                ApplyCategory(xTable, categories, table);

                foreach (var xAxis in xTable.Elements().Where(x => x.Name.LocalName == "XDFAXIS"))
                    ParseAxis(xAxis, xAxis.Attribute("id")?.Value ?? "", table, uniqueIdToTitle);

                if (table.Address > 0)
                    tables.Add(table);
              }
              catch (Exception) { /* one malformed table is skipped; the rest of the XDF still loads */ }
            }

            // XDFCONSTANT — scalar value (1×1 table)
            foreach (var xConst in doc.Descendants().Where(x => x.Name.LocalName == "XDFCONSTANT"))
            {
                try
                {
                    var table = ParseScalar(xConst, categories, uniqueIdToTitle);
                    if (table != null) tables.Add(table);
                }
                catch (Exception) { /* skipped, as above */ }
            }

            // XDFFLAG — single bit flag (1×1 table with mask)
            foreach (var xFlag in doc.Descendants().Where(x => x.Name.LocalName == "XDFFLAG"))
            {
                var table = TryParseScalar(xFlag, categories, uniqueIdToTitle);
                if (table != null)
                {
                    string? maskStr = xFlag.Elements().FirstOrDefault(x => x.Name.LocalName == "mask")?.Value;
                    if (maskStr != null)
                    {
                        // The mask is hex and can be wider than a byte (0x0100 on a 16-bit element); one that does not parse leaves the entry a plain value.
                        if (uint.TryParse(maskStr.Trim().Replace("0x", "").Replace("0X", ""), System.Globalization.NumberStyles.AllowHexSpecifier,
                                System.Globalization.CultureInfo.InvariantCulture, out uint mask))
                        {
                            table.Mask = mask;
                        }
                    }
                    tables.Add(table);
                }
            }

            // ── 3. Deduplicate by name ───────────────────────────────────
            // Some XDF exporters (Universal Patcher) emit two entries for the same
            // table: a stub with an empty/identity equation early in the file and a
            // full definition with the real equation later.  Keep the entry with the
            // most informative equation; if both are equivalent, keep the last one
            // (the later entry is typically the more complete definition).
            var result = DeduplicateTables(tables);

            // ── 4. Auto-derive % equations ───────────────────────────────
            // UP exports VE% tables with empty equations — it applies the formula
            // internally. Auto-derive: if a table ends in _% and has no equation,
            // look for a sibling at the same address without the _% suffix that
            // DOES have an equation, then combine with the cylinder volume constant.
            // Formula: (sibling_eq) * (28.9647 / ref:'cylinder_vol_name')
            // 28.9647 = molecular weight of air (g/mol) — a physical constant.
            AutoDerivePercentEquations(result);

            return result;
        }

        private static List<TableDefinition> DeduplicateTables(List<TableDefinition> tables)
        {
            // Score equations: higher = more useful
            static int EquationScore(string eq)
            {
                if (string.IsNullOrWhiteSpace(eq) || eq == "X" || eq == "x") return 0;
                return eq.Length; // longer equation = more complex = prefer it
            }

            // Only a stub and its full definition at the SAME address are duplicates. Two tables that merely share a
            // title (per-bank or per-segment tables) are different tables and must both stay.
            static string Key(TableDefinition t) => t.Name.ToUpperInvariant() + "@" + t.Address;

            var best = new Dictionary<string, (int score, TableDefinition table)>();

            foreach (var t in tables)
            {
                int score = EquationScore(t.ScalingEquation);
                string key = Key(t);
                if (!best.TryGetValue(key, out var existing) || score >= existing.score)
                    best[key] = (score, t);
            }

            // Preserve original order of first occurrence
            var seen = new HashSet<string>();
            var result = new List<TableDefinition>(best.Count);
            foreach (var t in tables)
            {
                string key = Key(t);
                if (seen.Add(key))
                    result.Add(best[key].table);
            }
            return result;
        }

        /// <summary>
        /// Auto-derives scaling equations for "percent" tables whose equations are
        /// empty. Pattern: a table named Foo_% at address A with no equation, paired
        /// with a table named Foo at the same address that has a real equation E(x),
        /// combined with a cylinder-volume constant C.
        /// Result: (E(x)) * (28.9647 / ref:'C_name')
        /// 28.9647 is the molecular weight of dry air (g/mol) — a physical constant.
        /// </summary>
        private static void AutoDerivePercentEquations(List<TableDefinition> tables)
        {
            // Find the cylinder volume constant (any table whose name contains
            // "cylinder" and "volume", case-insensitive).
            var cylVol = tables.FirstOrDefault(t =>
                t.Rows == 1 && t.Columns == 1 &&
                t.Name.Contains("cylinder", StringComparison.OrdinalIgnoreCase) &&
                t.Name.Contains("volume",   StringComparison.OrdinalIgnoreCase));

            if (cylVol == null) return; // no cylinder volume in this XDF

            // Build a lookup: address → list of tables at that address
            var byAddress = new Dictionary<uint, List<TableDefinition>>();
            foreach (var t in tables)
            {
                if (!byAddress.TryGetValue(t.Address, out var list))
                    byAddress[t.Address] = list = new List<TableDefinition>();
                list.Add(t);
            }

            foreach (var pctTable in tables)
            {
                // Only process tables whose names end in _% (or _Percent) with no equation
                bool isPct = pctTable.Name.EndsWith("_%", StringComparison.OrdinalIgnoreCase) ||
                             pctTable.Name.EndsWith("_Percent", StringComparison.OrdinalIgnoreCase);
                if (!isPct) continue;
                if (!string.IsNullOrEmpty(pctTable.ScalingEquation) &&
                    pctTable.ScalingEquation != "X" && pctTable.ScalingEquation != "x") continue;

                // Find a sibling at the same address whose name is the base name (without _%)
                string baseName = pctTable.Name.EndsWith("_%", StringComparison.OrdinalIgnoreCase)
                    ? pctTable.Name[..^2]
                    : pctTable.Name[..^8]; // strip "_Percent"

                if (!byAddress.TryGetValue(pctTable.Address, out var siblings)) continue;

                var sibling = siblings.FirstOrDefault(t =>
                    !ReferenceEquals(t, pctTable) &&
                    string.Equals(t.Name, baseName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(t.ScalingEquation) &&
                    t.ScalingEquation != "X" && t.ScalingEquation != "x");

                if (sibling == null) continue;

                // Build the derived % equation:
                // wrap sibling equation in parens if needed, then multiply by air constant / cyl vol
                string sibEq = sibling.ScalingEquation.Trim();
                if (!sibEq.StartsWith("(")) sibEq = $"({sibEq})";
                pctTable.ScalingEquation = $"{sibEq}*(28.9647/ref:'{cylVol.Name}')";
            }
        }

        private static void ApplyCategory(XElement xElem, Dictionary<int, string> categories, TableDefinition table)
        {
            // CATEGORYMEM index= is the level: 0 = main category, 1 = subcategory.
            // Some exporters (Universal Patcher) only emit a single CATEGORYMEM with
            // index="2" (or any value > 1) when they mean "main category".
            // Strategy: collect all CATEGORYMEM entries ordered by index, then assign
            // the first one as main category and the second as subcategory.
            var entries = new SortedDictionary<int, string>();

            foreach (var catMem in xElem.Elements().Where(x => x.Name.LocalName == "CATEGORYMEM"))
            {
                string? catIdxStr = catMem.Attribute("category")?.Value;
                string? levelStr  = catMem.Attribute("index")?.Value;
                if (catIdxStr == null) continue;

                if (!TryParseIndex(catIdxStr, out int catIdx)) continue;

                if (!categories.TryGetValue(catIdx - 1, out string? catName)) continue;

                int level = 0;
                if (levelStr != null) int.TryParse(levelStr, out level);
                entries[level] = catName;
            }

            if (entries.Count == 0) return;
            var vals = entries.Values.ToList();
            table.Category    = vals[0];
            if (vals.Count > 1) table.SubCategory    = vals[1];
            if (vals.Count > 2) table.SubSubCategory = vals[2];
        }

        /// <summary>As <see cref="ParseScalar"/>, but an entry that cannot be read is skipped (null) instead of ending the load.</summary>
        private static TableDefinition? TryParseScalar(XElement xElem, Dictionary<int, string> categories,
            Dictionary<string, string>? uniqueIdToTitle = null)
        {
            try { return ParseScalar(xElem, categories, uniqueIdToTitle); }
            catch (Exception) { return null; }
        }

        /// <summary>Parse an XDFCONSTANT or XDFFLAG as a 1×1 scalar TableDefinition.</summary>
        private static TableDefinition? ParseScalar(XElement xElem, Dictionary<int, string> categories,
            Dictionary<string, string>? uniqueIdToTitle = null)
        {
            var table = new TableDefinition { Rows = 1, Columns = 1 };

            table.Name        = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value ?? "Unnamed";
            table.Description = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "description")?.Value ?? "";

            ApplyCategory(xElem, categories, table);

            var embed = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "EMBEDDEDDATA");
            if (embed != null)
            {
                string? addr = embed.Attribute("mmedaddress")?.Value;
                if (!string.IsNullOrEmpty(addr))
                    { if (TryParseHexUInt(addr, out uint parsedAddress)) table.Address = parsedAddress; }

                string? bits = embed.Attribute("mmedelementsizebits")?.Value;
                if (bits != null && int.TryParse(bits, out int eb)) table.ElementBits = eb;

                string? datatype = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "datatype")?.Value;
                table.IsSigned = datatype == "1";
            }

            string? units  = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "units")?.Value;
            string? math   = ParseMathEquation(xElem, uniqueIdToTitle);
            string? decStr = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "decimalpl")?.Value;
            string? outStr = xElem.Elements().FirstOrDefault(x => x.Name.LocalName == "outputtype")?.Value;

            table.ZUnits          = units ?? "";
            table.ScalingEquation = NormalizeMath(math);
            if (decStr != null && int.TryParse(decStr, out int dp)) table.DecimalPlaces = dp;
            if (outStr != null && int.TryParse(outStr, out int ot)) table.OutputType    = ot;

            return table.Address > 0 ? table : null;
        }

        private static void ParseAxis(XElement xAxis, string id, TableDefinition table,
            Dictionary<string, string>? uniqueIdToTitle = null)
        {
            var embed = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "EMBEDDEDDATA");
            string? units = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "units")?.Value;
            string? math  = ParseMathEquation(xAxis, uniqueIdToTitle);

            if (id == "z")
            {
                // ── Z axis: the data ──────────────────────────────────
                if (embed != null)
                {
                    string? addr = embed.Attribute("mmedaddress")?.Value;
                    if (!string.IsNullOrEmpty(addr))
                        { if (TryParseHexUInt(addr, out uint parsedAddress)) table.Address = parsedAddress; }

                    string? bits = embed.Attribute("mmedelementsizebits")?.Value;
                    if (bits != null && int.TryParse(bits, out int eb)) table.ElementBits = eb;

                    string? rows = embed.Attribute("mmedrowcount")?.Value;
                    string? cols = embed.Attribute("mmedcolcount")?.Value;
                    if (rows != null && int.TryParse(rows, out int r)) table.Rows    = r;
                    if (cols != null && int.TryParse(cols, out int c)) table.Columns = c;

                    // Stride: only apply positive values (negative = special axis layout, not relevant for z data)
                    string? minorStr = embed.Attribute("mmedminorstridebits")?.Value;
                    string? majorStr = embed.Attribute("mmedmajorstridebits")?.Value;
                    if (minorStr != null && int.TryParse(minorStr, out int minor) && minor > 0)
                        table.MinorStrideBits = minor;
                    if (majorStr != null && int.TryParse(majorStr, out int major) && major > 0)
                        table.MajorStrideBits = major;
                }

                table.ZUnits          = units ?? "";
                table.ScalingEquation = NormalizeMath(math);

                string? decStr    = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "decimalpl")?.Value;
                string? outStr    = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "outputtype")?.Value;
                if (decStr != null && int.TryParse(decStr, out int dp)) table.DecimalPlaces = dp;
                if (outStr != null && int.TryParse(outStr, out int ot)) table.OutputType    = ot;

                // Per-row address overrides: <MATH row="N"><VAR type="address" address="0x..." sizeinbits="32"/></MATH>
                // row="N" is 1-indexed in TunerPro XDF (row="1" = first data row = index 0).
                foreach (var mathEl in xAxis.Elements().Where(x => x.Name.LocalName == "MATH"))
                {
                    string? rowAttr = mathEl.Attribute("row")?.Value;
                    if (rowAttr == null) continue;
                    if (!int.TryParse(rowAttr, out int rowN)) continue;
                    int rowIdx = rowN - 1; // convert to 0-based

                    var varEl = mathEl.Elements().FirstOrDefault(x => x.Name.LocalName == "VAR");
                    if (varEl == null) continue;
                    string? varType = varEl.Attribute("type")?.Value;
                    if (!string.Equals(varType, "address", StringComparison.OrdinalIgnoreCase)) continue;

                    string? addrStr = varEl.Attribute("address")?.Value;
                    if (string.IsNullOrEmpty(addrStr)) continue;
                    try
                    {
                        uint overrideAddr = Convert.ToUInt32(addrStr.Replace("0x","").Replace("0X",""), 16);
                        table.RowAddressOverrides ??= new Dictionary<int, uint>();
                        table.RowAddressOverrides[rowIdx] = overrideAddr;

                        // Also honour per-row element size if specified
                        string? sizeStr = varEl.Attribute("sizeinbits")?.Value;
                        if (sizeStr != null && int.TryParse(sizeStr, out int sib) && sib > 0)
                            table.ElementBits = sib; // all rows share same element size
                    }
                    catch { }
                }

                // mmedtypeflags (preferred): bit 0 = signed, bit 4 = little-endian
                // datatype (legacy fallback): "1" = signed
                string? typeFlags = embed?.Attribute("mmedtypeflags")?.Value;
                if (typeFlags != null)
                {
                    try
                    {
                        long flags = typeFlags.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? Convert.ToInt64(typeFlags, 16) : long.Parse(typeFlags);
                        table.IsSigned    = (flags & 0x01) != 0;
                        table.IsBigEndian = (flags & 0x10) == 0; // bit 4 set = little-endian
                    }
                    catch { }
                }
                else
                {
                    string? datatype = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "datatype")?.Value;
                    table.IsSigned = datatype == "1";
                }
            }
            else if (id == "x")
            {
                // ── X axis: column headers ────────────────────────────
                table.XUnits  = units ?? "";
                table.XLabels = StripUnitPrefix(ParseLabels(xAxis), table.XUnits);
                table.XLabelsAreReal = table.XLabels.Count > 0;

                // If no static labels, try to read count for placeholder labels
                if (table.XLabels.Count == 0)
                {
                    string? countStr = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "indexcount")?.Value;
                    if (countStr != null && int.TryParse(countStr, out int n))
                        for (int i = 1; i <= n; i++) table.XLabels.Add(i.ToString());
                }
            }
            else if (id == "y")
            {
                // ── Y axis: row headers ────────────────────────────────
                table.YUnits  = units ?? "";
                table.YLabels = StripUnitPrefix(ParseLabels(xAxis), table.YUnits);
                table.YLabelsAreReal = table.YLabels.Count > 0;

                if (table.YLabels.Count == 0)
                {
                    string? countStr = xAxis.Elements().FirstOrDefault(x => x.Name.LocalName == "indexcount")?.Value;
                    if (countStr != null && int.TryParse(countStr, out int n))
                        for (int i = 1; i <= n; i++) table.YLabels.Add(i.ToString());
                }
            }
        }

        private static List<string> ParseLabels(XElement xAxis)
        {
            var labels = new SortedDictionary<int, string>();
            foreach (var lbl in xAxis.Elements().Where(x => x.Name.LocalName == "LABEL"))
            {
                string? idxStr = lbl.Attribute("index")?.Value;
                string? val    = lbl.Attribute("value")?.Value;
                if (idxStr != null && val != null && int.TryParse(idxStr, out int i))
                    labels[i] = val.Trim();
            }
            return labels.Values.ToList();
        }

        /// <summary>
        /// Removes a leading unit prefix from axis labels so "RPM 800" becomes "800"
        /// and "kPa  0" becomes "0". Only strips when the prefix matches the axis units.
        /// </summary>
        private static List<string> StripUnitPrefix(List<string> labels, string units)
        {
            if (string.IsNullOrEmpty(units)) return labels;
            var result = new List<string>(labels.Count);
            foreach (var label in labels)
            {
                if (label.StartsWith(units, StringComparison.OrdinalIgnoreCase))
                    result.Add(label.Substring(units.Length).Trim());
                else
                    result.Add(label);
            }
            return result;
        }

        // ── Patch parsing ────────────────────────────────────────────
        public static List<PatchDefinition> ParsePatches(string xdfPath)
        {
            var doc        = LoadXdf(xdfPath);
            var categories = BuildCategoryDict(doc);
            var patches    = new List<PatchDefinition>();

            foreach (var xPatch in doc.Descendants().Where(x => x.Name.LocalName == "XDFPATCH"))
            {
                var patch = new PatchDefinition();
                patch.Name        = xPatch.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value ?? "Unnamed";
                patch.Description = xPatch.Elements().FirstOrDefault(x => x.Name.LocalName == "description")?.Value ?? "";

                foreach (var catMem in xPatch.Elements().Where(x => x.Name.LocalName == "CATEGORYMEM"))
                {
                    string? levelStr  = catMem.Attribute("index")?.Value;
                    string? catIdxStr = catMem.Attribute("category")?.Value;
                    if (catIdxStr == null || !TryParseIndex(catIdxStr, out int catIdx)) continue;
                    if (!categories.TryGetValue(catIdx - 1, out string? catName)) continue;
                    if (levelStr == "0") patch.Category    = catName;
                    if (levelStr == "1") patch.SubCategory = catName;
                }

                foreach (var xEntry in xPatch.Elements().Where(x => x.Name.LocalName == "XDFPATCHENTRY"))
                {
                    string? name     = xEntry.Attribute("name")?.Value;
                    string? addrStr  = xEntry.Attribute("address")?.Value;
                    string? dataStr  = xEntry.Attribute("patchdata")?.Value;
                    if (addrStr == null || dataStr == null) continue;

                    // An entry whose address or data is not hex is left out; the rest of the patch still loads.
                    byte[]? data = HexStringToBytes(dataStr);
                    if (data == null || !TryParseHexUInt(addrStr, out uint entryAddress)) continue;
                    patch.Entries.Add(new PatchEntry
                    {
                        Name    = name ?? "",
                        Address = entryAddress,
                        Data    = data,
                    });
                }

                patches.Add(patch);
            }

            return patches;
        }

        private static Dictionary<int, string> BuildCategoryDict(XDocument doc)
        {
            var categories = new Dictionary<int, string>();
            var header = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "XDFHEADER");
            if (header != null)
            {
                foreach (var cat in header.Elements().Where(x => x.Name.LocalName == "CATEGORY"))
                {
                    string? idxStr = cat.Attribute("index")?.Value;
                    string? name   = cat.Attribute("name")?.Value;
                    if (idxStr != null && name != null && TryParseIndex(idxStr, out int idx))
                    {
                        categories[idx] = name;
                    }
                }
            }
            return categories;
        }

        // ── XML Name character helpers (XML 1.0 spec §2.3) ───────────
        private static bool XmlNameStartChar(char c) =>
            c == ':' || c == '_'                         ||
            (c >= 'A'    && c <= 'Z')                    ||
            (c >= 'a'    && c <= 'z')                    ||
            (c >= '\xC0' && c <= '\xD6')                 ||
            (c >= '\xD8' && c <= '\xF6')                 ||
             c >= '\xF8';   // U+00F8 and above are all valid in the Latin block

        private static bool XmlNameChar(char c) =>
            XmlNameStartChar(c)                          ||
            c == '-' || c == '.' || c == '\xB7'          ||
            (c >= '0' && c <= '9');

        /// <summary>A hex number with or without a 0x prefix. False for anything else (empty, too long for 32 bits, not hex), so one bad entry does not stop the file loading.</summary>
        public static bool TryParseHexUInt(string? text, out uint value)
        {
            value = 0;
            string s = (text ?? string.Empty).Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return s.Length > 0 && uint.TryParse(s, System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>An index written as a plain number or as 0x hex (the category lists use both).</summary>
        public static bool TryParseIndex(string? text, out int value)
        {
            value = 0;
            string s = (text ?? string.Empty).Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
            return int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Hex digits to bytes, or null when the text is not hex.</summary>
        private static byte[]? HexStringToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("0x", "");
            if (hex.Length % 2 != 0) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.AllowHexSpecifier,
                        System.Globalization.CultureInfo.InvariantCulture, out bytes[i]))
                    return null;
            }
            return bytes;
        }

        /// <summary>
        /// Extract the equation string from a MATH child element.
        /// Handles both TunerPro-style ("equation" attribute) and Universal Patcher / other
        /// tools that use an "eq" attribute or store the formula as element text content.
        /// </summary>
        private static string? ParseMathEquation(XElement parent,
            Dictionary<string, string>? uniqueIdToTitle = null)
        {
            var mathEl = parent.Elements().FirstOrDefault(x => x.Name.LocalName == "MATH");
            if (mathEl == null) return null;

            // Try common attribute names first
            string? eq = mathEl.Attribute("equation")?.Value
                      ?? mathEl.Attribute("eq")?.Value
                      ?? mathEl.Attribute("Equation")?.Value;

            // Fallback: equation stored as element text (some exporters do this)
            if (string.IsNullOrWhiteSpace(eq))
                eq = mathEl.Value?.Trim();

            if (string.IsNullOrWhiteSpace(eq)) return null;

            // Resolve TunerPro VAR links: <VAR id="Y" type="link" linkid="0x6649"/>
            // Replace the var id in the equation with ref:'linked table title'
            // so our cross-ref resolver can look it up from the BIN at display time.
            if (uniqueIdToTitle != null)
            {
                foreach (var varEl in mathEl.Elements().Where(x => x.Name.LocalName == "VAR"))
                {
                    string? varType = varEl.Attribute("type")?.Value;
                    if (!string.Equals(varType, "link", StringComparison.OrdinalIgnoreCase)) continue;

                    string? varId   = varEl.Attribute("id")?.Value?.Trim();
                    string? linkId  = varEl.Attribute("linkid")?.Value?.Trim();
                    if (string.IsNullOrEmpty(varId) || string.IsNullOrEmpty(linkId)) continue;

                    // Normalise linkid: both "0x6649" and "6649" forms
                    string normLink = linkId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? linkId : "0x" + linkId;

                    if (!uniqueIdToTitle.TryGetValue(normLink, out string? linkedTitle) &&
                        !uniqueIdToTitle.TryGetValue(linkId, out linkedTitle))
                        continue;

                    // Replace the var id as a whole word (not part of another identifier)
                    eq = System.Text.RegularExpressions.Regex.Replace(
                        eq,
                        @"(?<![A-Za-z0-9_])" + System.Text.RegularExpressions.Regex.Escape(varId) + @"(?![A-Za-z0-9_])",
                        $"ref:'{linkedTitle}'");
                }
            }

            return eq;
        }

        /// <summary>
        /// Normalise the math equation string so DynamicExpresso can evaluate it.
        /// XDF files use either "X" or "x" as the variable name; normalise to uppercase "X".
        /// Use a word-boundary replacement so we don't mangle hex digits or identifiers.
        /// </summary>
        private static string NormalizeMath(string? eq)
        {
            if (string.IsNullOrWhiteSpace(eq)) return "X";
            // Replace standalone 'x' (not preceded or followed by a word char) with 'X'
            string normalized = System.Text.RegularExpressions.Regex.Replace(
                eq.Trim(), @"(?<![A-Za-z0-9_])x(?![A-Za-z0-9_])", "X");
            return normalized;
        }
    }
}
