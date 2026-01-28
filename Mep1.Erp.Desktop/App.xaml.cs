using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace Mep1.Erp.Desktop
{
    public partial class App : WpfApplication
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Global exception handlers (best-effort)
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            base.OnStartup(e);

            // Manual startup (instead of StartupUri)
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                WriteCrashLog("DispatcherUnhandledException", e.Exception);
                ShowCrashMessage(e.Exception);
            }
            catch
            {
                // swallow secondary failures
            }
            finally
            {
                e.Handled = true;
                Shutdown(-1);
            }
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception ?? new Exception("Non-Exception thrown: " + (e.ExceptionObject?.ToString() ?? "<null>"));
                WriteCrashLog("AppDomain.UnhandledException", ex);
                ShowCrashMessage(ex);
            }
            catch
            {
                // swallow secondary failures
            }
            finally
            {
                // If we're here, we're already in “we might be dying” territory
                Shutdown(-1);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
                // Usually don't need to pop UI for this, but it can be useful during live test.
                // Comment out next line if it becomes noisy.
                ShowCrashMessage(e.Exception);
            }
            catch
            {
                // swallow secondary failures
            }
            finally
            {
                e.SetObserved();
            }
        }

        private static void ShowCrashMessage(Exception ex)
        {
            WpfMessageBox.Show(
                "The ERP desktop app hit an unexpected error and must close.\n\n" +
                "A crash log has been written to:\n" +
                GetCrashLogDirectory() + "\n\n" +
                "Error:\n" + ex.Message,
                "MEP1BIM ERP - Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void WriteCrashLog(string source, Exception ex)
        {
            var dir = GetCrashLogDirectory();
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, $"crash-{DateTime.UtcNow:yyyy-MM-dd}.log");

            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine($"UTC: {DateTime.UtcNow:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Machine: {Environment.MachineName}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"64-bit Proc: {Environment.Is64BitProcess}");
            sb.AppendLine();

            sb.AppendLine(ex.ToString());
            sb.AppendLine();

            File.AppendAllText(file, sb.ToString());
        }

        private static string GetCrashLogDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MEP1BIM ERP",
                "Logs");
        }
    }
}
