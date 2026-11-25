using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// The Tuner's own definitions: XDFs it wrote itself, kept in the shared XDF library next to the ones the user added.
    /// The file name carries the BIN's OS ID, which is all the existing auto-detect needs to offer it for every BIN with that OS.
    /// </summary>
    public static class DefinitionStore
    {
        public const string Suffix = "my definition";

        /// <summary>
        /// Writes the tables as an XDF for the OS in this BIN and returns its path. A file already there is never replaced:
        /// the new one gets a number ("... (2).xdf").
        /// </summary>
        /// <param name="source">Where the tables came from: the XDF's file name, shown in the file's description and used to tell which part number is the OS.</param>
        /// <param name="osId">The OS ID to name the file after, or null to work it out.</param>
        public static string Save(IReadOnlyList<TableDefinition> tables, byte[] bin, string source, string folder, string? osId = null)
        {
            if (tables.Count == 0)
            {
                throw new InvalidOperationException("There are no tables to save.");
            }

            string id = osId ?? IdFromSource(bin, source) ?? OsIdDetector.PrimaryId(bin) ?? throw new InvalidOperationException(
                "No OS ID (an 8-digit GM part number) was found in this BIN, so a definition cannot be named for it.");

            Directory.CreateDirectory(folder);
            string path = UniquePath(folder, id);

            StringBuilder description = new();
            description.Append("Definitions for OS ").Append(id).Append(", saved by PCM Tuner from ").Append(source).Append('.');
            XdfWriter.Write(path, tables, "OS" + id + " - " + Suffix, description.ToString(), bin.Length);
            return path;
        }

        /// <summary>
        /// The part number in the BIN that the source XDF's file name carries. A BIN holds several part numbers (the OS, the calibrations...),
        /// and the XDF it was opened with is the best sign of which one is the OS. Null when the name has none of them.
        /// </summary>
        public static string? IdFromSource(byte[] bin, string source)
        {
            string name = Path.GetFileName(source);
            return OsIdDetector.ScanForOsIds(bin).FirstOrDefault(id => name.Contains(id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True for a file this store wrote (its name ends with "- my definition", possibly numbered).</summary>
        public static bool IsMine(string pathOrName) =>
            Path.GetFileName(pathOrName).Contains(" - " + Suffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>The path a new definition for this OS would get in the folder.</summary>
        public static string UniquePath(string folder, string osId)
        {
            string path = Path.Combine(folder, osId + " - " + Suffix + ".xdf");
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(folder, osId + " - " + Suffix + " (" + n + ").xdf");
            }

            return path;
        }
    }
}
