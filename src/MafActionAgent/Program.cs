using ElBruno.LocalEmbeddings.VectorData.Extensions;
using ElBruno.Text2Image.Foundry;
using MafActionAgent.Agents;
using MafActionAgent.Rag;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var configuration = builder.Configuration;
configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var mafHost = configuration["MAF_HOST"] ?? "127.0.0.1";
var mafPort = configuration["MAF_PORT"] ?? "5055";
builder.WebHost.UseUrls($"http://{mafHost}:{mafPort}");

// Logging
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
    var logLevel = Enum.TryParse<LogLevel>(configuration["MAF_LOG_LEVEL"] ?? "Information", out var level)
        ? level
        : LogLevel.Information;
    loggingBuilder.SetMinimumLevel(logLevel);
});

using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger = startupLoggerFactory.CreateLogger("Startup");
logger.LogInformation("Starting MAF Action Agent (grounded RAG + optional MCP)...");

// OpenTelemetry Setup
var otelEnabled = bool.TryParse(configuration["ENABLE_OTEL_TRACING"] ?? "true", out var enabled) && enabled;
if (otelEnabled)
{
    logger.LogInformation("Configuring OpenTelemetry tracing...");

    var resource = ResourceBuilder.CreateDefault()
        .AddService(serviceName: "maf-action-agent", serviceVersion: "1.0.0");
    var otlpEndpoint = MafGenAiTelemetry.ResolveOtlpEndpoint(configuration);

    builder.Services
        .AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resource)
                .AddSource(MafGenAiTelemetry.Source.Name)
                .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
                .AddHttpClientInstrumentation(opt => opt.RecordException = true);

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            }
        });

    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
        logging.ParseStateValues = true;
        logging.SetResourceBuilder(resource);

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            logging.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });
}

// Controllers / JSON (PascalCase preserved for cross-service contract compatibility)
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

// --- RAG: local ONNX embeddings + in-memory vector store (zero cloud) ---
builder.Services
    .AddLocalEmbeddingsWithInMemoryVectorStore(options =>
    {
        options.ModelName = configuration["EMBEDDINGS_MODEL"] ?? "sentence-transformers/all-MiniLM-L6-v2";
        options.MaxSequenceLength = 256;
        options.EnsureModelDownloaded = true;
    })
    .AddVectorStoreCollection<string, KnowledgeDocument>("runbooks");

builder.Services.AddSingleton<KnowledgeSearch>();
builder.Services.AddSingleton<KnowledgeCatalog>();
builder.Services.AddSingleton<McpToolProvider>();
builder.Services.AddHostedService<KnowledgeIngestionService>();

// --- Chat model (Azure OpenAI; null => DEMO_MODE deterministic responses) ---
var chatClient = AzureChatClientFactory.TryCreate(configuration, logger);
if (chatClient is not null)
{
    builder.Services.AddSingleton(chatClient);
}

// --- Grounded MAF agent + A2A bridge ---
builder.Services.AddSingleton<IActionExecutor>(sp => new GroundedActionAgent(
    sp.GetRequiredService<KnowledgeSearch>(),
    sp.GetRequiredService<McpToolProvider>(),
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<GroundedActionAgent>>(),
    sp.GetService<IChatClient>()));

builder.Services.AddSingleton<IA2ABridge, A2ABridge>();

// --- Optional pitch image agent (MEAI IImageGenerator via Microsoft Foundry / GPT-Image-2); default off ---
// Always register the agent as a resolvable singleton so the on-demand image endpoint can use it
// regardless of whether the background hosted service runs. When disabled/unconfigured it reports
// IsConfigured=false and returns no image.
builder.Services.AddSingleton<PitchImageAgent>();

if (PitchImageAgent.IsEnabled(configuration))
{
    var imageEndpoint = configuration["FOUNDRY_IMAGE_ENDPOINT"];
    var imageApiKey = configuration["FOUNDRY_IMAGE_API_KEY"];
    if (!string.IsNullOrWhiteSpace(imageEndpoint) && !string.IsNullOrWhiteSpace(imageApiKey))
    {
        // GPT-Image-2 (Azure OpenAI) is a high-quality but slow model (~3-4 min), so it is only ever
        // used to pre-render/startup-cache the cold-open hero image -- never on a live request path.
        // A generous timeout (default 300s, matching the library CLI) is applied. Registered as the
        // MEAI IImageGenerator building block so PitchImageAgent stays model-agnostic.
        var imageDeployment = configuration["FOUNDRY_IMAGE_DEPLOYMENT"] ?? "gpt-image-2";
        var imageModelName = configuration["FOUNDRY_IMAGE_MODEL_NAME"] ?? "GPT-Image-2";
        var imageTimeout = int.TryParse(configuration["FOUNDRY_IMAGE_TIMEOUT_SECONDS"], out var t) ? t : 300;

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<ElBruno.Text2Image.IImageGenerator>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new GptImage2Generator(
                endpoint: imageEndpoint,
                apiKey: imageApiKey,
                httpClient: httpClient,
                modelName: imageModelName,
                deploymentName: imageDeployment,
                timeoutSeconds: imageTimeout);
        });
        logger.LogInformation(
            "Pitch image agent enabled with GPT-Image-2 (deployment={Deployment}, timeout={Timeout}s).",
            imageDeployment, imageTimeout);
    }
    else
    {
        logger.LogWarning(
            "ENABLE_IMAGE_AGENT=true but FOUNDRY_IMAGE_ENDPOINT / FOUNDRY_IMAGE_API_KEY are not set; " +
            "the pitch image agent will run as a no-op.");
    }

    // Background pre-render uses the same singleton instance as the on-demand endpoint.
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PitchImageAgent>());
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();

// Health endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        };
        await context.Response.WriteAsJsonAsync(response);
    }
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});

// A2A Agent Card
app.MapGet("/.well-known/agent-card.json", () =>
{
    var card = new Dictionary<string, object>
    {
        { "name", "MAF Action Agent" },
        { "description", "Grounded action agent: plans and executes SRE actions using RAG over a local " +
                         "runbook/policy knowledge base (and optional MCP), citing the sources it relied on." },
        { "version", "1.0.0" },
        { "capabilities", new[] { "execute-actions", "trigger-alerts", "generate-reports", "retrieve-knowledge", "grounded-actions" } },
        { "endpoint", "/a2a/maf-action-agent" },
        { "a2a_version", "1.0" }
    };
    return Results.Ok(card);
})
.WithName("GetAgentCard")
.WithOpenApi();

// A2A JSON-RPC endpoint
app.MapPost("/a2a/maf-action-agent", async (HttpContext context, IA2ABridge bridge) =>
{
    using var reader = new System.IO.StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var response = await bridge.ProcessA2ARequestAsync(body, context.RequestAborted);
    return Results.Content(response, "application/json");
})
.WithName("HandleA2ARequest")
.WithOpenApi();

app.MapControllers();

// Action endpoints (grounded)
app.MapPost("/api/actions/execute", async (ActionRequest request, IActionExecutor executor, HttpContext ctx) =>
{
    var result = await executor.ExecuteActionAsync(request, ctx.RequestAborted);
    return Results.Ok(result);
})
.WithName("ExecuteAction")
.WithOpenApi();

app.MapPost("/api/actions/trigger-alert", async (AlertRequest request, IActionExecutor executor, HttpContext ctx) =>
{
    var result = await executor.TriggerAlertAsync(request, ctx.RequestAborted);
    return Results.Ok(result);
})
.WithName("TriggerAlert")
.WithOpenApi();

app.MapPost("/api/actions/generate-report", async (ReportRequest request, IActionExecutor executor, HttpContext ctx) =>
{
    var result = await executor.GenerateReportAsync(request, ctx.RequestAborted);
    return Results.Ok(result);
})
.WithName("GenerateReport")
.WithOpenApi();

// Optional pitch image (incident-hero) cold-open asset; 404 when the image agent is disabled/unavailable
app.MapGet("/api/pitch/hero-image", () =>
{
    var path = PitchImageAgent.CachedImagePath;
    return System.IO.File.Exists(path)
        ? Results.File(path, "image/png")
        : Results.NotFound(new { message = "No pitch image available. Enable ENABLE_IMAGE_AGENT to generate one." });
})
.WithName("GetPitchHeroImage")
.WithOpenApi();

// On-demand pitch image generation: returns the cached incident-hero image, generating it on demand
// when needed. 503 when the image agent is disabled/unconfigured. Used by the Web UI sample prompt.
app.MapPost("/api/pitch/generate-image", async (PitchImageAgent agent, HttpContext ctx) =>
{
    if (!agent.IsConfigured)
    {
        return Results.Json(
            new { message = "Image agent is disabled. Set ENABLE_IMAGE_AGENT=true with FOUNDRY_IMAGE_ENDPOINT / FOUNDRY_IMAGE_API_KEY to enable it." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var bytes = await agent.EnsureImageAsync(ctx.RequestAborted);
    return bytes is not null
        ? Results.File(bytes, "image/png")
        : Results.Json(
            new { message = "Image generation failed. Check the MAF agent logs for details." },
            statusCode: StatusCodes.Status502BadGateway);
})
.WithName("GeneratePitchHeroImage")
.WithOpenApi();

// --- Indexed knowledge documents (list + raw Markdown) for the Web UI document viewer ---
app.MapGet("/api/knowledge/docs", (KnowledgeCatalog catalog) =>
    Results.Ok(catalog.ListDocs()))
.WithName("ListKnowledgeDocs")
.WithOpenApi();

app.MapGet("/api/knowledge/doc/{docId}", (string docId, KnowledgeCatalog catalog) =>
{
    var doc = catalog.GetDoc(docId);
    return doc is not null
        ? Results.Ok(doc)
        : Results.NotFound(new { message = $"Unknown knowledge document '{docId}'." });
})
.WithName("GetKnowledgeDoc")
.WithOpenApi();

await app.RunAsync();
