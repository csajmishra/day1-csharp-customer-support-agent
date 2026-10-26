using Day1Agent.Core.Agents;
using Day1Agent.Core.Domain;
using Day1Agent.Core.Services;
using System.Text.Json;

namespace Day1Agent.Core.Tests;

public sealed class CustomerSupportServiceTests
{
    private const string CustomerId = "CUS-1001";

    [Fact]
    public async Task KnownOrderReturnsCorrectStatus()
    {
        IOrderService service = CreateOrderService();

        OrderStatusResult result = await service.GetOrderStatusAsync("ORD-1001", CustomerId);

        Assert.True(result.Found);
        Assert.Equal(ShipmentStatus.Delivered, result.Status);
        Assert.Equal("SYNTH-TRK-1001", result.TrackingNumber);
    }

    [Fact]
    public async Task SyntheticJsonDataLoadsForLocalUse()
    {
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        IOrderService service = await JsonOrderService.LoadAsync(
            Path.Combine(dataDirectory, "customers.json"),
            Path.Combine(dataDirectory, "orders.json"));

        OrderStatusResult result = await service.GetOrderStatusAsync("ORD-1001", CustomerId);

        Assert.True(result.Found);
        Assert.Equal(ShipmentStatus.Delivered, result.Status);
    }

    [Fact]
    public async Task UnknownOrderReturnsSafeNotFoundResponse()
    {
        IOrderService service = CreateOrderService();

        OrderStatusResult result = await service.GetOrderStatusAsync("ORD-NOPE", CustomerId);

        Assert.False(result.Found);
        Assert.Equal("Order not found.", result.Message);
        Assert.Null(result.TrackingNumber);
        Assert.Null(result.OrderId);
    }

    [Fact]
    public async Task ValidReturnSucceedsAfterApproval()
    {
        IReturnService service = new InMemoryReturnService(CreateOrderService());

        ReturnResult result = await service.CreateReturnAsync(CreateReturnRequest(), approved: true);

        Assert.True(result.Success);
        Assert.False(result.Duplicate);
        Assert.StartsWith("RET-", result.ReturnRequestId);
    }

    [Fact]
    public async Task ReturnWithoutApprovalIsRejected()
    {
        IReturnService service = new InMemoryReturnService(CreateOrderService());

        ReturnResult result = await service.CreateReturnAsync(CreateReturnRequest(), approved: false);

        Assert.False(result.Success);
        Assert.Contains("approval", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ReturnRequestId);
    }

    [Fact]
    public async Task IneligibleOrderCannotBeReturned()
    {
        IReturnService service = new InMemoryReturnService(CreateOrderService());
        ReturnRequest request = CreateReturnRequest(orderId: "ORD-1002", itemId: "ITEM-1002-A");

        ReturnResult result = await service.CreateReturnAsync(request, approved: true);

        Assert.False(result.Success);
        Assert.Contains("not eligible", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateReturnRequestIsIdempotent()
    {
        IReturnService service = new InMemoryReturnService(CreateOrderService());
        ReturnRequest request = CreateReturnRequest();

        ReturnResult first = await service.CreateReturnAsync(request, approved: true);
        ReturnResult second = await service.CreateReturnAsync(request, approved: true);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(second.Duplicate);
        Assert.Equal(first.ReturnRequestId, second.ReturnRequestId);
    }

    [Theory]
    [InlineData("{\"orderId\":\"   \"}")]
    [InlineData("{}")]
    [InlineData("{\"orderId\":42}")]
    [InlineData("{\"orderId\":\"ORD-1001\\n\"}")]
    public void InvalidOrEmptyToolArgumentsAreRejected(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Throws<ToolArgumentException>(
            () => ToolArgumentParser.GetRequiredString(document.RootElement, "orderId", 32));
    }

    [Fact]
    public async Task CustomerCannotRetrieveAnotherCustomersOrder()
    {
        IOrderService service = CreateOrderService();

        OrderStatusResult result = await service.GetOrderStatusAsync("ORD-2001", CustomerId);

        Assert.False(result.Found);
        Assert.Equal("Order not found.", result.Message);
        Assert.Null(result.TrackingNumber);
    }

    [Fact]
    public async Task EscalationProducesStructuredResultOnlyAfterApproval()
    {
        IEscalationService service = new InMemoryEscalationService(CreateOrderService());

        EscalationResult rejected = await service.EscalateAsync(
            new EscalationRequest(CustomerId, "I need additional help."),
            approved: false);
        EscalationResult approved = await service.EscalateAsync(
            new EscalationRequest(CustomerId, "I need additional help."),
            approved: true);

        Assert.False(rejected.Success);
        Assert.Null(rejected.CaseId);
        Assert.True(approved.Success);
        Assert.StartsWith("CASE-", approved.CaseId);
    }

    [Fact]
    public async Task EscalationCannotReferenceAnotherCustomersOrder()
    {
        IEscalationService service = new InMemoryEscalationService(CreateOrderService());

        EscalationResult result = await service.EscalateAsync(
            new EscalationRequest(CustomerId, "Please investigate this order.", "ORD-2001"),
            approved: true);

        Assert.False(result.Success);
        Assert.Equal("Order not found.", result.Message);
        Assert.Null(result.CaseId);
    }

    [Fact]
    public void AgentInstructionsContainSafetyAndApprovalConstraints()
    {
        Assert.Contains("explicit", CustomerSupportInstructions.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("another", CustomerSupportInstructions.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("override", CustomerSupportInstructions.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetOrderStatus", CustomerSupportInstructions.Text, StringComparison.Ordinal);
        Assert.Contains("CreateReturn", CustomerSupportInstructions.Text, StringComparison.Ordinal);
    }

    private static InMemoryOrderService CreateOrderService()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        Customer[] customers =
        [
            new(CustomerId, "Taylor Morgan"),
            new("CUS-2002", "Jordan Lee")
        ];
        Order[] orders =
        [
            new(
                "ORD-1001",
                CustomerId,
                today.AddDays(-10),
                today.AddDays(20),
                ShipmentStatus.Delivered,
                "SYNTH-TRK-1001",
                [new OrderItem("ITEM-1001-A", "Trail Lantern", 1)]),
            new(
                "ORD-1002",
                CustomerId,
                today.AddDays(-2),
                today.AddDays(20),
                ShipmentStatus.InTransit,
                "SYNTH-TRK-1002",
                [new OrderItem("ITEM-1002-A", "Daypack 24L", 1)]),
            new(
                "ORD-2001",
                "CUS-2002",
                today.AddDays(-3),
                today.AddDays(20),
                ShipmentStatus.Delivered,
                "SYNTH-TRK-2001",
                [new OrderItem("ITEM-2001-A", "Camp Stove", 1)])
        ];

        return new InMemoryOrderService(customers, orders);
    }

    private static ReturnRequest CreateReturnRequest(
        string orderId = "ORD-1001",
        string itemId = "ITEM-1001-A") =>
        new(orderId, itemId, CustomerId, "The item arrived damaged.");
}
