namespace Mep1.Erp.Core.Contracts;
public sealed class ProjectEditDto
{
    public string JobNameOrNumber { get; init; } = "";

    public int? CompanyId { get; init; }
    public string? CompanyCode { get; init; }
    public string? CompanyName { get; init; }

    public bool IsActive { get; init; }
}