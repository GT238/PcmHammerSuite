using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tuning.Core.Services
{
    /// <summary>A BIN that could serve as the reference for another one, with what comparing it found.</summary>
    public sealed record ReferenceCandidate(string BinPath, string Origin, string XdfPath, MigrationResult Result, double Similarity)
    {
        public string FileName => Path.GetFileName(BinPath);

        public string XdfName => Path.GetFileName(XdfPath);

        public int Content => Result.Count(MatchKind.Content);

        public int Inferred => Result.Count(MatchKind.Inferred);

        public int Unresolved => Result.Count(MatchKind.Unresolved);

        /// <summary>The share of the XDF's tables that were found by their own values (the surest way). A share, not a count, so a big XDF does not win just by having more tables.</summary>
        public double ContentShare => Result.Matches.Count == 0 ? 0 : (double)Content / Result.Matches.Count;

        /// <summary>The share of the XDF's tables that were placed at all.</summary>
        public double PlacedShare => Result.Matches.Count == 0 ? 0 : (double)(Content + Inferred) / Result.Matches.Count;
    }

    /// <summary>
    /// Looks through the BINs the user already has (the Reference BINs folder and every project) for the ones that make the best reference
    /// for a BIN that has no XDF: same size, an XDF the Tuner can read, and the most tables found by their values.
    /// Nothing is labelled by hand: how many tables line up says whether two BINs are the same family, engine and calibration.
    /// </summary>
    public static class ReferenceFinder
    {
        private static readonly ConcurrentDictionary<(string Path, DateTime Written), List<TableDefinition>?> Parsed = new();

        /// <summary>Every BIN under the root (the Reference BINs folder and each project), with the folder it was found in. The XDF library is skipped.</summary>
        public static IReadOnlyList<(string Path, string Origin)> Gather(string root, params string[] skipFolders)
        {
            List<(string, string)> found = new();
            if (!Directory.Exists(root))
            {
                return found;
            }

            try
            {
                foreach (string path in Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories))
                {
                    if (skipFolders.Any(s => AppPaths.IsInside(path, s)))
                    {
                        continue;
                    }

                    string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    int slash = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                    found.Add((path, slash > 0 ? relative.Substring(0, slash) : "(top folder)"));
                }
            }
            catch (Exception)
            {
                // Whatever could be listed is still useful.
            }

            return found;
        }

        /// <summary>
        /// The best references for <paramref name="target"/>, best first.
        /// </summary>
        /// <param name="candidates">The BINs to consider.</param>
        /// <param name="findXdfs">The XDF files that go with a BIN (by its part numbers), in the order to try them.</param>
        /// <param name="keep">How many to return.</param>
        /// <param name="maxTried">How many candidates (the most alike first) to compare in full.</param>
        public static IReadOnlyList<ReferenceCandidate> Rank(byte[] target, string? targetPath, IEnumerable<(string Path, string Origin)> candidates,
            Func<string, byte[], IReadOnlyList<string>> findXdfs, int keep = 5, int maxTried = 24, CancellationToken cancel = default)
        {
            string targetHash = Hash(target);
            HashSet<ulong> targetWindows = Windows(target);

            // Cheap pass: same size, not the target itself (or a copy of it), not a duplicate of another candidate; most alike first.
            Dictionary<string, (string Path, string Origin, byte[] Data, double Similarity, DateTime Written)> unique = new();
            foreach ((string path, string origin) in candidates)
            {
                cancel.ThrowIfCancellationRequested();
                if (targetPath != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byte[] data;
                try
                {
                    FileInfo info = new(path);
                    if (info.Length != target.Length)
                    {
                        continue;
                    }

                    data = File.ReadAllBytes(path);
                }
                catch (Exception)
                {
                    continue;
                }

                string hash = Hash(data);
                if (hash == targetHash || unique.ContainsKey(hash))
                {
                    continue;
                }

                unique[hash] = (path, origin, data, Similarity(data, targetWindows), File.GetLastWriteTimeUtc(path));
            }

            // Full pass: the most alike first, until enough have a readable XDF and have been compared.
            List<ReferenceCandidate> compared = new();
            int tried = 0;
            foreach (var c in unique.Values.OrderByDescending(c => c.Similarity).ThenByDescending(c => c.Written))
            {
                cancel.ThrowIfCancellationRequested();
                if (tried >= maxTried)
                {
                    break;
                }

                foreach (string xdf in findXdfs(c.Path, c.Data))
                {
                    List<TableDefinition>? tables = ReadTables(xdf);
                    if (tables == null || tables.Count == 0)
                    {
                        continue;   // protected or not an XDF: the next one
                    }

                    tried++;
                    MigrationResult result = DefinitionMigrator.Migrate(c.Data, target, tables, cancel: cancel);
                    compared.Add(new ReferenceCandidate(c.Path, c.Origin, xdf, result, c.Similarity));
                    break;
                }
            }

            // Best first. A tuned copy of a BIN that gives the same result as one already listed adds nothing, so only the first (newest) is kept.
            List<ReferenceCandidate> ranked = new();
            foreach (ReferenceCandidate c in compared
                         .OrderByDescending(c => c.ContentShare)
                         .ThenByDescending(c => c.PlacedShare)
                         .ThenByDescending(c => c.Similarity))
            {
                bool same = ranked.Any(r => r.XdfPath == c.XdfPath && r.Content == c.Content && r.Inferred == c.Inferred && r.Unresolved == c.Unresolved);
                if (!same)
                {
                    ranked.Add(c);
                }

                if (ranked.Count >= keep)
                {
                    break;
                }
            }

            return ranked;
        }

        /// <summary>The tables of an XDF, or null when it cannot be read. Remembered until the file changes.</summary>
        private static List<TableDefinition>? ReadTables(string xdf)
        {
            try
            {
                DateTime written = File.GetLastWriteTimeUtc(xdf);
                return Parsed.GetOrAdd((xdf, written), key =>
                {
                    try
                    {
                        return XdfParser.Parse(key.Path);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                });
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Hash(byte[] data)
        {
            using MD5 md5 = MD5.Create();
            return Convert.ToBase64String(md5.ComputeHash(data));
        }

        /// <summary>Every 16-byte run in the BIN (at any alignment), as a hash. Runs of one repeated byte (erased flash, padding) say nothing and are left out.</summary>
        public static HashSet<ulong> Windows(byte[] bin)
        {
            HashSet<ulong> set = new();
            for (int i = 0; i + 16 <= bin.Length; i++)
            {
                if (TryHash(bin, i, out ulong h))
                {
                    set.Add(h);
                }
            }

            return set;
        }

        /// <summary>The share of the candidate's 16-byte blocks (taken every 16 bytes) that appear somewhere in the target. 1 = the same file, near 0 = unrelated.</summary>
        public static double Similarity(byte[] candidate, HashSet<ulong> targetWindows)
        {
            int hit = 0, n = 0;
            for (int i = 0; i + 16 <= candidate.Length; i += 16)
            {
                if (!TryHash(candidate, i, out ulong h))
                {
                    continue;
                }

                n++;
                if (targetWindows.Contains(h))
                {
                    hit++;
                }
            }

            return n == 0 ? 0 : (double)hit / n;
        }

        private static bool TryHash(byte[] b, int i, out ulong hash)
        {
            hash = 14695981039346656037UL;
            bool flat = true;
            for (int k = 0; k < 16; k++)
            {
                hash = (hash ^ b[i + k]) * 1099511628211UL;
                if (b[i + k] != b[i])
                {
                    flat = false;
                }
            }

            return !flat;
        }
    }
}
