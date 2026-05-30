using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MafActionAgent.Rag;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace MafActionAgent.Agents;

public interface IActionExecutor
{
    Task<ActionResult> ExecuteActionAsync(ActionRequest request, CancellationToken cancellationToken = default);
    Task<ActionResult> TriggerAlertAsync(AlertRequest request, CancellationToken cancellationToken = default);
    Task<ActionResult> GenerateReportAsync(ReportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A real Microsoft Agent Framework agent that grounds every action in knowledge retrieved from the
/// local runbook/policy knowledge base (RAG) and, optionally, an MCP server. The model writes the
/// action narrative; the <see cref="ActionResult.Sources"/> citations are taken deterministically
/// from the retrieval step (not parsed from model prose). When no chat model is available, a
/// deterministic DEMO_MODE response is produced from the same retrieved sources so the demo never
/// fails on stage.
/// </summary>
public sealed class GroundedActionAgent : IActionExecutor
{
    private const string AgentInstructions =
        "You are the MAF Action Agent in an SRE/observability pipeline. The NeMo data-analysis agent " +
        "has already analyzed metrics; your job is to decide and describe the operational action to take, " +
        "grounded ONLY in the provided knowledge base context (runbooks, alert-severity policy, escalation " +
        "matrix, report templates). Always cite the relevant document ids (e.g. RB-014, ASP-001) in your " +
        "response. If the context does not contain a relevant procedure, say that no matching runbook was " +
        "found and do NOT invent an action. Be concise, decisive and specific about severity, the concrete " +
        "remediation step, and who to notify. You may call the search_runbooks tool for additional context.";

    private readonly IChatClient? _chatClient;
    private readonly KnowledgeSearch _knowledgeSearch;
    private readonly McpToolProvider _mcpToolProvider;
    private readonly ILogger<GroundedActionAgent> _logger;
    private readonly string _modelName;
    private readonly float? _temperature;

    public GroundedActionAgent(
        KnowledgeSearch knowledgeSearch,
        McpToolProvider mcpToolProvider,
        IConfiguration configuration,
        ILogger<GroundedActionAgent> logger,
        IChatClient? chatClient = null)
    {
        _knowledgeSearch = knowledgeSearch;
        _mcpToolProvider = mcpToolProvider;
        _logger = logger;
        _chatClient = chatClient;
        _modelName = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "maf-action-planner";

        // Temperature is optional: deterministic (0) reads best for classic chat models, but reasoning
        // models (e.g. gpt-5-mini) only accept the default value. Leave unset unless explicitly
        // configured via AZURE_OPENAI_TEMPERATURE so the agent works across both model families.
        _temperature = float.TryParse(configuration["AZURE_OPENAI_TEMPERATURE"], out var t) ? t : null;
    }

    public Task<ActionResult> ExecuteActionAsync(ActionRequest request, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(request.ActionType, request.Parameters);
        return RunGroundedAsync(
            operationName: "plan_action",
            actionType: string.IsNullOrWhiteSpace(request.ActionType) ? "execute-action" : request.ActionType,
            query: query,
            task: $"Decide and describe the action for request type '{request.ActionType}'. " +
                  $"Details: {query}",
            correlationId: request.CorrelationId,
            extraTags: a => a?.SetTag("gen_ai.request.type", request.ActionType),
            cancellationToken: cancellationToken);
    }

    public Task<ActionResult> TriggerAlertAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery($"{request.AlertLevel} alert", request.AlertData, request.Message);
        return RunGroundedAsync(
            operationName: "trigger_alert",
            actionType: "trigger-alert",
            query: query,
            task: $"An alert needs to be raised. Proposed level: '{request.AlertLevel}'. Message: " +
                  $"'{request.Message}'. Confirm the correct severity per policy and describe the alert " +
                  $"and who to notify.",
            correlationId: request.CorrelationId,
            extraTags: a => a?.SetTag("gen_ai.request.alert.level", request.AlertLevel),
            cancellationToken: cancellationToken);
    }

    public Task<ActionResult> GenerateReportAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery($"{request.ReportType} report", request.ReportData);
        return RunGroundedAsync(
            operationName: "generate_report",
            actionType: "generate-report",
            query: query,
            task: $"Generate a '{request.ReportType}' report. Use the relevant report template and " +
                  $"summarize the situation and recommended actions. Details: {query}",
            correlationId: request.CorrelationId,
            extraTags: a => a?.SetTag("gen_ai.request.report.type", request.ReportType),
            cancellationToken: cancellationToken);
    }

    private async Task<ActionResult> RunGroundedAsync(
        string operationName,
        string actionType,
        string query,
        string task,
        string? correlationId,
        Action<Activity?> extraTags,
        CancellationToken cancellationToken)
    {
        using var activity = MafGenAiTelemetry.Source.StartActivity($"maf.gen_ai.{operationName}", ActivityKind.Internal);
        activity?.SetTag("gen_ai.system", "microsoft.agent.framework");
        activity?.SetTag("gen_ai.operation.name", operationName);
        activity?.SetTag("gen_ai.request.model", _modelName);
        extraTags(activity);

        // 1) Deterministic retrieval — these become the citations regardless of model output.
        var sources = await _knowledgeSearch.SearchAsync(query, top: 3, cancellationToken);

        activity?.SetTag("gen_ai.retrieval.source", "local-runbook-kb");
        activity?.SetTag("gen_ai.retrieval.doc_count", sources.Count);
        if (sources.Count > 0)
        {
            activity?.SetTag("gen_ai.retrieval.doc_id", string.Join(",", sources.Select(s => s.DocId)));
        }

        // 2) No-result handling — never fabricate an action.
        if (sources.Count == 0)
        {
            activity?.SetTag("gen_ai.response.status", "no_match");
            _logger.LogWarning("No matching runbook found for action '{ActionType}' (query: {Query}).", actionType, query);
            return new ActionResult
            {
                Success = false,
                ActionType = actionType,
                ExecutedAt = DateTime.UtcNow,
                CorrelationId = correlationId,
                Details = "No matching runbook or policy was found in the knowledge base for this request. " +
                          "No action was taken to avoid an ungrounded decision.",
                Sources = new List<KnowledgeSource>()
            };
        }

        var context = BuildContext(sources);

        // 3) Produce the grounded narrative (LLM when available, deterministic fallback otherwise).
        string details;
        if (_chatClient is not null)
        {
            details = await RunAgentAsync(task, context, activity, cancellationToken);
        }
        else
        {
            activity?.SetTag("gen_ai.response.mode", "demo");
            details = BuildDeterministicNarrative(sources);
        }

        activity?.SetTag("gen_ai.response.status", "success");

        return new ActionResult
        {
            Success = true,
            ActionType = actionType,
            ExecutedAt = DateTime.UtcNow,
            CorrelationId = correlationId,
            Details = details,
            Sources = sources.ToList()
        };
    }

    private async Task<string> RunAgentAsync(string task, string context, Activity? activity, CancellationToken cancellationToken)
    {
        try
        {
            var tools = new List<AITool> { _knowledgeSearch.AsAIFunction() };
            tools.AddRange(await _mcpToolProvider.GetToolsAsync(cancellationToken));

            var agentOptions = new ChatClientAgentOptions
            {
                Name = "maf-action-agent",
                Description = "Grounded SRE action planner",
                ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions,
                    Temperature = _temperature,
                    Tools = tools
                }
            };

            var agent = new ChatClientAgent(_chatClient!, agentOptions);

            var prompt =
                $"{task}\n\n" +
                $"=== Retrieved knowledge base context ===\n{context}\n" +
                $"=== End context ===\n\n" +
                "Respond with the decided action, the severity, the concrete remediation step, who to " +
                "notify, and cite the relevant document ids.";

            var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
            var text = response.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                activity?.SetTag("gen_ai.response.mode", "demo_fallback");
                return BuildDeterministicNarrative(context);
            }

            activity?.SetTag("gen_ai.response.mode", "model");
            return text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Grounded agent run failed; returning deterministic fallback narrative.");
            activity?.SetTag("gen_ai.response.mode", "demo_fallback");
            return BuildDeterministicNarrative(context);
        }
    }

    private static string BuildQuery(string subject, IDictionary<string, object>? parameters, string? extra = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(subject))
        {
            parts.Add(subject);
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            parts.Add(extra);
        }

        if (parameters is not null)
        {
            foreach (var kvp in parameters)
            {
                parts.Add($"{kvp.Key}: {kvp.Value}");
            }
        }

        var query = string.Join(". ", parts);
        return string.IsNullOrWhiteSpace(query) ? subject : query;
    }

    private static string BuildContext(IReadOnlyList<KnowledgeSource> sources) =>
        string.Join("\n\n", sources.Select(s => $"[{s.DocId}] {s.Title}\n{s.Snippet}"));

    private static string BuildDeterministicNarrative(IReadOnlyList<KnowledgeSource> sources)
    {
        var top = sources[0];
        var cites = string.Join(", ", sources.Select(s => s.DocId));
        return
            $"Grounded action based on {top.DocId} ({top.Title}). {top.Snippet} " +
            $"This response is grounded in the following knowledge base sources: {cites}.";
    }

    private static string BuildDeterministicNarrative(string context) =>
        "Grounded action based on the retrieved knowledge base context:\n" + context;
}
