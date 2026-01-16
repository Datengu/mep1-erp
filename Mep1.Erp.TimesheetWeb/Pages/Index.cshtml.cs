using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mep1.Erp.TimesheetWeb.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            // Same pattern as EnterHours/Edit/Profile
            if (HttpContext.Session.GetString("MustChangePassword") == "true")
            {
                return RedirectToPage("/Timesheet/Profile");
            }

            if (User.Identity?.IsAuthenticated != true)
                return RedirectToPage("/Timesheet/EnterHours");

            var workerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");

            return RedirectToPage("/Timesheet/Login");
        }
    }
}
