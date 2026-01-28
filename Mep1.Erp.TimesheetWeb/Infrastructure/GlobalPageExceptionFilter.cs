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

        // 1) Auth failures coming back from API calls
        if (ex is HttpRequestException hre &&
            (hre.StatusCode == HttpStatusCode.Unauthorized || hre.StatusCode == HttpStatusCode.Forbidden))
        {
            await ForceSignOutWithMessage(context, "You were signed out. Please log in again.");
            return;
        }

        // 2) Connectivity / DNS / TLS / timeout etc. (HttpRequestException often has StatusCode == null)
        if (ex is HttpRequestException hre2 && hre2.StatusCode == null)
        {
            RedirectToTemporarilyUnavailable(context);
            return;
        }

        if (ex is TaskCanceledException)
        {
            RedirectToTemporarilyUnavailable(context);
            return;
        }

        // Otherwise let the normal exception handler handle it (/Error)
        // by not marking it handled.
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

    private static void RedirectToTemporarilyUnavailable(ExceptionContext context)
    {
        var http = context.HttpContext;

        // Store friendly message for the status page
        http.Session.SetString("FlashMessage", "Temporarily unavailable. Please try again in a moment.");

        context.Result = new RedirectToPageResult("/Timesheet/Unavailable");
        context.ExceptionHandled = true;
    }
}
