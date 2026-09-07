using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using AICShared;

namespace AICTrainer.Services
{
    public class TrainerSettings
    {
        public bool DoNotShowDisclaimer { get; set; } = false;
        public List<string> Favorites { get; set; } = new();
        public bool RememberConfig { get; set; } = false;
        public ModConfigDto? SavedConfig { get; set; }

        private static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AICTrainer");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return Path.Combine(dir, "settings.json");
            }
        }

        public static TrainerSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<TrainerSettings>(json) ?? new TrainerSettings();
                }
            }
            catch { }
            return new TrainerSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
