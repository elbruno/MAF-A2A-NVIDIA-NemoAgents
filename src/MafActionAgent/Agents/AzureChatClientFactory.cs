using System;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MafActionAgent.Agents;

/// <summary>
/// Builds the chat <see cref="IChatClient"/> for the grounded action agent from Azure OpenAI.
/// Supports both API-key and keyless (DefaultAzureCredential) authentication, and wires the
/// Microsoft.Extensions.AI function-invocation middleware so the agent can call RAG/MCP tools.
/// Returns <c>null</c> when Azure OpenAI is not configured so callers can fall back to DEMO_MODE.
/// </summary>
public static class AzureChatClientFactory
{
    public static IChatClient? TryCreate(IConfiguration configuration, ILogger logger)
    {
        var endpoint = configuration["AZURE_OPENAI_ENDPOINT"];
        var deployment = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
        {
            logger.LogWarning(
                "Azure OpenAI not configured (AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_DEPLOYMENT_NAME missing). " +
                "Grounded agent will run in DEMO_MODE with deterministic, retrieval-backed responses.");
            return null;
        }

        try
        {
            var apiKey = configuration["AZURE_OPENAI_API_KEY"];
            var azureClient = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            logger.LogInformation(
                "Azure OpenAI chat client created (endpoint={Endpoint}, deployment={Deployment}, auth={Auth}).",
                endpoint, deployment, string.IsNullOrWhiteSpace(apiKey) ? "DefaultAzureCredential" : "ApiKey");

            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();

            return chatClient
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create Azure OpenAI chat client. Falling back to DEMO_MODE.");
            return null;
        }
    }
}
