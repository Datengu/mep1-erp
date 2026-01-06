using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public class EvidencePackModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public EvidencePackModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    public string WorkerName { get; set; } = "";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [DataType(DataType.Date)]
        public DateTime From { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime To { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        // signature gate
        var sig = await _api.GetWorkerSignatureAsync(workerId.Value);
        if (sig is null)
            return RedirectToPage("/Timesheet/Login");

        WorkerName = sig.Name;

        if (string.IsNullOrWhiteSpace(sig.SignatureName))
        {
            // send them to signature page then back here
            return RedirectToPage("/Timesheet/Signature", new { returnTo = "/Timesheet/EvidencePack" });
        }

        // sensible default period: last full week ending Sunday
        var today = DateTime.Today;
        var daysSinceSunday = (int)today.DayOfWeek; // Sunday=0
        var lastSunday = today.AddDays(-daysSinceSunday);
        var weekStart = lastSunday.AddDays(-6);

        Input.From = weekStart;
        Input.To = lastSunday;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        if (!ModelState.IsValid)
            return Page();

        // signature gate again on POST
        var sig = await _api.GetWorkerSignatureAsync(workerId.Value);
        if (sig is null)
            return RedirectToPage("/Timesheet/Login");

        if (string.IsNullOrWhiteSpace(sig.SignatureName))
            return RedirectToPage("/Timesheet/Signature", new { returnTo = "/Timesheet/EvidencePack" });

        // basic validation
        if (Input.To.Date < Input.From.Date)
        {
            ModelState.AddModelError(string.Empty, "To date must be on or after From date.");
            return Page();
        }

        if (Input.To.Date > DateTime.Today)
        {
            ModelState.AddModelError(string.Empty, "Future dates are not allowed.");
            return Page();
        }

        // Next step: return ZIP
        // For now, just confirm wiring works:
        return RedirectToPage("/Timesheet/History");
    }
}
