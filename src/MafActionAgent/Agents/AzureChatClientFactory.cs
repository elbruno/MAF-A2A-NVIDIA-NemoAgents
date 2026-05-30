using System;
using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

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
            IChatClient chatClient;

            // Foundry models expose the OpenAI-compatible v1 surface
            // (e.g. https://<resource>.services.ai.azure.com/openai/v1). When the endpoint targets
            // that surface, use the OpenAI client with the configured API key; otherwise use the
            // classic Azure OpenAI client. Keyless (DefaultAzureCredential) remains the fallback.
            var isOpenAIv1 = endpoint.Contains("/openai/v1", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains("services.ai.azure.com", StringComparison.OrdinalIgnoreCase);

            if (isOpenAIv1 && !string.IsNullOrWhiteSpace(apiKey))
            {
                // The OpenAI client expects the v1 base (…/openai/v1). Accept the bare resource
                // host (…services.ai.azure.com/) too and normalize it so either form works.
                var v1Endpoint = endpoint.TrimEnd('/');
                if (!v1Endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
                {
                    v1Endpoint += "/openai/v1";
                }

                var openAIClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(v1Endpoint) });

                chatClient = openAIClient.GetChatClient(deployment).AsIChatClient();

                logger.LogInformation(
                    "OpenAI (Foundry v1) chat client created (endpoint={Endpoint}, model={Deployment}, auth=ApiKey).",
                    v1Endpoint, deployment);
            }
            else
            {
                var azureClient = string.IsNullOrWhiteSpace(apiKey)
                    ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                    : new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));

                chatClient = azureClient.GetChatClient(deployment).AsIChatClient();

                logger.LogInformation(
                    "Azure OpenAI chat client created (endpoint={Endpoint}, deployment={Deployment}, auth={Auth}).",
                    endpoint, deployment, string.IsNullOrWhiteSpace(apiKey) ? "DefaultAzureCredential" : "ApiKey");
            }

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
