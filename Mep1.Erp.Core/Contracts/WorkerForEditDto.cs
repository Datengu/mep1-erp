using System;
using System.Collections.Generic;

namespace Mep1.Erp.Core.Contracts
{
    public sealed class WorkerForEditDto
    {
        public int WorkerId { get; init; }
        public string Initials { get; init; } = "";
        public string Name { get; init; } = "";
        public string? SignatureName { get; init; }
        public bool IsActive { get; init; }

        public List<WorkerRateDto> Rates { get; init; } = new();
    }

    public sealed class UpdateWorkerDetailsRequestDto
    {
        public string Initials { get; init; } = "";
        public string Name { get; init; } = "";
        public string? SignatureName { get; init; }
    }

    public sealed record ChangeCurrentRateRequestDto(DateTime EffectiveFrom, decimal NewRatePerHour);

    public sealed record AddWorkerRateRequestDto(DateTime ValidFrom, DateTime ValidTo, decimal RatePerHour);

    public sealed record UpdateWorkerRateAmountRequestDto(decimal RatePerHour);
}
