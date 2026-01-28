using Mep1.Erp.TimesheetWeb;
using Mep1.Erp.TimesheetWeb.Infrastructure;
using Mep1.Erp.TimesheetWeb.Middleware;
using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // nginx is on the same box; we’ll trust forwarded headers from it
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<ErpApiSettings>(builder.Configuration.GetSection("ErpApi"));

// Needed for BearerTokenHandler
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<RefreshOnUnauthorizedHandler>();
builder.Services.AddTransient<BearerTokenHandler>();

builder.Services.AddHttpClient("ErpAuth", (sp, http) =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErpApiSettings>>().Value;

    http.BaseAddress = new Uri(cfg.BaseUrl);
    http.DefaultRequestHeaders.Add("X-Api-Key", cfg.ApiKey);
    http.DefaultRequestHeaders.Add("X-Client-App", "Portal");
});

builder.Services.AddHttpClient<ErpTimesheetApiClient>((sp, http) =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErpApiSettings>>().Value;

    http.BaseAddress = new Uri(cfg.BaseUrl);

    // IMPORTANT: must match API middleware header name exactly
    // API checks "X-Api-Key"
    http.DefaultRequestHeaders.Add("X-Api-Key", cfg.ApiKey);
    http.DefaultRequestHeaders.Add("X-Client-App", "Portal");
})
.AddHttpMessageHandler<RefreshOnUnauthorizedHandler>()
.AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(12);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add services to the container.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Timesheet/Login";
        options.LogoutPath = "/Timesheet/Logout";
        options.AccessDeniedPath = "/Timesheet/Login";

        options.SlidingExpiration = true;

        options.Cookie.Name = "MEP1_AUTH";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                // If they hit a protected page while signed out
                ctx.HttpContext.Session.SetString("FlashMessage", "Please log in to continue.");
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                // If authenticated but forbidden by policy/role (if you add that later)
                ctx.HttpContext.Session.SetString("FlashMessage", "You do not have access to that page.");
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Timesheet");
    options.Conventions.AllowAnonymousToPage("/Timesheet/Login");

    // Add global exception handling for Razor Pages
    options.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(GlobalPageExceptionFilter)));

    // Allow anonymous to Unavailable page too
    options.Conventions.AllowAnonymousToPage("/Timesheet/Unavailable");
});

builder.Services.AddScoped<GlobalPageExceptionFilter>();

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

app.UseForwardedHeaders();

app.UseMiddleware<CorrelationIdResponseHeaderMiddleware>();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();