using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public class SignatureModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public SignatureModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    public string WorkerName { get; set; } = "";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [StringLength(80)]
        public string SignatureName { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(string? returnTo = null)
    {
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        var info = await _api.GetWorkerSignatureAsync(workerId.Value);
        if (info is null)
            return RedirectToPage("/Timesheet/Login");

        WorkerName = info.Name;

        // If already captured, bounce them onward
        if (!string.IsNullOrWhiteSpace(info.SignatureName))
        {
            return Redirect(returnTo ?? "/Timesheet/History");
        }

        // Default typed signature = their name
        Input.SignatureName = info.Name;

        ViewData["ReturnTo"] = returnTo ?? "/Timesheet/History";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnTo = null)
    {
        var actorWorkerId = HttpContext.Session.GetInt32("WorkerId");
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        if (!ModelState.IsValid)
        {
            ViewData["ReturnTo"] = returnTo ?? "/Timesheet/History";
            return Page();
        }

        await _api.SetWorkerSignatureAsync(workerId.Value, actorWorkerId.Value, Input.SignatureName);

        return Redirect(returnTo ?? "/Timesheet/History");
    }
}
