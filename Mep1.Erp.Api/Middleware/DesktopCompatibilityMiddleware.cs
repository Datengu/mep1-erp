using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mep1.Erp.Api.Middleware
{
    public sealed class DesktopCompatibilityMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;

        public DesktopCompatibilityMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _config = config;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only enforce for Desktop callers.
            // TimesheetWeb should not send X-Client-App: Desktop, so it won't be affected.
            if (!context.Request.Headers.TryGetValue("X-Client-App", out var app) ||
                !string.Equals(app.ToString(), "Desktop", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var minStr =
                _config["Compatibility:MinDesktopVersion"] ??
                _config["MIN_DESKTOP_VERSION"];

            // If not configured, don't block (safe default).
            if (string.IsNullOrWhiteSpace(minStr) || !Version.TryParse(minStr, out var minV))
            {
                await _next(context);
                return;
            }

            var yourStr = context.Request.Headers.TryGetValue("X-Client-Version", out var v)
                ? v.ToString()
                : "";

            if (!Version.TryParse(yourStr, out var yourV))
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "upgrade_required",
                    message = "Invalid or missing X-Client-Version. Please close and re-open the desktop app to update.",
                    minDesktopVersion = minV.ToString(),
                    yourVersion = string.IsNullOrWhiteSpace(yourStr) ? null : yourStr
                }));
                return;
            }

            if (yourV < minV)
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "upgrade_required",
                    message = "Update required. Please close and re-open the desktop app to update.",
                    minDesktopVersion = minV.ToString(),
                    yourVersion = yourV.ToString()
                }));
                return;
            }

            await _next(context);
        }
    }
}