using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        // Clear everything the portal uses for "logged in" state
        HttpContext.Session.Clear();

        // Back to login
        return RedirectToPage("/Timesheet/Login");
    }
}
