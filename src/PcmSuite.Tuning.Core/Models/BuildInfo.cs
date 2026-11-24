using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// Which build is running: the version and the git commits of PcmSuite and of the PCM Hammer sources it contains.
    /// The app project stamps these into its assembly (build/BuildInfo.targets); anything else reads "unknown".
    /// </summary>
    public static class BuildInfo
    {
        private static string Meta(string key) =>
            Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value ? value : "unknown";

        public static string Version => Meta("PcmSuiteVersion");

        /// <summary>The PcmSuite commit; "+changes" means the build had uncommitted edits.</summary>
        public static string SuiteCommit => Meta("PcmSuiteCommit");

        /// <summary>The PCM Hammer commit this build contains.</summary>
        public static string HammerCommit => Meta("PcmHammerCommit");

        /// <summary>The details a bug report needs, as plain text.</summary>
        public static string Describe(string appName)
        {
            StringBuilder text = new();
            text.AppendLine($"{appName} {Version}");
            text.AppendLine($"PcmSuite commit: {SuiteCommit}");
            text.AppendLine($"PCM Hammer commit: {HammerCommit}");
            text.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
            text.AppendLine($"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            return text.ToString();
        }
    }
}
