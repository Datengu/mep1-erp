using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

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
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                Response.Redirect(ReturnUrl);
                return;
            }

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
            login = await _api.LoginAsync(Input.Username, Input.Password, Input.RememberMe);
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

        var claims = BuildClaimsFromJwt(login.AccessToken);

        // Store the raw access token in a claim so your API client can attach it.
        // Cookie auth cookie is encrypted/signed by ASP.NET Core.
        claims.Add(new Claim("access_token", login.AccessToken ?? ""));
        if (Input.RememberMe)
            claims.Add(new Claim("refresh_token", login.RefreshToken ?? ""));
        claims.Add(new Claim("must_change_password", login.MustChangePassword ? "true" : "false"));
        claims.Add(new Claim("display_name", login.Name ?? ""));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProps = new AuthenticationProperties
        {
            IsPersistent = Input.RememberMe,
            AllowRefresh = true
        };

        if (Input.RememberMe && login.RefreshExpiresUtc.HasValue)
        {
            authProps.ExpiresUtc = login.RefreshExpiresUtc.Value;
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProps);

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return Redirect(ReturnUrl);
        }

        return RedirectToPage("/Timesheet/EnterHours");
    }

    private static List<Claim> BuildClaimsFromJwt(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Access token missing.");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Keep JWT claims (wid, usr, role, plus standard ones)
        return jwt.Claims.ToList();
    }
}
