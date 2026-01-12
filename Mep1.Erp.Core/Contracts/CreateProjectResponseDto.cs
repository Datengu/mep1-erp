namespace Mep1.Erp.Core.Contracts
{
    public sealed class CreateProjectResponseDto
    {
        public int Id { get; init; }
        public string JobNameOrNumber { get; init; } = "";
        public string Company { get; init; } = "";
        public bool IsActive { get; init; }
    }
}
