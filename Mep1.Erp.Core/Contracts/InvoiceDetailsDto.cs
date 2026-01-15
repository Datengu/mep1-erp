namespace Mep1.Erp.Core.Contracts;
public sealed class InvoiceDetailsDto
{
    public int Id { get; init; }

    public string InvoiceNumber { get; init; } = "";

    // Project linkage (nullable for historical invoices)
    public int? ProjectId { get; init; }
    public string ProjectCode { get; init; } = "";
    public string? JobName { get; init; }
    public string? CompanyName { get; init; }

    public DateTime InvoiceDate { get; init; }
    public DateTime? DueDate { get; init; }

    public decimal NetAmount { get; init; }
    public decimal VatRate { get; init; }          // 0.20m for 20%
    public decimal VatAmount { get; init; }
    public decimal GrossAmount { get; init; }

    public decimal? PaymentAmount { get; init; }
    public DateTime? PaidDate { get; init; }

    public string? Status { get; init; }
    public bool IsPaid { get; init; }

    public string? FilePath { get; init; }
    public string? Notes { get; init; }
}