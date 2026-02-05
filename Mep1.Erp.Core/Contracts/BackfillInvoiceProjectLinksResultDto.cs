namespace Mep1.Erp.Core.Contracts;
public sealed class BackfillInvoiceProjectLinksResultDto
{
    public bool DryRun { get; set; }
    public int TotalCandidates { get; set; }
    public int Updated { get; set; }
    public int SkippedAlreadyLinked { get; set; }
    public int SkippedNoMatch { get; set; }
    public int SkippedAmbiguous { get; set; }
    public List<BackfillInvoiceProjectLinksItemDto> Items { get; set; } = new();
}