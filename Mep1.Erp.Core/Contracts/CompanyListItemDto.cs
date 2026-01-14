namespace Mep1.Erp.Core.Contracts;
public sealed class CompanyListItemDto
{
    public int Id { get; init; }
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public bool IsActive { get; init; }
}