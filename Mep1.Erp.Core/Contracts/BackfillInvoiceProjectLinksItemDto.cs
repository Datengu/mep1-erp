namespace Mep1.Erp.Core.Contracts;
public sealed class BackfillInvoiceProjectLinksItemDto
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceProjectCode { get; set; } = "";
    public string Outcome { get; set; } = "";
    public int? MatchedProjectId { get; set; }
    public string? MatchedProjectLabel { get; set; }
    public string? Note { get; set; }
}