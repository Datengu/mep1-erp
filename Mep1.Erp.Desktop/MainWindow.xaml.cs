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

            Settings = SettingsService.LoadSettings();

            if (string.IsNullOrWhiteSpace(Settings.ApiBaseUrl))
                throw new InvalidOperationException("API base URL is not configured.");

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
                    LoadSelectedProjectDetails(match);
                }
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
    }
}
