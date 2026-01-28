using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Api.Middleware;
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
builder.Services.AddScoped<RefreshTokenService>();

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

    // API Key
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key needed for server-to-server calls. Header: X-Api-Key: {your key}",
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey
    });

    // Bearer / JWT
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // IMPORTANT:
    // Declare that endpoints may be authorized with either ApiKey OR Bearer.
    // (Swagger represents this as multiple requirements = OR)
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

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// EF Core (SQLite for dev, Postgres-ready)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";

    var cs = builder.Configuration.GetConnectionString("ErpDb");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("ConnectionStrings:ErpDb is missing.");

    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        // NOTE: Will work once Npgsql.EntityFrameworkCore.PostgreSQL is installed
        Console.WriteLine("[DB] Using PostgreSQL");
        options.UseNpgsql(cs);
    }
    else
    {
        // Default: SQLite (local dev)
        var b = new SqliteConnectionStringBuilder(cs);

        if (!string.IsNullOrWhiteSpace(b.DataSource) && !Path.IsPathRooted(b.DataSource))
        {
            b.DataSource = Path.GetFullPath(
                Path.Combine(builder.Environment.ContentRootPath, b.DataSource));
        }

        Console.WriteLine($"[DB] Using SQLite file: {b.DataSource}");
        options.UseSqlite(b.ToString());
    }
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

app.UseMiddleware<DesktopCompatibilityMiddleware>();

// --- API Key auth (portal-only, JWT bypass) ---
var portalApiKey = builder.Configuration["Security:PortalApiKey"];

// In Production we expect a portal key for server-to-server calls.
// Desktop must NOT need a key.
if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(portalApiKey))
        throw new InvalidOperationException("Security:PortalApiKey must be set in Production.");
}

static bool HasBearerToken(HttpContext ctx)
{
    if (!ctx.Request.Headers.TryGetValue("Authorization", out var auth))
        return false;

    var s = auth.ToString();
    return s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    // Always allow these without any API key
    if (path.StartsWithSegments("/api/health") ||
        path.StartsWithSegments("/swagger") ||
        path.StartsWithSegments("/favicon.ico") ||
        // Auth endpoints must be reachable WITHOUT a key (login/refresh/etc.)
        path.StartsWithSegments("/api/auth"))
    {
        await next();
        return;
    }

    // If the request is JWT-authenticated (desktop after login, portal on-behalf-of-user),
    // then bypass API key checks entirely.
    if (HasBearerToken(context))
    {
        await next();
        return;
    }

    // If no portal key configured (typical dev), don't enforce API key at all
    if (string.IsNullOrWhiteSpace(portalApiKey))
    {
        await next();
        return;
    }

    // Otherwise require the portal API key for server-to-server calls
    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Missing API key.");
        return;
    }

    var key = provided.ToString();
    if (!string.Equals(key, portalApiKey, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Invalid API key.");
        return;
    }

    context.Items["ApiKeyKind"] = "Portal";
    await next();
});
// --- end API Key auth ---

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
