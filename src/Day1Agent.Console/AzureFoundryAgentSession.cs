using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Agents.Persistent;
using Day1Agent.Core.Agents;
using Day1Agent.Core.Domain;
using Day1Agent.Core.Services;

namespace Day1Agent.ConsoleApp;

internal sealed class AzureFoundryAgentSession : ICustomerSupportAgent
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly PersistentAgentsClient _client;
    private readonly PersistentAgent _agent;
    private readonly PersistentAgentThread _thread;
    private readonly string _customerId;
    private readonly IOrderService _orderService;
    private readonly IReturnService _returnService;
    private readonly IEscalationService _escalationService;
    private readonly Func<string, CancellationToken, Task<bool>> _confirmActionAsync;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private bool _disposed;

    private AzureFoundryAgentSession(
        PersistentAgentsClient client,
        PersistentAgent agent,
        PersistentAgentThread thread,
        string customerId,
        IOrderService orderService,
        IReturnService returnService,
        IEscalationService escalationService,
        Func<string, CancellationToken, Task<bool>> confirmActionAsync)
    {
        _client = client;
        _agent = agent;
        _thread = thread;
        _customerId = customerId;
        _orderService = orderService;
        _returnService = returnService;
        _escalationService = escalationService;
        _confirmActionAsync = confirmActionAsync;
    }

    public static async Task<AzureFoundryAgentSession> CreateAsync(
        PersistentAgentsClient client,
        string modelDeploymentName,
        string customerId,
        IOrderService orderService,
        IReturnService returnService,
        IEscalationService escalationService,
        Func<string, CancellationToken, Task<bool>> confirmActionAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDeploymentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(orderService);
        ArgumentNullException.ThrowIfNull(returnService);
        ArgumentNullException.ThrowIfNull(escalationService);
        ArgumentNullException.ThrowIfNull(confirmActionAsync);

        PersistentAgent agent = await client.Administration.CreateAgentAsync(
            model: modelDeploymentName,
            name: $"day1-customer-support-{Guid.NewGuid():N}"[..34],
            description: "Day 1 customer-support agent using synthetic order data.",
            instructions: CustomerSupportInstructions.Text,
            tools: CreateTools(),
            cancellationToken: cancellationToken);

        try
        {
            PersistentAgentThread thread = await client.Threads.CreateThreadAsync(cancellationToken: cancellationToken);
            return new AzureFoundryAgentSession(
                client,
                agent,
                thread,
                customerId,
                orderService,
                returnService,
                escalationService,
                confirmActionAsync);
        }
        catch
        {
            await client.Administration.DeleteAgentAsync(agent.Id);
            throw;
        }
    }

    public async Task<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        if (userMessage.Length > 4_000)
        {
            throw new ArgumentOutOfRangeException(nameof(userMessage), "Messages cannot exceed 4,000 characters.");
        }

        await _turnGate.WaitAsync(cancellationToken);
        try
        {
            await _client.Messages.CreateMessageAsync(
                _thread.Id,
                MessageRole.User,
                userMessage,
                cancellationToken: cancellationToken);

            ThreadRun run = await _client.Runs.CreateRunAsync(_thread, _agent, cancellationToken: cancellationToken);
            Stopwatch timeout = Stopwatch.StartNew();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeout.Elapsed > TimeSpan.FromMinutes(2))
                {
                    throw new TimeoutException();
                }

                if (run.Status == RunStatus.RequiresAction
                    && run.RequiredAction is SubmitToolOutputsAction submitToolOutputsAction)
                {
                    List<ToolOutput> outputs = [];
                    foreach (RequiredToolCall toolCall in submitToolOutputsAction.ToolCalls)
                    {
                        string output = await ResolveToolCallAsync(toolCall, cancellationToken);
                        outputs.Add(new ToolOutput(toolCall, output));
                    }

                    run = await _client.Runs.SubmitToolOutputsToRunAsync(run, outputs, cancellationToken);
                    continue;
                }

                if (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    run = await _client.Runs.GetRunAsync(_thread.Id, run.Id, cancellationToken);
                    continue;
                }

                if (run.Status != RunStatus.Completed)
                {
                    throw new InvalidOperationException("The Azure agent run did not complete successfully.");
                }

                return await GetLatestAgentMessageAsync(cancellationToken);
            }
        }
        finally
        {
            _turnGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            try
            {
                await _client.Threads.DeleteThreadAsync(_thread.Id);
            }
            finally
            {
                await _client.Administration.DeleteAgentAsync(_agent.Id);
            }
        }
        finally
        {
            _turnGate.Dispose();
        }
    }

    private async Task<string> ResolveToolCallAsync(
        RequiredToolCall toolCall,
        CancellationToken cancellationToken)
    {
        if (toolCall is not RequiredFunctionToolCall functionCall)
        {
            return Serialize(new { success = false, message = "Unsupported tool call." });
        }

        Console.WriteLine($"[tool] {functionCall.Name}");
        try
        {
            using JsonDocument arguments = JsonDocument.Parse(functionCall.Arguments);
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Serialize(new { success = false, message = "Tool arguments must be a JSON object." });
            }

            return functionCall.Name switch
            {
                "GetOrderStatus" => await GetOrderStatusAsync(arguments.RootElement, cancellationToken),
                "CreateReturn" => await CreateReturnAsync(arguments.RootElement, cancellationToken),
                "EscalateToHuman" => await EscalateAsync(arguments.RootElement, cancellationToken),
                _ => Serialize(new { success = false, message = "Unknown tool." })
            };
        }
        catch (JsonException)
        {
            return Serialize(new { success = false, message = "Tool arguments were not valid JSON." });
        }
        catch (ToolArgumentException exception)
        {
            return Serialize(new { success = false, message = exception.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[tool-error] {functionCall.Name} ({exception.GetType().Name})");
            return Serialize(new { success = false, message = "The requested tool could not be completed." });
        }
    }

    private async Task<string> GetOrderStatusAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string orderId = ToolArgumentParser.GetRequiredString(arguments, "orderId", 32);
        OrderStatusResult result = await _orderService.GetOrderStatusAsync(orderId, _customerId, cancellationToken);
        return Serialize(result);
    }

    private async Task<string> CreateReturnAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        ReturnRequest request = new(
            ToolArgumentParser.GetRequiredString(arguments, "orderId", 32),
            ToolArgumentParser.GetRequiredString(arguments, "itemId", 32),
            _customerId,
            ToolArgumentParser.GetRequiredString(arguments, "reason", 500));

        ReturnEligibilityResult eligibility = await _returnService.CheckEligibilityAsync(request, cancellationToken);
        if (!eligibility.Eligible || eligibility.Item is null)
        {
            return Serialize(new ReturnResult(false, eligibility.Message));
        }

        string summary = $"Create a return for {eligibility.Item.ProductName} ({request.ItemId}) "
            + $"from order {request.OrderId}. Reason: {request.Reason.Trim()}";
        bool approved = await _confirmActionAsync(summary, cancellationToken);
        return Serialize(await _returnService.CreateReturnAsync(request, approved, cancellationToken));
    }

    private async Task<string> EscalateAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string reason = ToolArgumentParser.GetRequiredString(arguments, "reason", 500);
        string? orderId = ToolArgumentParser.GetOptionalString(arguments, "orderId", 32);
        EscalationRequest request = new(_customerId, reason, orderId);
        string summary = $"Send this support request to a human agent. Reason: {reason.Trim()}";
        bool approved = await _confirmActionAsync(summary, cancellationToken);
        return Serialize(await _escalationService.EscalateAsync(request, approved, cancellationToken));
    }

    private async Task<string> GetLatestAgentMessageAsync(CancellationToken cancellationToken)
    {
        await foreach (PersistentThreadMessage message in _client.Messages
            .GetMessagesAsync(_thread.Id, order: ListSortOrder.Descending, cancellationToken: cancellationToken)
            .WithCancellation(cancellationToken))
        {
            if (message.Role != MessageRole.Agent)
            {
                continue;
            }

            string response = string.Join(
                Environment.NewLine,
                message.ContentItems
                    .OfType<MessageTextContent>()
                    .Select(content => content.Text));

            if (!string.IsNullOrWhiteSpace(response))
            {
                return response;
            }
        }

        throw new InvalidOperationException("The completed agent run returned no text response.");
    }

    private static IReadOnlyList<ToolDefinition> CreateTools() =>
    [
        new FunctionToolDefinition(
            "GetOrderStatus",
            "Get shipment status for an order belonging to the current customer.",
            CreateParameters(("orderId", "Synthetic order ID, for example ORD-1001.", true))),
        new FunctionToolDefinition(
            "CreateReturn",
            "Request a return for an eligible item. The application separately requires explicit user approval.",
            CreateParameters(
                ("orderId", "Synthetic order ID.", true),
                ("itemId", "Item ID from the specified order.", true),
                ("reason", "Customer's reason for the return.", true))),
        new FunctionToolDefinition(
            "EscalateToHuman",
            "Request human support. The application separately requires explicit user approval.",
            CreateParameters(
                ("reason", "Why the customer needs human support.", true),
                ("orderId", "Optional synthetic order ID.", false)))
    ];

    private static BinaryData CreateParameters(params (string Name, string Description, bool Required)[] fields)
    {
        Dictionary<string, object> properties = new(StringComparer.Ordinal);
        foreach (var (name, description, _) in fields)
        {
            properties[name] = new { type = "string", description };
        }

        return BinaryData.FromObjectAsJson(new
        {
            type = "object",
            properties,
            required = fields.Where(field => field.Required).Select(field => field.Name).ToArray(),
            additionalProperties = false
        });
    }

    private static string Serialize<T>(T result) => JsonSerializer.Serialize(result, JsonOptions);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
