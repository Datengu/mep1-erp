using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Mep1.Erp.TimesheetWeb.Infrastructure;

public sealed class GlobalPageExceptionFilter : IAsyncExceptionFilter
{
    private readonly ILogger<GlobalPageExceptionFilter> _log;

    public GlobalPageExceptionFilter(ILogger<GlobalPageExceptionFilter> log)
    {
        _log = log;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        var http = context.HttpContext;
        var ex = context.Exception;

        // Always log server-side with request id
        _log.LogError(ex, "Unhandled exception in TimesheetWeb. RequestId={RequestId}", http.TraceIdentifier);

        // 1) Auth failures coming back from API calls (expired JWT, forbidden, bad API key etc.)
        if (ex is Services.ErpTimesheetApiClient.TimesheetApiAuthException authEx)
        {
            await ForceSignOutWithMessage(context, authEx.Message);
            return;
        }

        // 2) Connectivity / DNS / TLS / timeout etc.
        if (ex is Services.ErpTimesheetApiClient.TimesheetApiUnavailableException)
        {
            RedirectToTemporarilyUnavailable(context, "Temporarily unavailable. Please try again in a moment.");
            return;
        }

        // 3) Rate limiting (429)
        if (ex is Services.ErpTimesheetApiClient.TimesheetApiRateLimitException rateEx)
        {
            RedirectToTemporarilyUnavailable(context, rateEx.Message);
            return;
        }

        // 4) Not found (if any call throws it; some of your methods convert 404 -> null already)
        if (ex is Services.ErpTimesheetApiClient.TimesheetApiNotFoundException nfEx)
        {
            RedirectToTemporarilyUnavailable(context, nfEx.Message);
            return;
        }

        // 5) Validation (400) - do NOT redirect globally; let the page handle ModelState later
        // if (ex is Services.ErpTimesheetApiClient.TimesheetApiValidationException) { }

        // Otherwise let the normal exception handler handle it (/Error)
    }

    private static async Task ForceSignOutWithMessage(ExceptionContext context, string message)
    {
        var http = context.HttpContext;

        // Store message for next request (Login page) - uses Session (you already enable Session)
        http.Session.SetString("FlashMessage", message);

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.Session.Remove("FlashMessageAfterSignoutDone"); // (optional, if you add extra flags later)

        // Redirect to login
        context.Result = new RedirectToPageResult("/Timesheet/Login");
        context.ExceptionHandled = true;
    }

    private static void RedirectToTemporarilyUnavailable(ExceptionContext context, string message)
    {
        var http = context.HttpContext;

        http.Session.SetString("FlashMessage", message);

        context.Result = new RedirectToPageResult("/Timesheet/Unavailable");
        context.ExceptionHandled = true;
    }
}
