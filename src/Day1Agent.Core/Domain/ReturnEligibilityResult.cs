namespace Day1Agent.Core.Domain;

public sealed record ReturnEligibilityResult(
    bool Eligible,
    string Message,
    OrderItem? Item = null);
