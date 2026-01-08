using Mep1.Erp.Core;
using Mep1.Erp.Application;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using static Mep1.Erp.Application.Reporting;
using Binding = System.Windows.Data.Binding;
using WpfMessageBox = System.Windows.MessageBox;

namespace Mep1.Erp.Desktop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // ---------------------------------------------
        // Bound properties
        // ---------------------------------------------

        private AppSettings _settings = new();
        public AppSettings Settings
        {
            get => _settings;
            set => SetField(ref _settings, value, nameof(Settings));
        }

        private DashboardSummaryDto _dashboard = null!;
        public DashboardSummaryDto Dashboard
        {
            get => _dashboard;
            set => SetField(ref _dashboard, value, nameof(Dashboard));
        }

        private List<ProjectSummaryDto> _projectSummaries = new();
        public List<ProjectSummaryDto> ProjectSummaries
        {
            get => _projectSummaries;
            set => SetField(ref _projectSummaries, value, nameof(ProjectSummaries));
        }

        private ProjectSummaryDto? _selectedProject;
        public ProjectSummaryDto? SelectedProject
        {
            get => _selectedProject;
            set => SetField(ref _selectedProject, value, nameof(SelectedProject));
        }

        private List<ProjectLabourByPersonRow> _selectedProjectLabourThisMonth = new();
        public List<ProjectLabourByPersonRow> SelectedProjectLabourThisMonth
        {
            get => _selectedProjectLabourThisMonth;
            set => SetField(ref _selectedProjectLabourThisMonth, value, nameof(SelectedProjectLabourThisMonth));
        }

        private List<ProjectLabourByPersonRow> _selectedProjectLabourAllTime = new();
        public List<ProjectLabourByPersonRow> SelectedProjectLabourAllTime
        {
            get => _selectedProjectLabourAllTime;
            set => SetField(ref _selectedProjectLabourAllTime, value, nameof(SelectedProjectLabourAllTime));
        }

        private List<ProjectRecentEntryRow> _selectedProjectRecentEntries = new();
        public List<ProjectRecentEntryRow> SelectedProjectRecentEntries
        {
            get => _selectedProjectRecentEntries;
            set => SetField(ref _selectedProjectRecentEntries, value, nameof(SelectedProjectRecentEntries));
        }

        private List<ProjectInvoiceRow> _selectedProjectInvoices = new();
        public List<ProjectInvoiceRow> SelectedProjectInvoices
        {
            get => _selectedProjectInvoices;
            set => SetField(ref _selectedProjectInvoices, value, nameof(SelectedProjectInvoices));
        }

        private ICollectionView _projectView = null!;
        public ICollectionView ProjectView
        {
            get => _projectView;
            set => SetField(ref _projectView, value, nameof(ProjectView));
        }

        private ProjectActiveFilter _projectFilter = ProjectActiveFilter.All;
        public ProjectActiveFilter ProjectFilter
        {
            get => _projectFilter;
            set
            {
                if (SetField(ref _projectFilter, value, nameof(ProjectFilter)))
                {
                    ProjectView?.Refresh();
                }
            }
        }

        private List<PeopleSummaryRowDto> _people = new();
        public List<PeopleSummaryRowDto> People
        {
            get => _people;
            set => SetField(ref _people, value, nameof(People));
        }

        private PeopleSummaryRowDto? _selectedPerson;
        public PeopleSummaryRowDto? SelectedPerson
        {
            get => _selectedPerson;
            set => SetField(ref _selectedPerson, value, nameof(SelectedPerson));
        }

        private List<PersonProjectBreakdownRow> _selectedPersonProjects = new();
        public List<PersonProjectBreakdownRow> SelectedPersonProjects
        {
            get => _selectedPersonProjects;
            set => SetField(ref _selectedPersonProjects, value, nameof(SelectedPersonProjects));
        }

        private List<PersonRecentEntryRow> _selectedPersonRecentEntries = new();
        public List<PersonRecentEntryRow> SelectedPersonRecentEntries
        {
            get => _selectedPersonRecentEntries;
            set => SetField(ref _selectedPersonRecentEntries, value, nameof(SelectedPersonRecentEntries));
        }

        private List<WorkerRate> _selectedPersonRates = new();
        public List<WorkerRate> SelectedPersonRates
        {
            get => _selectedPersonRates;
            set => SetField(ref _selectedPersonRates, value, nameof(SelectedPersonRates));
        }

        private ICollectionView _peopleView = null!;
        public ICollectionView PeopleView
        {
            get => _peopleView;
            set => SetField(ref _peopleView, value, nameof(PeopleView));
        }

        public enum PeopleActiveFilter
        {
            All = 0,
            ActiveOnly = 1,
            InactiveOnly = 2
        }

        private PeopleActiveFilter _peopleFilter = PeopleActiveFilter.All;
        public PeopleActiveFilter PeopleFilter
        {
            get => _peopleFilter;
            set
            {
                if (SetField(ref _peopleFilter, value, nameof(PeopleFilter)))
                {
                    PeopleView?.Refresh();
                }
            }
        }

        private PortalAccessDto? _portalAccess;

        public bool HasPortalAccount => _portalAccess?.Exists == true;

        public string PortalAccessStatusText
        {
            get
            {
                if (SelectedPerson == null) return "Select a person.";
                if (_portalAccess == null) return "Loading...";
                if (!_portalAccess.Exists) return "No portal account exists for this worker.";
                return $"Account exists. MustChangePassword: {_portalAccess.MustChangePassword}";
            }
        }

        public string PortalUsernameText { get; set; } = "";
        public bool CanEditPortalUsername => !HasPortalAccount; // v1: only set username at create

        public List<string> PortalRoleOptions { get; } = new() { "Worker", "Admin", "Owner" };

        public string SelectedPortalRole { get; set; } = "Worker";

        public bool PortalIsActive { get; set; } = true;

        public string PortalTempPasswordText { get; set; } = "";

        public bool CanCreatePortalAccount => SelectedPerson != null && !HasPortalAccount;

        private string _newWorkerInitialsText = "";
        public string NewWorkerInitialsText
        {
            get => _newWorkerInitialsText;
            set => SetField(ref _newWorkerInitialsText, value, nameof(NewWorkerInitialsText));
        }

        private string _newWorkerNameText = "";
        public string NewWorkerNameText
        {
            get => _newWorkerNameText;
            set => SetField(ref _newWorkerNameText, value, nameof(NewWorkerNameText));
        }

        private string _newWorkerRateText = "";
        public string NewWorkerRateText
        {
            get => _newWorkerRateText;
            set => SetField(ref _newWorkerRateText, value, nameof(NewWorkerRateText));
        }

        private bool _newWorkerIsActive = true;
        public bool NewWorkerIsActive
        {
            get => _newWorkerIsActive;
            set => SetField(ref _newWorkerIsActive, value, nameof(NewWorkerIsActive));
        }

        private string _addWorkerStatusText = "";
        public string AddWorkerStatusText
        {
            get => _addWorkerStatusText;
            set => SetField(ref _addWorkerStatusText, value, nameof(AddWorkerStatusText));
        }

        private List<InvoiceListEntryDto> _invoices = new();
        public List<InvoiceListEntryDto> Invoices
        {
            get => _invoices;
            set => SetField(ref _invoices, value, nameof(Invoices));
        }

        private ICollectionView _invoiceView = null!;
        public ICollectionView InvoiceView
        {
            get => _invoiceView;
            set => SetField(ref _invoiceView, value, nameof(InvoiceView));
        }

        private List<DueScheduleEntryDto> _dueSchedule = new();
        public List<DueScheduleEntryDto> DueSchedule
        {
            get => _dueSchedule;
            set => SetField(ref _dueSchedule, value, nameof(DueSchedule));
        }

        private List<UpcomingApplicationEntryDto> _upcomingApplications = new();
        public List<UpcomingApplicationEntryDto> UpcomingApplications
        {
            get => _upcomingApplications;
            set => SetField(ref _upcomingApplications, value, nameof(UpcomingApplications));
        }

        // Suppliers tab inputs
        public string? NewSupplierName { get; set; }
        public string? NewSupplierNotes { get; set; }
        public bool NewSupplierIsActive { get; set; } = true;

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetField(ref _selectedSupplier, value, nameof(SelectedSupplier));
        }

        public ObservableCollection<Supplier> Suppliers { get; } = new();
        public ObservableCollection<SupplierCostRow> SelectedProjectSupplierCosts { get; } = new();
        private Supplier? _supplierEdit;
        public Supplier? SupplierEdit
        {
            get => _supplierEdit;
            set => SetField(ref _supplierEdit, value, nameof(SupplierEdit));
        }
        public Supplier? NewSupplierCostSupplier { get; set; }
        private DateTime? _newSupplierCostDate = DateTime.Today;
        public DateTime? NewSupplierCostDate
        {
            get => _newSupplierCostDate;
            set
            {
                if (SetField(ref _newSupplierCostDate, value, nameof(NewSupplierCostDate)))
                {
                    // If user picks a date, ensure "Unknown date" is unticked.
                    if (_newSupplierCostDate.HasValue && NewSupplierCostDateUnknown)
                    {
                        _newSupplierCostDateUnknown = false;
                        OnPropertyChanged(nameof(NewSupplierCostDateUnknown));
                        OnPropertyChanged(nameof(IsSupplierCostDateEnabled));
                    }
                }
            }
        }
        private bool _newSupplierCostDateUnknown;
        public bool NewSupplierCostDateUnknown
        {
            get => _newSupplierCostDateUnknown;
            set
            {
                if (SetField(ref _newSupplierCostDateUnknown, value, nameof(NewSupplierCostDateUnknown)))
                {
                    if (value)
                    {
                        // Unknown date => clear date
                        _newSupplierCostDate = null;
                        OnPropertyChanged(nameof(NewSupplierCostDate));
                    }
                    else
                    {
                        // Switching back to known date => default to today if blank
                        if (_newSupplierCostDate == null)
                        {
                            _newSupplierCostDate = DateTime.Today;
                            OnPropertyChanged(nameof(NewSupplierCostDate));
                        }
                    }

                    OnPropertyChanged(nameof(IsSupplierCostDateEnabled));
                }
            }
        }

        // Used by XAML to disable the DatePicker when Unknown is checked
        public bool IsSupplierCostDateEnabled => !NewSupplierCostDateUnknown;

        public decimal NewSupplierCostAmount { get; set; }

        private string _newSupplierCostAmountText = "";
        public string NewSupplierCostAmountText
        {
            get => _newSupplierCostAmountText;
            set
            {
                if (SetField(ref _newSupplierCostAmountText, value, nameof(NewSupplierCostAmountText)))
                {
                    if (decimal.TryParse(value, out var parsed))
                    {
                        NewSupplierCostAmount = parsed;
                    }
                }
            }
        }

        public string? NewSupplierCostNote { get; set; }

        private SupplierCostRow? _selectedProjectSupplierCost;
        public SupplierCostRow? SelectedProjectSupplierCost
        {
            get => _selectedProjectSupplierCost;
            set => SetField(ref _selectedProjectSupplierCost, value, nameof(SelectedProjectSupplierCost));
        }

        private bool _isEditingSupplierCost;
        public bool IsEditingSupplierCost
        {
            get => _isEditingSupplierCost;
            set => SetField(ref _isEditingSupplierCost, value, nameof(IsEditingSupplierCost));
        }

        private int? _editingSupplierCostId;
        public int? EditingSupplierCostId
        {
            get => _editingSupplierCostId;
            set => SetField(ref _editingSupplierCostId, value, nameof(EditingSupplierCostId));
        }

        private int _projectDrilldownLoadVersion = 0;


        // ---------------------------------------------
        // Invoice filtering
        // ---------------------------------------------

        private Func<InvoiceListEntryDto, bool>? _invoiceFilterPredicate;

        // ---------------------------------------------
        // Project filtering
        // ---------------------------------------------

        public enum ProjectActiveFilter
        {
            All = 0,
            ActiveOnly = 1,
            InactiveOnly = 2
        }

        // ---------------------------------------------
        // Lifetime
        // ---------------------------------------------

        private ErpApiClient _api;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            Settings = SettingsService.LoadSettings();

            if (string.IsNullOrWhiteSpace(Settings.ApiBaseUrl))
                throw new InvalidOperationException("API base URL is not configured.");

            _api = new ErpApiClient(
                Settings.ApiBaseUrl ?? "https://localhost:7254",
                Settings.ApiKey
            );

            LoadData();
        }

        // ---------------------------------------------
        // Notify helpers
        // ---------------------------------------------

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // ---------------------------------------------
        // Data load / refresh
        // ---------------------------------------------

        private async void LoadData()
        {
            // Dashboard comes from API now
            Dashboard = await _api.GetDashboardSummaryAsync(Settings.UpcomingApplicationsDaysAhead);

            // ProjectSummaries comes from API now
            ProjectSummaries = await _api.GetProjectSummariesAsync();
            // quick debug:
            System.Diagnostics.Debug.WriteLine(
                string.Join(", ", ProjectSummaries.Take(5).Select(p => $"{p.JobNameOrNumber}:{p.IsActive}"))
            );

            // Invoices comes from API now
            Invoices = await _api.GetInvoicesAsync();

            // People comes from API now
            People = await _api.GetPeopleSummaryAsync();

            // Due Schedule comes from API now
            DueSchedule = await _api.GetDueScheduleAsync();

            // Upcoming Applications comes from API now
            UpcomingApplications = await _api.GetUpcomingApplicationsAsync(Settings.UpcomingApplicationsDaysAhead);

            EnsurePeopleView();
            EnsureInvoiceView();
            EnsureProjectView();
            LoadSuppliers();
            EnsureInitialSelections();
        }

        private void EnsureInvoiceView()
        {
            InvoiceView = CollectionViewSource.GetDefaultView(Invoices);
            InvoiceView.Filter = obj =>
            {
                if (obj is not InvoiceListEntryDto inv)
                    return false;

                return _invoiceFilterPredicate == null || _invoiceFilterPredicate(inv);
            };
        }

        private void EnsureProjectView()
        {
            ProjectView = CollectionViewSource.GetDefaultView(ProjectSummaries);

            ProjectView.Filter = obj =>
            {
                if (obj is not ProjectSummaryDto p)
                    return false;

                return ProjectFilter switch
                {
                    ProjectActiveFilter.All => true,
                    ProjectActiveFilter.ActiveOnly => p.IsActive,
                    ProjectActiveFilter.InactiveOnly => !p.IsActive,
                    _ => true
                };
            };
        }

        private async void EnsureInitialSelections()
        {
            if (ProjectSummaries.Count > 0 && SelectedProject == null)
            {
                SelectedProject = ProjectSummaries[0];
                LoadSelectedProjectDetails(SelectedProject);
            }

            if (People.Count > 0 && SelectedPerson == null)
            {
                SelectedPerson = People[0];
                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
            }
        }

        // =======================
        // Data loading helpers
        // =======================

        private async void LoadSuppliers()
        {
            // Keep current selection if possible
            var selectedId = SelectedSupplier?.Id;

            Suppliers.Clear();

            try
            {
                // Match old behaviour (you previously loaded ALL suppliers, active + inactive)
                var dtos = await _api.GetSuppliersAsync(includeInactive: true);

                foreach (var s in dtos.OrderBy(s => s.Name))
                {
                    // Keep using your existing Supplier type so XAML doesn't change
                    Suppliers.Add(new Supplier
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Notes = s.Notes,
                        IsActive = s.IsActive
                    });
                }

                if (selectedId.HasValue)
                {
                    var match = Suppliers.FirstOrDefault(x => x.Id == selectedId.Value);
                    if (match != null)
                    {
                        SelectedSupplier = match;
                        BeginSupplierEditFromSelection();
                    }
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to load suppliers:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ---------------------------------------------
        // Importer runner
        // ---------------------------------------------

        private string? FindImporterExe()
        {
            var baseDir = AppContext.BaseDirectory;

            var rootDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

            var rootExe = Path.Combine(rootDir, "Mep1.Erp.Importer.exe");
            if (File.Exists(rootExe))
                return rootExe;

            var importerBinRoot = Path.Combine(rootDir, "Mep1.Erp.Importer", "bin");
            if (!Directory.Exists(importerBinRoot))
                return null;

            var exe = Directory
                .GetFiles(importerBinRoot, "Mep1.Erp.Importer.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => f)
                .FirstOrDefault();

            return exe;
        }

        private async void RunImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RunImportButton.IsEnabled = false;
                RunImportButton.Content = "Running importer...";

                var exePath = FindImporterExe();
                if (exePath == null)
                {
                    WpfMessageBox.Show(
                        "Could not find the importer executable.\nExpected Mep1.Erp.Importer.exe in the solution root or under Mep1.Erp.Importer\\bin.",
                        "Importer not found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = Path.GetDirectoryName(exePath),
                        UseShellExecute = true
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                });

                LoadData();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Error while running importer:\n\n" + ex.Message,
                    "Import error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RunImportButton.IsEnabled = true;
                RunImportButton.Content = "Run Import (Refresh Data)";
            }
        }

        // ---------------------------------------------
        // Settings browse + save
        // ---------------------------------------------

        private void BrowseTimesheetFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select the '07. Timesheets' folder";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                Settings.TimesheetFolder = dialog.SelectedPath;
        }

        private void BrowseFinanceSheet_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Finance Sheet workbook",
                Filter = "Excel files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                Settings.FinanceSheetPath = dialog.FileName;
        }

        private void BrowseJobSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Job Source Info workbook",
                Filter = "Excel files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                Settings.JobSourcePath = dialog.FileName;
        }

        private void BrowseInvoiceRegister_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Invoice Register workbook",
                Filter = "Excel files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                Settings.InvoiceRegisterPath = dialog.FileName;
        }

        private void BrowseApplicationSchedule_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Application Schedule workbook",
                Filter = "Excel files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                Settings.ApplicationSchedulePath = dialog.FileName;
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.SaveSettings(Settings);

            WpfMessageBox.Show(
                "Settings saved.\n\nThe importer will use these paths next time it runs.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ---------------------------------------------
        // Invoices: filtering + dashboard shortcuts
        // ---------------------------------------------

        private void SetInvoiceFilter(Func<InvoiceListEntryDto, bool>? predicate)
        {
            _invoiceFilterPredicate = predicate;
            InvoiceView?.Refresh();
        }

        private static Func<InvoiceListEntryDto, bool> FilterOpenInvoices()
            => inv => inv.OutstandingNet > 0m;

        private static Func<InvoiceListEntryDto, bool> FilterOverdueInvoices(DateTime today)
            => inv => inv.OutstandingNet > 0m
                      && inv.DueDate.HasValue
                      && inv.DueDate.Value.Date < today.Date;

        private static Func<InvoiceListEntryDto, bool> FilterDueInNextDays(DateTime today, int days)
        {
            var horizon = today.Date.AddDays(days);

            return inv => inv.OutstandingNet > 0m
                          && inv.DueDate.HasValue
                          && inv.DueDate.Value.Date >= today.Date
                          && inv.DueDate.Value.Date <= horizon;
        }

        private void ShowAllInvoices_Click(object sender, RoutedEventArgs e)
            => SetInvoiceFilter(null);

        private void ShowOpenInvoices_Click(object sender, RoutedEventArgs e)
            => SetInvoiceFilter(FilterOpenInvoices());

        private void ShowOverdueInvoices_Click(object sender, RoutedEventArgs e)
            => SetInvoiceFilter(FilterOverdueInvoices(DateTime.Today));

        private void ShowDueNext30Invoices_Click(object sender, RoutedEventArgs e)
            => SetInvoiceFilter(FilterDueInNextDays(DateTime.Today, 30));

        private void InvoicesGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyType == typeof(DateTime) || e.PropertyType == typeof(DateTime?))
            {
                e.Column = new DataGridTextColumn
                {
                    Header = e.Column.Header,
                    Binding = new Binding(e.PropertyName) { StringFormat = "yyyy-MM-dd" }
                };
            }
        }

        private void NavigateToInvoicesTab()
        {
            if (InvoicesTabItem != null)
                MainTabControl.SelectedItem = InvoicesTabItem;
        }

        private void DashboardOutstandingCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            NavigateToInvoicesTab();
            SetInvoiceFilter(FilterOpenInvoices());
        }

        private void DashboardOverdueCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            NavigateToInvoicesTab();
            SetInvoiceFilter(FilterOverdueInvoices(DateTime.Today));
        }

        private void DashboardDueNext30Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            NavigateToInvoicesTab();
            SetInvoiceFilter(FilterDueInNextDays(DateTime.Today, 30));
        }

        // ---------------------------------------------
        // Projects: filtering
        // ---------------------------------------------

        private void SetProjectFilter(ProjectActiveFilter filter)
        {
            ProjectFilter = filter; // this already triggers ProjectView.Refresh() in the setter
        }

        private void ShowAllProjects_Click(object sender, RoutedEventArgs e)
            => SetProjectFilter(ProjectActiveFilter.All);

        private void ShowActiveProjects_Click(object sender, RoutedEventArgs e)
            => SetProjectFilter(ProjectActiveFilter.ActiveOnly);

        private void ShowInactiveProjects_Click(object sender, RoutedEventArgs e)
            => SetProjectFilter(ProjectActiveFilter.InactiveOnly);

        // ---------------------------------------------
        // Projects
        // ---------------------------------------------

        private async void ToggleProjectActive_Click(object sender, RoutedEventArgs e)
        {
            var proj = SelectedProject;
            if (proj == null)
                return;

            var jobKey = proj.JobNameOrNumber;

            // Toggle the current state
            var newIsActive = !proj.IsActive;

            var confirmText = newIsActive
                ? $"Activate project '{jobKey}'?"
                : $"Deactivate project '{jobKey}'?\n\nIt will disappear from the Timesheet project dropdown.";

            var result = WpfMessageBox.Show(
                confirmText,
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                // Calls your API to set active flag
                await _api.SetProjectActiveAsync(jobKey, newIsActive);

                // Refresh list + keep selection so UI updates
                RefreshProjects(keepSelection: true);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to change project active status:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ---------------------------------------------
        // Selection: People + Projects
        // ---------------------------------------------

        private async Task LoadSelectedPersonDetails(int workerId)
        {
            try
            {
                var drill = await _api.GetPersonDrilldownAsync(workerId);

                SelectedPersonRates = drill.Rates
                    .Select(r => new WorkerRate
                    {
                        ValidFrom = r.ValidFrom,
                        ValidTo = r.ValidTo,
                        RatePerHour = r.RatePerHour
                    })
                    .ToList();

                SelectedPersonProjects = drill.Projects
                    .Select(p => new PersonProjectBreakdownRow(
                        p.ProjectLabel,
                        p.ProjectCode,
                        p.Hours,
                        p.Cost))
                    .ToList();

                SelectedPersonRecentEntries = drill.RecentEntries
                    .Select(e => new PersonRecentEntryRow(
                        e.Date,
                        e.ProjectLabel,
                        e.ProjectCode,
                        e.Hours,
                        e.TaskDescription,
                        e.Cost))
                    .ToList();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to load person details:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SelectedPersonRates = new();
                SelectedPersonProjects = new();
                SelectedPersonRecentEntries = new();
            }
        }

        private async void PeopleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            if (grid.SelectedItem is not PeopleSummaryRowDto person)
                return;

            SelectedPerson = person;
            await LoadSelectedPersonDetails(person.WorkerId);
            await LoadPortalAccessAsync(person.WorkerId);
        }

        private async void LoadSelectedProjectDetails(ProjectSummaryDto projSummary)
        {
            if (projSummary == null) return;

            var myVersion = ++_projectDrilldownLoadVersion;

            // Immediate visual feedback (no waiting)
            SelectedProjectLabourThisMonth = new();
            SelectedProjectLabourAllTime = new();
            SelectedProjectRecentEntries = new();
            SelectedProjectInvoices = new();
            SelectedProjectSupplierCosts.Clear();

            try
            {
                var drill = await _api.GetProjectDrilldownAsync(projSummary.JobNameOrNumber, recentTake: 25);

                // If user clicked another project while we were loading, ignore this result
                if (myVersion != _projectDrilldownLoadVersion)
                    return;

                // Apply results
                SelectedProjectLabourThisMonth = drill.LabourThisMonth
                    .Select(x => new ProjectLabourByPersonRow(x.WorkerInitials, x.WorkerName, x.Hours, x.Cost))
                    .ToList();

                SelectedProjectLabourAllTime = drill.LabourAllTime
                    .Select(x => new ProjectLabourByPersonRow(x.WorkerInitials, x.WorkerName, x.Hours, x.Cost))
                    .ToList();

                SelectedProjectRecentEntries = drill.RecentEntries
                    .Select(x => new ProjectRecentEntryRow(x.Date, x.WorkerInitials, x.Hours, x.Cost, x.TaskDescription))
                    .ToList();

                SelectedProjectInvoices = drill.Invoices
                    .Select(x => new ProjectInvoiceRow(x.InvoiceNumber, x.InvoiceDate, x.DueDate, x.NetAmount, x.OutstandingNet, x.Status))
                    .ToList();

                SelectedProjectSupplierCosts.Clear();
                foreach (var sc in drill.SupplierCosts)
                    SelectedProjectSupplierCosts.Add(new SupplierCostRow(sc.Id, sc.Date, sc.SupplierId, sc.SupplierName, sc.Amount, sc.Note));
            }
            catch (Exception ex)
            {
                if (myVersion != _projectDrilldownLoadVersion)
                    return;
                // Keep it simple: show error and clear the drilldown so you don't display stale data.
                WpfMessageBox.Show(
                    "Failed to load project details from API:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SelectedProjectLabourThisMonth = new();
                SelectedProjectLabourAllTime = new();
                SelectedProjectRecentEntries = new();
                SelectedProjectInvoices = new();
                SelectedProjectSupplierCosts.Clear();
            }
        }

        private async void RefreshSelectedProjectSupplierCostsOnly()
        {
            var proj = SelectedProject;
            if (proj == null) return;

            var myVersion = ++_projectDrilldownLoadVersion;

            // Immediate feedback only for supplier costs
            SelectedProjectSupplierCosts.Clear();

            try
            {
                // Cheapest option (no new API endpoint): reuse drilldown but only apply SupplierCosts
                var drill = await _api.GetProjectDrilldownAsync(proj.JobNameOrNumber, recentTake: 25);

                if (myVersion != _projectDrilldownLoadVersion)
                    return;

                SelectedProjectSupplierCosts.Clear();
                foreach (var sc in drill.SupplierCosts)
                    SelectedProjectSupplierCosts.Add(new SupplierCostRow(sc.Id, sc.Date, sc.SupplierId, sc.SupplierName, sc.Amount, sc.Note));
            }
            catch (Exception ex)
            {
                if (myVersion != _projectDrilldownLoadVersion)
                    return;

                WpfMessageBox.Show(
                    "Failed to refresh supplier costs:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void ProjectsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            if (grid.SelectedItem is not ProjectSummaryDto proj)
                return;

            SelectedProject = proj;
            LoadSelectedProjectDetails(proj);
        }

        // =======================
        // Supplier costs (project drilldown)
        // =======================

        private async void AddSupplierCost_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProject == null) return;
            if (NewSupplierCostSupplier == null) return;
            if (NewSupplierCostAmount <= 0m) return;

            try
            {
                var dto = new UpsertSupplierCostDto(
                    SupplierId: NewSupplierCostSupplier.Id,
                    Date: NewSupplierCostDateUnknown ? null : (NewSupplierCostDate ?? DateTime.Today).Date,
                    Amount: NewSupplierCostAmount,
                    Note: string.IsNullOrWhiteSpace(NewSupplierCostNote) ? null : NewSupplierCostNote.Trim());

                await _api.AddProjectSupplierCostAsync(SelectedProject.JobNameOrNumber, dto);

                // Refresh UI (drilldown now comes from API anyway)
                RefreshSelectedProjectSupplierCostsOnly();

                // Refresh project list so profit updates
                RefreshProjects(keepSelection: true);

                // Keep ALL your existing reset behaviour exactly as-is (you already reset + clear edit state here)
                // (leave the rest of your method unchanged)
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to add sub-contractor cost:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            // Reset fields (QoL)
            NewSupplierCostSupplier = null;

            // If you have the unknown-date checkbox feature:
            NewSupplierCostDateUnknown = false;
            NewSupplierCostDate = DateTime.Today;

            // Clear amount + textbox-bound string (if you use AmountText in XAML)
            NewSupplierCostAmount = 0m;
            NewSupplierCostAmountText = "";

            // Clear note
            NewSupplierCostNote = null;

            // Notify UI
            OnPropertyChanged(nameof(NewSupplierCostSupplier));
            OnPropertyChanged(nameof(NewSupplierCostDateUnknown));
            OnPropertyChanged(nameof(NewSupplierCostDate));
            OnPropertyChanged(nameof(IsSupplierCostDateEnabled));
            OnPropertyChanged(nameof(NewSupplierCostAmount));
            OnPropertyChanged(nameof(NewSupplierCostAmountText));
            OnPropertyChanged(nameof(NewSupplierCostNote));

            // Optional: also clear any selected row/edit state if you’re doing edit/delete UI
            SelectedProjectSupplierCost = null;
            IsEditingSupplierCost = false;
            EditingSupplierCostId = null;
            OnPropertyChanged(nameof(SelectedProjectSupplierCost));
            OnPropertyChanged(nameof(IsEditingSupplierCost));
            OnPropertyChanged(nameof(EditingSupplierCostId));
        }

        private void EditSelectedSupplierCost_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProject == null) return;
            if (SelectedProjectSupplierCost == null) return;

            // Enter edit mode
            IsEditingSupplierCost = true;
            EditingSupplierCostId = SelectedProjectSupplierCost.Id;

            // Pre-fill the existing "new cost" inputs from the selected row
            NewSupplierCostDateUnknown = !SelectedProjectSupplierCost.Date.HasValue;
            NewSupplierCostDate = SelectedProjectSupplierCost.Date ?? DateTime.Today;

            NewSupplierCostAmount = SelectedProjectSupplierCost.Amount;
            NewSupplierCostAmountText = SelectedProjectSupplierCost.Amount.ToString("0.##");

            NewSupplierCostNote = SelectedProjectSupplierCost.Note;

            // Match supplier by name (you have a unique supplier Name constraint in practice)
            NewSupplierCostSupplier = Suppliers.FirstOrDefault(s =>
                s.Name.Equals(SelectedProjectSupplierCost.SupplierName, StringComparison.OrdinalIgnoreCase));

            // Notify bindings
            OnPropertyChanged(nameof(NewSupplierCostDateUnknown));
            OnPropertyChanged(nameof(NewSupplierCostDate));
            OnPropertyChanged(nameof(IsSupplierCostDateEnabled));
            OnPropertyChanged(nameof(NewSupplierCostAmountText));
            OnPropertyChanged(nameof(NewSupplierCostNote));
            OnPropertyChanged(nameof(NewSupplierCostSupplier));
        }

        private async void SaveSupplierCostEdit_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProject == null) return;
            if (!IsEditingSupplierCost) return;
            if (EditingSupplierCostId == null) return;

            if (NewSupplierCostSupplier == null) return;
            if (NewSupplierCostAmount <= 0m) return;

            try
            {
                var dto = new UpsertSupplierCostDto(
                    SupplierId: NewSupplierCostSupplier.Id,
                    Date: NewSupplierCostDateUnknown ? null : (NewSupplierCostDate ?? DateTime.Today).Date,
                    Amount: NewSupplierCostAmount,
                    Note: string.IsNullOrWhiteSpace(NewSupplierCostNote) ? null : NewSupplierCostNote.Trim());

                await _api.UpdateProjectSupplierCostAsync(SelectedProject.JobNameOrNumber, EditingSupplierCostId.Value, dto);

                // Refresh drilldown + project summaries (profit)
                RefreshSelectedProjectSupplierCostsOnly();
                RefreshProjects(keepSelection: true);

                // Keep your existing flow
                CancelSupplierCostEdit(); // reset edit state + inputs
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to save sub-contractor cost changes:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelSupplierCostEdit_Click(object sender, RoutedEventArgs e)
        {
            CancelSupplierCostEdit();
        }

        private void CancelSupplierCostEdit()
        {
            IsEditingSupplierCost = false;
            EditingSupplierCostId = null;

            // Reset fields back to "add" defaults
            NewSupplierCostSupplier = null;

            NewSupplierCostDateUnknown = false;
            NewSupplierCostDate = DateTime.Today;

            NewSupplierCostAmount = 0m;
            NewSupplierCostAmountText = "";

            NewSupplierCostNote = null;

            OnPropertyChanged(nameof(NewSupplierCostSupplier));
            OnPropertyChanged(nameof(NewSupplierCostDateUnknown));
            OnPropertyChanged(nameof(NewSupplierCostDate));
            OnPropertyChanged(nameof(IsSupplierCostDateEnabled));
            OnPropertyChanged(nameof(NewSupplierCostAmountText));
            OnPropertyChanged(nameof(NewSupplierCostNote));
        }

        private async void DeleteSelectedSupplierCost_Click(object sender, RoutedEventArgs e)
        {
            // Capture everything we need up-front, because selection can change after MessageBox
            var selectedRow = SelectedProjectSupplierCost;
            var selectedProj = SelectedProject;

            if (selectedProj == null) return;
            if (selectedRow == null) return;

            int idToDelete = selectedRow.Id;
            string jobKey = selectedProj.JobNameOrNumber;

            var result = WpfMessageBox.Show(
                "Delete the selected sub-contractor cost?\n\nThis cannot be undone.",
                "Confirm delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _api.DeleteProjectSupplierCostAsync(jobKey, idToDelete);

                // Refresh drilldown list + project summaries (profit)
                RefreshSelectedProjectSupplierCostsOnly();
                RefreshProjects(keepSelection: true);

                // If we were editing this row, exit edit mode (keep your existing behaviour)
                if (EditingSupplierCostId == idToDelete)
                    CancelSupplierCostEdit();

                // Keep your existing selection clear behaviour
                SelectedProjectSupplierCost = null;
                OnPropertyChanged(nameof(SelectedProjectSupplierCost));
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to delete sub-contractor cost:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =======================
        // Suppliers
        // =======================

        private async void AddSupplier_Click(object sender, RoutedEventArgs e)
        {
            var name = (NewSupplierName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Optional: local duplicate check for instant feedback (API should also enforce)
            var existsLocal = Suppliers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existsLocal)
            {
                WpfMessageBox.Show("A supplier with that name already exists.", "Duplicate supplier",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dto = new UpsertSupplierDto(
                    Name: name,
                    Notes: string.IsNullOrWhiteSpace(NewSupplierNotes) ? null : NewSupplierNotes.Trim(),
                    IsActive: NewSupplierIsActive);

                await _api.AddSupplierAsync(dto);

                LoadSuppliers();

                // clear inputs (keep your existing behaviour)
                NewSupplierName = null;
                NewSupplierNotes = null;
                NewSupplierIsActive = true;
                OnPropertyChanged(nameof(NewSupplierName));
                OnPropertyChanged(nameof(NewSupplierNotes));
                OnPropertyChanged(nameof(NewSupplierIsActive));
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to add supplier:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void SaveSupplierEdits_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSupplier == null || SupplierEdit == null) return;

            var newName = (SupplierEdit.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
                return;

            // Optional: local duplicate check (API should also enforce)
            var duplicateLocal = Suppliers.Any(s =>
                s.Id != SelectedSupplier.Id &&
                s.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

            if (duplicateLocal)
            {
                WpfMessageBox.Show("Another supplier already has that name.", "Duplicate supplier",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dto = new UpsertSupplierDto(
                    Name: newName,
                    Notes: string.IsNullOrWhiteSpace(SupplierEdit.Notes) ? null : SupplierEdit.Notes.Trim(),
                    IsActive: SupplierEdit.IsActive);

                await _api.UpdateSupplierAsync(SelectedSupplier.Id, dto);

                LoadSuppliers();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to save supplier changes:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void DeactivateSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSupplier == null) return;

            try
            {
                await _api.DeactivateSupplierAsync(SelectedSupplier.Id);

                LoadSuppliers();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to deactivate supplier:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =======================
        // Projects helpers
        // =======================

        private async void RefreshProjects(bool keepSelection)
        {
            var selectedJob = keepSelection ? SelectedProject?.JobNameOrNumber : null;

            ProjectSummaries = await _api.GetProjectSummariesAsync();
            EnsureProjectView();

            if (keepSelection && selectedJob != null)
            {
                var match = ProjectSummaries.FirstOrDefault(p => p.JobNameOrNumber == selectedJob);
                if (match != null)
                {
                    SelectedProject = match;
                    LoadSelectedProjectDetails(match);
                }
            }
        }

        // =======================
        // New functions to be moved to correct section when cleaning file
        // =======================

        private void SupplierCostsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            SelectedProjectSupplierCost = grid.SelectedItem as SupplierCostRow;
        }

        private void BeginSupplierEditFromSelection()
        {
            SupplierEdit = SelectedSupplier == null
                ? null
                : new Supplier
                {
                    Id = SelectedSupplier.Id,
                    Name = SelectedSupplier.Name,
                    Notes = SelectedSupplier.Notes,
                    IsActive = SelectedSupplier.IsActive
                };
        }

        private void SuppliersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            if (grid.SelectedItem is not Supplier supplier)
                return;

            SelectedSupplier = supplier;
            BeginSupplierEditFromSelection();
        }

        private async Task LoadPortalAccessAsync(int workerId)
        {
            try
            {
                _portalAccess = await _api.GetPortalAccessAsync(workerId);

                if (_portalAccess.Exists)
                {
                    PortalUsernameText = _portalAccess.Username ?? "";
                    SelectedPortalRole = _portalAccess.Role ?? "Worker";
                    PortalIsActive = _portalAccess.IsActive;
                }
                else
                {
                    // Prefill username to speed up account creation
                    PortalUsernameText = BuildSuggestedUsername(SelectedPerson?.Name);

                    SelectedPortalRole = "Worker";
                    PortalIsActive = true;
                }

                OnPropertyChanged(nameof(HasPortalAccount));
                OnPropertyChanged(nameof(CanCreatePortalAccount));
                OnPropertyChanged(nameof(CanEditPortalUsername));
                OnPropertyChanged(nameof(PortalAccessStatusText));
                OnPropertyChanged(nameof(PortalUsernameText));
                OnPropertyChanged(nameof(SelectedPortalRole));
                OnPropertyChanged(nameof(PortalIsActive));
                OnPropertyChanged(nameof(PortalTempPasswordText));
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to load portal access:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void CreatePortalAccount_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;

            if (string.IsNullOrWhiteSpace(PortalUsernameText))
            {
                WpfMessageBox.Show("Enter a username first.", "Portal Access", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var created = await _api.CreatePortalAccessAsync(
                    SelectedPerson.WorkerId,
                    new CreatePortalAccessRequest(PortalUsernameText.Trim(), SelectedPortalRole));

                System.Windows.Clipboard.SetText(created.TemporaryPassword);
                PortalTempPasswordText = "Temporary password: " + created.TemporaryPassword;

                WpfMessageBox.Show(
                    "Portal account created.\n\nPassword copied to clipboard and shown on page (it won't be shown again).",
                    "Portal Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await LoadPortalAccessAsync(SelectedPerson.WorkerId);

                PortalTempPasswordText = "Temporary password: " + created.TemporaryPassword;
                OnPropertyChanged(nameof(PortalTempPasswordText));
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show("Create failed:\n\n" + ex.Message, "Portal Access", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SavePortalAccess_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;

            try
            {
                await _api.UpdatePortalAccessAsync(
                    SelectedPerson.WorkerId,
                    new UpdatePortalAccessRequest(
                        Role: SelectedPortalRole,
                        IsActive: PortalIsActive));

                WpfMessageBox.Show("Saved.", "Portal Access", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadPortalAccessAsync(SelectedPerson.WorkerId);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show("Save failed:\n\n" + ex.Message, "Portal Access", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ResetPortalPassword_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;

            try
            {
                var result = await _api.ResetPortalPasswordAsync(SelectedPerson.WorkerId);

                System.Windows.Clipboard.SetText(result.TemporaryPassword);
                PortalTempPasswordText = "Temporary password: " + result.TemporaryPassword;

                WpfMessageBox.Show(
                    "Password reset.\n\nPassword copied to clipboard and shown on page (it won't be shown again).",
                    "Portal Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                OnPropertyChanged(nameof(PortalTempPasswordText));
                await LoadPortalAccessAsync(SelectedPerson.WorkerId);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show("Reset failed:\n\n" + ex.Message, "Portal Access", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildSuggestedUsername(string? fullName)
        {
            return string.IsNullOrWhiteSpace(fullName) ? "" : fullName.Trim();
        }

        private void EnsurePeopleView()
        {
            PeopleView = CollectionViewSource.GetDefaultView(People);

            PeopleView.Filter = obj =>
            {
                if (obj is not PeopleSummaryRowDto p)
                    return false;

                return PeopleFilter switch
                {
                    PeopleActiveFilter.All => true,
                    PeopleActiveFilter.ActiveOnly => p.IsActive,
                    PeopleActiveFilter.InactiveOnly => !p.IsActive,
                    _ => true
                };
            };
        }

        private void SetPeopleFilter(PeopleActiveFilter filter)
        {
            PeopleFilter = filter; // triggers PeopleView.Refresh() in setter
        }

        private void ShowAllPeople_Click(object sender, RoutedEventArgs e)
            => SetPeopleFilter(PeopleActiveFilter.All);

        private void ShowActivePeople_Click(object sender, RoutedEventArgs e)
            => SetPeopleFilter(PeopleActiveFilter.ActiveOnly);

        private void ShowInactivePeople_Click(object sender, RoutedEventArgs e)
            => SetPeopleFilter(PeopleActiveFilter.InactiveOnly);

        private async void TogglePersonActive_Click(object sender, RoutedEventArgs e)
        {
            var person = SelectedPerson;
            if (person == null)
                return;

            var newIsActive = !person.IsActive;

            var confirmText = newIsActive
                ? $"Activate '{person.Name}'?"
                : $"Deactivate '{person.Name}'?\n\nThey will be hidden from the Timesheet worker dropdown (if you filter to active workers).";

            var result = WpfMessageBox.Show(
                confirmText,
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _api.SetWorkerActiveAsync(person.WorkerId, newIsActive);

                // IMPORTANT: await the refresh so the right panel (portal) updates deterministically
                await RefreshPeopleAsync(keepSelection: true);

                // Extra safety: if your API deactivates portal access when worker deactivates,
                // this guarantees the checkbox reflects the latest state immediately.
                await LoadPortalAccessAsync(person.WorkerId);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to change worker active status:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task RefreshPeopleAsync(bool keepSelection)
        {
            var selectedWorkerId = keepSelection ? SelectedPerson?.WorkerId : null;

            People = await _api.GetPeopleSummaryAsync();
            EnsurePeopleView();

            if (keepSelection && selectedWorkerId.HasValue)
            {
                // Try to rebind SelectedPerson to the refreshed list (if still visible)
                var match = People.FirstOrDefault(p => p.WorkerId == selectedWorkerId.Value);
                if (match != null)
                    SelectedPerson = match;

                // Always refresh RHS panels for the worker id (even if filtered out of the left list)
                await LoadSelectedPersonDetails(selectedWorkerId.Value);
                await LoadPortalAccessAsync(selectedWorkerId.Value);
            }
        }

        private async void AddWorker_Click(object sender, RoutedEventArgs e)
        {
            AddWorkerStatusText = "";

            var initials = (NewWorkerInitialsText ?? "").Trim();
            var name = (NewWorkerNameText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(initials))
            {
                AddWorkerStatusText = "Initials are required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                AddWorkerStatusText = "Name is required.";
                return;
            }

            // Parse rate (allow "25", "25.00", "£25.00")
            var rateText = (NewWorkerRateText ?? "").Trim().Replace("£", "");
            if (!decimal.TryParse(rateText, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.CurrentCulture, out var rate) &&
                !decimal.TryParse(rateText, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out rate))
            {
                AddWorkerStatusText = "Rate must be a valid number (e.g. 25.00).";
                return;
            }

            if (rate < 0)
            {
                AddWorkerStatusText = "Rate cannot be negative.";
                return;
            }

            try
            {
                var req = new CreateWorkerRequestDto
                {
                    Initials = initials,
                    Name = name,
                    RatePerHour = rate,
                    IsActive = NewWorkerIsActive
                };

                var created = await _api.CreateWorkerAsync(req);

                AddWorkerStatusText = $"Created worker (ID {created.Id}).";

                // Clear inputs (keep Active default true)
                NewWorkerInitialsText = "";
                NewWorkerNameText = "";
                NewWorkerRateText = "";

                // Refresh people list (this should also refresh selection + right panel data next time selected)
                await RefreshPeopleAsync(keepSelection: false);
            }
            catch (Exception ex)
            {
                AddWorkerStatusText = $"Failed to create worker: {ex.Message}";
            }
        }
    }
}
