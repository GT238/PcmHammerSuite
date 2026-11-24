using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// A shared folder of reference BINs (factory files and others), next to the projects and the XDF library. Compare pickers start there, and a BIN
    /// that is opened is matched against it by the GM part numbers (OS IDs) stored inside the file.
    /// </summary>
    public static class StockBins
    {
        /// <summary>Where the reference BINs live. It is created on first use.</summary>
        public static string Folder => ProjectInfo.StockBinsFolder;

        /// <summary>True when the folder holds at least one BIN (so a file picker is worth starting there).</summary>
        public static bool HasFiles()
        {
            try
            {
                return Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.bin", SearchOption.AllDirectories).Any();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The BIN in the folder that shares the most OS IDs with this one, or null. Only files of the same size count (a compare needs that),
        /// the file itself and a byte-for-byte copy of it are skipped, and an unreadable file is ignored. Folders inside it are searched too.
        /// </summary>
        public static string? FindMatch(byte[] bin, string? ownPath, string folder)
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            HashSet<string> ids = OsIdDetector.ScanForOsIds(bin).ToHashSet();
            if (ids.Count == 0)
            {
                return null;
            }

            string? own = ownPath == null ? null : Path.GetFullPath(ownPath);
            string? best = null;
            int bestShared = 0;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.bin", SearchOption.AllDirectories).Take(1000).ToList();
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (own != null && string.Equals(Path.GetFullPath(file), own, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (new FileInfo(file).Length != bin.Length)
                    {
                        continue;
                    }

                    byte[] other = File.ReadAllBytes(file);
                    if (other.AsSpan().SequenceEqual(bin))
                    {
                        continue;
                    }

                    int shared = OsIdDetector.ScanForOsIds(other).Count(ids.Contains);
                    if (shared > bestShared)
                    {
                        bestShared = shared;
                        best = file;
                    }
                }
                catch (Exception)
                {
                    // One unreadable file must not stop the search.
                }
            }

            return best;
        }
    }
}
