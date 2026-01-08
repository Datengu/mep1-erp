using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mep1.Erp.TimesheetWeb.Services;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class LoginModel : PageModel
{
    private const string RememberCookieName = "MEP1_REMEMBER";
    private const string RememberProtectorPurpose = "Mep1.Erp.TimesheetWeb.RememberMe.v1";
    private static readonly TimeSpan RememberDuration = TimeSpan.FromDays(30);

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

        public bool RememberMe { get; set; }
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

        // Session (current behaviour)
        HttpContext.Session.SetInt32("WorkerId", login.WorkerId);
        HttpContext.Session.SetString("WorkerName", login.Name ?? "");
        HttpContext.Session.SetString("WorkerInitials", login.Initials ?? "");
        HttpContext.Session.SetString("UserRole", login.Role ?? "Worker");
        HttpContext.Session.SetString("Username", login.Username ?? "");
        HttpContext.Session.SetString("MustChangePassword", login.MustChangePassword ? "true" : "false");

        // Remember-me cookie (only if ticked)
        if (Input.RememberMe)
        {
            SetRememberMeCookie(login);
        }
        else
        {
            // If user logs in without remember-me, ensure any old remember cookie is removed.
            Response.Cookies.Delete(RememberCookieName);
        }

        return RedirectToPage("/Timesheet/EnterHours");
    }

    private void SetRememberMeCookie(ErpTimesheetApiClient.TimesheetLoginResponse login)
    {
        var provider = HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector(RememberProtectorPurpose);

        var payload = new RememberPayload
        {
            WorkerId = login.WorkerId,
            Name = login.Name ?? "",
            Initials = login.Initials ?? "",
            Role = login.Role ?? "Worker",
            Username = login.Username ?? "",
            MustChangePassword = login.MustChangePassword,
        };

        var json = JsonSerializer.Serialize(payload);
        var protectedValue = protector.Protect(json);

        var opts = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(RememberDuration)
        };

        Response.Cookies.Append(RememberCookieName, protectedValue, opts);
    }

    private sealed class RememberPayload
    {
        public int WorkerId { get; set; }
        public string Name { get; set; } = "";
        public string Initials { get; set; } = "";
        public string Role { get; set; } = "Worker";
        public string Username { get; set; } = "";
        public bool MustChangePassword { get; set; }
    }
}
