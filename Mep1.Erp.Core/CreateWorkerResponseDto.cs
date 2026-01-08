namespace Mep1.Erp.Core;
public sealed class CreateWorkerResponseDto
{
    public int Id { get; set; }
    public string Initials { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}