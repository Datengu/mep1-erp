using Mep1.Erp.Core.Contracts;

namespace Mep1.Erp.Desktop;

public static class DesktopActorSession
{
    public static bool IsLoggedIn => ActorToken != null;

    public static int ActorWorkerId { get; private set; }
    public static string Username { get; private set; } = "";
    public static string Role { get; private set; } = "";
    public static string Name { get; private set; } = "";
    public static string Initials { get; private set; } = "";
    public static bool MustChangePassword { get; private set; }
    public static string? ActorToken { get; private set; }
    public static DateTime ExpiresUtc { get; private set; }

    public static void Set(DesktopAdminLoginResponseDto dto)
    {
        ActorWorkerId = dto.ActorWorkerId;
        Username = dto.Username;
        Role = dto.Role;
        Name = dto.Name;
        Initials = dto.Initials;
        MustChangePassword = dto.MustChangePassword;
        ActorToken = dto.ActorToken;
        ExpiresUtc = dto.ExpiresUtc;
    }

    public static void Clear()
    {
        ActorWorkerId = 0;
        Username = "";
        Role = "";
        Name = "";
        Initials = "";
        MustChangePassword = false;
        ActorToken = null;
        ExpiresUtc = default;
    }

    public static void SetFromJwtLogin(
    int workerId,
    string username,
    string role,
    string name,
    string initials,
    bool mustChangePassword,
    DateTime expiresUtc)
    {
        ActorWorkerId = workerId;
        Username = username;
        Role = role;
        Name = name;
        Initials = initials;
        MustChangePassword = mustChangePassword;
        ExpiresUtc = expiresUtc;
    }
}
