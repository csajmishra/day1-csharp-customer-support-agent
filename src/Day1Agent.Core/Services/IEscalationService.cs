using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public interface IEscalationService
{
    Task<EscalationResult> EscalateAsync(
        EscalationRequest request,
        bool approved,
        CancellationToken cancellationToken = default);
}
