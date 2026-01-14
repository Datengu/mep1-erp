namespace Mep1.Erp.Core.Contracts
{
    public sealed class CreateProjectResponseDto
    {
        public int Id { get; set; }
        public string JobNameOrNumber { get; set; } = "";
        public int? CompanyId { get; init; }
        public string? CompanyCode { get; set; }
        public string? CompanyName { get; set; }
        public bool IsActive { get; set; }
    }
}
