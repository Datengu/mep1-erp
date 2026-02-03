namespace Mep1.Erp.Core.Contracts;

public sealed class UpdateApplicationRequestDto
{
    public int? ProjectId { get; set; }

    public DateTime ApplicationDate { get; set; }

    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }

    public decimal? AgreedNetAmount { get; set; }
    public DateTime? DateAgreed { get; set; }
    public string? ExternalReference { get; set; }

    public string Status { get; set; } = "Submitted";
    public string? Notes { get; set; }
}