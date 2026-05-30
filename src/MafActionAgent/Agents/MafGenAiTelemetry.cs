using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace MafActionAgent.Agents;

/// <summary>
/// Activity source and OTLP endpoint resolution for the MAF Action Agent's gen-AI telemetry.
/// </summary>
public static class MafGenAiTelemetry
{
    public static readonly ActivitySource Source = new("MafActionAgent.GenAI");

    public static string? ResolveOtlpEndpoint(IConfiguration configuration)
    {
        var aspireEndpoint = configuration["ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(aspireEndpoint))
        {
            return aspireEndpoint;
        }

        var standardEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(standardEndpoint))
        {
            return standardEndpoint;
        }

        return null;
    }
}
