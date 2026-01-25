using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;

namespace Mep1.Erp.Core
{
    public class AppSettings : INotifyPropertyChanged
    {
        public string? ApiBaseUrl { get; set; }
        public string? ErpDbConnectionString { get; set; }

        private string _timesheetFolder = "";
        public string TimesheetFolder
        {
            get => _timesheetFolder;
            set { _timesheetFolder = value; OnPropertyChanged(); }
        }

        private string _financeSheetPath = "";
        public string FinanceSheetPath
        {
            get => _financeSheetPath;
            set { _financeSheetPath = value; OnPropertyChanged(); }
        }

        private string _jobSourcePath = "";
        public string JobSourcePath
        {
            get => _jobSourcePath;
            set { _jobSourcePath = value; OnPropertyChanged(); }
        }

        private string _invoiceRegisterPath = "";
        public string InvoiceRegisterPath
        {
            get => _invoiceRegisterPath;
            set { _invoiceRegisterPath = value; OnPropertyChanged(); }
        }

        private string _applicationSchedulePath = "";
        public string ApplicationSchedulePath
        {
            get => _applicationSchedulePath;
            set { _applicationSchedulePath = value; OnPropertyChanged(); }
        }

        private int _upcomingApplicationsDaysAhead = 30;
        public int UpcomingApplicationsDaysAhead
        {
            get => _upcomingApplicationsDaysAhead;
            set { _upcomingApplicationsDaysAhead = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class AppSettingsHelper
    {
        public static string GetConfigPath()
        {
            // Installed/MSIX-safe per-user location
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MEP1 BIM LTD",
                "Mep1Erp"
            );

            Directory.CreateDirectory(baseDir);
            return Path.Combine(baseDir, "settings.json");
        }
    }
}
