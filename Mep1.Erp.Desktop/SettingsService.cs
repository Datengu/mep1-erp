using Mep1.Erp.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Mep1.Erp.Desktop
{
    public static class SettingsService
    {
        private const string TemplateProdFileName = "settings.template.prod.json";
        private const string TemplateStagingFileName = "settings.template.staging.json";

        private static string PickTemplateFileName()
        {
            // Optional override (useful for CI or rare manual cases)
            var env = (Environment.GetEnvironmentVariable("MEP1_ERP_ENV") ?? "").Trim().ToLowerInvariant();
            if (env == "prod" || env == "production") return TemplateProdFileName;
            if (env == "staging" || env == "stage") return TemplateStagingFileName;

#if DEBUG
            return TemplateStagingFileName;
#else
            return TemplateProdFileName;
#endif
        }

        private static void EnsureSettingsSeeded()
        {
            var destPath = AppSettingsHelper.GetConfigPath();
            if (File.Exists(destPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            // Choose template based on build config (and optional env var)
            var templateFileName = PickTemplateFileName();
            var templatePath = Path.Combine(AppContext.BaseDirectory, templateFileName);

            if (File.Exists(templatePath))
            {
                File.Copy(templatePath, destPath);
                return;
            }

            // Fallback if template missing
#if DEBUG
            var fallbackUrl = "https://staging-portal.mep1bim.co.uk/";
#else
            var fallbackUrl = "https://portal.mep1bim.co.uk/";
#endif

            var fallback = new AppSettings
            {
                ApiBaseUrl = fallbackUrl
            };
            SaveSettings(fallback);
        }

        public static AppSettings LoadSettings()
        {
            EnsureSettingsSeeded();

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
