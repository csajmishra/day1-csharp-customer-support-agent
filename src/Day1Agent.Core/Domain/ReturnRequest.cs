namespace Day1Agent.Core.Domain;

public sealed record ReturnRequest(
    string OrderId,
    string ItemId,
    string CustomerId,
    string Reason);
