namespace Mep1.Erp.Core.Contracts;

public sealed class WorkerSignatureDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Initials { get; set; } = "";
    public string? SignatureName { get; set; }
    public DateTime? SignatureCapturedAtUtc { get; set; }
}