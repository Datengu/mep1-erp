using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mep1.Erp.TimesheetWeb.Services;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class LogoutModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public LogoutModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    public async Task<IActionResult> OnGet()
    {
        // If we have a refresh token (remember-me), revoke it server-side
        var refreshToken = User.FindFirst("refresh_token")?.Value;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                await _api.LogoutAsync(refreshToken);
            }
            catch
            {
                // Don't block logout if API is down or request fails.
                // Worst case: refresh token remains active until expiry.
            }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear(); // optional (you can remove if you no longer use session)
        return RedirectToPage("/Timesheet/Login");
    }
}
