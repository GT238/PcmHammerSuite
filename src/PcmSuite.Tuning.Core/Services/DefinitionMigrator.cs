using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tuning.Core.Services
{
    /// <summary>How sure a placement is.</summary>
    public enum MatchKind
    {
        /// <summary>The table's own bytes were found in the other BIN, almost unchanged, in one place only.</summary>
        Content,

        /// <summary>Not found by its bytes, but the tables on both sides of it moved by the same amount, so it moved with them.</summary>
        Inferred,

        /// <summary>Not placed: nothing reliable says where it went.</summary>
        Unresolved,
    }

    /// <summary>Where one table of the reference definition went in the other BIN.</summary>
    public sealed record TableMatch(TableDefinition Source, MatchKind Kind, int Shift, double Similarity);

    public sealed class MigrationResult
    {
        public MigrationResult(IReadOnlyList<TableMatch> matches) => Matches = matches;

        public IReadOnlyList<TableMatch> Matches { get; }

        public int Count(MatchKind kind) => Matches.Count(m => m.Kind == kind);

        public IEnumerable<TableMatch> Placed => Matches.Where(m => m.Kind != MatchKind.Unresolved);

        /// <summary>
        /// The table as it would be in the other BIN: the same definition moved by the shift, with a note in its description saying
        /// how it was placed (the note is saved with the definition, so it is still there when it is used later).
        /// </summary>
        public static TableDefinition Place(TableMatch match, string referenceName)
        {
            TableDefinition t = match.Source.Clone();
            Move(t, match.Shift);

            string how = match.Kind == MatchKind.Content
                ? "its values were found in this BIN"
                : "the tables around it moved by the same amount";
            t.Description = "[Placed by comparing with " + referenceName + ": " + how + ", moved by " + match.Shift.ToString("+#;-#;0") +
                            " bytes. Check that the numbers look right before relying on it.]" +
                            (string.IsNullOrEmpty(t.Description) ? string.Empty : "\r\n" + t.Description);
            return t;
        }

        private static void Move(TableDefinition t, int shift)
        {
            t.Address = (uint)((int)t.Address + shift);
            if (t.RowAddressOverrides != null)
            {
                foreach (int row in t.RowAddressOverrides.Keys.ToList())
                {
                    t.RowAddressOverrides[row] = (uint)((int)t.RowAddressOverrides[row] + shift);
                }
            }

            if (t.XAxis != null) t.XAxis.Address = (uint)((int)t.XAxis.Address + shift);
            if (t.YAxis != null) t.YAxis.Address = (uint)((int)t.YAxis.Address + shift);
        }
    }

    /// <summary>
    /// Finds where the tables of one OS's definition are in a BIN of a similar OS, so a definition does not have to exist for every OS.
    /// Between versions of the same family the calibration moves by small amounts that stay the same along a stretch, so
    /// (1) tables whose bytes can be found nearby are matched by their content, and (2) the rest are placed between matched neighbours that moved alike.
    /// It only suggests: the result is shown to the user and nothing is applied without their say.
    /// </summary>
    public static class DefinitionMigrator
    {
        /// <summary>Smaller tables are too short to recognise by their bytes.</summary>
        public const int MinContentBytes = 8;

        /// <summary>A table counts as found when this share of its bytes is the same.</summary>
        public const double MinSimilarity = 0.9;

        /// <summary>...and the best place must beat the runner-up by this share of its bytes (a flat run of zeros matches everywhere).</summary>
        public const double MinMargin = 0.1;

        /// <param name="source">The BIN the definition was made for.</param>
        /// <param name="target">The BIN to find the tables in.</param>
        /// <param name="tables">The definition for <paramref name="source"/>.</param>
        /// <param name="window">How far a table may have moved from where the previous one did.</param>
        public static MigrationResult Migrate(byte[] source, byte[] target, IReadOnlyList<TableDefinition> tables, int window = 600,
            IProgress<double>? progress = null, CancellationToken cancel = default)
        {
            Dictionary<TableDefinition, TableMatch> found = new();
            List<(int Start, int Shift)> anchors = new();

            List<TableDefinition> ordered = tables.OrderBy(t => t.Address).ToList();
            int running = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Report((double)i / Math.Max(1, ordered.Count));

                TableDefinition t = ordered[i];
                (int start, int end) = t.Extent();
                int n = end - start;
                if (t.RowAddressOverrides != null || t.Mask != 0 || n < MinContentBytes || start < 0 || end > source.Length)
                {
                    continue;
                }

                (int shift, int best, int second) = Search(source, target, start, n, running, window);
                if (best < 0)
                {
                    continue;
                }

                double similarity = (double)best / n;
                double margin = (double)(best - Math.Max(0, second)) / n;
                if (similarity >= MinSimilarity && margin >= MinMargin)
                {
                    found[t] = new TableMatch(t, MatchKind.Content, shift, similarity);
                    anchors.Add((start, shift));
                    running = shift;
                }
            }

            // Everything else: between two matched neighbours that moved by the same amount, it moved by that amount too.
            List<TableMatch> all = new();
            foreach (TableDefinition t in tables)
            {
                if (found.TryGetValue(t, out TableMatch? match))
                {
                    all.Add(match);
                    continue;
                }

                all.Add(Infer(t, source, target, anchors));
            }

            progress?.Report(1);
            return new MigrationResult(all);
        }

        private static TableMatch Infer(TableDefinition t, byte[] source, byte[] target, List<(int Start, int Shift)> anchors)
        {
            TableMatch none = new(t, MatchKind.Unresolved, 0, 0);
            int at = (int)t.Address;
            (int Start, int Shift)? before = null, after = null;
            foreach ((int start, int shift) in anchors)
            {
                if (start <= at)
                {
                    before = (start, shift);
                }
                else
                {
                    after = (start, shift);
                    break;
                }
            }

            if (before == null || after == null || before.Value.Shift != after.Value.Shift)
            {
                return none;
            }

            int d = before.Value.Shift;
            (int start0, int end0) = t.Extent();
            int lowest = t.RowAddressOverrides?.Values.Select(v => (int)v).DefaultIfEmpty(start0).Min() ?? start0;
            if (lowest + d < 0 || end0 + d > target.Length || end0 > source.Length)
            {
                return none;
            }

            double similarity = 0;
            if (t.RowAddressOverrides == null && end0 - start0 > 0)
            {
                int same = 0;
                for (int k = 0; k < end0 - start0; k++)
                {
                    if (source[start0 + k] == target[start0 + d + k])
                    {
                        same++;
                    }
                }

                similarity = (double)same / (end0 - start0);
            }

            return new TableMatch(t, MatchKind.Inferred, d, similarity);
        }

        /// <summary>The shift (within the window around <paramref name="around"/>) at which the table's bytes agree most with the target, with the best and second-best counts.</summary>
        private static (int Shift, int Best, int Second) Search(byte[] source, byte[] target, int start, int n, int around, int window)
        {
            int bestShift = 0, best = -1, second = -1;
            ReadOnlySpan<byte> pattern = source.AsSpan(start, n);
            for (int d = around - window; d <= around + window; d++)
            {
                int o = start + d;
                if (o < 0 || o + n > target.Length)
                {
                    continue;
                }

                ReadOnlySpan<byte> candidate = target.AsSpan(o, n);
                int score = 0;
                for (int k = 0; k < n; k++)
                {
                    if (pattern[k] == candidate[k])
                    {
                        score++;
                    }
                }

                if (score > best)
                {
                    second = best;
                    best = score;
                    bestShift = d;
                }
                else if (score > second)
                {
                    second = score;
                }
            }

            return (bestShift, best, second);
        }
    }
}
