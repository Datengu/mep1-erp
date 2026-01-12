namespace Mep1.Erp.Core.Contracts
{
    public sealed class CreateProjectRequestDto
    {
        public string JobNameOrNumber { get; init; } = "";
        public string? Company { get; init; }
        public bool IsActive { get; init; } = true;
    }
}