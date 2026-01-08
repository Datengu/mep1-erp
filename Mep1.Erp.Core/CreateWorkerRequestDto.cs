namespace Mep1.Erp.Core;
public sealed class CreateWorkerRequestDto
{
    public string Initials { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? RatePerHour { get; set; }
    public bool? IsActive { get; set; }
}