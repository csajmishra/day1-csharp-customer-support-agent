using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public sealed class InMemoryEscalationService(IOrderService orderService) : IEscalationService
{
    private int _nextCaseNumber;

    public async Task<EscalationResult> EscalateAsync(
        EscalationRequest request,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.CustomerId)
            || request.CustomerId.Length > 32
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 5 or > 500
            || request.Reason.Any(char.IsControl)
            || (request.OrderId is not null
                && (string.IsNullOrWhiteSpace(request.OrderId)
                    || request.OrderId.Length > 32
                    || request.OrderId.Any(char.IsControl))))
        {
            return new EscalationResult(false, "The escalation request is incomplete or invalid.");
        }

        if (request.OrderId is not null
            && await orderService.GetOrderForCustomerAsync(
                request.OrderId,
                request.CustomerId,
                cancellationToken) is null)
        {
            return new EscalationResult(false, "Order not found.");
        }

        if (!approved)
        {
            return new EscalationResult(
                false,
                "Explicit approval is required before escalating this request.");
        }

        string caseId = $"CASE-{Interlocked.Increment(ref _nextCaseNumber):D4}";
        return new EscalationResult(true, "The request was escalated to a human support queue.", caseId);
    }
}
