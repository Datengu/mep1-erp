namespace Mep1.Erp.Core.Contracts
{
    public sealed class CreateProjectRequestDto
    {
        public string JobNameOrNumber { get; set; } = "";
        public string? CompanyCode { get; set; }
        public string? CompanyName { get; set; }
        public bool IsActive { get; set; } = true;
    }
}