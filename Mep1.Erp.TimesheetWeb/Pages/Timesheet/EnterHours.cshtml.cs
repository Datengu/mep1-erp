using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Mep1.Erp.Core;
using Mep1.Erp.TimesheetWeb.Services;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class EnterHoursModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public EnterHoursModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

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

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Message { get; private set; }

    public sealed class InputModel
    {
        public DateTime Date { get; set; } = DateTime.Today;

        public decimal Hours { get; set; } = 0;

        public string JobKey { get; set; } = "";

        public string Code { get; set; } = "BU";

        public string? CcfRef { get; set; }

        public string? TaskDescription { get; set; }
    }

    public async Task<IActionResult> OnGet()
    {
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
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        await LoadOptionsAsync();

        // ---- Validation (minimal but solid) ----
        if (string.IsNullOrWhiteSpace(Input.JobKey))
            ModelState.AddModelError("Input.JobKey", "Please select a job.");

        if (Input.Hours <= 0 || Input.Hours > 24)
            ModelState.AddModelError("Input.Hours", "Hours must be between 0 and 24.");

        if (string.IsNullOrWhiteSpace(Input.Code))
            ModelState.AddModelError("Input.Code", "Please select a code.");

        if (Input.Code == "VO" && string.IsNullOrWhiteSpace(Input.CcfRef))
            ModelState.AddModelError("Input.CcfRef", "CCF Ref is required when Code is VO.");

        if (string.IsNullOrWhiteSpace(Input.TaskDescription))
            ModelState.AddModelError("Input.TaskDescription", "Please enter a task description.");

        if (!ModelState.IsValid)
            return Page();

        // ---- Build DTO & POST to API ----
        var cleanedCcf = string.IsNullOrWhiteSpace(Input.CcfRef)
            ? null
            : Input.CcfRef.Trim();

        var cleanedTask = string.IsNullOrWhiteSpace(Input.TaskDescription)
            ? null
            : Input.TaskDescription.Trim();

        // Your DTO constructor is: (int WorkerId, string JobKey, DateTime Date, decimal Hours, string Code, string? TaskDescription, string? CcfRef)
        var dto = new CreateTimesheetEntryDto(
            WorkerId: workerId.Value,
            JobKey: Input.JobKey,
            Date: Input.Date.Date,
            Hours: Input.Hours,
            Code: Input.Code,
            CcfRef: cleanedCcf,
            TaskDescription: cleanedTask
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
        var projects = await _api.GetActiveProjectsAsync() ?? new List<TimesheetProjectOptionDto>();

        ProjectOptions = projects
            .Select(p => new SelectListItem(p.Label, p.JobKey))
            .ToList();

        CodeOptions = TimesheetCodeList
            .Select(c => new SelectListItem($"{c.Code} - {c.Description}", c.Code))
            .ToList();
    }
}
