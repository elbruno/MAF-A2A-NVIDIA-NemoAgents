using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace MafActionAgent.Agents;

public interface IA2ABridge
{
    Task<string> ProcessA2ARequestAsync(string jsonRpcRequest, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bridges inbound A2A JSON-RPC requests to the grounded action agent. Parses the JSON-RPC envelope,
/// routes the method to the appropriate executor (execute-action / trigger-alert / generate-report,
/// or a free-text A2A message), runs the grounded agent, and returns a JSON-RPC result that includes
/// the action details and the knowledge-base citations.
/// </summary>
public sealed class A2ABridge : IA2ABridge
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IActionExecutor _executor;
    private readonly ILogger<A2ABridge> _logger;

    public A2ABridge(IActionExecutor executor, ILogger<A2ABridge> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task<string> ProcessA2ARequestAsync(string jsonRpcRequest, CancellationToken cancellationToken = default)
    {
        using var activity = MafGenAiTelemetry.Source.StartActivity("maf.gen_ai.process_a2a", ActivityKind.Server);
        activity?.SetTag("gen_ai.system", "microsoft.agent.framework");
        activity?.SetTag("gen_ai.operation.name", "a2a_request");

        _logger.LogDebug("Processing A2A request: {Request}", jsonRpcRequest);

        JsonElement root;
        JsonElement? id = null;
        string method;

        try
        {
            using var document = JsonDocument.Parse(jsonRpcRequest);
            root = document.RootElement.Clone();
            if (root.TryGetProperty("id", out var idElement))
            {
                id = idElement.Clone();
            }

            method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON-RPC request received.");
            return Error(null, -32700, "Parse error: request is not valid JSON.");
        }

        activity?.SetTag("gen_ai.a2a.method", method);

        try
        {
            var paramsElement = root.TryGetProperty("params", out var p) ? p : default;
            ActionResult result = await RouteAsync(method, paramsElement, cancellationToken);

            activity?.SetTag("gen_ai.response.status", result.Success ? "success" : "no_match");
            return Success(id, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process A2A request for method '{Method}'.", method);
            activity?.SetTag("gen_ai.response.status", "error");
            return Error(id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private Task<ActionResult> RouteAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "trigger-alert":
            case "trigger_alert":
                var alert = Deserialize<AlertRequest>(parameters) ?? new AlertRequest();
                if (string.IsNullOrWhiteSpace(alert.Message))
                {
                    alert.Message = ExtractText(parameters);
                }
                return _executor.TriggerAlertAsync(alert, cancellationToken);

            case "generate-report":
            case "generate_report":
                var report = Deserialize<ReportRequest>(parameters) ?? new ReportRequest();
                if (string.IsNullOrWhiteSpace(report.ReportType))
                {
                    report.ReportType = "incident-summary";
                }
                return _executor.GenerateReportAsync(report, cancellationToken);

            case "execute-action":
            case "execute_action":
                var action = Deserialize<ActionRequest>(parameters) ?? new ActionRequest();
                if (string.IsNullOrWhiteSpace(action.ActionType))
                {
                    action.ActionType = "execute-action";
                }
                return _executor.ExecuteActionAsync(action, cancellationToken);

            default:
                // Free-text A2A message (e.g. message/send) — ground it as a generic action.
                var text = ExtractText(parameters);
                return _executor.ExecuteActionAsync(new ActionRequest
                {
                    ActionType = "execute-action",
                    Parameters = new Dictionary<string, object> { ["request"] = text }
                }, cancellationToken);
        }
    }

    private static T? Deserialize<T>(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        try
        {
            return element.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Best-effort extraction of human text from common A2A param shapes.</summary>
    private static string ExtractText(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return parameters.ValueKind == JsonValueKind.String ? parameters.GetString() ?? string.Empty : string.Empty;
        }

        if (parameters.TryGetProperty("message", out var message))
        {
            if (message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? string.Empty;
            }

            if (message.ValueKind == JsonValueKind.Object && message.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                    {
                        return partText.GetString() ?? string.Empty;
                    }
                }
            }
        }

        foreach (var name in new[] { "text", "prompt", "query", "request" })
        {
            if (parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string Success(JsonElement? id, ActionResult result)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } idValue ? JsonValueToObject(idValue) : null,
            ["result"] = result
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static string Error(JsonElement? id, int code, string message)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } idValue ? JsonValueToObject(idValue) : null,
            ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message }
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static object? JsonValueToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => element.ToString()
    };
}
