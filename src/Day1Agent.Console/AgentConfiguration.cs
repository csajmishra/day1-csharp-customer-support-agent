namespace Day1Agent.ConsoleApp;

internal sealed record AgentConfiguration(Uri ProjectEndpoint, string ModelDeploymentName)
{
    public static AgentConfiguration FromEnvironment()
    {
        string? endpointValue = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
        string? modelDeploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME");

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ConfigurationException(
                "Set AZURE_AI_PROJECT_ENDPOINT to the HTTPS URL of your Microsoft Foundry project.");
        }

        if (string.IsNullOrWhiteSpace(modelDeploymentName)
            || modelDeploymentName.Length > 128
            || modelDeploymentName.Any(char.IsControl))
        {
            throw new ConfigurationException(
                "Set AZURE_AI_MODEL_DEPLOYMENT_NAME to the name of a model deployment in your project.");
        }

        return new AgentConfiguration(endpoint, modelDeploymentName.Trim());
    }
}

internal sealed class ConfigurationException(string message) : Exception(message);
