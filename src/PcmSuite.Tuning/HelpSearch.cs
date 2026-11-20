using System.Net;
using System.Text.RegularExpressions;

namespace PcmSuite.Tune;

/// <summary>One section of a help page (from one heading to the next).</summary>
public sealed record HelpSection(string Topic, string TopicTitle, string Title, string? Anchor, string Text);

/// <summary>A search result: the section, how well it matched, and a short piece of its text around the match.</summary>
public sealed record HelpHit(HelpSection Section, double Score, string Snippet);

/// <summary>
/// Searches the help pages offline. Each page is split into sections at its headings; a question is cut into words, the
/// everyday ones ("how", "do", "the") are dropped, similar words are treated as the same ("write" finds "flash"), and the
/// sections are ranked by how many of the words they contain and whether those words are in their title.
/// </summary>
public sealed class HelpSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "i", "my", "me", "to", "of", "in", "on", "for", "is", "are", "can", "how", "do", "does", "what",
        "why", "where", "when", "which", "it", "this", "that", "and", "or", "with", "from", "be", "use", "using", "want",
        "need", "get", "make", "should", "would", "could", "there", "any", "some", "you", "your", "we", "our", "so", "if",
    };

    // Words that mean nearly the same thing to someone looking for help.
    private static readonly string[][] SynonymGroups =
    {
        new[] { "checksum", "validate", "validation", "valid", "verify", "crc" },
        new[] { "flash", "write", "program", "burn", "upload" },
        new[] { "read", "download", "backup", "dump" },
        new[] { "undo", "revert", "rollback", "redo" },
        new[] { "compare", "diff", "difference", "change", "stock", "base" },
        new[] { "xdf", "definition" },
        new[] { "bin", "file", "rom", "image" },
        new[] { "project", "vehicle", "car", "folder" },
        new[] { "log", "logger", "logging", "record", "recording", "csv", "datalog" },
        new[] { "ve", "volumetric", "efficiency" },
        new[] { "save", "store", "export" },
        new[] { "protected", "unreadable", "encrypted", "locked" },
        new[] { "connect", "adapter", "obdx", "interface", "cable", "device", "connection" },
        new[] { "kernel" },
        new[] { "error", "problem", "fail", "wrong", "broken", "trouble", "nothing" },
        new[] { "table", "grid", "cell", "map" },
        new[] { "note", "comment" },
        new[] { "live", "trace", "realtime" },
        new[] { "shortcut", "key", "keyboard", "hotkey" },
    };

    private readonly List<Indexed> sections = new();

    private sealed record Indexed(HelpSection Section, HashSet<string> TitleStems, Dictionary<string, int> BodyStems, string LowerText);

    /// <summary>Add a page: it is split into sections at its h1, h2 and h3 headings.</summary>
    public void AddPage(string topic, string topicTitle, string html)
    {
        html = Regex.Replace(html, "<script.*?</script>|<style.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        MatchCollection headings = Regex.Matches(html, @"<h([1-4])\b([^>]*)>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        List<Match> starts = headings.Where(h => h.Groups[1].Value != "4").ToList();
        for (int i = 0; i < starts.Count; i++)
        {
            Match heading = starts[i];
            int bodyStart = heading.Index + heading.Length;
            int bodyEnd = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
            string title = PlainText(heading.Groups[3].Value);
            Match id = Regex.Match(heading.Groups[2].Value, "id\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            string text = PlainText(html.Substring(bodyStart, bodyEnd - bodyStart));
            if (title.Length == 0 || (text.Length == 0 && heading.Groups[1].Value != "1"))
            {
                continue;
            }

            HelpSection section = new(topic, topicTitle, title, id.Success ? id.Groups[1].Value : null, text);
            Dictionary<string, int> body = new();
            foreach (string word in Words(text))
            {
                string stem = Stem(word);
                body[stem] = body.GetValueOrDefault(stem) + 1;
            }

            this.sections.Add(new Indexed(section, Words(title).Select(Stem).ToHashSet(), body, text.ToLowerInvariant()));
        }
    }

    public int SectionCount => this.sections.Count;

    public IReadOnlyList<HelpHit> Search(string query, int max = 12)
    {
        List<string> words = Words(query).ToList();
        List<string> meaningful = words.Where(w => !StopWords.Contains(w)).ToList();
        if (meaningful.Count == 0)
        {
            meaningful = words;
        }

        if (meaningful.Count == 0)
        {
            return Array.Empty<HelpHit>();
        }

        List<(string Stem, string[] Alternatives)> terms = meaningful
            .Select(w => (Stem(w), SynonymsOf(w).Select(Stem).Distinct().ToArray()))
            .DistinctBy(t => t.Item1)
            .ToList();

        // First, how well does each term match each section (and in how many sections does it appear at all)?
        double[,] strength = new double[this.sections.Count, terms.Count];
        bool[,] inTitleOf = new bool[this.sections.Count, terms.Count];
        int[] sectionsWithTerm = new int[terms.Count];
        for (int s = 0; s < this.sections.Count; s++)
        {
            Indexed entry = this.sections[s];
            for (int t = 0; t < terms.Count; t++)
            {
                double best = 0;
                foreach (string candidate in terms[t].Alternatives.Prepend(terms[t].Stem))
                {
                    double weight = candidate == terms[t].Stem ? 1.0 : 0.6;
                    if (entry.TitleStems.Any(x => SameWord(x, candidate)))
                    {
                        best = Math.Max(best, 8 * weight);
                        inTitleOf[s, t] = true;
                    }

                    int count = entry.BodyStems.Where(b => SameWord(b.Key, candidate)).Sum(b => b.Value);
                    if (count > 0)
                    {
                        best = Math.Max(best, Math.Min(count, 4) * weight);
                    }
                }

                strength[s, t] = best;
                if (best > 0)
                {
                    sectionsWithTerm[t]++;
                }
            }
        }

        // A word found in nearly every section ("change", "table") says little; a rare one ("undo") says a lot.
        double[] rarity = sectionsWithTerm
            .Select(df => Math.Log((this.sections.Count + 1.0) / (df + 0.5)))
            .ToArray();

        List<HelpHit> hits = new();
        for (int s = 0; s < this.sections.Count; s++)
        {
            double score = 0;
            int matched = 0;
            int inTitle = 0;
            for (int t = 0; t < terms.Count; t++)
            {
                if (strength[s, t] > 0)
                {
                    matched++;
                    score += strength[s, t] * rarity[t];
                    if (inTitleOf[s, t])
                    {
                        inTitle++;
                    }
                }
            }

            // A question with several words should find sections that contain most of them.
            if (matched == 0 || (terms.Count >= 3 && matched * 2 < terms.Count))
            {
                continue;
            }

            score *= (double)matched / terms.Count + 0.25;
            if (inTitle == terms.Count)
            {
                score += 6;
            }

            hits.Add(new HelpHit(this.sections[s].Section, score, Snippet(this.sections[s], meaningful)));
        }

        return hits.OrderByDescending(h => h.Score).Take(max).ToList();
    }

    private static string Snippet(Indexed entry, List<string> words)
    {
        int at = -1;
        foreach (string word in words)
        {
            string root = Stem(word);
            int found = root.Length >= 3 ? entry.LowerText.IndexOf(root, StringComparison.Ordinal) : -1;
            if (found >= 0 && (at < 0 || found < at))
            {
                at = found;
            }
        }

        string text = entry.Section.Text;
        if (at < 0)
        {
            return text.Length > 140 ? text.Substring(0, 140).TrimEnd() + "..." : text;
        }

        int start = Math.Max(0, at - 50);
        int length = Math.Min(text.Length - start, 150);
        string piece = text.Substring(start, length).Trim();
        return (start > 0 ? "..." : string.Empty) + piece + (start + length < text.Length ? "..." : string.Empty);
    }

    private static IEnumerable<string> SynonymsOf(string word)
    {
        string stem = Stem(word);
        foreach (string[] group in SynonymGroups)
        {
            if (group.Any(g => Stem(g) == stem))
            {
                return group;
            }
        }

        return Array.Empty<string>();
    }

    // Two words match when their stems are equal, or one begins the other (both at least 4 letters: "calibrat" / "calibration").
    private static bool SameWord(string a, string b) =>
        a == b || (a.Length >= 4 && b.Length >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)));

    private static string Stem(string word)
    {
        word = word.ToLowerInvariant();
        if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            word = word.Substring(0, word.Length - 3);
        }
        else if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal))
        {
            word = word.Substring(0, word.Length - 2);
        }
        else if (word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal))
        {
            word = word.Substring(0, word.Length - 2);
        }
        else if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
        {
            word = word.Substring(0, word.Length - 1);
        }

        // "save", "saves", "saving" and "saved" should all end up the same.
        return word.Length > 3 && word.EndsWith('e') ? word.Substring(0, word.Length - 1) : word;
    }

    private static IEnumerable<string> Words(string text) =>
        Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+(?:-[a-z0-9]+)*").Select(m => m.Value);

    private static string PlainText(string html)
    {
        string text = Regex.Replace(html, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
