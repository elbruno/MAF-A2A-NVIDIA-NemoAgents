using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MafActionAgent.Agents;

/// <summary>
/// Optional Model Context Protocol (MCP) tool provider. When <c>ENABLE_MCP_RETRIEVAL</c> is true,
/// connects to a streamable-HTTP MCP server (default: Microsoft Learn) and exposes its tools to the
/// grounded agent. Disabled by default and fully fault-tolerant: any failure degrades gracefully to
/// local RAG only, so the on-stage demo never depends on external availability.
/// </summary>
public sealed class McpToolProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpToolProvider> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public McpToolProvider(IConfiguration configuration, ILogger<McpToolProvider> logger, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public bool IsEnabled =>
        bool.TryParse(_configuration["ENABLE_MCP_RETRIEVAL"], out var enabled) && enabled;

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Array.Empty<AITool>();
        }

        var endpoint = _configuration["MCP_SERVER_ENDPOINT"] ?? "https://learn.microsoft.com/api/mcp";

        try
        {
            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(endpoint),
                    Name = "microsoft-learn-mcp"
                },
                _loggerFactory);

            var client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("MCP retrieval enabled: loaded {Count} tool(s) from {Endpoint}.", tools.Count, endpoint);
            return tools.Cast<AITool>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP retrieval is enabled but the server at {Endpoint} could not be reached. " +
                                   "Continuing with local RAG only.", endpoint);
            return Array.Empty<AITool>();
        }
    }
}
