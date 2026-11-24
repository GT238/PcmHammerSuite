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
        /// <param name="source">Where the tables came from (shown in the file's description).</param>
        /// <param name="osId">The OS ID to name the file after, or null to take the BIN's own.</param>
        public static string Save(IReadOnlyList<TableDefinition> tables, byte[] bin, string source, string folder, string? osId = null)
        {
            if (tables.Count == 0)
            {
                throw new InvalidOperationException("There are no tables to save.");
            }

            string id = osId ?? OsIdDetector.PrimaryId(bin) ?? throw new InvalidOperationException(
                "No OS ID (an 8-digit GM part number) was found in this BIN, so a definition cannot be named for it.");

            Directory.CreateDirectory(folder);
            string path = UniquePath(folder, id);

            StringBuilder description = new();
            description.Append("Definitions for OS ").Append(id).Append(", saved by PCM Tuner from ").Append(source).Append('.');
            XdfWriter.Write(path, tables, "OS" + id + " - " + Suffix, description.ToString(), bin.Length);
            return path;
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
