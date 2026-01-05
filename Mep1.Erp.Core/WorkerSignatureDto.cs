namespace Mep1.Erp.Core;

public sealed class WorkerSignatureDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? SignatureName { get; set; }
    public DateTime? SignatureCapturedAtUtc { get; set; }
}