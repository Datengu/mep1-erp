using Mep1.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("ErpDb");
    options.UseSqlite(cs);
});

var app = builder.Build();

//Do not enable Swagger in Production unless protected.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- API Key auth (minimal) ---
var apiKey = builder.Configuration["Security:ApiKey"];

if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(apiKey))
        throw new InvalidOperationException("Security:ApiKey must be set in Production.");
}

app.Use(async (context, next) =>
{
    // Allow swagger in dev without a key (optional)
    if (app.Environment.IsDevelopment() &&
        (context.Request.Path.StartsWithSegments("/swagger") ||
         context.Request.Path.StartsWithSegments("/favicon.ico")))
    {
        await next();
        return;
    }

    // If no key configured (dev), allow all (optional)
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
        !string.Equals(provided.ToString(), apiKey, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Missing or invalid API key.");
        return;
    }

    await next();
});
// --- end API Key auth ---

app.UseHttpsRedirection();
app.UseAuthorization(); // don’t expose the app publicly without proper auth in place.
app.MapControllers();
app.Run();
