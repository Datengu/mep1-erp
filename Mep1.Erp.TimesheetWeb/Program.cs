using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Mep1.Erp.TimesheetWeb;
using Mep1.Erp.TimesheetWeb.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ErpApiSettings>(builder.Configuration.GetSection("ErpApi"));

builder.Services.AddHttpClient<ErpTimesheetApiClient>((sp, http) =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErpApiSettings>>().Value;

    http.BaseAddress = new Uri(cfg.BaseUrl);
    http.DefaultRequestHeaders.Add("X-API-KEY", cfg.ApiKey);
});

// Data protection (used to encrypt/decrypt remember-me cookie)
builder.Services.AddDataProtection();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(12);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSingleton<Mep1.Erp.TimesheetWeb.Services.TechnicalDiaryPdfBuilder>();

builder.WebHost.UseUrls("http://0.0.0.0:5292");

var app = builder.Build();

QuestPDF.Settings.License = LicenseType.Community;

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// ---- Timesheet auth guard (ReturnUrl support) ----
app.Use(async (context, next) =>
{
    // Only protect /Timesheet/*
    if (!context.Request.Path.StartsWithSegments("/Timesheet", out var remaining))
    {
        await next();
        return;
    }

    // Allow anonymous pages
    var p = context.Request.Path.Value ?? "";
    if (p.Equals("/Timesheet/Login", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/Timesheet/Logout", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    // If not logged in (no session), send to login with returnUrl
    var workerId = context.Session.GetInt32("WorkerId");
    if (workerId is null)
    {
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        var loginUrl = "/Timesheet/Login?returnUrl=" + Uri.EscapeDataString(returnUrl);
        context.Response.Redirect(loginUrl);
        return;
    }

    await next();
});

// Remember-me rehydrate middleware (restores session if session expired but cookie exists)
const string RememberCookieName = "MEP1_REMEMBER";
const string RememberProtectorPurpose = "Mep1.Erp.TimesheetWeb.RememberMe.v1";

app.Use(async (ctx, next) =>
{
    // If already logged in via session, carry on
    if (ctx.Session.GetInt32("WorkerId") is not null)
    {
        await next();
        return;
    }

    // If cookie exists, try to restore session
    if (ctx.Request.Cookies.TryGetValue(RememberCookieName, out var protectedValue)
        && !string.IsNullOrWhiteSpace(protectedValue))
    {
        try
        {
            var provider = ctx.RequestServices.GetRequiredService<IDataProtectionProvider>();
            var protector = provider.CreateProtector(RememberProtectorPurpose);

            var json = protector.Unprotect(protectedValue);

            var payload = JsonSerializer.Deserialize<RememberPayload>(json);
            if (payload is not null && payload.WorkerId > 0)
            {
                ctx.Session.SetInt32("WorkerId", payload.WorkerId);
                ctx.Session.SetString("WorkerName", payload.Name ?? "");
                ctx.Session.SetString("WorkerInitials", payload.Initials ?? "");
                ctx.Session.SetString("UserRole", payload.Role ?? "Worker");
                ctx.Session.SetString("Username", payload.Username ?? "");
                ctx.Session.SetString("MustChangePassword", payload.MustChangePassword ? "true" : "false");
            }
        }
        catch
        {
            // If cookie is invalid/tampered/old, just delete it and continue as logged out.
            ctx.Response.Cookies.Delete(RememberCookieName);
        }
    }

    await next();
});

app.UseAuthorization();

app.MapRazorPages();

app.Run();

file sealed class RememberPayload
{
    public int WorkerId { get; set; }
    public string? Name { get; set; }
    public string? Initials { get; set; }
    public string? Role { get; set; }
    public string? Username { get; set; }
    public bool MustChangePassword { get; set; }
}
