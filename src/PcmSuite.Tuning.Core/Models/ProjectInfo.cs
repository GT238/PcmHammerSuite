using System;
using System.IO;
using System.Text.Json.Serialization;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// Persisted metadata for a vehicle tuning project.
    /// Stored as project.json inside the project folder.
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

        /// <summary>File name (not full path) of the last BIN opened in this project.</summary>
        public string? LastBinName { get; set; }

        public DateTime Created  { get; set; } = DateTime.Now;
        public DateTime Modified { get; set; } = DateTime.Now;

        // ── Runtime-only (not persisted) ──────────────────────────────
        [JsonIgnore]
        public string ProjectFolder { get; set; } = "";

        [JsonIgnore]
        public string ProjectFile => Path.Combine(ProjectFolder, "project.json");

        [JsonIgnore]
        public string ReadsFolder => Path.Combine(ProjectFolder, "reads");

        [JsonIgnore]
        public string EditsFolder => Path.Combine(ProjectFolder, "edits");

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

        // ── Static helpers ────────────────────────────────────────────
        public static string DefaultProjectsRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "LSStudio Projects");

        /// <summary>Shared XDF library folder — one copy of each XDF, referenced by all projects.</summary>
        public static string XdfLibraryFolder =>
            Path.Combine(DefaultProjectsRoot, "XDFs");

        /// <summary>Derives a safe folder name from the project display name.</summary>
        public static string SafeFolderName(string displayName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                displayName = displayName.Replace(c, '_');
            return displayName.Trim().Replace(' ', '_');
        }
    }
}
