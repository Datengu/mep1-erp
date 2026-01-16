using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Security;

public sealed class RefreshTokenService
{
    private readonly AppDbContext _db;

    // 30 days
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    public RefreshTokenService(AppDbContext db) => _db = db;

    public static string GenerateToken()
    {
        // 64 bytes -> base64url-ish string
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes); // stable, easy
    }

    public async Task<(string refreshToken, DateTime expiresUtc)> IssueAsync(int timesheetUserId)
    {
        var raw = GenerateToken();
        var hash = HashToken(raw);

        var now = DateTime.UtcNow;
        var expires = now.Add(RefreshLifetime);

        _db.RefreshTokens.Add(new RefreshToken
        {
            TimesheetUserId = timesheetUserId,
            TokenHash = hash,
            CreatedUtc = now,
            ExpiresUtc = expires
        });

        await _db.SaveChangesAsync();
        return (raw, expires);
    }

    public async Task<(TimesheetUser user, RefreshToken tokenRow)?> ValidateAsync(string rawRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return null;

        var hash = HashToken(rawRefreshToken);

        var row = await _db.RefreshTokens
            .Include(x => x.TimesheetUser)
            .FirstOrDefaultAsync(x => x.TokenHash == hash);

        if (row == null || !row.IsActive || !row.TimesheetUser.IsActive)
            return null;

        return (row.TimesheetUser, row);
    }

    public async Task<(string newRefreshToken, DateTime newRefreshExpiresUtc)> RotateAsync(RefreshToken oldRow)
    {
        var now = DateTime.UtcNow;

        var newRaw = GenerateToken();
        var newHash = HashToken(newRaw);

        oldRow.RevokedUtc = now;
        oldRow.ReplacedByTokenHash = newHash;

        _db.RefreshTokens.Add(new RefreshToken
        {
            TimesheetUserId = oldRow.TimesheetUserId,
            TokenHash = newHash,
            CreatedUtc = now,
            ExpiresUtc = now.Add(RefreshLifetime)
        });

        await _db.SaveChangesAsync();
        return (newRaw, now.Add(RefreshLifetime));
    }

    public async Task RevokeAsync(string rawRefreshToken)
    {
        var hash = HashToken(rawRefreshToken);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (row == null) return;

        if (row.RevokedUtc == null)
        {
            row.RevokedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
