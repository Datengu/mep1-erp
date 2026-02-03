namespace Mep1.Erp.Core.Contracts;

public sealed class CreateApplicationRequestDto
{
    public int ProjectId { get; set; }

    // Keep string to match desktop textbox UX (same as InvoiceNumber)
    public string ApplicationNumber { get; set; } = "";

    public DateTime ApplicationDate { get; set; }

    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; } // e.g. 0.20m

    public decimal? AgreedNetAmount { get; set; }
    public DateTime? DateAgreed { get; set; }

    public string Status { get; set; } = "Submitted";
    public string? Notes { get; set; }
}