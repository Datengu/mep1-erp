using Microsoft.AspNetCore.Authentication.Cookies;
using Mep1.Erp.TimesheetWeb;
using Mep1.Erp.TimesheetWeb.Services;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

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
        //options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // set Always in prod if always https
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

builder.Services.AddAuthorization();

builder.Services.AddRazorPages(options =>
{
    // Protect /Timesheet/* by default
    options.Conventions.AuthorizeFolder("/Timesheet");

    // Allow anonymous access to login
    options.Conventions.AllowAnonymousToPage("/Timesheet/Login");
});

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

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();