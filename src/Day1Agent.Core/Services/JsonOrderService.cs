using System.Text.Json;
using System.Text.Json.Serialization;
using Day1Agent.Core.Domain;

namespace Day1Agent.Core.Services;

public static class JsonOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<InMemoryOrderService> LoadAsync(
        string customersPath,
        string ordersPath,
        CancellationToken cancellationToken = default)
    {
        Customer[] customers = await ReadFileAsync<Customer[]>(customersPath, cancellationToken);
        Order[] orders = await ReadFileAsync<Order[]>(ordersPath, cancellationToken);

        HashSet<string> customerIds = customers
            .Where(customer => customer is not null)
            .Select(customer => customer.CustomerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (customers.Length == 0
            || orders.Length == 0
            || customerIds.Count != customers.Length
            || customers.Any(customer => customer is null || string.IsNullOrWhiteSpace(customer.CustomerId))
            || orders.Any(order => order is null
                || string.IsNullOrWhiteSpace(order.OrderId)
                || order.Items is null
                || order.Items.Count == 0
                || !customerIds.Contains(order.CustomerId)
                || order.Items.Any(item => item is null
                    || string.IsNullOrWhiteSpace(item.ItemId)
                    || string.IsNullOrWhiteSpace(item.ProductName)
                    || item.Quantity <= 0)))
        {
            throw new InvalidDataException("The synthetic customer or order data is empty or invalid.");
        }

        try
        {
            return new InMemoryOrderService(customers, orders);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The synthetic data contains duplicate IDs.", exception);
        }
    }

    private static async Task<T> ReadFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"Synthetic data file '{Path.GetFileName(path)}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Synthetic data file '{Path.GetFileName(path)}' is not valid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Synthetic data file '{Path.GetFileName(path)}' could not be read.", exception);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
