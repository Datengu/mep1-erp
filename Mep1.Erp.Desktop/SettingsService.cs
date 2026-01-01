using System.IO;
using System.Text.Json;
using Mep1.Erp.Core;

namespace Mep1.Erp.Desktop
{
    public static class SettingsService
    {
        public static AppSettings LoadSettings()
        {
            var path = AppSettingsHelper.GetConfigPath();

            if (!File.Exists(path))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch
            {
                // If file is corrupted, just fall back to defaults
                return new AppSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            var path = AppSettingsHelper.GetConfigPath();
            var options = new JsonSerializerOptions { WriteIndented = true };

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, options));
        }
    }
}
