namespace Day1Agent.Core.Domain;

public sealed record Order(
    string OrderId,
    string CustomerId,
    DateOnly PlacedOn,
    DateOnly ReturnByDate,
    ShipmentStatus ShipmentStatus,
    string TrackingNumber,
    IReadOnlyList<OrderItem> Items);
