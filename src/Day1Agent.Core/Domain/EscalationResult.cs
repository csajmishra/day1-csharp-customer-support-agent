namespace Day1Agent.Core.Domain;

public sealed record EscalationResult(
    bool Success,
    string Message,
    string? CaseId = null);
