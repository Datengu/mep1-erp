using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
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

    public List<SelectListItem> ProjectOptions { get; private set; } = new();
    public List<SelectListItem> CodeOptions { get; private set; } = new();

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public string JobKey { get; set; } = "";
        public string Code { get; set; } = "BU";
        public string? CcfRef { get; set; }
    }

    public async Task OnGet()
    {
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPost()
    {
        await LoadOptionsAsync();

        // For now: just validate and stay on page.
        if (string.IsNullOrWhiteSpace(Input.JobKey))
        {
            ModelState.AddModelError("", "Please select a project.");
        }

        if (Input.Code == "VO" && string.IsNullOrWhiteSpace(Input.CcfRef))
        {
            ModelState.AddModelError("", "CCF Ref is required when Code is VO.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Next step will POST hours to API.
        TempData["Message"] = "OK - form validated (POST not implemented yet).";
        return RedirectToPage();
    }

    private async Task LoadOptionsAsync()
    {
        var projects = await _api.GetActiveProjectsAsync() ?? new List<TimesheetProjectOptionDto>();

        ProjectOptions = projects
            .Select(p => new SelectListItem(p.Label, p.JobKey))
            .ToList();

        CodeOptions = TimesheetCodes.All
            .Select(c => new SelectListItem(c, c))
            .ToList();
    }
}
