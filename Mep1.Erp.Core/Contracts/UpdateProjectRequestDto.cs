namespace Mep1.Erp.Core.Contracts;
public sealed class UpdateProjectRequestDto
{
    public int? CompanyId { get; init; }
    public bool IsActive { get; init; }
}