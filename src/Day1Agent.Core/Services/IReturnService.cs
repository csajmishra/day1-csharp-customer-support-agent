using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public interface IReturnService
{
    Task<ReturnEligibilityResult> CheckEligibilityAsync(
        ReturnRequest request,
        CancellationToken cancellationToken = default);

    Task<ReturnResult> CreateReturnAsync(
        ReturnRequest request,
        bool approved,
        CancellationToken cancellationToken = default);
}
