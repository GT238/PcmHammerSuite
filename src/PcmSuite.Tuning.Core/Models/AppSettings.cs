using System;
using System.IO;
using System.Text.Json;

namespace PcmSuite.Tuning.Core.Models
{
    public class AppSettings
    {
        public string DefaultPort      { get; set; } = "COM3";
        public string DefaultBinFolder { get; set; } = "";

        /// <summary>The BIN files opened most recently, newest first.</summary>
        public System.Collections.Generic.List<string> RecentBins { get; set; } = new();

        /// <summary>The project folder that was open when the app last closed (opened again at the next start).</summary>
        public string LastProject { get; set; } = "";

        private static string FilePath =>
            Path.Combine(AppPaths.DataFolder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                           ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
