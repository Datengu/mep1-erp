namespace Mep1.Erp.Core.Contracts;
public sealed class UpdateInvoiceRequestDto
{
    // v1: allow fixing project link if needed (optional)
    public int? ProjectId { get; set; }

    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }

    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; } // 0.20m = 20%

    public string Status { get; set; } = "Outstanding";

    public decimal? PaymentAmount { get; set; }
    public DateTime? PaidDate { get; set; }

    public string? FilePath { get; set; }
    public string? Notes { get; set; }
}