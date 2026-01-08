using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mep1.Erp.TimesheetWeb.Services;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class LoginModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public LoginModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }

    public void OnGet()
    {
        // If already logged in, go straight to Enter Hours
        if (HttpContext.Session.GetInt32("WorkerId") is not null)
        {
            Response.Redirect("/Timesheet/EnterHours");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        ErpTimesheetApiClient.TimesheetLoginResponse? login;

        try
        {
            login = await _api.LoginAsync(Input.Username, Input.Password);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Throttled/locked out (your new rate limit)
            ErrorMessage = "Too many failed login attempts. Please wait a moment and try again.";
            return Page();
        }
        catch (HttpRequestException)
        {
            // Any other non-success (e.g. API down, 5xx, etc.)
            ErrorMessage = "Login temporarily unavailable. Please try again.";
            return Page();
        }

        if (login is null)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }


        HttpContext.Session.SetInt32("WorkerId", login.WorkerId);
        HttpContext.Session.SetString("WorkerName", login.Name ?? "");
        HttpContext.Session.SetString("WorkerInitials", login.Initials ?? "");
        HttpContext.Session.SetString("UserRole", login.Role ?? "Worker");
        HttpContext.Session.SetString("Username", login.Username ?? "");
        HttpContext.Session.SetString(
            "MustChangePassword",
            login.MustChangePassword ? "true" : "false");

        return RedirectToPage("/Timesheet/EnterHours");
    }
}
