namespace Day1Agent.Core.Agents;

public interface ICustomerSupportAgent : IAsyncDisposable
{
    Task<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default);
}
