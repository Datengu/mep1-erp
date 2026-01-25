using Mep1.Erp.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Mep1.Erp.Desktop
{
    public static class SettingsService
    {
        private const string TemplateFileName = "settings.template.json";

        private static void EnsureSettingsSeeded()
        {
            var destPath = AppSettingsHelper.GetConfigPath();
            if (File.Exists(destPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            var templatePath = Path.Combine(AppContext.BaseDirectory, TemplateFileName);

            if (File.Exists(templatePath))
            {
                File.Copy(templatePath, destPath);
                return;
            }

            // Fallback if template missing
            var fallback = new AppSettings
            {
                ApiBaseUrl = "https://portal.mep1bim.co.uk/"
            };
            SaveSettings(fallback);
        }

        public static AppSettings LoadSettings()
        {
            EnsureSettingsSeeded();

            var path = AppSettingsHelper.GetConfigPath();

            //MessageBox.Show("SETTINGS PATH:\n" + path);

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
