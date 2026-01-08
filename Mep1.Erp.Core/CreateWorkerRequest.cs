namespace Mep1.Erp.Core;
public sealed class CreateWorkerRequest
{
    public string? Initials { get; set; }
    public string? Name { get; set; }

    // optional initial rate
    public decimal? RatePerHour { get; set; }

    // if you want to be able to create inactive people (normally false is fine)
    public bool? IsActive { get; set; }
}