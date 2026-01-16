using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// JWT auth
builder.Services.AddSingleton<Mep1.Erp.Api.Security.JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Security:JwtIssuer"] ?? "mep1-erp";
        var audience = builder.Configuration["Security:JwtAudience"] ?? "mep1-erp";
        var signingKey = builder.Configuration["Security:JwtSigningKey"] ?? "";

        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("Security:JwtSigningKey is missing.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            ValidateAudience = true,
            ValidAudience = audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOrOwner", p => p.RequireRole(
        TimesheetUserRole.Admin.ToString(),
        TimesheetUserRole.Owner.ToString()));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMemoryCache();
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

// Audit Log
builder.Services.AddScoped<AuditLogger>();

var app = builder.Build();

//Do not enable Swagger in Production unless protected.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// --- API Key auth (minimal) ---
var portalApiKey = builder.Configuration["Security:PortalApiKey"];
var adminApiKey = builder.Configuration["Security:AdminApiKey"];

// Backward compat (optional): if you still have Security:ApiKey set, treat it as admin key
var legacyApiKey = builder.Configuration["Security:ApiKey"];
if (!string.IsNullOrWhiteSpace(legacyApiKey) && string.IsNullOrWhiteSpace(adminApiKey))
    adminApiKey = legacyApiKey;

if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(portalApiKey))
        throw new InvalidOperationException("Security:PortalApiKey must be set in Production.");

    if (string.IsNullOrWhiteSpace(adminApiKey))
        throw new InvalidOperationException("Security:AdminApiKey must be set in Production.");
}

app.Use(async (context, next) =>
{
    // Allow swagger + health in dev without a key (optional)
    if (app.Environment.IsDevelopment() &&
        (context.Request.Path.StartsWithSegments("/swagger") ||
         context.Request.Path.StartsWithSegments("/favicon.ico") ||
         context.Request.Path.StartsWithSegments("/api/health")))
    {
        await next();
        return;
    }

    // If no keys configured (dev), allow all (optional)
    if (string.IsNullOrWhiteSpace(portalApiKey) && string.IsNullOrWhiteSpace(adminApiKey))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Missing API key.");
        return;
    }

    var key = provided.ToString();

    if (!string.IsNullOrWhiteSpace(adminApiKey) &&
        string.Equals(key, adminApiKey, StringComparison.Ordinal))
    {
        context.Items["ApiKeyKind"] = "Admin";
        await next();
        return;
    }

    if (!string.IsNullOrWhiteSpace(portalApiKey) &&
        string.Equals(key, portalApiKey, StringComparison.Ordinal))
    {
        context.Items["ApiKeyKind"] = "Portal";
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsync("Invalid API key.");
});
// --- end API Key auth ---

app.UseAuthentication();
app.UseAuthorization(); // don’t expose the app publicly without proper auth in place.

app.MapControllers();
app.Run();
