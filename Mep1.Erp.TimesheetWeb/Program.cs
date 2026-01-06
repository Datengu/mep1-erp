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
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseSession();

app.MapRazorPages();

app.Run();
