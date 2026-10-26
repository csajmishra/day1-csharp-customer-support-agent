using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public interface IOrderService
{
    Task<OrderStatusResult> GetOrderStatusAsync(
        string orderId,
        string customerId,
        CancellationToken cancellationToken = default);

    Task<Order?> GetOrderForCustomerAsync(
        string orderId,
        string customerId,
        CancellationToken cancellationToken = default);
}
