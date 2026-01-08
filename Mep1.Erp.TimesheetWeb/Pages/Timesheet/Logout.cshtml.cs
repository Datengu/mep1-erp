using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        HttpContext.Session.Clear();
        Response.Cookies.Delete("MEP1_REMEMBER");
        return RedirectToPage("/Timesheet/Login");
    }
}
