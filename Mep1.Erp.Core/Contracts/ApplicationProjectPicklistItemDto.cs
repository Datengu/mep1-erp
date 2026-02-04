namespace Mep1.Erp.Core.Contracts;
public sealed class ApplicationProjectPicklistItemDto
{
    public int ProjectId { get; set; }
    public string JobNameOrNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";
}