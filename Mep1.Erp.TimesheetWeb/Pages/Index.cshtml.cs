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

            var workerId = HttpContext.Session.GetInt32("WorkerId");
            if (workerId is not null)
            {
                return RedirectToPage("/Timesheet/EnterHours");
            }

            return RedirectToPage("/Timesheet/Login");
        }
    }
}
