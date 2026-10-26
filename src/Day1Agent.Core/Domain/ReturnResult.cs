namespace Day1Agent.Core.Domain;

public sealed record ReturnResult(
    bool Success,
    string Message,
    string? ReturnRequestId = null,
    bool Duplicate = false);
