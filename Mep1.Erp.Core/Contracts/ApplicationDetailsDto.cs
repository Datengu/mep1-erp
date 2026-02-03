namespace Mep1.Erp.Core.Contracts;

public sealed class ApplicationDetailsDto
{
    public int Id { get; init; }

    public string ApplicationNumber { get; init; } = "";

    public int? ProjectId { get; init; }
    public string ProjectCode { get; init; } = "";
    public string? JobName { get; init; }
    public string? CompanyName { get; init; }

    public DateTime ApplicationDate { get; init; }

    public decimal NetAmount { get; init; }
    public decimal VatRate { get; init; }
    public decimal VatAmount { get; init; }
    public decimal GrossAmount { get; init; }

    public string? Status { get; init; }
    public string? Notes { get; init; }

    // Existing domain fields (kept for future UI without breaking contracts)
    public decimal? AgreedNetAmount { get; init; }
    public DateTime? DateAgreed { get; init; }
    public string? ExternalReference { get; init; }
}