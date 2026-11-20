using System.Text.Json;

namespace PcmSuite.Tune;

/// <summary>
/// Pins, tags and notes for tuning parameters, kept in one small JSON file in the app-data folder.
/// Pins and tags belong to a table name (they apply to every BIN); notes belong to one BIN file.
/// </summary>
public sealed class TuneNotesStore
{
    public static IReadOnlyList<string> AllTags { get; } = new[] { "(none)", "Done", "Needs Work", "WOT", "Review", "Skip" };

    private readonly string path;
    private Data data = new();

    private readonly bool keyByFileName;

    /// <param name="path">The JSON file (default: the app-data file).</param>
    /// <param name="keyByFileName">Key notes by the BIN's file name, so a project folder can be moved or copied with its notes.</param>
    public TuneNotesStore(string? path = null, bool keyByFileName = false)
    {
        this.keyByFileName = keyByFileName;
        this.path = path ?? Path.Combine(PcmSuite.Tuning.Core.Models.AppPaths.DataFolder, "tuning-meta.json");
        this.Load();
    }

    public IReadOnlyCollection<string> PinnedNames => this.data.Pins;

    public bool IsPinned(string table) => this.data.Pins.Contains(table);

    public void SetPinned(string table, bool pinned)
    {
        if (pinned)
        {
            this.data.Pins.Add(table);
        }
        else
        {
            this.data.Pins.Remove(table);
        }

        this.Save();
    }

    public string GetTag(string table) => this.data.Tags.TryGetValue(table, out string? tag) ? tag : string.Empty;

    public void SetTag(string table, string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag == "(none)")
        {
            this.data.Tags.Remove(table);
        }
        else
        {
            this.data.Tags[table] = tag;
        }

        this.Save();
    }

    private string? Key(string? bin) => bin != null && this.keyByFileName ? Path.GetFileName(bin) : bin;

    public string GetNote(string? bin, string table) =>
        Key(bin) is { } key && this.data.Notes.TryGetValue(key, out Dictionary<string, string>? notes)
        && notes.TryGetValue(table, out string? text)
            ? text
            : string.Empty;

    public void SetNote(string? binPath, string table, string text)
    {
        if (Key(binPath) is not { } bin)
        {
            return;
        }

        if (!this.data.Notes.TryGetValue(bin, out Dictionary<string, string>? notes))
        {
            notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.data.Notes[bin] = notes;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            notes.Remove(table);
        }
        else
        {
            notes[table] = text.Trim();
        }

        if (notes.Count == 0)
        {
            this.data.Notes.Remove(bin);
        }

        this.Save();
    }

    /// <summary>Every note written for one BIN, by table name, in name order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> NotesFor(string? bin) =>
        Key(bin) is { } key && this.data.Notes.TryGetValue(key, out Dictionary<string, string>? notes)
            ? notes.OrderBy(n => n.Key, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<KeyValuePair<string, string>>();

    /// <summary>Notes follow a BIN that is saved under a new name.</summary>
    public void CopyNotes(string fromPath, string toPath)
    {
        string fromBin = Key(fromPath)!;
        string toBin = Key(toPath)!;
        if (string.Equals(fromBin, toBin, StringComparison.OrdinalIgnoreCase)
            || !this.data.Notes.TryGetValue(fromBin, out Dictionary<string, string>? notes))
        {
            return;
        }

        this.data.Notes[toBin] = new Dictionary<string, string>(notes, StringComparer.OrdinalIgnoreCase);
        this.Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(this.path))
            {
                Data? loaded = JsonSerializer.Deserialize<Data>(File.ReadAllText(this.path));
                if (loaded != null)
                {
                    this.data = new Data
                    {
                        Pins = new HashSet<string>(loaded.Pins ?? new(), StringComparer.OrdinalIgnoreCase),
                        Tags = new Dictionary<string, string>(loaded.Tags ?? new(), StringComparer.OrdinalIgnoreCase),
                        Notes = new Dictionary<string, Dictionary<string, string>>(
                            (loaded.Notes ?? new()).ToDictionary(
                                n => n.Key,
                                n => new Dictionary<string, string>(n.Value, StringComparer.OrdinalIgnoreCase)),
                            StringComparer.OrdinalIgnoreCase),
                    };
                }
            }
        }
        catch
        {
            // A damaged file just means starting empty.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            File.WriteAllText(this.path, JsonSerializer.Serialize(this.data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best effort; the in-memory copy still works for this session.
        }
    }

    private sealed class Data
    {
        public HashSet<string> Pins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, string>> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
