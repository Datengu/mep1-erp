using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mep1.Erp.Core;
using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Mep1.Erp.Core.Contracts;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class EditModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public EditModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    private List<TimesheetProjectOptionDto> _projectsCache = new();

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public List<SelectListItem> ProjectOptions { get; private set; } = new();
    public List<SelectListItem> CodeOptions { get; private set; } = new();

    public static readonly string[] LevelOptions =
    [
        // Below ground
        "B4", "B3", "B2", "B1", "LG", "UG",

        // Ground / podium / mezzanine
        "G", "POD", "M",

        // Upper floors
        .. Enumerable.Range(1, 30).Select(i => $"L{i:00}"),

        // Roof / plant
        .. new[] { "P", "RF" },
    ];

    public List<SelectListItem> LevelSelectItems { get; private set; } = new();

    public static readonly string[] AreaOptions =
    [
        "Cores",
        "Office",
        "Plant rooms",
        "Switch rooms",
        "Comms Rooms",
        "Residential",
        "Retail",
        "Kitchens",
        "Pantries",
    ];

    private static readonly HashSet<string> WorkDetailsCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "P", "IC", "EC", "RD", "QA", "VO"
    };

    private static bool AllowsWorkDetails(string? code)
        => !string.IsNullOrWhiteSpace(code) && WorkDetailsCodes.Contains(code.Trim());

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public DateTime Date { get; set; }
        public decimal Hours { get; set; }
        public string JobKey { get; set; } = "";
        public string Code { get; set; } = "";
        public string? CcfRef { get; set; }
        public string? TaskDescription { get; set; }
        public string? WorkType { get; set; }
        public List<string> Levels { get; set; } = new(); // multi-select
        public List<string> Areas { get; set; } = new();  // multi-select
    }

    public bool ShowWorkDetails { get; private set; }

    public Dictionary<string, bool> JobKeyShowsWorkDetails { get; private set; } = new();

    public async Task<IActionResult> OnGet()
    {
        if (User.FindFirst("must_change_password")?.Value == "true")
            return RedirectToPage("/Timesheet/Profile");

        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Timesheet/Login");

        var workerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");

        if (Id <= 0)
            return RedirectToPage("/Timesheet/History");

        await LoadOptionsAsync(workerId);

        var entry = await _api.GetTimesheetEntryAsync(Id);
        if (entry is null)
            return RedirectToPage("/Timesheet/History");

        Input = new InputModel
        {
            Date = entry.Date.Date,
            Hours = entry.Hours,
            JobKey = entry.JobKey,
            Code = entry.Code,
            TaskDescription = entry.TaskDescription,
            CcfRef = entry.CcfRef,

            WorkType = string.IsNullOrWhiteSpace(entry.WorkType) ? null : entry.WorkType,
            Levels = entry.Levels ?? new List<string>(),
            Areas = entry.Areas ?? new List<string>()
        };

        var selected = _projectsCache.FirstOrDefault(p => p.JobKey == Input.JobKey);
        var isProjectJob = selected?.Category == "Project";
        ShowWorkDetails = isProjectJob && AllowsWorkDetails(Input.Code);

        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        if (User.FindFirst("must_change_password")?.Value == "true")
            return RedirectToPage("/Timesheet/Profile");

        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Timesheet/Login");

        var workerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");

        await LoadOptionsAsync(workerId);

        if (Id <= 0)
            return RedirectToPage("/Timesheet/History");

        // ---- Enforce Job <-> Code rules based on selected "job" ----
        var selected = _projectsCache.FirstOrDefault(p => p.JobKey == Input.JobKey);

        var isProjectJob = selected?.Category == "Project";
        ShowWorkDetails = isProjectJob && AllowsWorkDetails(Input.Code);

        if (selected is null)
        {
            ModelState.AddModelError("Input.JobKey", "Please select a valid job.");
        }
        else
        {
            // Match on the internal-job names you have in the Projects table:
            var jobName = (selected.Label ?? selected.JobKey ?? "").Trim();

            bool isHoliday = jobName.Equals("Holiday", StringComparison.OrdinalIgnoreCase)
                          || jobName.Equals("Bank Holiday", StringComparison.OrdinalIgnoreCase);

            bool isSick = jobName.Equals("Sick", StringComparison.OrdinalIgnoreCase);
            bool isFeeProposal = jobName.Equals("Fee Proposal", StringComparison.OrdinalIgnoreCase);
            bool isTender = jobName.Equals("Tender Presentation", StringComparison.OrdinalIgnoreCase);

            if (isHoliday)
            {
                // must be HOL + no hours
                Input.Code = "HOL";
                Input.Hours = 0;
            }
            else if (isSick)
            {
                Input.Code = "SI";
                Input.Hours = 0;
            }
            else if (isFeeProposal)
            {
                Input.Code = "FP";
            }
            else if (isTender)
            {
                Input.Code = "TP";
            }
        }

        ShowWorkDetails = (selected?.Category == "Project") && AllowsWorkDetails(Input.Code);

        // ---- Validation ----
        var today = DateTime.Today;

        if (Input.Date.Date > today)
        {
            ModelState.AddModelError("Input.Date", "You cannot submit hours for a future date.");
            await LoadOptionsAsync(workerId);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.JobKey))
            ModelState.AddModelError("Input.JobKey", "Please select a job.");

        // Allow 0 hours ONLY for HOL / SI (holiday/sick jobs)
        var allowZeroHours = Input.Code == "HOL" || Input.Code == "SI";

        if ((!allowZeroHours && (Input.Hours <= 0 || Input.Hours > 24)) ||
            (allowZeroHours && (Input.Hours < 0 || Input.Hours > 24)))
        {
            ModelState.AddModelError("Input.Hours", "Hours must be between 0 and 24.");
        }

        // ensure hours is in 0.5 increments
        var halfHours = Input.Hours * 2m;
        if (halfHours != decimal.Truncate(halfHours))
        {
            ModelState.AddModelError("Input.Hours", "Hours must be in 0.5 increments.");
            await LoadOptionsAsync(workerId);
            return Page();
        }

        // Require code
        if (string.IsNullOrWhiteSpace(Input.Code))
            ModelState.AddModelError("Input.Code", "Please select a code.");

        // Force CCF Ref when VO is selected code
        if (Input.Code == "VO" && string.IsNullOrWhiteSpace(Input.CcfRef))
            ModelState.AddModelError("Input.CcfRef", "CCF Ref is required when Code is VO.");

        // Only require description for non-holiday/sick
        if (Input.Code != "HOL" && Input.Code != "SI")
        {
            if (string.IsNullOrWhiteSpace(Input.TaskDescription))
                ModelState.AddModelError("Input.TaskDescription", "Please enter a task description.");
        }

        if (ShowWorkDetails)
        {
            if (Input.WorkType != "S" && Input.WorkType != "M")
                ModelState.AddModelError("Input.WorkType", "Please select Sheet or Modelling.");

            if (Input.Levels.Any(l => !LevelOptions.Contains(l)))
                ModelState.AddModelError("Input.Levels", "Please select valid level(s).");
        }
        else
        {
            Input.WorkType = null;
            Input.Levels.Clear();
            Input.Areas.Clear();
        }

        if (!ModelState.IsValid)
            return Page();

        // ---- Build DTO & POST to API ----
        var cleanedCcf = string.IsNullOrWhiteSpace(Input.CcfRef) ? null : Input.CcfRef.Trim();
        var cleanedTask = string.IsNullOrWhiteSpace(Input.TaskDescription) ? null : Input.TaskDescription.Trim();

        var dto = new UpdateTimesheetEntryDto(
            WorkerId: workerId,
            JobKey: Input.JobKey,
            Date: DateTime.SpecifyKind(Input.Date.Date, DateTimeKind.Utc),
            Hours: Input.Hours,
            Code: Input.Code,
            CcfRef: cleanedCcf,
            TaskDescription: cleanedTask,
            WorkType: Input.WorkType,
            Levels: Input.Levels,
            Areas: Input.Areas
        );

        await _api.UpdateTimesheetEntryAsync(Id, dto);

        return RedirectToPage("/Timesheet/History");
    }

    private async Task LoadOptionsAsync(int workerId)
    {
        _projectsCache = await _api.GetActiveProjectsAsync() ?? new List<TimesheetProjectOptionDto>();

        ProjectOptions = _projectsCache
            .Select(p => new SelectListItem(p.Label, p.JobKey))
            .ToList();

        var codes = await _api.GetTimesheetCodesAsync() ?? new List<TimesheetCodeDto>();

        CodeOptions = codes
            .Select(c => new SelectListItem($"{c.Code} - {c.Description}", c.Code))
            .ToList();

        LevelSelectItems = LevelOptions
            .Select(l => new SelectListItem(l, l))
            .ToList();

        JobKeyShowsWorkDetails = _projectsCache.ToDictionary(
            p => p.JobKey,
            p => string.Equals(p.Category, "Project", StringComparison.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public async Task<IActionResult> OnGetCcfRefs([FromQuery] string jobKey)
    {
        if (User.Identity?.IsAuthenticated != true)
            return new JsonResult(Array.Empty<string>());

        var workerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");
        await LoadOptionsAsync(workerId);

        var exists = _projectsCache.Any(p => string.Equals(p.JobKey, jobKey, StringComparison.OrdinalIgnoreCase));
        if (!exists) return new JsonResult(Array.Empty<string>());

        var refs = await _api.GetCcfRefsForJobAsync(jobKey) ?? new List<string>();
        return new JsonResult(refs);
    }
}
