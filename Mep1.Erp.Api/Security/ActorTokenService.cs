using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mep1.Erp.Core;

namespace Mep1.Erp.Api.Security;

public sealed class ActorTokenService
{
    private readonly byte[] _key;

    public ActorTokenService(string signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("Security:ActorTokenSigningKey is missing.");

        _key = Encoding.UTF8.GetBytes(signingKey);
        if (_key.Length < 32)
            throw new InvalidOperationException("ActorTokenSigningKey must be at least 32 bytes.");
    }

    private sealed record ActorTokenPayload(
        int WorkerId,
        string Role,
        string Username,
        long ExpUtcTicks,
        string Nonce
    );

    public string CreateToken(int workerId, TimesheetUserRole role, string username, DateTime expiresUtc)
    {
        var payload = new ActorTokenPayload(
            WorkerId: workerId,
            Role: role.ToString(),
            Username: username,
            ExpUtcTicks: expiresUtc.Ticks,
            Nonce: Guid.NewGuid().ToString("N")
        );

        var json = JsonSerializer.Serialize(payload);
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var sigB64 = Base64UrlEncode(ComputeHmac(Encoding.UTF8.GetBytes(payloadB64)));

        return payloadB64 + "." + sigB64;
    }

    public bool TryValidate(string token, out ActorContext? actor, out string error)
    {
        actor = null;
        error = "Invalid token.";

        if (string.IsNullOrWhiteSpace(token))
        {
            error = "Missing token.";
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            error = "Invalid token format.";
            return false;
        }

        var payloadB64 = parts[0];
        var sigB64 = parts[1];

        var expectedSig = Base64UrlEncode(ComputeHmac(Encoding.UTF8.GetBytes(payloadB64)));
        if (!CryptographicEquals(sigB64, expectedSig))
        {
            error = "Invalid token signature.";
            return false;
        }

        ActorTokenPayload? payload;
        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
            payload = JsonSerializer.Deserialize<ActorTokenPayload>(json);
        }
        catch
        {
            error = "Invalid token payload.";
            return false;
        }

        if (payload == null)
        {
            error = "Invalid token payload.";
            return false;
        }

        if (!Enum.TryParse<TimesheetUserRole>(payload.Role, ignoreCase: true, out var role))
        {
            error = "Invalid role in token.";
            return false;
        }

        var expiresUtc = new DateTime(payload.ExpUtcTicks, DateTimeKind.Utc);
        if (DateTime.UtcNow > expiresUtc)
        {
            error = "Token expired.";
            return false;
        }

        actor = new ActorContext
        {
            WorkerId = payload.WorkerId,
            Role = role,
            Username = payload.Username ?? "",
            ExpiresUtc = expiresUtc
        };

        error = "";
        return true;
    }

    private byte[] ComputeHmac(byte[] data)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(data);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
