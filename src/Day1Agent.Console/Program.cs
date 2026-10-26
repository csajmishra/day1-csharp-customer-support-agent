using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Day1Agent.Core.Services;

namespace Day1Agent.ConsoleApp;

internal static class Program
{
    private const string DemoCustomerId = "CUS-1001";

    private static async Task<int> Main()
    {
        using CancellationTokenSource cancellationSource = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        try
        {
            AgentConfiguration configuration = AgentConfiguration.FromEnvironment();
            string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
            IOrderService orderService = await JsonOrderService.LoadAsync(
                Path.Combine(dataDirectory, "customers.json"),
                Path.Combine(dataDirectory, "orders.json"),
                cancellationSource.Token);

            IReturnService returnService = new InMemoryReturnService(orderService);
            IEscalationService escalationService = new InMemoryEscalationService(orderService);
            DefaultAzureCredential credential = new();
            PersistentAgentsClient client = new(configuration.ProjectEndpoint.AbsoluteUri, credential);

            await using AzureFoundryAgentSession agent = await AzureFoundryAgentSession.CreateAsync(
                client,
                configuration.ModelDeploymentName,
                DemoCustomerId,
                orderService,
                returnService,
                escalationService,
                ConfirmActionAsync,
                cancellationSource.Token);

            Console.WriteLine("Contoso Outdoor support demo (synthetic data only).");
            Console.WriteLine("Ask about an order, an eligible return, or request human support.");
            Console.WriteLine("Try: Where is order ORD-1001? Type 'exit' to leave.");
            Console.WriteLine("This demo is fixed to synthetic customer CUS-1001.");

            while (!cancellationSource.IsCancellationRequested)
            {
                Console.Write("\nYou: ");
                string? input = await Console.In.ReadLineAsync(cancellationSource.Token);
                if (input is null || string.Equals(input.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                if (input.Length > 4_000)
                {
                    Console.WriteLine("Please keep each message under 4,000 characters.");
                    continue;
                }

                string response = await agent.RespondAsync(input, cancellationSource.Token);
                Console.WriteLine($"\nSupport: {response}");
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            Console.WriteLine("\nSession cancelled.");
            return 0;
        }
        catch (ConfigurationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (AuthenticationFailedException)
        {
            Console.Error.WriteLine("Azure authentication failed. Sign in with Azure CLI or configure a supported DefaultAzureCredential identity.");
            return 3;
        }
        catch (RequestFailedException exception)
        {
            Console.Error.WriteLine($"The Azure agent request failed (HTTP {exception.Status}). Check endpoint, deployment, and project permissions.");
            return 3;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("The agent did not finish in time. Please try again.");
            return 4;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The session failed unexpectedly ({exception.GetType().Name}). No stack trace or sensitive details were displayed.");
            return 1;
        }
    }

    private static async Task<bool> ConfirmActionAsync(string summary, CancellationToken cancellationToken)
    {
        Console.WriteLine($"\nProposed action: {summary}");
        Console.Write("Type YES to approve, or anything else to cancel: ");
        string? answer = await Console.In.ReadLineAsync(cancellationToken);
        return string.Equals(answer?.Trim(), "YES", StringComparison.OrdinalIgnoreCase);
    }
}
