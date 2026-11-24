using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// Leaves a file behind when the app dies from an unhandled exception, so an early tester has something to send.
    /// Files go in the "crashes" folder of the app's data folder; the newest few are kept.
    /// </summary>
    public static class CrashLog
    {
        private const int KeepNewest = 20;

        public static string Folder => Path.Combine(AppPaths.DataFolder, "crashes");

        /// <summary>Start recording unhandled exceptions. Call once, as early as possible.</summary>
        public static void Install(string appName)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Write(appName, e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown error"),
                    e.IsTerminating ? "unhandled exception (the app closed)" : "unhandled exception");

            TaskScheduler.UnobservedTaskException += (_, e) => Write(appName, e.Exception, "task exception nobody was waiting for");
        }

        /// <summary>Write one crash file. Never throws: it runs while the app is already failing. Returns the path, or null if it could not be written.</summary>
        public static string? Write(string appName, Exception exception, string kind)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"crash_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");

                StringBuilder text = new();
                text.AppendLine($"{appName} crash log, {DateTime.Now:u}");
                text.AppendLine($"What happened: {kind}");
                text.AppendLine();
                text.Append(BuildInfo.Describe(appName));
                text.AppendLine();
                text.AppendLine(exception.ToString());
                File.WriteAllText(path, text.ToString());

                Trim();
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The crash files, newest first.</summary>
        public static string[] Files()
        {
            try
            {
                return Directory.Exists(Folder)
                    ? Directory.GetFiles(Folder, "crash_*.log").OrderByDescending(f => f, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static void Trim()
        {
            foreach (string old in Files().Skip(KeepNewest))
            {
                try { File.Delete(old); } catch (Exception) { }
            }
        }
    }
}
