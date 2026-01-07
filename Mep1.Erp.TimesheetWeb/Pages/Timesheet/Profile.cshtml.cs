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

    public WorkerSignatureDto? Signature { get; private set; }

    [BindProperty]
    public string SignatureName { get; set; } = "";

    public string? SuccessMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        WorkerId = HttpContext.Session.GetInt32("WorkerId");
        if (WorkerId is null)
            return RedirectToPage("/Timesheet/Login");

        Username = HttpContext.Session.GetString("Username") ?? "";
        MustChangePassword = HttpContext.Session.GetString("MustChangePassword") == "true";

        Signature = await _api.GetWorkerSignatureAsync(WorkerId.Value);
        SignatureName = Signature?.SignatureName ?? "";

        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        WorkerId = HttpContext.Session.GetInt32("WorkerId");
        if (WorkerId is null)
            return RedirectToPage("/Timesheet/Login");

        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrWhiteSpace(username))
            return RedirectToPage("/Timesheet/Login");

        var (ok, error) = await _api.ChangePasswordAsync(username, CurrentPassword, NewPassword);

        if (!ok)
        {
            ErrorMessage = error ?? "Password change failed.";
            Username = HttpContext.Session.GetString("Username") ?? "";
            MustChangePassword = HttpContext.Session.GetString("MustChangePassword") == "true";
            await LoadSignatureForPageAsync();
            return Page();
        }

        HttpContext.Session.SetString("MustChangePassword", "false");
        return RedirectToPage("/Timesheet/History");
    }

    public async Task<IActionResult> OnPostSaveSignatureAsync()
    {
        WorkerId = HttpContext.Session.GetInt32("WorkerId");
        if (WorkerId is null)
            return RedirectToPage("/Timesheet/Login");

        MustChangePassword = HttpContext.Session.GetString("MustChangePassword") == "true";
        Username = HttpContext.Session.GetString("Username") ?? "";

        var sig = (SignatureName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(sig))
        {
            ErrorMessage = "Signature cannot be empty.";

            // rehydrate state for re-render
            Username = HttpContext.Session.GetString("Username") ?? "";
            MustChangePassword = HttpContext.Session.GetString("MustChangePassword") == "true";

            await LoadSignatureForPageAsync();
            return Page();
        }

        await _api.SetWorkerSignatureAsync(
            workerId: WorkerId.Value,
            actorWorkerId: WorkerId.Value,
            signatureName: sig);

        SuccessMessage = "Signature updated.";
        await LoadSignatureForPageAsync(); // reload to show saved value
        return Page();
    }

    private async Task LoadSignatureForPageAsync()
    {
        if (WorkerId is null) return;

        Signature = await _api.GetWorkerSignatureAsync(WorkerId.Value);
        SignatureName = Signature?.SignatureName ?? "";
    }

}
