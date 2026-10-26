using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public sealed class InMemoryReturnService(IOrderService orderService) : IReturnService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _returnIdsByRequest = new(StringComparer.OrdinalIgnoreCase);
    private int _nextReturnNumber;

    public async Task<ReturnEligibilityResult> CheckEligibilityAsync(
        ReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(request))
        {
            return new ReturnEligibilityResult(false, "The return request is incomplete or invalid.");
        }

        Order? order = await orderService.GetOrderForCustomerAsync(
            request.OrderId,
            request.CustomerId,
            cancellationToken);

        if (order is null)
        {
            return new ReturnEligibilityResult(false, "Order not found.");
        }

        OrderItem? item = order.Items.FirstOrDefault(
            candidate => string.Equals(candidate.ItemId, request.ItemId, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            return new ReturnEligibilityResult(false, "The item was not found in this order.");
        }

        if (order.ShipmentStatus != ShipmentStatus.Delivered || DateOnly.FromDateTime(DateTime.UtcNow) > order.ReturnByDate)
        {
            return new ReturnEligibilityResult(false, "This order is not eligible for a return.");
        }

        return new ReturnEligibilityResult(true, "The item is eligible for a return.", item);
    }

    public async Task<ReturnResult> CreateReturnAsync(
        ReturnRequest request,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        ReturnEligibilityResult eligibility = await CheckEligibilityAsync(request, cancellationToken);
        if (!eligibility.Eligible)
        {
            return new ReturnResult(false, eligibility.Message);
        }

        if (!approved)
        {
            return new ReturnResult(false, "Explicit approval is required before creating a return.");
        }

        string key = CreateIdempotencyKey(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_returnIdsByRequest.TryGetValue(key, out string? existingId))
            {
                return new ReturnResult(true, "A return request already exists for this item.", existingId, Duplicate: true);
            }

            string returnId = $"RET-{Interlocked.Increment(ref _nextReturnNumber):D4}";
            _returnIdsByRequest.Add(key, returnId);
            return new ReturnResult(true, "Return request created.", returnId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValid(ReturnRequest request) =>
        !string.IsNullOrWhiteSpace(request.OrderId)
        && request.OrderId.Length <= 32
        && !request.OrderId.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(request.ItemId)
        && request.ItemId.Length <= 32
        && !request.ItemId.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(request.CustomerId)
        && request.CustomerId.Length <= 32
        && !request.CustomerId.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(request.Reason)
        && request.Reason.Trim().Length is >= 5 and <= 500
        && !request.Reason.Any(char.IsControl);

    private static string CreateIdempotencyKey(ReturnRequest request) =>
        $"{request.CustomerId.Trim()}|{request.OrderId.Trim()}|{request.ItemId.Trim()}";
}
