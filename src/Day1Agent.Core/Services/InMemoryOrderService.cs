using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public sealed class InMemoryOrderService : IOrderService
{
    private readonly HashSet<string> _customerIds;
    private readonly Dictionary<string, Order> _orders;

    public InMemoryOrderService(IEnumerable<Customer> customers, IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(orders);

        _customerIds = customers
            .Select(customer => customer.CustomerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _orders = orders.ToDictionary(order => order.OrderId, StringComparer.OrdinalIgnoreCase);
    }

    public Task<OrderStatusResult> GetOrderStatusAsync(
        string orderId,
        string customerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(customerId))
        {
            return Task.FromResult(new OrderStatusResult(false, "Order not found."));
        }

        if (!_customerIds.Contains(customerId.Trim())
            || !_orders.TryGetValue(orderId.Trim(), out Order? order)
            || !string.Equals(order.CustomerId, customerId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new OrderStatusResult(false, "Order not found."));
        }

        return Task.FromResult(new OrderStatusResult(
            true,
            "Order status found.",
            order.OrderId,
            order.ShipmentStatus,
            order.TrackingNumber));
    }

    public Task<Order?> GetOrderForCustomerAsync(
        string orderId,
        string customerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(customerId)
            || !_customerIds.Contains(customerId.Trim())
            || !_orders.TryGetValue(orderId.Trim(), out Order? order)
            || !string.Equals(order.CustomerId, customerId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<Order?>(null);
        }

        return Task.FromResult<Order?>(order);
    }
}
