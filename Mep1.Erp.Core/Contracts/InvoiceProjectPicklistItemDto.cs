namespace Mep1.Erp.Core.Contracts;
public sealed class InvoiceProjectPicklistItemDto
{
    public int ProjectId { get; set; }
    public string JobNameOrNumber { get; set; } = "";

    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
}
