namespace Day1Agent.Core.Domain;

public sealed record OrderStatusResult(
    bool Found,
    string Message,
    string? OrderId = null,
    ShipmentStatus? Status = null,
    string? TrackingNumber = null);
