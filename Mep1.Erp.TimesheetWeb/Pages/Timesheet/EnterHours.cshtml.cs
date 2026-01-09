using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Mep1.Erp.TimesheetWeb.Services;
using Mep1.Erp.Core.Contracts;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class EnterHoursModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public EnterHoursModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    private List<TimesheetProjectOptionDto> _projectsCache = new();

    private static readonly (string Code, string Description)[] TimesheetCodeList =
    {
        ("P", "Programmed Drawing Input"),
        ("IC", "Updating to Internal Comments"),
        ("EC", "Updating to External Comments"),
        ("GM", "General Management"),
        ("M", "Meetings"),
        ("RD", "Record Drawings"),
        ("S", "Surveys"),
        ("T", "Training"),
        ("BIM", "BIM Works"),
        ("DC", "Document Control"),
        ("FP", "Fee Proposal"),
        ("BU", "Business Works"),
        ("QA", "Drawing QA Check"),
        ("TP", "Tender Presentation"),
        ("VO", "Variations"),
        ("SI", "Sick"),
        ("HOL", "Holiday")
    };

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

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Message { get; private set; }

    public sealed class InputModel
    {
        public DateTime Date { get; set; } = DateTime.Today;

        public decimal Hours { get; set; } = 0;

        public string JobKey { get; set; } = "";

        public string Code { get; set; } = "P";

        public string? CcfRef { get; set; }

        public string? TaskDescription { get; set; }

        public string WorkType { get; set; } = "M";      // "S" or "M"
        public List<string> Levels { get; set; } = new(); // multi-select
        public string? AreasRaw { get; set; }             // comma-separated input

    }

    public async Task<IActionResult> OnGet()
    {
        if (HttpContext.Session.GetString("MustChangePassword") == "true")
        {
            return RedirectToPage("/Timesheet/Profile");
        }

        var workerId = HttpContext.Session.GetInt32("WorkerId");

        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        await LoadOptionsAsync();

        // If redirected back with a TempData message
        if (TempData.TryGetValue("Message", out var msgObj))
            Message = msgObj?.ToString();

        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        if (HttpContext.Session.GetString("MustChangePassword") == "true")
        {
            return RedirectToPage("/Timesheet/Profile");
        }

        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        await LoadOptionsAsync();

        // ---- Enforce Job <-> Code rules based on selected "job" ----
        var selected = _projectsCache.FirstOrDefault(p => p.JobKey == Input.JobKey);

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

        // ---- Validation ----
        var today = DateTime.Today;

        if (Input.Date.Date > today)
        {
            ModelState.AddModelError("Input.Date", "You cannot submit hours for a future date.");
            await LoadOptionsAsync();
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
            await LoadOptionsAsync();
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

        if (Input.WorkType != "S" && Input.WorkType != "M")
            ModelState.AddModelError("Input.WorkType", "Please select Sheet or Modelling.");

        if (Input.Levels.Any(l => !LevelOptions.Contains(l)))
            ModelState.AddModelError("Input.Levels", "Please select valid level(s).");

        if (!ModelState.IsValid)
            return Page();

        // ---- Build DTO & POST to API ----
        var cleanedCcf = string.IsNullOrWhiteSpace(Input.CcfRef)
            ? null
            : Input.CcfRef.Trim();

        var cleanedTask = string.IsNullOrWhiteSpace(Input.TaskDescription)
            ? null
            : Input.TaskDescription.Trim();

        static List<string> ParseTags(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(x => x.Trim())
                      .Where(x => x.Length > 0)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        var areas = ParseTags(Input.AreasRaw);

        // Your DTO constructor is: (int WorkerId, string JobKey, DateTime Date, decimal Hours, string Code, string? TaskDescription, string? CcfRef)
        var dto = new CreateTimesheetEntryDto(
            WorkerId: workerId.Value,
            JobKey: Input.JobKey,
            Date: Input.Date.Date,
            Hours: Input.Hours,
            Code: Input.Code,
            CcfRef: cleanedCcf,
            TaskDescription: cleanedTask,
            WorkType: Input.WorkType,
            Levels: Input.Levels,
            Areas: areas
        );

        await _api.CreateTimesheetEntryAsync(dto);

        // Clear fields for next entry (keep date + code if you want)
        Input.Hours = 0;
        Input.TaskDescription = "";
        Input.CcfRef = "";
        // leave Input.Date as-is; leave Input.Code as-is; keep JobKey? up to you:
        // Input.JobKey = "";

        TempData["Message"] = "Submitted.";
        return RedirectToPage("/Timesheet/EnterHours");
    }

    private async Task LoadOptionsAsync()
    {
        _projectsCache = await _api.GetActiveProjectsAsync() ?? new List<TimesheetProjectOptionDto>();

        ProjectOptions = _projectsCache
            .Select(p => new SelectListItem(p.Label, p.JobKey))
            .ToList();

        CodeOptions = TimesheetCodeList
            .Select(c => new SelectListItem($"{c.Code} - {c.Description}", c.Code))
            .ToList();

        LevelSelectItems = LevelOptions
            .Select(l => new SelectListItem(l, l))
            .ToList();
    }
}
