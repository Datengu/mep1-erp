using Mep1.Erp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Mep1 ERP API", Version = "v1" });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key needed to access the endpoints. Add it as: X-Api-Key: {your key}",
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("ErpDb");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("ConnectionStrings:ErpDb is missing.");

    var b = new SqliteConnectionStringBuilder(cs);

    // If it's a relative path, resolve it relative to the API ContentRoot (project folder)
    if (!string.IsNullOrWhiteSpace(b.DataSource) && !Path.IsPathRooted(b.DataSource))
    {
        b.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, b.DataSource));
    }

    Console.WriteLine($"[DB] Using SQLite file: {b.DataSource}");
    options.UseSqlite(b.ToString());
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
         context.Request.Path.StartsWithSegments("/favicon.ico") ||
         context.Request.Path.StartsWithSegments("/api/health")))
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
