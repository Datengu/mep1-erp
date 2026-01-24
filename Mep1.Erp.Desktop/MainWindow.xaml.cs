using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
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
using System.Globalization;
using static Mep1.Erp.Application.Reporting;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Binding = System.Windows.Data.Binding;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace Mep1.Erp.Desktop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

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

        private string _newProjectJobNameText = "";
        public string NewProjectJobNameText
        {
            get => _newProjectJobNameText;
            set => SetField(ref _newProjectJobNameText, value, nameof(NewProjectJobNameText));
        }

        private string _newProjectCompanyText = "";
        public string NewProjectCompanyText
        {
            get => _newProjectCompanyText;
            set
            {
                if (SetField(ref _newProjectCompanyText, value, nameof(NewProjectCompanyText)))
                {
                    OnPropertyChanged(nameof(CanAddProject));
                }
            }
        }

        private bool _newProjectIsActive = true;
        public bool NewProjectIsActive
        {
            get => _newProjectIsActive;
            set => SetField(ref _newProjectIsActive, value, nameof(NewProjectIsActive));
        }

        private string _addProjectStatusText = "";
        public string AddProjectStatusText
        {
            get => _addProjectStatusText;
            set => SetField(ref _addProjectStatusText, value, nameof(AddProjectStatusText));
        }

        // Add Project builder inputs
        public List<string> ProjectPrefixOptions { get; } = new() { "PN", "SW" };

        private string _selectedProjectPrefix = "PN";
        public string SelectedProjectPrefix
        {
            get => _selectedProjectPrefix;
            set
            {
                if (SetField(ref _selectedProjectPrefix, value, nameof(SelectedProjectPrefix)))
                    RebuildNewProjectJobName();
            }
        }

        private string _newProjectNumberText = "";
        public string NewProjectNumberText
        {
            get => _newProjectNumberText;
            set
            {
                if (SetField(ref _newProjectNumberText, value, nameof(NewProjectNumberText)))
                    RebuildNewProjectJobName();
            }
        }

        private string _newProjectNameText = "";
        public string NewProjectNameText
        {
            get => _newProjectNameText;
            set
            {
                if (SetField(ref _newProjectNameText, value, nameof(NewProjectNameText)))
                    RebuildNewProjectJobName();
            }
        }

        private void RebuildNewProjectJobName()
        {
            var prefix = (SelectedProjectPrefix ?? "").Trim();
            var numRaw = (NewProjectNumberText ?? "").Trim();
            var name = (NewProjectNameText ?? "").Trim();

            // Parse number; if not valid, just show prefix + whatever they typed (keeps it responsive)
            string numberPart;
            if (int.TryParse(numRaw, out var n) && n >= 0)
                numberPart = n.ToString("0000");  // 27 -> 0027
            else
                numberPart = numRaw;

            var basePart = (prefix + numberPart).Trim();

            NewProjectJobNameText = string.IsNullOrWhiteSpace(name)
                ? basePart
                : $"{basePart} - {name}";

            // Ensure dependent UI updates
            OnPropertyChanged(nameof(NewProjectJobNameText));
            OnPropertyChanged(nameof(CanAddProject));
        }

        public bool CanAddProject
        {
            get
            {
                // Prefix is effectively always set, but keep it explicit
                if (string.IsNullOrWhiteSpace(SelectedProjectPrefix))
                    return false;

                // Project number must be a valid non-negative int
                if (!int.TryParse(NewProjectNumberText?.Trim(), out var n) || n < 0)
                    return false;

                // Job name is required
                if (string.IsNullOrWhiteSpace(NewProjectNameText))
                    return false;

                // Company is required
                if (string.IsNullOrWhiteSpace(NewProjectCompanyText))
                    return false;

                return true;
            }
        }

        public ObservableCollection<CompanyListItemDto> Companies { get; } = new();
        public CompanyListItemDto? SelectedCompany { get; set; }

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

        private string _editPersonInitialsText = "";
        public string EditPersonInitialsText
        {
            get => _editPersonInitialsText;
            set => SetField(ref _editPersonInitialsText, value, nameof(EditPersonInitialsText));
        }

        private string _editPersonNameText = "";
        public string EditPersonNameText
        {
            get => _editPersonNameText;
            set => SetField(ref _editPersonNameText, value, nameof(EditPersonNameText));
        }

        private string _editPersonSignatureNameText = "";
        public string EditPersonSignatureNameText
        {
            get => _editPersonSignatureNameText;
            set => SetField(ref _editPersonSignatureNameText, value, nameof(EditPersonSignatureNameText));
        }

        private string _editPersonStatusText = "";
        public string EditPersonStatusText
        {
            get => _editPersonStatusText;
            set => SetField(ref _editPersonStatusText, value, nameof(EditPersonStatusText));
        }

        private List<WorkerRate> _editPersonRates = new();
        public List<WorkerRate> EditPersonRates
        {
            get => _editPersonRates;
            set => SetField(ref _editPersonRates, value, nameof(EditPersonRates));
        }

        private WorkerRate? _selectedEditPersonRate;
        public WorkerRate? SelectedEditPersonRate
        {
            get => _selectedEditPersonRate;
            set
            {
                if (!SetField(ref _selectedEditPersonRate, value, nameof(SelectedEditPersonRate)))
                    return;

                // When a row is selected, prefill the amount box so Update is obvious/explicit.
                if (value == null)
                {
                    UpdateSelectedRateAmountText = "";
                }
                else
                {
                    UpdateSelectedRateAmountText = value.RatePerHour.ToString("0.00");
                }

                // If your SetField doesn’t automatically raise this dependent property change:
                OnPropertyChanged(nameof(UpdateSelectedRateAmountText));
            }
        }

        private string _editRatesStatusText = "";
        public string EditRatesStatusText
        {
            get => _editRatesStatusText;
            set => SetField(ref _editRatesStatusText, value, nameof(EditRatesStatusText));
        }

        // Rate editor inputs
        private DateTime? _changeCurrentEffectiveFrom = DateTime.Today;
        public DateTime? ChangeCurrentEffectiveFrom
        {
            get => _changeCurrentEffectiveFrom;
            set => SetField(ref _changeCurrentEffectiveFrom, value, nameof(ChangeCurrentEffectiveFrom));
        }

        private string _changeCurrentRateText = "";
        public string ChangeCurrentRateText
        {
            get => _changeCurrentRateText;
            set => SetField(ref _changeCurrentRateText, value, nameof(ChangeCurrentRateText));
        }

        private DateTime? _addRateValidFrom = DateTime.Today;
        public DateTime? AddRateValidFrom
        {
            get => _addRateValidFrom;
            set => SetField(ref _addRateValidFrom, value, nameof(AddRateValidFrom));
        }

        private DateTime? _addRateValidTo = DateTime.Today;
        public DateTime? AddRateValidTo
        {
            get => _addRateValidTo;
            set => SetField(ref _addRateValidTo, value, nameof(AddRateValidTo));
        }

        private string _addRateAmountText = "";
        public string AddRateAmountText
        {
            get => _addRateAmountText;
            set => SetField(ref _addRateAmountText, value, nameof(AddRateAmountText));
        }

        private string _updateSelectedRateAmountText = "";
        public string UpdateSelectedRateAmountText
        {
            get => _updateSelectedRateAmountText;
            set => SetField(ref _updateSelectedRateAmountText, value, nameof(UpdateSelectedRateAmountText));
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

        private string _newInvoiceNumberText = "";
        public string NewInvoiceNumberText
        {
            get => _newInvoiceNumberText;
            set
            {
                if (SetField(ref _newInvoiceNumberText, value, nameof(NewInvoiceNumberText)))
                {
                    UpdateAddInvoiceValidation();
                }
            }
        }

        private DateTime? _newInvoiceDate = DateTime.Today;
        public DateTime? NewInvoiceDate
        {
            get => _newInvoiceDate;
            set
            {
                if (SetField(ref _newInvoiceDate, value, nameof(NewInvoiceDate)))
                {
                    // v1 helper:
                    // If Due Date is not set yet, default it to Invoice Date
                    if (_newInvoiceDueDate == null && _newInvoiceDate != null)
                    {
                        NewInvoiceDueDate = _newInvoiceDate;
                    }

                    UpdateAddInvoiceValidation();
                }
            }
        }

        private DateTime? _newInvoiceDueDate;
        public DateTime? NewInvoiceDueDate
        {
            get => _newInvoiceDueDate;
            set
            {
                if (SetField(ref _newInvoiceDueDate, value, nameof(NewInvoiceDueDate)))
                {
                    // Due date is optional, but still affects validation/help text sometimes.
                    UpdateAddInvoiceValidation();
                }
            }
        }

        private CompanyListItemDto? _newInvoiceSelectedCompany;
        public CompanyListItemDto? NewInvoiceSelectedCompany
        {
            get => _newInvoiceSelectedCompany;
            set
            {
                if (SetField(ref _newInvoiceSelectedCompany, value, nameof(NewInvoiceSelectedCompany)))
                {
                    UpdateAddInvoiceValidation();
                }
            }
        }

        private InvoiceProjectPicklistItemDto? _newInvoiceSelectedProject;
        public InvoiceProjectPicklistItemDto? NewInvoiceSelectedProject
        {
            get => _newInvoiceSelectedProject;
            set
            {
                if (!SetField(ref _newInvoiceSelectedProject, value, nameof(NewInvoiceSelectedProject)))
                    return;

                DeriveInvoiceCompanyFromProject();
                UpdateAddInvoiceValidation();
            }
        }

        // Text inputs for amounts
        private string _newInvoiceNetAmountText = "";
        public string NewInvoiceNetAmountText
        {
            get => _newInvoiceNetAmountText;
            set
            {
                if (SetField(ref _newInvoiceNetAmountText, value, nameof(NewInvoiceNetAmountText)))
                {
                    RecalculateAddInvoiceTotals();
                    UpdateAddInvoiceValidation();
                }
            }
        }

        // VAT rate selection (string-based; ComboBox items are strings)
        private string _newInvoiceVatRateText = "20%";
        public string NewInvoiceVatRateText
        {
            get => _newInvoiceVatRateText;
            set
            {
                if (SetField(ref _newInvoiceVatRateText, value, nameof(NewInvoiceVatRateText)))
                {
                    RecalculateAddInvoiceTotals();
                    UpdateAddInvoiceValidation();
                }
            }
        }

        // Calculated display strings
        private string _newInvoiceVatAmountText = "";
        public string NewInvoiceVatAmountText
        {
            get => _newInvoiceVatAmountText;
            set => SetField(ref _newInvoiceVatAmountText, value, nameof(NewInvoiceVatAmountText));
        }

        private string _newInvoiceGrossAmountText = "";
        public string NewInvoiceGrossAmountText
        {
            get => _newInvoiceGrossAmountText;
            set => SetField(ref _newInvoiceGrossAmountText, value, nameof(NewInvoiceGrossAmountText));
        }

        private string _newInvoiceNotesText = "";
        public string NewInvoiceNotesText
        {
            get => _newInvoiceNotesText;
            set => SetField(ref _newInvoiceNotesText, value, nameof(NewInvoiceNotesText));
        }

        // Status selection (string-based; ComboBox items are strings)
        private string _newInvoiceStatusText = "Outstanding";
        public string NewInvoiceStatusText
        {
            get => _newInvoiceStatusText;
            set
            {
                if (SetField(ref _newInvoiceStatusText, value, nameof(NewInvoiceStatusText)))
                {
                    UpdateAddInvoiceValidation();
                }
            }
        }

        private string _addInvoiceStatusText = "";
        public string AddInvoiceStatusText
        {
            get => _addInvoiceStatusText;
            set => SetField(ref _addInvoiceStatusText, value, nameof(AddInvoiceStatusText));
        }

        private string _addInvoiceValidationText = "";
        public string AddInvoiceValidationText
        {
            get => _addInvoiceValidationText;
            set => SetField(ref _addInvoiceValidationText, value, nameof(AddInvoiceValidationText));
        }

        private string _newInvoiceDerivedCompanyName = "";
        public string NewInvoiceDerivedCompanyName
        {
            get => _newInvoiceDerivedCompanyName;
            set => SetField(ref _newInvoiceDerivedCompanyName, value, nameof(NewInvoiceDerivedCompanyName));
        }

        private List<InvoiceProjectPicklistItemDto> _invoiceProjectPicklist = new();
        public List<InvoiceProjectPicklistItemDto> InvoiceProjectPicklist
        {
            get => _invoiceProjectPicklist;
            set => SetField(ref _invoiceProjectPicklist, value, nameof(InvoiceProjectPicklist));
        }

        private InvoiceListEntryDto? _selectedInvoiceListItem;
        public InvoiceListEntryDto? SelectedInvoiceListItem
        {
            get => _selectedInvoiceListItem;
            set
            {
                if (SetField(ref _selectedInvoiceListItem, value, nameof(SelectedInvoiceListItem)))
                {
                    UpdateEditInvoiceValidation();
                    UpdateEditInvoiceSelectedSummary();
                }
            }
        }

        private int? _editInvoiceLoadedId;
        public int? EditInvoiceLoadedId
        {
            get => _editInvoiceLoadedId;
            set => SetField(ref _editInvoiceLoadedId, value, nameof(EditInvoiceLoadedId));
        }

        private string _editInvoiceSelectedSummaryText = "No invoice selected.";
        public string EditInvoiceSelectedSummaryText
        {
            get => _editInvoiceSelectedSummaryText;
            set => SetField(ref _editInvoiceSelectedSummaryText, value, nameof(EditInvoiceSelectedSummaryText));
        }

        private string _editInvoiceNumberText = "";
        public string EditInvoiceNumberText
        {
            get => _editInvoiceNumberText;
            set => SetField(ref _editInvoiceNumberText, value, nameof(EditInvoiceNumberText));
        }

        private string _editInvoiceStatusText = "Outstanding";
        public string EditInvoiceStatusText
        {
            get => _editInvoiceStatusText;
            set
            {
                if (SetField(ref _editInvoiceStatusText, value, nameof(EditInvoiceStatusText)))
                {
                    UpdateEditInvoiceValidation();
                }
            }
        }

        private DateTime? _editInvoiceDate = DateTime.Today;
        public DateTime? EditInvoiceDate
        {
            get => _editInvoiceDate;
            set
            {
                if (SetField(ref _editInvoiceDate, value, nameof(EditInvoiceDate)))
                {
                    UpdateEditInvoiceValidation();
                }
            }
        }

        private DateTime? _editInvoiceDueDate;
        public DateTime? EditInvoiceDueDate
        {
            get => _editInvoiceDueDate;
            set
            {
                if (SetField(ref _editInvoiceDueDate, value, nameof(EditInvoiceDueDate)))
                {
                    UpdateEditInvoiceValidation();
                }
            }
        }

        private InvoiceProjectPicklistItemDto? _editInvoiceSelectedProject;
        public InvoiceProjectPicklistItemDto? EditInvoiceSelectedProject
        {
            get => _editInvoiceSelectedProject;
            set
            {
                if (SetField(ref _editInvoiceSelectedProject, value, nameof(EditInvoiceSelectedProject)))
                {
                    EditInvoiceDerivedCompanyName = value?.CompanyName ?? "";
                    UpdateEditInvoiceValidation();
                }
            }
        }

        private string _editInvoiceDerivedCompanyName = "";
        public string EditInvoiceDerivedCompanyName
        {
            get => _editInvoiceDerivedCompanyName;
            set => SetField(ref _editInvoiceDerivedCompanyName, value, nameof(EditInvoiceDerivedCompanyName));
        }

        private string _editInvoiceNetAmountText = "";
        public string EditInvoiceNetAmountText
        {
            get => _editInvoiceNetAmountText;
            set
            {
                if (SetField(ref _editInvoiceNetAmountText, value, nameof(EditInvoiceNetAmountText)))
                {
                    RecalculateEditInvoiceTotals();
                    UpdateEditInvoiceValidation();
                }
            }
        }

        private string _editInvoiceVatRateText = "20%";
        public string EditInvoiceVatRateText
        {
            get => _editInvoiceVatRateText;
            set
            {
                if (SetField(ref _editInvoiceVatRateText, value, nameof(EditInvoiceVatRateText)))
                {
                    RecalculateEditInvoiceTotals();
                    UpdateEditInvoiceValidation();
                }
            }
        }

        private string _editInvoiceVatAmountText = "0.00";
        public string EditInvoiceVatAmountText
        {
            get => _editInvoiceVatAmountText;
            set => SetField(ref _editInvoiceVatAmountText, value, nameof(EditInvoiceVatAmountText));
        }

        private string _editInvoiceGrossAmountText = "0.00";
        public string EditInvoiceGrossAmountText
        {
            get => _editInvoiceGrossAmountText;
            set => SetField(ref _editInvoiceGrossAmountText, value, nameof(EditInvoiceGrossAmountText));
        }

        private string _editInvoiceNotesText = "";
        public string EditInvoiceNotesText
        {
            get => _editInvoiceNotesText;
            set => SetField(ref _editInvoiceNotesText, value, nameof(EditInvoiceNotesText));
        }

        private string _editInvoiceStatusBarText = "Select an invoice and click Load selected.";
        public string EditInvoiceStatusBarText
        {
            get => _editInvoiceStatusBarText;
            set => SetField(ref _editInvoiceStatusBarText, value, nameof(EditInvoiceStatusBarText));
        }

        private string _editInvoiceValidationText = "";
        public string EditInvoiceValidationText
        {
            get => _editInvoiceValidationText;
            set => SetField(ref _editInvoiceValidationText, value, nameof(EditInvoiceValidationText));
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

        private List<AuditLogRowDto> _auditLogs = new();

        public List<AuditLogRowDto> AuditLogs 
        {
            get => _auditLogs;
            set => SetField(ref _auditLogs, value, nameof(AuditLogs));
        }

        private string _auditSearchText = "";
        public string AuditSearchText
        {
            get => _auditSearchText;
            set => SetField(ref _auditSearchText, value, nameof(AuditSearchText));
        }

        private string _auditEntityTypeText = "";
        public string AuditEntityTypeText
        {
            get => _auditEntityTypeText;
            set => SetField(ref _auditEntityTypeText, value, nameof(AuditEntityTypeText));
        }

        private string _auditEntityIdText = "";
        public string AuditEntityIdText
        {
            get => _auditEntityIdText;
            set => SetField(ref _auditEntityIdText, value, nameof(AuditEntityIdText));
        }

        private string _auditActorWorkerIdText = "";
        public string AuditActorWorkerIdText
        {
            get => _auditActorWorkerIdText;
            set => SetField(ref _auditActorWorkerIdText, value, nameof(AuditActorWorkerIdText));
        }

        private string _auditActionText = "";
        public string AuditActionText
        {
            get => _auditActionText;
            set => SetField(ref _auditActionText, value, nameof(AuditActionText));
        }

        private PeopleSummaryRowDto? _timesheetSelectedWorker;
        public PeopleSummaryRowDto? TimesheetSelectedWorker
        {
            get => _timesheetSelectedWorker;
            set
            {
                if (SetField(ref _timesheetSelectedWorker, value, nameof(TimesheetSelectedWorker)))
                {
                    // refresh list when worker changes
                    _ = RefreshTimesheetEntriesAsync();
                    OnPropertyChanged(nameof(TimesheetIsOnBehalf));
                }
            }
        }

        public bool TimesheetIsOnBehalf => false; // v1: we don't know actor id here yet

        private List<TimesheetProjectOptionDto> _timesheetProjects = new();
        public List<TimesheetProjectOptionDto> TimesheetProjects
        {
            get => _timesheetProjects;
            set => SetField(ref _timesheetProjects, value, nameof(TimesheetProjects));
        }

        private TimesheetProjectOptionDto? _selectedTimesheetProject;
        public TimesheetProjectOptionDto? SelectedTimesheetProject
        {
            get => _selectedTimesheetProject;
            set
            {
                // capture "from" before changing
                var prevLabel = (_selectedTimesheetProject?.Label ?? _selectedTimesheetProject?.JobKey);

                if (!SetField(ref _selectedTimesheetProject, value, nameof(SelectedTimesheetProject)))
                    return;

                _timesheetLastChangeSource = TimesheetChangeSource.Project;
                _timesheetPrevProjectWasSpecial = IsSpecialTimesheetProjectLabel(prevLabel);

                ApplyTimesheetAllRules();

                OnPropertyChanged(nameof(IsTimesheetWorkTypeEnabled));
                OnPropertyChanged(nameof(IsTimesheetWorkDetailsVisible));

            }
        }

        private DateTime _timesheetDate = DateTime.Today;
        public DateTime TimesheetDate
        {
            get => _timesheetDate;
            set => SetField(ref _timesheetDate, value, nameof(TimesheetDate));
        }

        private string _timesheetHoursText = "0";
        public string TimesheetHoursText
        {
            get => _timesheetHoursText;
            set => SetField(ref _timesheetHoursText, value, nameof(TimesheetHoursText));
        }

        private string _timesheetCodeText = "P";
        public string TimesheetCodeText
        {
            get => _timesheetCodeText;
            set
            {
                if (!SetField(ref _timesheetCodeText, value, nameof(TimesheetCodeText)))
                    return;

                _timesheetLastChangeSource = TimesheetChangeSource.Code;

                // Refresh VO-only visibility
                OnPropertyChanged(nameof(IsTimesheetCcfRefVisible));

                // UX: if you move away from VO, clear CCF
                if (!IsTimesheetCcfRefVisible)
                    TimesheetCcfRefText = "";

                // New: SI/HOL UX (hours/task lock + 0 hours), etc.
                ApplyTimesheetAllRules();
            }
        }

        private List<TimesheetCodeDto> _timesheetCodes = new();
        public List<TimesheetCodeDto> TimesheetCodes
        {
            get => _timesheetCodes;
            set => SetField(ref _timesheetCodes, value, nameof(TimesheetCodes));
        }

        private string _timesheetCcfRefText = "";
        public string TimesheetCcfRefText
        {
            get => _timesheetCcfRefText;
            set => SetField(ref _timesheetCcfRefText, value, nameof(TimesheetCcfRefText));
        }

        private string _timesheetTaskDescriptionText = "";
        public string TimesheetTaskDescriptionText
        {
            get => _timesheetTaskDescriptionText;
            set => SetField(ref _timesheetTaskDescriptionText, value, nameof(TimesheetTaskDescriptionText));
        }

        private string _timesheetWorkTypeText = "";
        public string TimesheetWorkTypeText
        {
            get => _timesheetWorkTypeText;
            set => SetField(ref _timesheetWorkTypeText, value, nameof(TimesheetWorkTypeText));
        }

        private List<TimesheetEntrySummaryDto> _timesheetEntries = new();
        public List<TimesheetEntrySummaryDto> TimesheetEntries
        {
            get => _timesheetEntries;
            set => SetField(ref _timesheetEntries, value, nameof(TimesheetEntries));
        }

        private string _timesheetStatusText = "";
        public string TimesheetStatusText
        {
            get => _timesheetStatusText;
            set => SetField(ref _timesheetStatusText, value, nameof(TimesheetStatusText));
        }

        private bool _isTimesheetCodeLocked;
        public bool IsTimesheetCodeLocked
        {
            get => _isTimesheetCodeLocked;
            set
            {
                if (!SetField(ref _isTimesheetCodeLocked, value, nameof(IsTimesheetCodeLocked)))
                    return;
                OnPropertyChanged(nameof(IsTimesheetCodeEnabled));
            }
        }

        private bool _isTimesheetHoursLocked;
        public bool IsTimesheetHoursLocked
        {
            get => _isTimesheetHoursLocked;
            set
            {
                if (!SetField(ref _isTimesheetHoursLocked, value, nameof(IsTimesheetHoursLocked)))
                    return;
                OnPropertyChanged(nameof(IsTimesheetHoursEnabled));
            }
        }

        private bool _isTimesheetTaskLocked;
        public bool IsTimesheetTaskLocked
        {
            get => _isTimesheetTaskLocked;
            set
            {
                if (!SetField(ref _isTimesheetTaskLocked, value, nameof(IsTimesheetTaskLocked)))
                    return;
                OnPropertyChanged(nameof(IsTimesheetTaskEnabled));
            }
        }

        public bool IsTimesheetCodeEnabled => !IsTimesheetCodeLocked;
        public bool IsTimesheetHoursEnabled => !IsTimesheetHoursLocked;
        public bool IsTimesheetTaskEnabled => !IsTimesheetTaskLocked;

        private bool _applyingTimesheetRules;


        public bool IsTimesheetCcfRefVisible
        {
            get
            {
                var code = (TimesheetCodeText ?? "").Trim();
                return code.Equals("VO", StringComparison.OrdinalIgnoreCase);
            }
        }

        private enum TimesheetChangeSource
        {
            None = 0,
            Project = 1,
            Code = 2
        }

        private TimesheetChangeSource _timesheetLastChangeSource = TimesheetChangeSource.None;
        private bool _timesheetPrevProjectWasSpecial = false;

        private static bool IsSpecialTimesheetProjectLabel(string? jobLabelOrKey)
        {
            var job = (jobLabelOrKey ?? "").Trim();
            return job.Equals("Holiday", StringComparison.OrdinalIgnoreCase)
                || job.Equals("Bank Holiday", StringComparison.OrdinalIgnoreCase)
                || job.Equals("Sick", StringComparison.OrdinalIgnoreCase)
                || job.Equals("Fee Proposal", StringComparison.OrdinalIgnoreCase)
                || job.Equals("Tender Presentation", StringComparison.OrdinalIgnoreCase);
        }

        public List<string> TimesheetHourOptions { get; } = BuildTimesheetHourOptions();

        private static List<string> BuildTimesheetHourOptions()
        {
            var list = new List<string>(49);
            for (int i = 0; i <= 48; i++)
            {
                var h = i * 0.5m; // 0, 0.5, ... 24
                list.Add(h.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            return list;
        }

        public sealed record TimesheetWorkTypeOption(string Code, string Label);

        public List<TimesheetWorkTypeOption> TimesheetWorkTypeOptions { get; } = new()
        {
            new TimesheetWorkTypeOption("S", "Sheet"),
            new TimesheetWorkTypeOption("M", "Modelling")
        };

        private bool IsSelectedTimesheetJobProjectCategory()
        {
            // TimesheetProjectOptionDto includes Category from API
            var cat = (SelectedTimesheetProject?.Category ?? "").Trim();
            return cat.Equals("Project", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsTimesheetWorkTypeEnabled => IsSelectedTimesheetJobProjectCategory();

        public List<string> TimesheetLevelOptions { get; } = new()
        {
            // Below ground
            "B4", "B3", "B2", "B1", "LG", "UG",

            // Ground / podium / mezzanine
            "G", "POD", "M",

            // Upper floors
            "L01","L02","L03","L04","L05","L06","L07","L08","L09","L10",
            "L11","L12","L13","L14","L15","L16","L17","L18","L19","L20",
            "L21","L22","L23","L24","L25","L26","L27","L28","L29","L30",

            // Roof / plant
            "P", "RF"
        };

        private List<string> _timesheetSelectedLevels = new();
        public List<string> TimesheetSelectedLevels
        {
            get => _timesheetSelectedLevels;
            set => SetField(ref _timesheetSelectedLevels, value, nameof(TimesheetSelectedLevels));
        }

        private string _timesheetAreasRawText = "";
        public string TimesheetAreasRawText
        {
            get => _timesheetAreasRawText;
            set => SetField(ref _timesheetAreasRawText, value, nameof(TimesheetAreasRawText));
        }

        public bool IsTimesheetWorkDetailsVisible => IsSelectedTimesheetJobProjectCategory();

        private TimesheetEntrySummaryDto? _selectedTimesheetEntry;
        public TimesheetEntrySummaryDto? SelectedTimesheetEntry
        {
            get => _selectedTimesheetEntry;
            set
            {
                if (SetField(ref _selectedTimesheetEntry, value, nameof(SelectedTimesheetEntry)))
                {
                    OnPropertyChanged(nameof(HasSelectedTimesheetEntry));
                    OnPropertyChanged(nameof(SelectedTimesheetEntrySummaryText));
                }
            }
        }

        public bool HasSelectedTimesheetEntry => SelectedTimesheetEntry != null;

        public string SelectedTimesheetEntrySummaryText
        {
            get
            {
                var e = SelectedTimesheetEntry;
                if (e == null) return "No entry selected.";
                return $"{e.Date:yyyy-MM-dd} | {e.JobKey} | {e.Code} | {e.Hours:0.##}h";
            }
        }

        private bool _isTimesheetEditMode;
        public bool IsTimesheetEditMode
        {
            get => _isTimesheetEditMode;
            set
            {
                if (SetField(ref _isTimesheetEditMode, value, nameof(IsTimesheetEditMode)))
                    OnPropertyChanged(nameof(TimesheetPrimaryActionText));
            }
        }

        private int _editingTimesheetEntryId;
        public int EditingTimesheetEntryId
        {
            get => _editingTimesheetEntryId;
            set => SetField(ref _editingTimesheetEntryId, value, nameof(EditingTimesheetEntryId));
        }

        public string TimesheetPrimaryActionText => IsTimesheetEditMode ? "Save Changes" : "Add Entry";

        private ObservableCollection<ProjectCcfRefDetailsDto> _projectCcfRefs = new();
        public ObservableCollection<ProjectCcfRefDetailsDto> ProjectCcfRefs
        {
            get => _projectCcfRefs;
            set { _projectCcfRefs = value; OnPropertyChanged(nameof(ProjectCcfRefs)); }
        }

        private string _newProjectCcfRefCode = "";
        public string NewProjectCcfRefCode
        {
            get => _newProjectCcfRefCode;
            set { _newProjectCcfRefCode = value; OnPropertyChanged(nameof(NewProjectCcfRefCode)); }
        }

        private bool _showInactiveProjectCcfRefs;
        public bool ShowInactiveProjectCcfRefs
        {
            get => _showInactiveProjectCcfRefs;
            set
            {
                _showInactiveProjectCcfRefs = value;
                OnPropertyChanged(nameof(ShowInactiveProjectCcfRefs));
                _ = LoadProjectCcfRefsAsync(); // fire and forget refresh
            }
        }

        private ProjectSummaryDto? _selectedProjectForCcf;
        public ProjectSummaryDto? SelectedProjectForCcf
        {
            get => _selectedProjectForCcf;
            set
            {
                if (SetField(ref _selectedProjectForCcf, value, nameof(SelectedProjectForCcf)))
                {
                    _ = LoadProjectCcfRefsAsync(); // refresh when project changes
                }
            }
        }

        private string _addCcf_Code = "";
        public string AddCcf_Code
        {
            get => _addCcf_Code;
            set
            {
                if (SetField(ref _addCcf_Code, value, nameof(AddCcf_Code)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string _addCcf_EstimatedValueText = "";
        public string AddCcf_EstimatedValueText
        {
            get => _addCcf_EstimatedValueText;
            set
            {
                if (SetField(ref _addCcf_EstimatedValueText, value, nameof(AddCcf_EstimatedValueText)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string _addCcf_QuotedValueText = "";
        public string AddCcf_QuotedValueText
        {
            get => _addCcf_QuotedValueText;
            set
            {
                if (SetField(ref _addCcf_QuotedValueText, value, nameof(AddCcf_QuotedValueText)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string _addCcf_AgreedValueText = "";
        public string AddCcf_AgreedValueText
        {
            get => _addCcf_AgreedValueText;
            set
            {
                if (SetField(ref _addCcf_AgreedValueText, value, nameof(AddCcf_AgreedValueText)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string _addCcf_ActualValueText = "";
        public string AddCcf_ActualValueText
        {
            get => _addCcf_ActualValueText;
            set
            {
                if (SetField(ref _addCcf_ActualValueText, value, nameof(AddCcf_ActualValueText)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string _addCcf_Status = "Draft";
        public string AddCcf_Status
        {
            get => _addCcf_Status;
            set
            {
                if (SetField(ref _addCcf_Status, value, nameof(AddCcf_Status)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string? _addCcf_Notes = "";
        public string? AddCcf_Notes
        {
            get => _addCcf_Notes;
            set
            {
                if (SetField(ref _addCcf_Notes, value, nameof(AddCcf_Notes)))
                    OnPropertyChanged(nameof(CanSaveAddCcf));
            }
        }

        private string _addCcf_StatusText = "";
        public string AddCcf_StatusText
        {
            get => _addCcf_StatusText;
            set => SetField(ref _addCcf_StatusText, value, nameof(AddCcf_StatusText));
        }

        private DateTime? _addCcf_QuotedDate;
        public DateTime? AddCcf_QuotedDate
        {
            get => _addCcf_QuotedDate;
            set
            {
                if (_addCcf_QuotedDate != value)
                {
                    _addCcf_QuotedDate = value;
                    OnPropertyChanged(nameof(AddCcf_QuotedDate));
                    OnPropertyChanged(nameof(CanSaveAddCcf));
                }
            }
        }

        private DateTime? _addCcf_AgreedDate;
        public DateTime? AddCcf_AgreedDate
        {
            get => _addCcf_AgreedDate;
            set
            {
                if (_addCcf_AgreedDate != value)
                {
                    _addCcf_AgreedDate = value;
                    OnPropertyChanged(nameof(AddCcf_AgreedDate));
                    OnPropertyChanged(nameof(CanSaveAddCcf));
                }
            }
        }

        public IReadOnlyList<string> CcfStatuses { get; } =
            new List<string> { "Draft", "Quoted", "Agreed", "Rejected", "Invoiced" };

        private ProjectCcfRefDetailsDto? _selectedProjectCcfRef;
        public ProjectCcfRefDetailsDto? SelectedProjectCcfRef
        {
            get => _selectedProjectCcfRef;
            set 
            { 
                _selectedProjectCcfRef = value; 
                OnPropertyChanged(nameof(SelectedProjectCcfRef));
            }
        }

        private int? _editCcf_Id;
        public int? EditCcf_Id
        {
            get => _editCcf_Id;
            set { _editCcf_Id = value; OnPropertyChanged(nameof(EditCcf_Id)); }
        }

        private string _editCcf_Code = "";
        public string EditCcf_Code
        {
            get => _editCcf_Code;
            set { _editCcf_Code = value; OnPropertyChanged(nameof(EditCcf_Code)); }
        }

        private string _editCcf_EstimatedValueText = "";
        public string EditCcf_EstimatedValueText
        {
            get => _editCcf_EstimatedValueText;
            set
            {
                if (SetField(ref _editCcf_EstimatedValueText, value, nameof(EditCcf_EstimatedValueText)))
                    OnPropertyChanged(nameof(CanSaveEditCcf));
            }
        }

        private string _editCcf_QuotedValueText = "";
        public string EditCcf_QuotedValueText
        {
            get => _editCcf_QuotedValueText;
            set
            {
                if (SetField(ref _editCcf_QuotedValueText, value, nameof(EditCcf_QuotedValueText)))
                    OnPropertyChanged(nameof(CanSaveEditCcf));
            }
        }

        private DateTime? _editCcf_QuotedDate;
        public DateTime? EditCcf_QuotedDate
        {
            get => _editCcf_QuotedDate;
            set
            {
                if (_editCcf_QuotedDate != value)
                {
                    _editCcf_QuotedDate = value;
                    OnPropertyChanged(nameof(EditCcf_QuotedDate));
                    OnPropertyChanged(nameof(CanSaveEditCcf));
                }
            }
        }

        private string _editCcf_AgreedValueText = "";
        public string EditCcf_AgreedValueText
        {
            get => _editCcf_AgreedValueText;
            set
            {
                if (SetField(ref _editCcf_AgreedValueText, value, nameof(EditCcf_AgreedValueText)))
                    OnPropertyChanged(nameof(CanSaveEditCcf));
            }
        }

        private DateTime? _editCcf_AgreedDate;
        public DateTime? EditCcf_AgreedDate
        {
            get => _editCcf_AgreedDate;
            set
            {
                if (_editCcf_AgreedDate != value)
                {
                    _editCcf_AgreedDate = value;
                    OnPropertyChanged(nameof(EditCcf_AgreedDate));
                    OnPropertyChanged(nameof(CanSaveEditCcf));
                }
            }
        }

        private string _editCcf_ActualValueText = "";
        public string EditCcf_ActualValueText
        {
            get => _editCcf_ActualValueText;
            set
            {
                if (SetField(ref _editCcf_ActualValueText, value, nameof(EditCcf_ActualValueText)))
                    OnPropertyChanged(nameof(CanSaveEditCcf));
            }
        }

        private string _editCcf_Status = "Draft";
        public string EditCcf_Status
        {
            get => _editCcf_Status;
            set
            {
                if (SetField(ref _editCcf_Status, value, nameof(EditCcf_Status)))
                    OnPropertyChanged(nameof(CanSaveEditCcf));
            }
        }

        private string? _editCcf_Notes;
        public string? EditCcf_Notes
        {
            get => _editCcf_Notes;
            set
            {
                if (SetField(ref _editCcf_Notes, value, nameof(EditCcf_Notes)))
                    OnPropertyChanged(nameof(CanSaveEditCcf));
            }
        }

        private string _editCcf_StatusText = "";
        public string EditCcf_StatusText
        {
            get => _editCcf_StatusText;
            set => SetField(ref _editCcf_StatusText, value, nameof(EditCcf_StatusText));
        }

        public bool CanSaveAddCcf =>
            !string.IsNullOrWhiteSpace(AddCcf_Code) ||
            !string.IsNullOrWhiteSpace(AddCcf_EstimatedValueText) ||
            !string.IsNullOrWhiteSpace(AddCcf_QuotedValueText) ||
            AddCcf_QuotedDate != null ||
            !string.IsNullOrWhiteSpace(AddCcf_AgreedValueText) ||
            AddCcf_AgreedDate != null ||
            !string.IsNullOrWhiteSpace(AddCcf_ActualValueText) ||
            !string.IsNullOrWhiteSpace(AddCcf_Notes) ||
            AddCcf_Status != null;

        public bool CanSaveEditCcf =>
            !string.IsNullOrWhiteSpace(EditCcf_EstimatedValueText) ||
            !string.IsNullOrWhiteSpace(EditCcf_QuotedValueText) ||
            EditCcf_QuotedDate != null ||
            !string.IsNullOrWhiteSpace(EditCcf_AgreedValueText) ||
            EditCcf_AgreedDate != null ||
            !string.IsNullOrWhiteSpace(EditCcf_ActualValueText) ||
            !string.IsNullOrWhiteSpace(EditCcf_Notes) ||
            EditCcf_Status != null;

        private string _editProjectPrefixText = "";
        public string EditProjectPrefixText
        {
            get => _editProjectPrefixText;
            set => SetField(ref _editProjectPrefixText, value, nameof(EditProjectPrefixText));
        }

        private string _editProjectNumberText = "";
        public string EditProjectNumberText
        {
            get => _editProjectNumberText;
            set => SetField(ref _editProjectNumberText, value, nameof(EditProjectNumberText));
        }

        private string _editProjectNameText = "";
        public string EditProjectNameText
        {
            get => _editProjectNameText;
            set => SetField(ref _editProjectNameText, value, nameof(EditProjectNameText));
        }

        private string _editProjectJobKeyText = "";
        public string EditProjectJobKeyText
        {
            get => _editProjectJobKeyText;
            set => SetField(ref _editProjectJobKeyText, value, nameof(EditProjectJobKeyText));
        }

        private string _editProjectCompanyText = "";
        public string EditProjectCompanyText
        {
            get => _editProjectCompanyText;
            set => SetField(ref _editProjectCompanyText, value, nameof(EditProjectCompanyText));
        }

        private CompanyListItemDto? _editSelectedCompany;
        public CompanyListItemDto? EditSelectedCompany
        {
            get => _editSelectedCompany;
            set => SetField(ref _editSelectedCompany, value, nameof(EditSelectedCompany));
        }

        private bool _editProjectIsActive;
        public bool EditProjectIsActive
        {
            get => _editProjectIsActive;
            set => SetField(ref _editProjectIsActive, value, nameof(EditProjectIsActive));
        }

        private string _editProjectStatusText = "";
        public string EditProjectStatusText
        {
            get => _editProjectStatusText;
            set => SetField(ref _editProjectStatusText, value, nameof(EditProjectStatusText));
        }

        private ProjectEditDto? _loadedEditProject; // snapshot from API for cancel/reset

        private readonly Dictionary<string, (ProjectDrilldownDto dto, DateTime fetchedUtc)> _projectDrilldownCache
            = new(StringComparer.Ordinal);

        private static readonly TimeSpan ProjectDrilldownCacheTtl = TimeSpan.FromSeconds(30);

        private bool _suppressProjectSelectionChanged;

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

            RebuildNewProjectJobName();
            InitializeNewInvoice();
            ConfigureTimesheetDatePicker();

            Settings = SettingsService.LoadSettings();

            if (string.IsNullOrWhiteSpace(Settings.ApiBaseUrl))
            {
                // PROD default (adjust if your API lives elsewhere)
                Settings.ApiBaseUrl = "https://portal.mep1bim.co.uk/";
                SettingsService.SaveSettings(Settings);
            }

            _api = new ErpApiClient(
                Settings.ApiBaseUrl ?? "https://localhost:7254",
                Settings.ApiKey
            );

            // Desktop login (Admin/Owner only). Session stays in memory.
            var login = new LoginWindow(_api);
            var ok = login.ShowDialog();
            if (ok != true)
            {
                WpfApplication.Current.Shutdown();
                return;
            }

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

            if (SelectedProjectForCcf == null && ProjectSummaries.Count > 0)
            {
                SelectedProjectForCcf = ProjectSummaries.FirstOrDefault(p => p.IsActive) ?? ProjectSummaries[0];
            }

            InvoiceProjectPicklist = await _api.GetInvoiceProjectPicklistAsync();

            // quick debug:
            System.Diagnostics.Debug.WriteLine(
                string.Join(", ", ProjectSummaries.Take(5).Select(p => $"{p.JobNameOrNumber}:{p.IsActive}"))
            );

            // Invoices comes from API now
            Invoices = await _api.GetInvoicesAsync();

            SuggestNextInvoiceNumberIfEmpty();

            // People comes from API now
            People = await _api.GetPeopleSummaryAsync();

            // Timesheet tab setup (v1)
            TimesheetProjects = await _api.GetTimesheetActiveProjectsAsync();
            TimesheetCodes = await _api.GetTimesheetCodesAsync();

            // Default selection: first active person if none set
            if (TimesheetSelectedWorker == null)
                TimesheetSelectedWorker = People.FirstOrDefault(p => p.IsActive) ?? People.FirstOrDefault();

            SelectedTimesheetProject ??= TimesheetProjects.FirstOrDefault();

            await RefreshTimesheetEntriesAsync();

            // Due Schedule comes from API now
            DueSchedule = await _api.GetDueScheduleAsync();

            // Upcoming Applications comes from API now
            UpcomingApplications = await _api.GetUpcomingApplicationsAsync(Settings.UpcomingApplicationsDaysAhead);

            await LoadAuditLogsAsync();

            EnsurePeopleView();
            EnsureInvoiceView();
            EnsureProjectView();
            LoadSuppliers();
            await LoadCompaniesAsync();
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

        private void ApplyInvoiceFilter()
        {
            // Invoices is reassigned (new List instance) after reload, so rebuild the view.
            EnsureInvoiceView();

            // Re-apply the current predicate (Filter delegates to _invoiceFilterPredicate)
            InvoiceView?.Refresh();
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
                _ = LoadSelectedProjectDetailsAsync(SelectedProject);
                await LoadEditProjectAsync(SelectedProject.JobNameOrNumber);
            }

            if (People.Count > 0 && SelectedPerson == null)
            {
                SelectedPerson = People[0];
                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
            }

            if (ProjectSummaries.Count > 0 && SelectedProjectForCcf == null)
            {
                SelectedProjectForCcf = ProjectSummaries[0];
            }
        }

        private async Task RefreshProjectDependentPicklistsAsync()
        {
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine("[PERF ▶] RefreshProjectDependentPicklistsAsync");

            // Preserve selections (because new lists = new object instances)
            var newInvoiceJobKey = NewInvoiceSelectedProject?.JobNameOrNumber;
            var editInvoiceJobKey = EditInvoiceSelectedProject?.JobNameOrNumber;
            var timesheetJobKey = SelectedTimesheetProject?.JobKey;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            InvoiceProjectPicklist = await _api.GetInvoiceProjectPicklistAsync();
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] GetInvoiceProjectPicklistAsync = {sw.ElapsedMilliseconds} ms");

            if (!string.IsNullOrWhiteSpace(newInvoiceJobKey))
                NewInvoiceSelectedProject = InvoiceProjectPicklist.FirstOrDefault(p => p.JobNameOrNumber == newInvoiceJobKey);

            if (!string.IsNullOrWhiteSpace(editInvoiceJobKey))
                EditInvoiceSelectedProject = InvoiceProjectPicklist.FirstOrDefault(p => p.JobNameOrNumber == editInvoiceJobKey);

            sw.Restart();
            TimesheetProjects = await _api.GetTimesheetActiveProjectsAsync();
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] GetTimesheetActiveProjectsAsync = {sw.ElapsedMilliseconds} ms");

            if (!string.IsNullOrWhiteSpace(timesheetJobKey))
                SelectedTimesheetProject = TimesheetProjects.FirstOrDefault(p => p.JobKey == timesheetJobKey);

            swAll.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF ◀] RefreshProjectDependentPicklistsAsync = {swAll.ElapsedMilliseconds} ms");
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

        private async Task LoadCompaniesAsync()
        {
            Companies.Clear();

            var companies = await _api.GetCompaniesAsync();
            foreach (var c in companies)
                Companies.Add(c);
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
                await Perf("SetProjectActiveAsync", async () =>
                {
                    await _api.SetProjectActiveAsync(jobKey, newIsActive);
                });

                await Perf("ReloadProjectSummariesAsync", async () =>
                {
                    await ReloadProjectSummariesAsync(keepSelection: true);
                });

                // Don't await: but DO log when it finishes
                _ = Task.Run(async () =>
                {
                    await Perf("RefreshProjectDependentPicklistsAsync", async () =>
                    {
                        await RefreshProjectDependentPicklistsAsync();
                    });
                });

                await Perf("RefreshProjects (UI-only)", async () =>
                {
                    RefreshProjects(keepSelection: true);
                    await Task.CompletedTask;
                });
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
                        Id = r.Id,                    
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

            await LoadEditPersonAsync(person.WorkerId); // NEW
        }

        private async Task LoadSelectedProjectDetailsAsync(ProjectSummaryDto projSummary)
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
                ProjectDrilldownDto drill;

                var cacheKey = projSummary.JobNameOrNumber;
                if (_projectDrilldownCache.TryGetValue(cacheKey, out var cached) &&
                    (DateTime.UtcNow - cached.fetchedUtc) <= ProjectDrilldownCacheTtl)
                {
                    drill = cached.dto;
                }
                else
                {
                    drill = await _api.GetProjectDrilldownAsync(cacheKey, recentTake: 25);
                    _projectDrilldownCache[cacheKey] = (drill, DateTime.UtcNow);
                }

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
            if (_suppressProjectSelectionChanged) return;

            if (sender is not DataGrid grid)
                return;

            if (grid.SelectedItem is not ProjectSummaryDto proj)
                return;

            SelectedProject = proj;

            // 1) Drilldown (heavy) - still loads
            _ = LoadSelectedProjectDetailsAsync(proj);

            // 2) CCF refs (separate call)
            _ = LoadProjectCcfRefsAsync();

            // IMPORTANT: do NOT preload Edit Project here.
            // Only load edit data when user actually clicks the Edit button / opens the tab.
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

                // Reload project summaries so profit updates
                await ReloadProjectSummariesAsync(keepSelection: true);
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
                await ReloadProjectSummariesAsync(keepSelection: true);
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
                await ReloadProjectSummariesAsync(keepSelection: true);
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

        private void RefreshProjects(bool keepSelection)
        {
            // UI-only refresh. NO API calls here.
            var selectedJob = keepSelection ? SelectedProject?.JobNameOrNumber : null;

            EnsureProjectView();

            // When ProjectSummaries is replaced, SelectedProject may point to an old object instance.
            // Re-bind it to the new instance so DataGrid selection stays sane.
            if (keepSelection && !string.IsNullOrWhiteSpace(selectedJob))
            {
                var match = ProjectSummaries.FirstOrDefault(p => p.JobNameOrNumber == selectedJob);
                if (match != null && !ReferenceEquals(SelectedProject, match))
                {
                    _suppressProjectSelectionChanged = true;
                    try
                    {
                        SelectedProject = match;
                    }
                    finally
                    {
                        _suppressProjectSelectionChanged = false;
                    }
                }
            }
        }

        private async Task ReloadProjectSummariesAsync(bool keepSelection)
        {
            var selectedJob = keepSelection ? SelectedProject?.JobNameOrNumber : null;

            ProjectSummaries = await _api.GetProjectSummariesAsync();

            // Rebuild/re-apply project view filtering
            EnsureProjectView();

            // Re-select project using the *new* object instances
            if (keepSelection && !string.IsNullOrWhiteSpace(selectedJob))
            {
                var match = ProjectSummaries.FirstOrDefault(p => p.JobNameOrNumber == selectedJob);
                if (match != null)
                {
                    _suppressProjectSelectionChanged = true;
                    try
                    {
                        SelectedProject = match;
                    }
                    finally
                    {
                        _suppressProjectSelectionChanged = false;
                    }
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

                    // if it's taken, auto-suggest a free one (jason.dean2, etc.)
                    try
                    {
                        await EnsurePortalUsernameAvailableAsync(showMessageIfTaken: false);
                    }
                    catch
                    {
                        // ignore here (prefill is QoL only); create will still validate
                    }

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

            // server-check availability before creating
            try
            {
                var ok = await EnsurePortalUsernameAvailableAsync(showMessageIfTaken: true);
                if (!ok)
                    return; // user can just click Create again (now with suggested username)
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to validate username availability:\n\n" + ex.Message,
                    "Portal Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                var created = await _api.CreatePortalAccessAsync(
                    SelectedPerson.WorkerId,
                    new CreatePortalAccessRequestDto(PortalUsernameText.Trim(), SelectedPortalRole));

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
                    new UpdatePortalAccessRequestDto(
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
            if (string.IsNullOrWhiteSpace(fullName))
                return "";

            var parts = fullName.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return "";

            var raw = parts.Length == 1
                ? parts[0]
                : parts[0] + "." + parts[parts.Length - 1];

            // letters/digits/dot only
            raw = raw.ToLowerInvariant();

            var chars = raw.Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray();
            return new string(chars);
        }

        private async Task<bool> EnsurePortalUsernameAvailableAsync(bool showMessageIfTaken)
        {
            var typed = (PortalUsernameText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(typed))
                return false;

            var check = await _api.GetPortalUsernameAvailabilityAsync(typed);

            // If available, keep exactly what the user typed (but you *could* replace with check.Normalized if you want)
            if (check.Available)
                return true;

            // Not available: switch to suggested and notify UI
            PortalUsernameText = check.Suggested ?? typed;
            OnPropertyChanged(nameof(PortalUsernameText));

            if (showMessageIfTaken)
            {
                WpfMessageBox.Show(
                    $"That portal username is already taken.\n\nSuggested: {PortalUsernameText}",
                    "Portal Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
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

        private async Task LoadAuditLogsAsync()
        {
            try
            {
                int? actorId = null;
                if (!string.IsNullOrWhiteSpace(AuditActorWorkerIdText) &&
                    int.TryParse(AuditActorWorkerIdText.Trim(), out var parsed))
                {
                    actorId = parsed;
                }

                AuditLogs = await _api.GetAuditLogsAsync(
                    take: 300,
                    skip: 0,
                    search: string.IsNullOrWhiteSpace(AuditSearchText) ? null : AuditSearchText,
                    entityType: string.IsNullOrWhiteSpace(AuditEntityTypeText) ? null : AuditEntityTypeText,
                    entityId: string.IsNullOrWhiteSpace(AuditEntityIdText) ? null : AuditEntityIdText,
                    actorWorkerId: actorId,
                    action: string.IsNullOrWhiteSpace(AuditActionText) ? null : AuditActionText
                );

                OnPropertyChanged(nameof(AuditLogs));
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to load audit logs:\n\n" + ex.Message,
                    "API error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void RefreshAudit_Click(object sender, RoutedEventArgs e)
        {
            await LoadAuditLogsAsync();
        }

        private async Task LoadEditPersonAsync(int workerId)
        {
            EditPersonStatusText = "";
            EditRatesStatusText = "";

            try
            {
                // Needs new API endpoint / DTO (see API section below)
                var dto = await _api.GetWorkerForEditAsync(workerId);

                EditPersonInitialsText = dto.Initials ?? "";
                EditPersonNameText = dto.Name ?? "";
                EditPersonSignatureNameText = dto.SignatureName ?? "";

                EditPersonRates = dto.Rates
                    .Select(r => new WorkerRate
                    {
                        Id = r.Id,                    // NEW
                        ValidFrom = r.ValidFrom,
                        ValidTo = r.ValidTo,
                        RatePerHour = r.RatePerHour
                    })
                    .OrderByDescending(r => r.ValidFrom)
                    .ToList();

                SelectedEditPersonRate = null;
            }
            catch (Exception ex)
            {
                EditPersonStatusText = "Failed to load edit data: " + ex.Message;
                EditPersonRates = new();
                SelectedEditPersonRate = null;
            }
        }

        private async void ReloadEditPerson_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;
            await LoadEditPersonAsync(SelectedPerson.WorkerId);
        }

        private async void SavePersonDetails_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;

            EditPersonStatusText = "";

            var initials = (EditPersonInitialsText ?? "").Trim();
            var name = (EditPersonNameText ?? "").Trim();
            var signature = string.IsNullOrWhiteSpace(EditPersonSignatureNameText) ? null : EditPersonSignatureNameText.Trim();

            if (string.IsNullOrWhiteSpace(initials))
            {
                EditPersonStatusText = "Initials are required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                EditPersonStatusText = "Name is required.";
                return;
            }

            try
            {
                await _api.UpdateWorkerDetailsAsync(
                    SelectedPerson.WorkerId,
                    new UpdateWorkerDetailsRequestDto
                    {
                        Initials = initials,
                        Name = name,
                        SignatureName = signature
                    });

                EditPersonStatusText = "Saved.";

                // Refresh left list + keep selection so grid shows updated values
                await RefreshPeopleAsync(keepSelection: true);

                // Reload edit + drilldown for consistency
                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
                await LoadEditPersonAsync(SelectedPerson.WorkerId);
            }
            catch (Exception ex)
            {
                EditPersonStatusText = "Save failed: " + ex.Message;
            }
        }

        private async void ChangeCurrentRate_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;

            EditRatesStatusText = "";

            if (!ChangeCurrentEffectiveFrom.HasValue)
            {
                EditRatesStatusText = "Effective From is required.";
                return;
            }

            var rateText = (ChangeCurrentRateText ?? "").Trim().Replace("£", "");
            if (!decimal.TryParse(rateText, out var newRate) || newRate < 0)
            {
                EditRatesStatusText = "New rate must be a valid non-negative number.";
                return;
            }

            try
            {
                await _api.ChangeCurrentWorkerRateAsync(
                    SelectedPerson.WorkerId,
                    new ChangeCurrentRateRequestDto(
                        EffectiveFrom: ChangeCurrentEffectiveFrom.Value.Date,
                        NewRatePerHour: newRate));

                EditRatesStatusText = "Current rate changed (split applied).";

                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
                await LoadEditPersonAsync(SelectedPerson.WorkerId);
                await RefreshPeopleAsync(keepSelection: true);
            }
            catch (Exception ex)
            {
                EditRatesStatusText = "Change current rate failed: " + ex.Message;
            }
        }

        private async void AddHistoricalRate_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;

            EditRatesStatusText = "";

            if (!AddRateValidFrom.HasValue || !AddRateValidTo.HasValue)
            {
                EditRatesStatusText = "Valid From and Valid To are required for historical rates.";
                return;
            }

            if (AddRateValidTo.Value.Date < AddRateValidFrom.Value.Date)
            {
                EditRatesStatusText = "Valid To cannot be before Valid From.";
                return;
            }

            var rateText = (AddRateAmountText ?? "").Trim().Replace("£", "");
            if (!decimal.TryParse(rateText, out var amount) || amount < 0)
            {
                EditRatesStatusText = "Rate must be a valid non-negative number.";
                return;
            }

            try
            {
                await _api.AddWorkerRateAsync(
                    SelectedPerson.WorkerId,
                    new AddWorkerRateRequestDto(
                        ValidFrom: AddRateValidFrom.Value.Date,
                        ValidTo: AddRateValidTo.Value.Date,
                        RatePerHour: amount));

                EditRatesStatusText = "Historical rate added.";

                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
                await LoadEditPersonAsync(SelectedPerson.WorkerId);
                await RefreshPeopleAsync(keepSelection: true);
            }
            catch (Exception ex)
            {
                EditRatesStatusText = "Add historical rate failed: " + ex.Message;
            }
        }

        private async void UpdateSelectedRateAmount_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;
            if (SelectedEditPersonRate == null) return;

            EditRatesStatusText = "";

            var rateText = (UpdateSelectedRateAmountText ?? "").Trim().Replace("£", "");
            if (!decimal.TryParse(rateText, out var amount) || amount < 0)
            {
                EditRatesStatusText = "Rate must be a valid non-negative number.";
                return;
            }

            try
            {
                // Needs rate Id in the grid model; see API section (you likely want WorkerRateDto.Id)
                await _api.UpdateWorkerRateAmountAsync(
                    SelectedPerson.WorkerId,
                    SelectedEditPersonRate.Id,
                    new UpdateWorkerRateAmountRequestDto(amount));

                EditRatesStatusText = "Selected rate updated.";

                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
                await LoadEditPersonAsync(SelectedPerson.WorkerId);
                await RefreshPeopleAsync(keepSelection: true);
            }
            catch (Exception ex)
            {
                EditRatesStatusText = "Update failed: " + ex.Message;
            }
        }

        private async void DeleteSelectedRate_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPerson == null) return;
            if (SelectedEditPersonRate == null) return;

            var result = System.Windows.MessageBox.Show(
                "Delete this rate?\n\nThis should only be used to correct mistakes.",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _api.DeleteWorkerRateAsync(SelectedPerson.WorkerId, SelectedEditPersonRate.Id);

                EditRatesStatusText = "Deleted.";

                await LoadSelectedPersonDetails(SelectedPerson.WorkerId);
                await LoadEditPersonAsync(SelectedPerson.WorkerId);
                await RefreshPeopleAsync(keepSelection: true);
            }
            catch (Exception ex)
            {
                EditRatesStatusText = "Delete failed: " + ex.Message;
            }
        }

        private async void AddProject_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAddProject)
            {
                AddProjectStatusText = "Please fill in Prefix, Number, Job name, and CompanyCode before adding the project.";
                OnPropertyChanged(nameof(AddProjectStatusText));
                return;
            }

            AddProjectStatusText = "";

            var job = (NewProjectJobNameText ?? "").Trim();
            var company = (NewProjectCompanyText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(job))
            {
                AddProjectStatusText = "Job name / number is required.";
                return;
            }

            try
            {
                var req = new CreateProjectRequestDto
                {
                    JobNameOrNumber = job,
                    CompanyCode = string.IsNullOrWhiteSpace(company) ? null : company,
                    IsActive = NewProjectIsActive
                };

                var created = await _api.CreateProjectAsync(req);

                AddProjectStatusText = $"Created project \"{created.JobNameOrNumber}\".";

                // Clear inputs
                NewProjectNumberText = "";
                NewProjectNameText = "";
                SelectedProjectPrefix = ProjectPrefixOptions.FirstOrDefault() ?? "D";

                NewProjectCompanyText = "";
                NewProjectIsActive = true;

                RebuildNewProjectJobName();

                // Refresh and auto-select the created project
                ProjectSummaries = await _api.GetProjectSummariesAsync();
                EnsureProjectView();

                var match = ProjectSummaries.FirstOrDefault(p => p.JobNameOrNumber == created.JobNameOrNumber);
                if (match != null)
                {
                    SelectedProject = match;
                    _ = LoadSelectedProjectDetailsAsync(match);
                }

                // Refresh dependent dropdowns (Add Invoice project dropdown, Timesheet projects, etc.)
                await RefreshProjectDependentPicklistsAsync();
            }
            catch (Exception ex)
            {
                AddProjectStatusText = $"Error creating project: {ex.Message}";
            }
        }

        private static string? GetComboSelectionText(object? selection)
        {
            // When binding SelectedItem and using ComboBoxItem in XAML,
            // selection will usually be ComboBoxItem.
            if (selection is ComboBoxItem cbi)
                return cbi.Content?.ToString();

            return selection?.ToString();
        }

        private static bool TryParseMoney(string? input, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Remove common currency symbols and whitespace, but DO NOT delete commas blindly.
            // Let the culture/NumberStyles handle thousands separators properly.
            var cleaned = input.Trim()
                .Replace("£", "")
                .Replace(" ", "");

            const System.Globalization.NumberStyles styles =
                System.Globalization.NumberStyles.AllowLeadingWhite |
                System.Globalization.NumberStyles.AllowTrailingWhite |
                System.Globalization.NumberStyles.AllowThousands |
                System.Globalization.NumberStyles.AllowDecimalPoint |
                System.Globalization.NumberStyles.AllowLeadingSign;

            // UK-first (your business context), then current machine culture, then invariant.
            var uk = System.Globalization.CultureInfo.GetCultureInfo("en-GB");

            if (decimal.TryParse(cleaned, styles, uk, out value))
                return true;

            if (decimal.TryParse(cleaned, styles, System.Globalization.CultureInfo.CurrentCulture, out value))
                return true;

            return decimal.TryParse(cleaned, styles, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseVatRate(string? selectionText, out decimal rate)
        {
            rate = 0m;

            if (string.IsNullOrWhiteSpace(selectionText))
                return false;

            var t = selectionText.Trim();

            // Expect "20%" / "5%" / "0%"
            if (t.EndsWith("%", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1).Trim();

            if (!decimal.TryParse(t,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var percent))
                return false;

            rate = percent / 100m;
            return true;
        }

        private void RecalculateAddInvoiceTotals()
        {
            // Default: blank computed fields unless we have valid inputs.
            NewInvoiceVatAmountText = "";
            NewInvoiceGrossAmountText = "";

            if (!TryParseMoney(NewInvoiceNetAmountText, out var net))
                return;

            var vatSelectionText = GetComboSelectionText(NewInvoiceVatRateText);
            if (!TryParseVatRate(vatSelectionText, out var vatRate))
                return;

            var vat = Math.Round(net * vatRate, 2, MidpointRounding.AwayFromZero);
            var gross = net + vat;

            NewInvoiceVatAmountText = vat.ToString("0.00");
            NewInvoiceGrossAmountText = gross.ToString("0.00");
        }

        private void UpdateAddInvoiceValidation()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(NewInvoiceNumberText))
                problems.Add("Invoice number is required.");

            if (NewInvoiceSelectedProject == null)
                problems.Add("Project is required.");

            if (NewInvoiceSelectedProject != null && string.IsNullOrWhiteSpace(NewInvoiceDerivedCompanyName))
                problems.Add("Company could not be derived from the selected project.");

            if (!NewInvoiceDate.HasValue)
                problems.Add("Invoice date is required.");

            if (NewInvoiceDueDate == null)
                problems.Add("Due date required.");

            if (!TryParseMoney(NewInvoiceNetAmountText, out var net) || net <= 0m)
                problems.Add("Net amount must be a number greater than 0 (e.g. 1200.00).");

            var vatSelectionText = GetComboSelectionText(NewInvoiceVatRateText);
            if (!TryParseVatRate(vatSelectionText, out _))
                problems.Add("VAT rate must be selected.");

            // Status should exist (we default it, but be explicit)
            var statusText = GetComboSelectionText(NewInvoiceStatusText);
            if (string.IsNullOrWhiteSpace(statusText))
                problems.Add("Status must be selected.");

            AddInvoiceValidationText = problems.Count == 0
                ? "Ready to save."
                : string.Join("\n", problems);
        }

        private void InitializeNewInvoice()
        {
            NewInvoiceDate = DateTime.Today;
            NewInvoiceDueDate = null;

            // Defaults
            NewInvoiceVatRateText = "20%";
            NewInvoiceStatusText = "Outstanding";

            AddInvoiceStatusText = "";
            AddInvoiceValidationText = "";

            RecalculateAddInvoiceTotals();
            UpdateAddInvoiceValidation();
        }

        private void ClearAddInvoiceForm_Click(object sender, RoutedEventArgs e)
        {
            ClearAddInvoiceForm(setStatusMessage: true);
        }

        private async void AddInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddInvoiceStatusText = "";
                UpdateAddInvoiceValidation();

                if (!string.Equals(AddInvoiceValidationText, "Ready to save.", StringComparison.Ordinal))
                {
                    AddInvoiceStatusText = "Fix the required fields before saving.";
                    return;
                }

                if (NewInvoiceSelectedProject == null)
                {
                    AddInvoiceStatusText = "Project is required.";
                    return;
                }

                if (!TryParseMoney(NewInvoiceNetAmountText, out var net) || net <= 0m)
                {
                    AddInvoiceStatusText = "Net amount must be > 0.";
                    return;
                }

                var vatSelectionText = GetComboSelectionText(NewInvoiceVatRateText);
                if (!TryParseVatRate(vatSelectionText, out var vatRate))
                {
                    AddInvoiceStatusText = "VAT rate must be selected.";
                    return;
                }

                var statusText = GetComboSelectionText(NewInvoiceStatusText);
                if (string.IsNullOrWhiteSpace(statusText))
                    statusText = "Unpaid";

                if (!NewInvoiceDate.HasValue)
                {
                    AddInvoiceStatusText = "Invoice date is required.";
                    return;
                }

                if (!NewInvoiceDueDate.HasValue)
                {
                    AddInvoiceStatusText = "Due date is required.";
                    return;
                }

                var dto = new CreateInvoiceRequestDto
                {
                    ProjectId = NewInvoiceSelectedProject.ProjectId,
                    InvoiceNumber = NewInvoiceNumberText.Trim(),
                    InvoiceDate = NewInvoiceDate.Value.Date,
                    DueDate = NewInvoiceDueDate.Value.Date,
                    NetAmount = net,
                    VatRate = vatRate,
                    Status = statusText.Trim(),
                    Notes = string.IsNullOrWhiteSpace(NewInvoiceNotesText) ? null : NewInvoiceNotesText.Trim()
                };

                AddInvoiceStatusText = "Saving invoice...";
                var created = await _api.CreateInvoiceAsync(dto);

                AddInvoiceStatusText = $"Saved: {created.InvoiceNumber} ({created.CompanyName}) - £{created.GrossAmount:0.00}";

                // Refresh invoices list + view without touching your filter logic:
                Invoices = await _api.GetInvoicesAsync();
                ApplyInvoiceFilter();

                // Optional: clear form after save
                ClearAddInvoiceForm(setStatusMessage: false);

                SuggestNextInvoiceNumberIfEmpty();
            }
            catch (Exception ex)
            {
                AddInvoiceStatusText = ex.Message;
            }
        }

        private void DeriveInvoiceCompanyFromProject()
        {
            NewInvoiceDerivedCompanyName = "";

            var proj = NewInvoiceSelectedProject;
            if (proj == null)
                return;

            // v1: derive directly from picklist (no reflection, no guessing)
            NewInvoiceDerivedCompanyName = proj.CompanyName ?? "";
        }

        private void SuggestNextInvoiceNumberIfEmpty()
        {
            if (!string.IsNullOrWhiteSpace(NewInvoiceNumberText))
                return;

            if (Invoices == null || Invoices.Count == 0)
                return;

            var max = -1;

            foreach (var inv in Invoices)
            {
                var s = inv.InvoiceNumber?.Trim();
                if (string.IsNullOrWhiteSpace(s))
                    continue;

                // Take leading digits only (handles 0507a, 0507b, etc.)
                int i = 0;
                while (i < s.Length && char.IsDigit(s[i])) i++;

                if (i == 0)
                    continue;

                var prefix = s.Substring(0, i);

                if (int.TryParse(prefix, out var n))
                {
                    if (n > max) max = n;
                }
            }

            if (max < 0)
                return;

            var next = max + 1;

            // Keep your desired leading zeros (0500 style)
            NewInvoiceNumberText = next.ToString("D4");

            UpdateAddInvoiceValidation();
        }

        private void ClearAddInvoiceForm(bool setStatusMessage)
        {
            // Reset fields
            NewInvoiceNumberText = "";
            NewInvoiceDate = DateTime.Today;
            NewInvoiceDueDate = null;

            NewInvoiceSelectedProject = null;

            NewInvoiceDerivedCompanyName = "";
            NewInvoiceNetAmountText = "";
            NewInvoiceNotesText = "";

            // Default selections
            NewInvoiceVatRateText = "20%";
            NewInvoiceStatusText = "Outstanding";

            RecalculateAddInvoiceTotals();
            UpdateAddInvoiceValidation();
            SuggestNextInvoiceNumberIfEmpty();

            if (setStatusMessage)
                AddInvoiceStatusText = "Cleared.";
        }

        private void UpdateEditInvoiceSelectedSummary()
        {
            if (SelectedInvoiceListItem == null)
            {
                EditInvoiceSelectedSummaryText = "No invoice selected.";
                return;
            }

            EditInvoiceSelectedSummaryText = $"{SelectedInvoiceListItem.InvoiceNumber} - {SelectedInvoiceListItem.ClientName} ({SelectedInvoiceListItem.JobName})";
        }

        private decimal ParseVatRateText(string vatText)
        {
            if (string.IsNullOrWhiteSpace(vatText)) return 0.20m;
            vatText = vatText.Trim().Replace("%", "");
            if (decimal.TryParse(vatText, out var pct))
                return pct / 100m;
            return 0.20m;
        }

        private void RecalculateEditInvoiceTotals()
        {
            if (!TryParseMoney(EditInvoiceNetAmountText, out var net) || net <= 0m)
            {
                EditInvoiceVatAmountText = "0.00";
                EditInvoiceGrossAmountText = "0.00";
                return;
            }

            var vatRate = ParseVatRateText(EditInvoiceVatRateText);
            var vat = Math.Round(net * vatRate, 2, MidpointRounding.AwayFromZero);
            var gross = net + vat;

            EditInvoiceVatAmountText = vat.ToString("0.00");
            EditInvoiceGrossAmountText = gross.ToString("0.00");
        }

        private void UpdateEditInvoiceValidation()
        {
            var issues = new List<string>();

            if (EditInvoiceLoadedId == null)
                issues.Add("Load an invoice first.");

            if (EditInvoiceDate == null)
                issues.Add("Invoice date is required.");

            if (!TryParseMoney(EditInvoiceNetAmountText, out var net) || net <= 0m)
                issues.Add("Net amount must be a valid number > 0.");

            if (EditInvoiceSelectedProject == null)
                issues.Add("Project is required.");

            EditInvoiceValidationText = issues.Count == 0
                ? "Ready to save."
                : string.Join(Environment.NewLine, issues);
        }

        private async void LoadSelectedInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedInvoiceListItem == null)
            {
                EditInvoiceStatusBarText = "Select an invoice in the list first.";
                return;
            }

            try
            {
                EditInvoiceStatusBarText = "Loading invoice...";
                var dto = await _api.GetInvoiceByIdAsync(SelectedInvoiceListItem.Id);

                EditInvoiceLoadedId = dto.Id;
                EditInvoiceNumberText = dto.InvoiceNumber;

                EditInvoiceStatusText = string.IsNullOrWhiteSpace(dto.Status) ? "Outstanding" : dto.Status;
                EditInvoiceDate = dto.InvoiceDate;
                EditInvoiceDueDate = dto.DueDate;

                // Select project in picklist if possible
                if (dto.ProjectId.HasValue)
                    EditInvoiceSelectedProject = InvoiceProjectPicklist?.FirstOrDefault(p => p.ProjectId == dto.ProjectId.Value);
                else
                    EditInvoiceSelectedProject = null;

                EditInvoiceDerivedCompanyName = dto.CompanyName ?? "";

                EditInvoiceNetAmountText = dto.NetAmount.ToString("0.00");
                EditInvoiceVatRateText = $"{(dto.VatRate * 100m):0}%";
                RecalculateEditInvoiceTotals();

                EditInvoiceNotesText = dto.Notes ?? "";

                EditInvoiceStatusBarText = "Loaded. Make changes and click Save Changes.";
                UpdateEditInvoiceValidation();
            }
            catch (Exception ex)
            {
                EditInvoiceStatusBarText = $"Load failed: {ex.Message}";
            }
        }

        private async void SaveInvoiceEdits_Click(object sender, RoutedEventArgs e)
        {
            if (EditInvoiceLoadedId == null)
            {
                EditInvoiceStatusBarText = "Nothing loaded. Select an invoice and click Load selected.";
                return;
            }

            UpdateEditInvoiceValidation();
            if (EditInvoiceValidationText != "Ready to save.")
            {
                EditInvoiceStatusBarText = "Fix validation issues before saving.";
                return;
            }

            if (!TryParseMoney(EditInvoiceNetAmountText, out var net))
            {
                EditInvoiceStatusBarText = "Net amount is invalid.";
                return;
            }

            var vatRate = ParseVatRateText(EditInvoiceVatRateText);

            try
            {
                EditInvoiceStatusBarText = "Saving changes...";

                var req = new UpdateInvoiceRequestDto
                {
                    ProjectId = EditInvoiceSelectedProject?.ProjectId,
                    InvoiceDate = EditInvoiceDate!.Value,
                    DueDate = EditInvoiceDueDate,
                    NetAmount = net,
                    VatRate = vatRate,
                    Status = EditInvoiceStatusText ?? "Outstanding",
                    Notes = string.IsNullOrWhiteSpace(EditInvoiceNotesText) ? null : EditInvoiceNotesText.Trim()
                };

                await _api.UpdateInvoiceAsync(EditInvoiceLoadedId.Value, req);

                EditInvoiceStatusBarText = "Saved.";

                // Refresh invoices list so grid reflects changes
                await RefreshInvoicesAsync();
            }
            catch (Exception ex)
            {
                EditInvoiceStatusBarText = $"Save failed: {ex.Message}";
            }
        }

        private void ClearEditInvoiceForm_Click(object sender, RoutedEventArgs e)
        {
            EditInvoiceLoadedId = null;
            EditInvoiceNumberText = "";
            EditInvoiceStatusText = "Outstanding";
            EditInvoiceDate = DateTime.Today;
            EditInvoiceDueDate = null;

            EditInvoiceSelectedProject = null;
            EditInvoiceDerivedCompanyName = "";

            EditInvoiceNetAmountText = "";
            EditInvoiceVatRateText = "20%";
            EditInvoiceNotesText = "";

            RecalculateEditInvoiceTotals();
            UpdateEditInvoiceValidation();

            EditInvoiceStatusBarText = "Cleared. Select an invoice and click Load selected.";
        }

        private async Task RefreshInvoicesAsync()
        {
            await LoadInvoicesAsync();
        }

        private async Task LoadInvoicesAsync()
        {
            // Pull latest invoices from API
            Invoices = await _api.GetInvoicesAsync();

            // Rebuild the view because Invoices is a new List instance after reload
            ApplyInvoiceFilter();

            // Optional QoL: if the Add form invoice number is blank, re-suggest
            SuggestNextInvoiceNumberIfEmpty();
        }

        private async Task RefreshTimesheetEntriesAsync()
        {
            try
            {
                TimesheetStatusText = "Loading...";
                var subjectId = TimesheetSelectedWorker?.WorkerId;

                TimesheetEntries = await _api.GetTimesheetEntriesAsync(
                    skip: 0,
                    take: 50,
                    subjectWorkerId: subjectId
                );

                TimesheetStatusText = $"Loaded {TimesheetEntries.Count} entries.";
            }
            catch (Exception ex)
            {
                TimesheetStatusText = "Failed to load timesheet entries.";
                WpfMessageBox.Show(ex.Message, "Timesheet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshTimesheet_Click(object sender, RoutedEventArgs e)
        {
            await RefreshTimesheetEntriesAsync();
        }

        private async void CreateTimesheetEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryValidateTimesheetEntry(
                    out var err,
                    out var hours,
                    out var code,
                    out var ccf,
                    out var task,
                    out var workType,
                    out var levels,
                    out var areas))
                {
                    TimesheetStatusText = err;
                    WpfMessageBox.Show(err, "Timesheet", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Sanity (validator already checks these)
                if (TimesheetSelectedWorker == null)
                    throw new InvalidOperationException("Select a worker.");

                if (SelectedTimesheetProject == null)
                    throw new InvalidOperationException("Select a project.");

                var dto = new CreateTimesheetEntryDto(
                    WorkerId: TimesheetSelectedWorker.WorkerId,
                    JobKey: SelectedTimesheetProject.JobKey,
                    Date: TimesheetDate.Date,
                    Hours: hours,
                    Code: code,
                    CcfRef: ccf,
                    TaskDescription: task,
                    WorkType: workType,
                    Levels: levels,
                    Areas: areas
                );

                TimesheetStatusText = "Creating entry...";

                // On-behalf routing uses subjectWorkerId
                await _api.CreateTimesheetEntryAsync(dto, subjectWorkerId: TimesheetSelectedWorker.WorkerId);

                TimesheetStatusText = "Created.";

                // Clear (keep code/work type in place; clear hours/task/ccf)
                TimesheetHoursText = "";
                TimesheetTaskDescriptionText = "";
                TimesheetCcfRefText = "";

                await RefreshTimesheetEntriesAsync();
            }
            catch (Exception ex)
            {
                TimesheetStatusText = "Create failed.";
                WpfMessageBox.Show(ex.Message, "Timesheet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string NormalizeCode(string? code)
        {
            return (code ?? "").Trim().ToUpperInvariant();
        }

        private static bool TryParseHours(string? text, out decimal hours)
        {
            hours = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            // be forgiving: allow "1,5"
            var cleaned = text.Trim().Replace(',', '.');

            return decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out hours);
        }

        /// <summary>
        /// Validates entry inputs and applies the same rule-enforcement as Timesheet Web.
        /// Returns true when valid, and outputs cleaned/enforced values.
        /// </summary>
        private bool TryValidateTimesheetEntry(
            out string errorMessage,
            out decimal validatedHours,
            out string validatedCode,
            out string? cleanedCcfRef,
            out string? cleanedTaskDescription,
            out string? cleanedWorkType,
            out List<string> validatedLevels,
            out List<string> validatedAreas)
        {
            errorMessage = "";
            validatedHours = 0;
            validatedCode = "";
            cleanedCcfRef = null;
            cleanedTaskDescription = null;
            cleanedWorkType = null;
            validatedLevels = new List<string>();
            validatedAreas = new List<string>();

            if (TimesheetSelectedWorker == null)
            {
                errorMessage = "Select a worker.";
                return false;
            }

            if (SelectedTimesheetProject == null)
            {
                errorMessage = "Select a project.";
                return false;
            }

            // Date cannot be in the future
            var date = TimesheetDate.Date;
            if (date > DateTime.Today)
            {
                errorMessage = "You cannot submit hours for a future date.";
                return false;
            }

            // Parse hours
            if (!TryParseHours(TimesheetHoursText, out var hours))
            {
                errorMessage = "Enter a valid hours value.";
                return false;
            }

            // Enforce code/hours based on selected job name (same as Timesheet web)
            var jobName = ((SelectedTimesheetProject.Label ?? SelectedTimesheetProject.JobKey) ?? "").Trim();

            bool isHoliday = jobName.Equals("Holiday", StringComparison.OrdinalIgnoreCase)
                          || jobName.Equals("Bank Holiday", StringComparison.OrdinalIgnoreCase);
            bool isSick = jobName.Equals("Sick", StringComparison.OrdinalIgnoreCase);
            bool isFeeProposal = jobName.Equals("Fee Proposal", StringComparison.OrdinalIgnoreCase);
            bool isTender = jobName.Equals("Tender Presentation", StringComparison.OrdinalIgnoreCase);

            var code = NormalizeCode(TimesheetCodeText);

            if (TimesheetCodes == null || TimesheetCodes.Count == 0)
            {
                errorMessage = "Timesheet codes have not loaded yet.";
                return false;
            }

            var isKnownCode = TimesheetCodes.Any(c =>
                string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

            if (!isKnownCode)
            {
                errorMessage = "Code must be one of the dropdown options.";
                return false;
            }

            if (isHoliday)
            {
                code = "HOL";
                hours = 0;
            }
            else if (isSick)
            {
                code = "SI";
                hours = 0;
            }
            else if (isFeeProposal)
            {
                code = "FP";
            }
            else if (isTender)
            {
                code = "TP";
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                errorMessage = "Enter a code.";
                return false;
            }

            // Allow 0 hours ONLY for HOL/SI
            var allowZeroHours = code == "HOL" || code == "SI";

            if ((!allowZeroHours && (hours <= 0 || hours > 24)) ||
                (allowZeroHours && (hours < 0 || hours > 24)))
            {
                errorMessage = "Hours must be between 0 and 24.";
                return false;
            }

            // 0.5 increments rule
            var halfHours = hours * 2m;
            if (halfHours != decimal.Truncate(halfHours))
            {
                errorMessage = "Hours must be in 0.5 increments.";
                return false;
            }

            var hoursTextNorm = hours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (!TimesheetHourOptions.Contains(hoursTextNorm))
            {
                errorMessage = "Hours must be one of the dropdown options (0 to 24 in 0.5 steps).";
                return false;
            }

            // Clean other fields
            var ccf = string.IsNullOrWhiteSpace(TimesheetCcfRefText) ? null : TimesheetCcfRefText.Trim();
            var task = string.IsNullOrWhiteSpace(TimesheetTaskDescriptionText) ? null : TimesheetTaskDescriptionText.Trim();

            var isProjectJob = IsSelectedTimesheetJobProjectCategory();

            if (isProjectJob)
            {
                validatedLevels = TimesheetSelectedLevels
                    .Where(l => TimesheetLevelOptions.Contains(l, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                validatedAreas = ParseAreas(TimesheetAreasRawText);
            }
            else
            {
                TimesheetSelectedLevels.Clear();
                TimesheetAreasRawText = "";
            }

            string? workType = null;

            if (isProjectJob)
            {
                var wt = (TimesheetWorkTypeText ?? "").Trim().ToUpperInvariant();

                if (wt != "S" && wt != "M")
                {
                    errorMessage = "Work type is required for Project jobs.";
                    return false;
                }

                workType = wt;
            }
            else
            {
                // Non-project job: must not send work type
                if (!string.IsNullOrWhiteSpace(TimesheetWorkTypeText))
                    TimesheetWorkTypeText = ""; // keep UI predictable

                workType = null;
            }

            // VO requires CCF Ref
            if (code == "VO" && string.IsNullOrWhiteSpace(ccf))
            {
                errorMessage = "CCF Ref is required when Code is VO.";
                return false;
            }

            // Task description required for non HOL/SI
            if (code != "HOL" && code != "SI")
            {
                if (string.IsNullOrWhiteSpace(task))
                {
                    errorMessage = "Task description is required for this code.";
                    return false;
                }
            }

            // Write back enforced values so the UI reflects what was applied
            TimesheetCodeText = code;
            TimesheetHoursText = hours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            validatedHours = hours;
            validatedCode = code;
            cleanedCcfRef = ccf;
            cleanedTaskDescription = task;
            cleanedWorkType = workType;

            // If modelling, require levels (recommended)
            if (isProjectJob && workType == "M" && validatedLevels.Count == 0)
            {
                errorMessage = "At least one level is required for Modelling work.";
                return false;
            }

            return true;
        }

        private void ConfigureTimesheetDatePicker()
        {
            if (TimesheetDatePicker == null)
                return;

            // Prevent selecting future dates in the calendar UI
            TimesheetDatePicker.DisplayDateEnd = DateTime.Today;

            // Also blackout future dates explicitly (covers the calendar surface)
            TimesheetDatePicker.BlackoutDates.Clear();
            TimesheetDatePicker.BlackoutDates.Add(
                new CalendarDateRange(DateTime.Today.AddDays(1), DateTime.MaxValue));
        }


        private TimesheetProjectOptionDto? FindTimesheetProjectByLabel(string label)
        {
            if (TimesheetProjects == null || TimesheetProjects.Count == 0)
                return null;

            return TimesheetProjects.FirstOrDefault(p =>
                string.Equals((p.Label ?? p.JobKey)?.Trim(), label, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyTimesheetAllRules()
        {
            if (_applyingTimesheetRules)
                return;

            _applyingTimesheetRules = true;
            try
            {
                ApplyTimesheetJobDrivenRules();
                ApplyTimesheetCodeDrivenRules();
            }
            finally
            {
                _applyingTimesheetRules = false;
            }
        }

        private void ApplyTimesheetJobDrivenRules()
        {
            // Need a project selected to apply rules
            if (SelectedTimesheetProject == null)
            {
                IsTimesheetCodeLocked = false;
                IsTimesheetHoursLocked = false;
                IsTimesheetTaskLocked = false;
                return;
            }

            var jobName = ((SelectedTimesheetProject.Label ?? SelectedTimesheetProject.JobKey) ?? "").Trim();

            bool isHoliday = jobName.Equals("Holiday", StringComparison.OrdinalIgnoreCase)
                          || jobName.Equals("Bank Holiday", StringComparison.OrdinalIgnoreCase);

            bool isSick = jobName.Equals("Sick", StringComparison.OrdinalIgnoreCase);
            bool isFeeProposal = jobName.Equals("Fee Proposal", StringComparison.OrdinalIgnoreCase);
            bool isTender = jobName.Equals("Tender Presentation", StringComparison.OrdinalIgnoreCase);

            // WorkType is only applicable to "Project" category jobs
            if (!IsSelectedTimesheetJobProjectCategory())
            {
                if (!string.IsNullOrWhiteSpace(TimesheetWorkTypeText))
                    TimesheetWorkTypeText = "";
            }

            if (isHoliday)
            {
                IsTimesheetCodeLocked = true;
                IsTimesheetHoursLocked = true;
                IsTimesheetTaskLocked = true;

                TimesheetCodeText = "HOL";
                TimesheetHoursText = "0";
                TimesheetTaskDescriptionText = "";
                // CCF should not be used when not VO
                TimesheetCcfRefText = "";
            }
            else if (isSick)
            {
                IsTimesheetCodeLocked = true;
                IsTimesheetHoursLocked = true;
                IsTimesheetTaskLocked = true;

                TimesheetCodeText = "SI";
                TimesheetHoursText = "0";
                TimesheetTaskDescriptionText = "";
                TimesheetCcfRefText = "";
            }
            else if (isFeeProposal)
            {
                IsTimesheetCodeLocked = true;
                IsTimesheetHoursLocked = false;
                IsTimesheetTaskLocked = false;

                TimesheetCodeText = "FP";
                TimesheetCcfRefText = "";
            }
            else if (isTender)
            {
                IsTimesheetCodeLocked = true;
                IsTimesheetHoursLocked = false;
                IsTimesheetTaskLocked = false;

                TimesheetCodeText = "TP";
                TimesheetCcfRefText = "";
            }
            else
            {
                // Normal jobs: user controls everything
                IsTimesheetCodeLocked = false;
                IsTimesheetHoursLocked = false;
                IsTimesheetTaskLocked = false;

                // Only do this cleanup when the user changed PROJECT away from a special project.
                // If the user typed SI/HOL/FP/TP, we must NOT wipe it here (code-driven rules need to see it).
                if (_timesheetLastChangeSource == TimesheetChangeSource.Project && _timesheetPrevProjectWasSpecial)
                {
                    var codeNow = NormalizeCode(TimesheetCodeText);
                    if (codeNow == "SI" || codeNow == "HOL" || codeNow == "FP" || codeNow == "TP")
                    {
                        TimesheetCodeText = "P";
                        TimesheetCcfRefText = "";
                    }
                }
            }
        }

        private void ApplyTimesheetCodeDrivenRules()
        {
            // If the job locked the code, then the job rules already decide everything.
            if (IsTimesheetCodeLocked)
                return;

            var code = NormalizeCode(TimesheetCodeText);

            // If user chooses one of these codes manually, jump Project to the matching “special project”
            // and then apply job-driven rules (which will lock the code, etc).
            string? targetProjectLabel = code switch
            {
                "SI" => "Sick",
                "HOL" => "Holiday",           // if you also have "Bank Holiday", see note below
                "FP" => "Fee Proposal",
                "TP" => "Tender Presentation",
                _ => null
            };

            if (targetProjectLabel != null)
            {
                var match = FindTimesheetProjectByLabel(targetProjectLabel);

                // For HOL you might only have Bank Holiday in the list (or both).
                // Fallback if "Holiday" isn't found.
                if (match == null && code == "HOL")
                    match = FindTimesheetProjectByLabel("Bank Holiday");

                if (match != null)
                {
                    // Avoid pointless re-assignments
                    if (SelectedTimesheetProject?.JobKey != match.JobKey)
                    {
                        // IMPORTANT: setting SelectedTimesheetProject here will NOT recurse forever
                        // because ApplyTimesheetAllRules() is guarded by _applyingTimesheetRules.
                        SelectedTimesheetProject = match;
                    }

                    // Because SelectedTimesheetProject setter won't re-run rules while we're in the guard,
                    // we must apply job-driven rules explicitly.
                    ApplyTimesheetJobDrivenRules();
                    return;
                }

                // If we can't find the special project, fall through to "normal" behaviour.
            }

            // Existing behaviour for SI/HOL when not project-switching (or when project wasn’t found):
            var isSiOrHol = code == "SI" || code == "HOL";

            if (isSiOrHol)
            {
                IsTimesheetHoursLocked = true;
                IsTimesheetTaskLocked = true;

                TimesheetHoursText = "0";
                TimesheetTaskDescriptionText = "";
                TimesheetCcfRefText = "";
            }
            else
            {
                IsTimesheetHoursLocked = false;
                IsTimesheetTaskLocked = false;
            }
        }

        private void TimesheetLevels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox lb)
                return;

            var items = lb.SelectedItems
                .OfType<string>()
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            TimesheetSelectedLevels = items;
        }

        private static List<string> ParseAreas(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async void EditSelectedTimesheetEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = SelectedTimesheetEntry;
                if (selected == null) return;

                var subjectWorkerId = TimesheetSelectedWorker?.WorkerId;

                // Pull authoritative edit DTO from API
                var dto = await _api.GetTimesheetEntryForEditAsync(selected.Id, subjectWorkerId);

                // Enter edit mode
                IsTimesheetEditMode = true;
                EditingTimesheetEntryId = dto.Id;

                // Populate the existing Add-form fields (reuse your current bindings)
                TimesheetDate = dto.Date.Date;

                // Project pick
                SelectedTimesheetProject = TimesheetProjects
                    .FirstOrDefault(p => string.Equals(p.JobKey, dto.JobKey, StringComparison.OrdinalIgnoreCase))
                    ?? SelectedTimesheetProject;

                TimesheetCodeText = (dto.Code ?? "").Trim();
                TimesheetHoursText = dto.Hours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                TimesheetTaskDescriptionText = dto.TaskDescription ?? "";
                TimesheetCcfRefText = dto.CcfRef ?? "";
                TimesheetWorkTypeText = dto.WorkType ?? "";

                // Work details
                TimesheetSelectedLevels = dto.Levels ?? new List<string>();
                TimesheetAreasRawText = dto.Areas != null ? string.Join(", ", dto.Areas) : "";

                // Switch user to the Add tab (View=0, Add=1, Edit=2)
                SelectTimesheetSubTab(1); // go to Add (edit mode)

                TimesheetStatusText = $"Editing entry #{dto.Id}. Make changes and click Save Changes.";
            }
            catch (Exception ex)
            {
                TimesheetStatusText = ex.Message;
            }
        }

        private async void SoftDeleteSelectedTimesheetEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = SelectedTimesheetEntry;
                if (selected == null) return;

                var subjectWorkerId = TimesheetSelectedWorker?.WorkerId;

                await _api.DeleteTimesheetEntryAsync(selected.Id, subjectWorkerId);

                TimesheetStatusText = "Entry deleted.";
                SelectedTimesheetEntry = null;

                await RefreshTimesheetEntriesAsync();
            }
            catch (Exception ex)
            {
                TimesheetStatusText = ex.Message;
            }
        }

        private void TimesheetCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            IsTimesheetEditMode = false;
            EditingTimesheetEntryId = 0;
            TimesheetStatusText = "Edit cancelled.";

            ResetTimesheetEntryInputs();
            SelectTimesheetSubTab(2); // back to Edit
        }

        private async void TimesheetPrimaryAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TimesheetSelectedWorker == null)
                {
                    TimesheetStatusText = "Select a worker first.";
                    return;
                }

                if (SelectedTimesheetProject == null)
                {
                    TimesheetStatusText = "Select a project/job first.";
                    return;
                }

                // Build Levels/Areas from your current UI fields
                var levels = TimesheetSelectedLevels ?? new List<string>();

                var areas = (TimesheetAreasRawText ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

                // Parse hours string (your existing validation logic can stay; this is just a safe parse)
                if (!decimal.TryParse(
                        (TimesheetHoursText ?? "").Trim(),
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var hours))
                {
                    TimesheetStatusText = "Hours is invalid.";
                    return;
                }

                var dto = new UpdateTimesheetEntryDto(
                    WorkerId: TimesheetSelectedWorker.WorkerId,
                    JobKey: SelectedTimesheetProject.JobKey,
                    Date: TimesheetDate.Date,
                    Hours: hours,
                    Code: TimesheetCodeText ?? "",
                    TaskDescription: TimesheetTaskDescriptionText,
                    CcfRef: TimesheetCcfRefText,
                    WorkType: TimesheetWorkTypeText,
                    Levels: levels,
                    Areas: areas
                );

                var subjectWorkerId = TimesheetSelectedWorker.WorkerId;

                if (IsTimesheetEditMode)
                {
                    await _api.UpdateTimesheetEntryAsync(EditingTimesheetEntryId, dto, subjectWorkerId);

                    TimesheetStatusText = "Entry updated.";
                    IsTimesheetEditMode = false;
                    EditingTimesheetEntryId = 0;
                    ResetTimesheetEntryInputs(); // IMPORTANT: prevents accidental Add after edit
                    SelectTimesheetSubTab(2); // back to Edit
                }
                else
                {
                    // You already have CreateTimesheetEntryDto in the project; reuse your existing create flow here.
                    // If your existing create handler already does more validation, you can keep it and call it instead.
                    var createDto = new CreateTimesheetEntryDto(
                        WorkerId: dto.WorkerId,
                        JobKey: dto.JobKey,
                        Date: dto.Date,
                        Hours: dto.Hours,
                        Code: dto.Code,
                        TaskDescription: dto.TaskDescription,
                        CcfRef: dto.CcfRef,
                        WorkType: dto.WorkType,
                        Levels: dto.Levels,
                        Areas: dto.Areas
                    );

                    await _api.CreateTimesheetEntryAsync(createDto, subjectWorkerId);
                    TimesheetStatusText = "Entry added.";
                }

                await RefreshTimesheetEntriesAsync();
            }
            catch (Exception ex)
            {
                TimesheetStatusText = ex.Message;
            }
        }

        private void ResetTimesheetEntryInputs()
        {
            // Keep top controls as-is:
            // - TimesheetSelectedWorker
            // - SelectedTimesheetProject

            TimesheetDate = DateTime.Today;
            TimesheetHoursText = "";
            TimesheetCodeText = "";
            TimesheetCcfRefText = "";
            TimesheetTaskDescriptionText = "";

            TimesheetWorkTypeText = "";
            TimesheetAreasRawText = "";
            TimesheetSelectedLevels = new List<string>();

            // Clear UI selection too (because SelectedItems isn't bound)
            TimesheetLevelsListBox?.UnselectAll();

            ApplyTimesheetAllRules();
        }

        private void SelectTimesheetSubTab(int index)
        {
            // 0 = View, 1 = Add, 2 = Edit (based on your current XAML order)
            if (TimesheetSubTabs != null && TimesheetSubTabs.Items.Count > index)
                TimesheetSubTabs.SelectedIndex = index;
        }

        private async Task LoadProjectCcfRefsAsync()
        {
            if (SelectedProjectForCcf == null)
            {
                ProjectCcfRefs = new ObservableCollection<ProjectCcfRefDetailsDto>();
                return;
            }

            try
            {
                var rows = await _api.GetProjectCcfRefsByJobKeyAsync(
                    SelectedProjectForCcf.JobNameOrNumber,
                    includeInactive: ShowInactiveProjectCcfRefs);

                ProjectCcfRefs = new ObservableCollection<ProjectCcfRefDetailsDto>(rows);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to load CCF refs.\n\n{ex.Message}");
                ProjectCcfRefs = new ObservableCollection<ProjectCcfRefDetailsDto>();
            }
        }

        private async void RefreshProjectCcfRefs_Click(object sender, RoutedEventArgs e)
        {
            await LoadProjectCcfRefsAsync();
        }

        private async void AddProjectCcfRef_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProjectForCcf == null) return;

            var input = (NewProjectCcfRefCode ?? "").Trim();
            if (input.Length == 0) return;

            try
            {
                await _api.CreateProjectCcfRefByJobKeyAsync(SelectedProjectForCcf.JobNameOrNumber, input);
                NewProjectCcfRefCode = "";
                await LoadProjectCcfRefsAsync();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to add/reactivate.\n\n{ex.Message}");
            }
        }

        private async void ActivateProjectCcfRef_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProjectForCcf == null) return;
            if ((sender as FrameworkElement)?.DataContext is not ProjectCcfRefDto row) return;

            try
            {
                await _api.SetProjectCcfRefActiveByJobKeyAsync(SelectedProjectForCcf.JobNameOrNumber, row.Id, true);
                await LoadProjectCcfRefsAsync();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to activate.\n\n{ex.Message}");
            }
        }

        private async void DeactivateProjectCcfRef_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProjectForCcf == null) return;
            if ((sender as FrameworkElement)?.DataContext is not ProjectCcfRefDto row) return;

            try
            {
                await _api.SetProjectCcfRefActiveByJobKeyAsync(SelectedProjectForCcf.JobNameOrNumber, row.Id, false);
                await LoadProjectCcfRefsAsync();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to deactivate.\n\n{ex.Message}");
            }
        }

        private static decimal? ParseNullableMoney(string? text)
        {
            var t = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
                return null;

            // Handle accounting negatives like "(123.45)"
            bool parenNegative = t.StartsWith("(") && t.EndsWith(")");
            if (parenNegative)
                t = t[1..^1].Trim();

            // Remove common clutter (keep digits, separators, sign, decimal point)
            t = t.Replace("£", "").Replace(",", "").Trim();

            // Allow both "." and "," decimal users might paste (keep it simple: if it contains one comma and no dot, treat comma as dot)
            if (t.Contains(',') && !t.Contains('.'))
                t = t.Replace(',', '.');

            var styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

            if (!decimal.TryParse(t, styles, UkCulture, out var v))
                return null;

            if (parenNegative)
                v = -v;

            return v;
        }

        private async void AddCcf_Save_Click(object sender, RoutedEventArgs e)
        {
            AddCcf_StatusText = "";

            if (SelectedProjectForCcf == null)
            {
                AddCcf_StatusText = "Select a project first.";
                return;
            }

            var jobKey = SelectedProjectForCcf.JobNameOrNumber;
            var codeInput = (AddCcf_Code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(codeInput))
            {
                AddCcf_StatusText = "CCF Code is required.";
                return;
            }

            try
            {
                // 1) Create (or reactivate if already exists) - API does NormalizeCcfRef server-side
                var created = await _api.CreateProjectCcfRefByJobKeyAsync(jobKey, codeInput);

                // 2) Update commercial fields
                var updateDto = new UpdateProjectCcfRefDto(
                    EstimatedValue: ParseNullableMoney(AddCcf_EstimatedValueText),
                    QuotedValue: ParseNullableMoney(AddCcf_QuotedValueText),
                    QuotedDateUtc: AddCcf_QuotedDate,
                    AgreedValue: ParseNullableMoney(AddCcf_AgreedValueText),
                    AgreedDateUtc: AddCcf_AgreedDate,
                    ActualValue: ParseNullableMoney(AddCcf_ActualValueText),
                    Status: string.IsNullOrWhiteSpace(AddCcf_Status) ? "" : AddCcf_Status.Trim(),
                    Notes: string.IsNullOrWhiteSpace(AddCcf_Notes) ? null : AddCcf_Notes.Trim()
                );

                await _api.UpdateProjectCcfRefByJobKeyAsync(jobKey, created.Id, updateDto);

                // 3) Refresh grid
                await LoadProjectCcfRefsAsync();

                AddCcf_Clear_Click(sender, e);
                AddCcf_StatusText = $"Saved {created.Code}.";
            }
            catch (Exception ex)
            {
                AddCcf_StatusText = ex.Message;
            }
        }

        private void AddCcf_Clear_Click(object sender, RoutedEventArgs e)
        {
            AddCcf_Code = "";
            AddCcf_EstimatedValueText = "";
            AddCcf_QuotedValueText = "";
            AddCcf_AgreedValueText = "";
            AddCcf_ActualValueText = "";
            AddCcf_Status = "Draft";
            AddCcf_Notes = "";
            AddCcf_StatusText = "";

            AddCcf_QuotedDate = null;
            AddCcf_AgreedDate = null;

            OnPropertyChanged(nameof(CanSaveAddCcf));
        }

        private void EditProjectCcfRef_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProjectForCcf == null || SelectedProjectCcfRef == null)
            {
                WpfMessageBox.Show("Select a project and a CCF ref first.");
                return;
            }

            var row = SelectedProjectCcfRef;

            EditCcf_Id = row.Id;
            EditCcf_Code = row.Code;

            EditCcf_EstimatedValueText = row.EstimatedValue?.ToString() ?? "";
            EditCcf_QuotedValueText = row.QuotedValue?.ToString() ?? "";
            EditCcf_QuotedDate = row.QuotedDateUtc;

            EditCcf_AgreedValueText = row.AgreedValue?.ToString() ?? "";
            EditCcf_AgreedDate = row.AgreedDateUtc;

            EditCcf_ActualValueText = row.ActualValue?.ToString() ?? "";

            EditCcf_Status = string.IsNullOrWhiteSpace(row.Status) ? "Draft" : row.Status;
            EditCcf_Notes = row.Notes ?? "";

            EditCcf_StatusText = "";

            OnPropertyChanged(nameof(CanSaveEditCcf));

            // Switch to "Edit CCF Ref" tab:
            // Adjust index based on your actual tab order.
            ProjectsRootTabControl.SelectedIndex = 1;
            CcfTabControl.SelectedIndex = 2;
        }

        private async void EditCcf_Save_Click(object sender, RoutedEventArgs e)
        {
            EditCcf_StatusText = "";
            OnPropertyChanged(nameof(EditCcf_StatusText));

            if (SelectedProjectForCcf == null || EditCcf_Id == null)
            {
                EditCcf_StatusText = "Select a project and a CCF ref first.";
                OnPropertyChanged(nameof(EditCcf_StatusText));
                return;
            }

            try
            {
                var jobKey = SelectedProjectForCcf.JobNameOrNumber;

                var dto = new UpdateProjectCcfRefDto(
                    EstimatedValue: ParseNullableMoney(EditCcf_EstimatedValueText),
                    QuotedValue: ParseNullableMoney(EditCcf_QuotedValueText),
                    QuotedDateUtc: EditCcf_QuotedDate,
                    AgreedValue: ParseNullableMoney(EditCcf_AgreedValueText),
                    AgreedDateUtc: EditCcf_AgreedDate,
                    ActualValue: ParseNullableMoney(EditCcf_ActualValueText),
                    Status: string.IsNullOrWhiteSpace(EditCcf_Status) ? "Draft" : EditCcf_Status.Trim(),
                    Notes: string.IsNullOrWhiteSpace(EditCcf_Notes) ? null : EditCcf_Notes.Trim()
                );

                await _api.UpdateProjectCcfRefByJobKeyAsync(jobKey, EditCcf_Id.Value, dto);

                await LoadProjectCcfRefsAsync();

                EditCcf_StatusText = "Saved.";
                OnPropertyChanged(nameof(EditCcf_StatusText));
            }
            catch (Exception ex)
            {
                EditCcf_StatusText = ex.Message;
                OnPropertyChanged(nameof(EditCcf_StatusText));
            }
        }

        private void EditCcf_Cancel_Click(object sender, RoutedEventArgs e)
        {
            EditCcf_Id = null;
            EditCcf_Code = "";
            EditCcf_EstimatedValueText = "";
            EditCcf_QuotedValueText = "";
            EditCcf_QuotedDate = null;
            EditCcf_AgreedValueText = "";
            EditCcf_AgreedDate = null;
            EditCcf_ActualValueText = "";
            EditCcf_Status = "Draft";
            EditCcf_Notes = "";
            EditCcf_StatusText = "";

            OnPropertyChanged(nameof(CanSaveEditCcf));

            ProjectsRootTabControl.SelectedIndex = 1;
            CcfTabControl.SelectedIndex = 0;
        }

        private async void DeleteProjectCcfRef_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedProjectForCcf == null)
                {
                    WpfMessageBox.Show("Select a project first.");
                    return;
                }

                var selected = SelectedProjectCcfRef;
                if (selected == null)
                {
                    WpfMessageBox.Show("Select a CCF ref to delete first.");
                    return;
                }

                var jobKey = SelectedProjectForCcf.JobNameOrNumber;

                var confirmText =
                    $"Delete CCF ref '{selected.Code}' from project '{jobKey}'?\n\n" +
                    "This will hide it from the list.";

                var result = WpfMessageBox.Show(
                    confirmText,
                    "Confirm delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                await _api.SetProjectCcfRefDeletedByJobKeyAsync(jobKey, selected.Id, true);

                EditCcf_StatusText = "CCF ref deleted.";
                SelectedProjectCcfRef = null;

                OnPropertyChanged(nameof(CanSaveEditCcf));

                await LoadProjectCcfRefsAsync();
            }
            catch (Exception ex)
            {
                EditCcf_StatusText = ex.Message;
            }
        }

        private void PopulateEditJobKeyParts(string jobNameOrNumber)
        {
            // Expected: "PN0027 - Biggin Hill"
            var raw = (jobNameOrNumber ?? "").Trim();
            EditProjectJobKeyText = raw;

            var prefix = "";
            var number = "";
            var name = "";

            // Split into left/right around " - "
            var parts = raw.Split(new[] { " - " }, 2, StringSplitOptions.None);
            var left = parts.Length > 0 ? parts[0].Trim() : raw;
            name = parts.Length == 2 ? parts[1].Trim() : "";

            // left should be like PN0027
            // Take leading letters as prefix, remaining digits as number (best-effort)
            int i = 0;
            while (i < left.Length && char.IsLetter(left[i])) i++;
            prefix = left.Substring(0, i);

            int j = i;
            while (j < left.Length && char.IsDigit(left[j])) j++;
            number = (j > i) ? left.Substring(i, j - i) : left.Substring(i);

            EditProjectPrefixText = prefix;
            EditProjectNumberText = number;
            EditProjectNameText = name;
        }

        private async Task LoadEditProjectAsync(string jobKey)
        {
            try
            {
                EditProjectStatusText = "";
                _loadedEditProject = await _api.GetProjectForEditAsync(jobKey);

                PopulateEditJobKeyParts(_loadedEditProject.JobNameOrNumber);

                // Company binding
                EditProjectCompanyText = _loadedEditProject.CompanyCode ?? "";
                EditSelectedCompany = Companies.FirstOrDefault(c => c.Id == _loadedEditProject.CompanyId);

                EditProjectIsActive = _loadedEditProject.IsActive;

                EditProjectStatusText = "";
            }
            catch (Exception ex)
            {
                EditProjectStatusText = "Failed to load project for edit: " + ex.Message;
            }
        }

        private void ResetEditProjectToLoaded()
        {
            if (_loadedEditProject == null)
                return;

            PopulateEditJobKeyParts(_loadedEditProject.JobNameOrNumber);

            EditProjectCompanyText = _loadedEditProject.CompanyCode ?? "";
            EditSelectedCompany = Companies.FirstOrDefault(c => c.Id == _loadedEditProject.CompanyId);
            EditProjectIsActive = _loadedEditProject.IsActive;

            EditProjectStatusText = "Changes cancelled.";
        }

        private async void SaveEditProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedProject == null)
                {
                    EditProjectStatusText = "Select a project first.";
                    return;
                }

                if (_loadedEditProject == null)
                {
                    EditProjectStatusText = "Edit model not loaded yet.";
                    return;
                }

                // CompanyId comes from selected company (v1: we only allow picking existing)
                var companyId = EditSelectedCompany?.Id;

                var dto = new UpdateProjectRequestDto
                {
                    CompanyId = companyId,
                    IsActive = EditProjectIsActive
                };

                await _api.UpdateProjectAsync(SelectedProject.JobNameOrNumber, dto);

                EditProjectStatusText = "Saved.";

                // Kick off refresh in background so the button feels instant
                _ = RefreshProjectsAndDependentPicklistsAsync(keepSelection: true);

                // Reload edit snapshot (this one is small and directly relevant to this screen)
                await LoadEditProjectAsync(SelectedProject.JobNameOrNumber);
            }
            catch (Exception ex)
            {
                EditProjectStatusText = "Save failed: " + ex.Message;
            }
        }

        private void CancelEditProject_Click(object sender, RoutedEventArgs e)
        {
            ResetEditProjectToLoaded();
        }

        private void SelectProjectsTab(string header)
        {
            // ProjectsTabControl is x:Name="ProjectsTabControl" in XAML
            if (ProjectsTabControl == null) return;

            foreach (var item in ProjectsTabControl.Items)
            {
                if (item is System.Windows.Controls.TabItem tab &&
                    string.Equals(tab.Header?.ToString(), header, StringComparison.Ordinal))
                {
                    ProjectsTabControl.SelectedItem = tab;
                    return;
                }
            }
        }

        private async void EditProjectFromDrilldown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedProject == null)
                    return;

                // In your DTOs, JobNameOrNumber is what you’re using as the job key identifier.
                await LoadEditProjectAsync(SelectedProject.JobNameOrNumber);

                // Switch to the Edit Project tab
                SelectProjectsTab("Edit Project");
            }
            catch (Exception ex)
            {
                EditProjectStatusText = "Failed to open edit: " + ex.Message;
            }
        }

        private async Task RefreshProjectsAndDependentPicklistsAsync(bool keepSelection)
        {
            try
            {
                await ReloadProjectSummariesAsync(keepSelection: keepSelection);

                // Keep invoice/timesheet picklists in sync
                await RefreshProjectDependentPicklistsAsync();

                // UI-only refresh (filter + selection rebinding safety)
                RefreshProjects(keepSelection: keepSelection);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Refresh after update failed:\n\n" + ex.Message,
                    "Refresh error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task RefreshAfterProjectActiveToggleAsync(string jobKey)
        {
            try
            {
                // Summaries refresh (API)
                await ReloadProjectSummariesAsync(keepSelection: true);

                // Picklists refresh (API x2) - keep it here so Timesheet dropdown updates
                await RefreshProjectDependentPicklistsAsync();

                // UI-only refresh
                RefreshProjects(keepSelection: true);

                // If the project was deactivated and your view filters out inactive projects,
                // it may no longer exist in the visible list. Ensure SelectedProject isn't stale.
                if (SelectedProject != null && SelectedProject.JobNameOrNumber == jobKey)
                {
                    // OK - still selected
                }
                else
                {
                    // If selection is now invalid/stale, clear it without triggering heavy loads
                    _suppressProjectSelectionChanged = true;
                    try
                    {
                        if (SelectedProject != null && SelectedProject.JobNameOrNumber == jobKey)
                            SelectedProject = null;
                    }
                    finally
                    {
                        _suppressProjectSelectionChanged = false;
                    }
                }
            }
            catch
            {
                // Swallow: user already performed the server action successfully.
                // If refresh fails, the next manual refresh / reopen will correct it.
                // (Optional: add logging)
            }
        }

        private static async Task Perf(string name, Func<Task> action)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"[PERF ▶] {name}");

            try
            {
                await action();
            }
            finally
            {
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"[PERF ◀] {name} = {sw.ElapsedMilliseconds} ms");
            }
        }
    }
}
