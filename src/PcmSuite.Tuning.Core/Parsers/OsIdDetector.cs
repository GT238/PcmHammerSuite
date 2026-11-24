using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PcmSuite.Tuning.Core.Parsers
{
    /// <summary>
    /// Scans a BIN file for GM part-number strings (8-digit, ASCII, starts with "12")
    /// and matches them against the XDF files available in a folder.
    /// </summary>
    public static class OsIdDetector
    {
        /// <summary>
        /// Returns the filename of the best-matching XDF for the given BIN data,
        /// or null if no match is found.
        /// Scoring: prefer the XDF whose matching OS ID appears most frequently in
        /// the BIN (the primary calibration number is repeated more than references).
        /// </summary>
        public static string? FindMatchingXdf(byte[] bin, string xdfFolder)
        {
            if (!Directory.Exists(xdfFolder)) return null;
            var xdfFiles = Directory.GetFiles(xdfFolder, "*.xdf");
            if (xdfFiles.Length == 0) return null;

            // Collect all OS IDs found in the BIN
            var allIds = ScanForOsIds(bin).ToList();
            if (allIds.Count == 0) return null;

            // For each candidate XDF, find the best-matching ID and count its occurrences
            string? bestXdf   = null;
            int     bestScore = -1;

            foreach (var xdf in xdfFiles)
            {
                string fname = Path.GetFileName(xdf);
                foreach (string id in allIds)
                {
                    if (!fname.Contains(id, StringComparison.OrdinalIgnoreCase)) continue;

                    // Count raw occurrences of this ID in the BIN to score the match
                    int score = CountOccurrences(bin, id);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestXdf   = fname;
                    }
                    break; // only score each XDF once (against its first matching ID)
                }
            }

            return bestXdf;
        }

        /// <summary>
        /// Every XDF in the folder whose file name carries one of the BIN's OS IDs, best match first (ties in name
        /// order). A caller can try them in turn, because some XDFs in a folder may be protected and unreadable.
        /// </summary>
        public static IReadOnlyList<string> FindMatchingXdfs(byte[] bin, string xdfFolder)
        {
            if (!Directory.Exists(xdfFolder)) return Array.Empty<string>();
            var allIds = ScanForOsIds(bin).ToList();
            if (allIds.Count == 0) return Array.Empty<string>();

            var matches = new List<(string Name, int Score, bool Mine, DateTime Written)>();
            foreach (var xdf in Directory.GetFiles(xdfFolder, "*.xdf"))
            {
                string fname = Path.GetFileName(xdf);
                foreach (string id in allIds)
                {
                    if (!fname.Contains(id, StringComparison.OrdinalIgnoreCase)) continue;
                    // A definition the user saved from the Tuner (see DefinitionStore) beats the XDF it was made from.
                    bool mine = PcmSuite.Tuning.Core.Models.DefinitionStore.IsMine(fname);
                    matches.Add((fname, CountOccurrences(bin, id), mine, mine ? File.GetLastWriteTimeUtc(xdf) : DateTime.MinValue));
                    break;
                }
            }

            return matches
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => m.Mine)
                .ThenByDescending(m => m.Written)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => m.Name)
                .ToList();
        }

        /// <summary>
        /// The OS ID this BIN most likely is: the part number that appears most often (the calibration's own number is repeated
        /// more than the ones it merely refers to). Ties go to the one found first. Null if the BIN holds none.
        /// </summary>
        public static string? PrimaryId(byte[] bin)
        {
            string? best = null;
            int bestCount = 0;
            foreach (string id in ScanForOsIds(bin))
            {
                int count = CountOccurrences(bin, id);
                if (count > bestCount)
                {
                    best = id;
                    bestCount = count;
                }
            }

            return best;
        }

        private static int CountOccurrences(byte[] bin, string id)
        {
            byte[] pattern = Encoding.ASCII.GetBytes(id);
            int count = 0;
            for (int i = 0; i <= bin.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                    if (bin[i + j] != pattern[j]) { match = false; break; }
                if (match) count++;
            }
            return count;
        }

        /// <summary>
        /// Reads all OS IDs from the BIN in the order they appear,
        /// so callers can inspect or display them.
        /// </summary>
        public static IEnumerable<string> ScanForOsIds(byte[] bin)
        {
            var seen = new HashSet<string>();
            // An 8-digit GM part number in ASCII: first byte '1', second '2'-'9'
            for (int i = 0; i < bin.Length - 8; i++)
            {
                if (bin[i] != (byte)'1') continue;
                if (bin[i + 1] < (byte)'2' || bin[i + 1] > (byte)'9') continue;

                bool valid = true;
                for (int j = 0; j < 8; j++)
                {
                    if (bin[i + j] < (byte)'0' || bin[i + j] > (byte)'9')
                    { valid = false; break; }
                }
                // Must be followed by a non-digit (null, space, etc.) to avoid
                // matching longer sequences
                if (valid && i + 8 < bin.Length && bin[i + 8] >= (byte)'0' && bin[i + 8] <= (byte)'9')
                    valid = false;

                if (!valid) continue;

                string id = Encoding.ASCII.GetString(bin, i, 8);
                if (seen.Add(id))
                    yield return id;
            }
        }
    }
}
