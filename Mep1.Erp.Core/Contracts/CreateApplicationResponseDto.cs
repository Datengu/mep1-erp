namespace Mep1.Erp.Core.Contracts;

public sealed class CreateApplicationResponseDto
{
    public int Id { get; set; }

    public string ApplicationNumber { get; set; } = "";

    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string JobNameOrNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";

    public DateTime ApplicationDate { get; set; }

    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }

    public decimal? AgreedNetAmount { get; set; }
    public DateTime? DateAgreed { get; set; }

    public string Status { get; set; } = "Submitted";
    public string? Notes { get; set; }
}