using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PcmSuite.Tune;

/// <summary>A logging profile file (PCM Hammer's .LogProfile format) offered for a tuning task.</summary>
public sealed class LogProfileInfo
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    /// <summary>What the profile is for, taken from the comment at the top of the file.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The logged parameters, as readable names.</summary>
    public string Parameters { get; init; } = string.Empty;

    public int Count { get; init; }

    public string CountText => this.Count + " parameters";
}

/// <summary>
/// The .LogProfile files shared by the Logger and the Tune tab: one folder that both read from and save to.
/// </summary>
public static class LogProfileCatalog
{
    /// <summary>The shared folder, in the user's Documents.</summary>
    public static string SharedFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcmSuite", "Profiles");

    /// <summary>
    /// Copies the profiles that ship with PcmSuite into the shared folder. A file that is already there is never
    /// overwritten, so the user's edits to a shipped profile survive an update.
    /// </summary>
    public static void Seed(string bundledFolder, string sharedFolder)
    {
        try
        {
            Directory.CreateDirectory(sharedFolder);
            if (!Directory.Exists(bundledFolder))
            {
                return;
            }

            foreach (string source in Directory.GetFiles(bundledFolder, "*.LogProfile"))
            {
                string target = System.IO.Path.Combine(sharedFolder, System.IO.Path.GetFileName(source));
                if (!File.Exists(target))
                {
                    File.Copy(source, target);
                }
            }
        }
        catch
        {
            // No shared folder (read-only Documents, for example): the page then shows an empty list and says so.
        }
    }

    /// <summary>Copies a profile file into the shared folder and returns its new path.</summary>
    public static string Import(string file, string sharedFolder)
    {
        Directory.CreateDirectory(sharedFolder);
        string target = System.IO.Path.Combine(sharedFolder, System.IO.Path.GetFileName(file));
        if (!string.Equals(System.IO.Path.GetFullPath(file), System.IO.Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(file, target, overwrite: true);
        }

        return target;
    }

    public static IReadOnlyList<LogProfileInfo> Load(string? folder)
    {
        List<LogProfileInfo> result = new();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return result;
        }

        foreach (string path in Directory.GetFiles(folder, "*.LogProfile").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                XDocument xml = XDocument.Load(path);
                string description = xml.Root?.Nodes().OfType<XComment>().FirstOrDefault()?.Value.Trim() ?? string.Empty;
                List<string> ids = xml.Descendants()
                    .Where(e => e.Name.LocalName.EndsWith("Parameter", StringComparison.Ordinal) && e.Attribute("id") != null)
                    .Select(e => Readable(e.Attribute("id")!.Value))
                    .ToList();

                result.Add(new LogProfileInfo
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Description = description,
                    Parameters = string.Join(", ", ids),
                    Count = ids.Count,
                });
            }
            catch
            {
                // A file that is not valid XML is left out of the list.
            }
        }

        return result;
    }

    /// <summary>"ShortTermFTLeftBank" becomes "Short Term FT Left Bank".</summary>
    private static string Readable(string id) =>
        Regex.Replace(Regex.Replace(id, "([a-z0-9])([A-Z])", "$1 $2"), "([A-Z]+)([A-Z][a-z])", "$1 $2");
}
