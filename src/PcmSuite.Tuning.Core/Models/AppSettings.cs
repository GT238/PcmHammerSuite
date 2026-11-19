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

        private static string FilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PcmSuite", "settings.json");

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
