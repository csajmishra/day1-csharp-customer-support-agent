using System.Text.Json;

namespace Day1Agent.Core.Agents;

public static class ToolArgumentParser
{
    public static string GetRequiredString(JsonElement arguments, string name, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        if (!arguments.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new ToolArgumentException($"The '{name}' argument is required.");
        }

        string? result = value.GetString();
        if (string.IsNullOrWhiteSpace(result)
            || result.Length > maximumLength
            || result.Any(char.IsControl))
        {
            throw new ToolArgumentException($"The '{name}' argument is empty or too long.");
        }

        return result.Trim();
    }

    public static string? GetOptionalString(JsonElement arguments, string name, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        if (!arguments.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ToolArgumentException($"The '{name}' argument must be a string.");
        }

        string? result = value.GetString();
        if (result is not null
            && (result.Length > maximumLength || result.Any(char.IsControl)))
        {
            throw new ToolArgumentException($"The '{name}' argument is too long.");
        }

        return result?.Trim();
    }
}

public sealed class ToolArgumentException(string message) : Exception(message);
