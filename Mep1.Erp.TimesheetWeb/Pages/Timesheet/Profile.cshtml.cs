using Mep1.Erp.Core;
using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class ProfileModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public ProfileModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    public int? WorkerId { get; private set; }

    public string Username { get; private set; } = "";
    public bool MustChangePassword { get; private set; }

    [BindProperty]
    public string CurrentPassword { get; set; } = "";

    [BindProperty]
    public string NewPassword { get; set; } = "";

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        Username = HttpContext.Session.GetString("Username") ?? "";
        MustChangePassword = HttpContext.Session.GetString("MustChangePassword") == "true";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        WorkerId = HttpContext.Session.GetInt32("WorkerId");
        if (WorkerId is null)
            return RedirectToPage("/Timesheet/Login");

        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrWhiteSpace(username))
            return RedirectToPage("/Login");

        var result = await _api.ChangePasswordAsync(
            username,
            CurrentPassword,
            NewPassword);

        if (!result)
        {
            ErrorMessage = "Password change failed.";
            return Page();
        }

        HttpContext.Session.SetString("MustChangePassword", "false");
        return RedirectToPage("/Timesheet/History");
    }
}
