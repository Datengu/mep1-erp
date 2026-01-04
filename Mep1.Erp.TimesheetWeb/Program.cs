using Mep1.Erp.TimesheetWeb;
using Mep1.Erp.TimesheetWeb.Services;

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

var app = builder.Build();

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
