
namespace Mep1.Erp.Core.Contracts;
public sealed record PortalUsernameAvailableDto(
    string Normalized,
    bool Available,
    string Suggested
);
