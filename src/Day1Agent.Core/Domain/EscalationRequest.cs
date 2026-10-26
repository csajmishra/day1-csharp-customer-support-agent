namespace Day1Agent.Core.Domain;

public sealed record EscalationRequest(
    string CustomerId,
    string Reason,
    string? OrderId = null);
