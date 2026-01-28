using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class UnavailableModel : PageModel
{
    public string RequestId { get; private set; } = "";
    public string Message { get; private set; } = "Temporarily unavailable. Please try again.";

    public void OnGet()
    {
        RequestId = HttpContext.TraceIdentifier;

        var msg = HttpContext.Session.GetString("FlashMessage");
        if (!string.IsNullOrWhiteSpace(msg))
        {
            Message = msg;
            HttpContext.Session.Remove("FlashMessage");
        }
    }
}