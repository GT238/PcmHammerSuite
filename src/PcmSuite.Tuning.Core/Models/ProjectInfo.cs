using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// A vehicle tuning project: one folder holding everything about that vehicle (reads, saved tunes, logs, the
    /// change log, notes). The details are stored as project.json inside the folder.
    /// </summary>
    public class ProjectInfo
    {
        public string  Name    { get; set; } = "";
        public string  Year    { get; set; } = "";
        public string  Make    { get; set; } = "";
        public string  Model   { get; set; } = "";
        public string  Engine  { get; set; } = "";
        public string  Vin     { get; set; } = "";
        public string  Notes   { get; set; } = "";
        /// <summary>File name only (e.g. "P01.xdf") — resolved against XdfLibraryFolder at runtime.</summary>
        public string? XdfFileName { get; set; }

        /// <summary>The last BIN opened in this project, relative to the project folder.</summary>
        public string? LastBinName { get; set; }

        /// <summary>
        /// The untouched starting BIN (a read from the PCM, or the file the project began with), relative to the project
        /// folder. Other BINs are compared against it automatically.
        /// </summary>
        public string? BaseBinName { get; set; }

        /// <summary>True once notes from the old LS-Studio / app-wide stores were moved into this project (done once).</summary>
        public bool NotesImported { get; set; }

        public DateTime Created  { get; set; } = DateTime.Now;
        public DateTime Modified { get; set; } = DateTime.Now;

        // ── Runtime-only (not persisted) ──────────────────────────────
        [JsonIgnore]
        public string ProjectFolder { get; set; } = "";

        [JsonIgnore]
        public string ProjectFile => Path.Combine(ProjectFolder, "project.json");

        /// <summary>BINs read from the PCM (the Flash tab saves its reads wherever you point it; point it here).</summary>
        [JsonIgnore]
        public string ReadsFolder => Path.Combine(ProjectFolder, "reads");

        /// <summary>BINs you saved from the Tuner.</summary>
        [JsonIgnore]
        public string EditsFolder => Path.Combine(ProjectFolder, "edits");

        /// <summary>Logger recordings (CSV) for this vehicle.</summary>
        [JsonIgnore]
        public string LogsFolder => Path.Combine(ProjectFolder, "logs");

        [JsonIgnore]
        public string ChangeLogFile => Path.Combine(ProjectFolder, "changes.log");

        [JsonIgnore]
        public string NotesFile => Path.Combine(ProjectFolder, "notes.json");

        /// <summary>One line describing the vehicle, for lists.</summary>
        [JsonIgnore]
        public string Summary
        {
            get
            {
                string vehicle = $"{Year} {Make} {Model} {Engine}".Replace("  ", " ").Trim();
                return vehicle.Length > 0 ? vehicle : "(no vehicle details)";
            }
        }

        /// <summary>Human-readable title used in the UI.</summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Name;
                string parts = $"{Year} {Make} {Model}".Trim();
                return string.IsNullOrWhiteSpace(parts) ? "(unnamed project)" : parts;
            }
        }

        /// <summary>The BIN last opened, as a full path (null when none, or it no longer exists).</summary>
        [JsonIgnore]
        public string? LastBinPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LastBinName)) return null;
                string path = Path.Combine(ProjectFolder, LastBinName);
                return File.Exists(path) ? path : null;
            }
        }

        [JsonIgnore]
        public string? BaseBinPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(BaseBinName)) return null;
                string path = Path.Combine(ProjectFolder, BaseBinName);
                return File.Exists(path) ? path : null;
            }
        }

        /// <summary>
        /// The BIN to open when the project is opened: the one used last, otherwise the newest saved tune, otherwise the
        /// newest read, otherwise any BIN lying in the project folder itself. Null for an empty project.
        /// </summary>
        public string? FindBestBin()
        {
            if (LastBinPath is { } last) return last;
            foreach (string folder in new[] { EditsFolder, ReadsFolder, ProjectFolder })
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    string? newest = Directory.GetFiles(folder, "*.bin")
                        .OrderByDescending(File.GetLastWriteTime)
                        .FirstOrDefault();
                    if (newest != null) return newest;
                }
                catch
                {
                    // Try the next folder.
                }
            }

            return null;
        }

        /// <summary>Copy a file into a folder without overwriting anything (adds _2, _3 ... when the name is taken).</summary>
        public static string CopyIn(string source, string folder)
        {
            Directory.CreateDirectory(folder);
            string name = Path.GetFileNameWithoutExtension(source);
            string ext = Path.GetExtension(source);
            string dest = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(dest); n++)
            {
                dest = Path.Combine(folder, $"{name}_{n}{ext}");
            }

            File.Copy(source, dest, false);
            return dest;
        }

        /// <summary>Create the project's folders (safe to call again).</summary>
        public void EnsureFolders()
        {
            Directory.CreateDirectory(ProjectFolder);
            Directory.CreateDirectory(ReadsFolder);
            Directory.CreateDirectory(EditsFolder);
            Directory.CreateDirectory(LogsFolder);
        }

        public void Save()
        {
            Modified = DateTime.Now;
            Directory.CreateDirectory(ProjectFolder);
            File.WriteAllText(ProjectFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static ProjectInfo? Load(string folder)
        {
            try
            {
                string file = Path.Combine(folder, "project.json");
                if (!File.Exists(file)) return null;
                ProjectInfo? info = JsonSerializer.Deserialize<ProjectInfo>(File.ReadAllText(file));
                if (info == null) return null;
                info.ProjectFolder = folder;
                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The project whose folder holds this file (looking up a few levels), or null.</summary>
        public static ProjectInfo? FindContaining(string filePath)
        {
            try
            {
                string? folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
                for (int level = 0; level < 5 && !string.IsNullOrEmpty(folder); level++)
                {
                    if (File.Exists(Path.Combine(folder, "project.json")))
                    {
                        return Load(folder);
                    }

                    folder = Path.GetDirectoryName(folder);
                }
            }
            catch
            {
                // Not inside a project.
            }

            return null;
        }

        /// <summary>Every project in the projects folder, newest activity first.</summary>
        public static IReadOnlyList<ProjectInfo> ListProjects()
        {
            List<ProjectInfo> found = new();
            try
            {
                if (Directory.Exists(DefaultProjectsRoot))
                {
                    foreach (string folder in Directory.GetDirectories(DefaultProjectsRoot))
                    {
                        ProjectInfo? info = Load(folder);
                        if (info != null) found.Add(info);
                    }
                }
            }
            catch
            {
                // An unreadable folder just means a shorter list.
            }

            return found.OrderByDescending(p => p.Modified).ToList();
        }

        // ── Static helpers ────────────────────────────────────────────
        public static string DefaultProjectsRoot =>
            Path.Combine(AppPaths.DocumentsFolder, "PcmSuite Projects");

        /// <summary>The folder LS-Studio used. Its projects and XDFs are copied to the new root once.</summary>
        public static string LegacyProjectsRoot =>
            Path.Combine(AppPaths.DocumentsFolder, "LSStudio Projects");

        /// <summary>Shared XDF library folder — one copy of each XDF, referenced by all projects.</summary>
        public static string XdfLibraryFolder =>
            Path.Combine(DefaultProjectsRoot, "XDFs");

        /// <summary>Shared folder of reference BINs (factory files and others), for comparing against. See <see cref="StockBins"/>.</summary>
        public static string StockBinsFolder =>
            Path.Combine(DefaultProjectsRoot, "Reference BINs");

        /// <summary>
        /// First run after the rename: copy the old LS-Studio folder to the new one. The old folder is left alone, so
        /// LS-Studio keeps working. Returns true when something was copied.
        /// </summary>
        public static bool MigrateLegacyRoot()
        {
            // Copy into a staging folder and rename it when the copy is complete. A copy that stops half way (a locked file, a full disk) then
            // leaves nothing at the real path, so the next start tries again, instead of finding a half-filled folder and thinking it is done.
            string staging = DefaultProjectsRoot + ".migrating";
            try
            {
                if (Directory.Exists(DefaultProjectsRoot) || !Directory.Exists(LegacyProjectsRoot))
                {
                    return false;
                }

                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }

                CopyFolder(LegacyProjectsRoot, staging);
                Directory.Move(staging, DefaultProjectsRoot);
                return true;
            }
            catch
            {
                try
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, true);
                    }
                }
                catch
                {
                    // Left behind; the next attempt removes it first.
                }

                return false;
            }
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
            }

            foreach (string folder in Directory.GetDirectories(from))
            {
                CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
        }

        /// <summary>Derives a safe folder name from the project display name.</summary>
        public static string SafeFolderName(string displayName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                displayName = displayName.Replace(c, '_');
            return displayName.Trim().Replace(' ', '_');
        }
    }
}
