using Mep1.Erp.Core.Contracts;
using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

public class ProfileModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public ProfileModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    public int WorkerId { get; private set; }

    public string Username { get; private set; } = "";
    public bool MustChangePassword { get; private set; }

    [BindProperty]
    public string CurrentPassword { get; set; } = "";

    [BindProperty]
    public string NewPassword { get; set; } = "";

    public WorkerSignatureDto? Signature { get; private set; }

    [BindProperty]
    public string SignatureName { get; set; } = "";

    public string? SuccessMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!TryLoadActorFromClaims(out var redirect))
            return redirect!;

        Signature = await _api.GetWorkerSignatureAsync(WorkerId);
        SignatureName = Signature?.SignatureName ?? "";

        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        if (!TryLoadActorFromClaims(out var redirect))
            return redirect!;

        var (ok, error) = await _api.ChangePasswordAsync(Username, CurrentPassword, NewPassword);

        if (!ok)
        {
            ErrorMessage = error ?? "Password change failed.";
            await LoadSignatureForPageAsync();
            return Page();
        }

        // v1-simple: sign out so the user logs in again and gets a fresh token/claims.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Timesheet/Login");
    }

    public async Task<IActionResult> OnPostSaveSignatureAsync()
    {
        if (!TryLoadActorFromClaims(out var redirect))
            return redirect!;

        var sig = (SignatureName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(sig))
        {
            ErrorMessage = "Signature cannot be empty.";
            await LoadSignatureForPageAsync();
            return Page();
        }

        await _api.SetWorkerSignatureAsync(workerId: WorkerId, signatureName: sig);

        SuccessMessage = "Signature updated.";
        await LoadSignatureForPageAsync();
        return Page();
    }

    private async Task LoadSignatureForPageAsync()
    {
        if (WorkerId <= 0) return;
        Signature = await _api.GetWorkerSignatureAsync(WorkerId);
        SignatureName = Signature?.SignatureName ?? "";
    }

    private bool TryLoadActorFromClaims(out IActionResult? redirect)
    {
        redirect = null;

        if (User.Identity?.IsAuthenticated != true)
        {
            redirect = RedirectToPage("/Timesheet/Login");
            return false;
        }

        WorkerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");
        if (WorkerId <= 0)
        {
            redirect = RedirectToPage("/Timesheet/Login");
            return false;
        }

        Username = User.FindFirst("usr")?.Value ?? "";
        if (string.IsNullOrWhiteSpace(Username))
        {
            redirect = RedirectToPage("/Timesheet/Login");
            return false;
        }

        MustChangePassword = User.FindFirst("must_change_password")?.Value == "true";
        return true;
    }
}
