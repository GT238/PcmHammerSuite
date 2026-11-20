using System;
using System.IO;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// Where the Tuner keeps its settings and projects. The two environment variables exist so that test programs can
    /// point at a scratch folder instead of the real one.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Settings and pin/tag data (PCMSUITE_DATA overrides).</summary>
        public static string DataFolder =>
            Environment.GetEnvironmentVariable("PCMSUITE_DATA") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcmSuite");

        /// <summary>Where LS-Studio kept its settings and notes (PCMSUITE_LSSTUDIO overrides).</summary>
        public static string LsStudioDataFolder =>
            Environment.GetEnvironmentVariable("PCMSUITE_LSSTUDIO") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LSStudio");

        /// <summary>The folder that holds "PcmSuite Projects" (PCMSUITE_DOCS overrides; default is Documents).</summary>
        public static string DocumentsFolder =>
            Environment.GetEnvironmentVariable("PCMSUITE_DOCS") is { Length: > 0 } custom
                ? custom
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
