using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MafActionAgent.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Shared.Models;

namespace MafActionAgent.Rag;

/// <summary>
/// Retrieval-Augmented Generation search over the local runbook/policy knowledge base.
/// Results are returned as deterministic <see cref="KnowledgeSource"/> records taken directly
/// from the vector store (NOT parsed from model prose) so action citations are reproducible.
/// </summary>
public sealed class KnowledgeSearch
{
    private readonly VectorStoreCollection<string, KnowledgeDocument> _collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<KnowledgeSearch> _logger;

    public KnowledgeSearch(
        VectorStoreCollection<string, KnowledgeDocument> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<KnowledgeSearch> logger)
    {
        _collection = collection;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Returns the top matching knowledge chunks for a query. Deduplicated by document id so the
    /// most relevant chunk per runbook/policy is surfaced.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSource>> SearchAsync(string query, int top = 3, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<KnowledgeSource>();
        }

        try
        {
            var results = await ElBruno.LocalEmbeddings.VectorData.Extensions.VectorStoreCollectionExtensions
                .SearchByTextAsync(_collection, _embeddingGenerator, query, Math.Max(top * 2, top), cancellationToken: cancellationToken);

            var sources = new List<KnowledgeSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in results.OrderByDescending(r => r.Score ?? 0))
            {
                var record = result.Record;
                if (record is null || !seen.Add(record.DocId))
                {
                    continue;
                }

                sources.Add(new KnowledgeSource
                {
                    DocId = record.DocId,
                    Title = record.Title,
                    Snippet = BuildSnippet(record.Text),
                    Score = Math.Round(result.Score ?? 0, 4)
                });

                if (sources.Count >= top)
                {
                    break;
                }
            }

            _logger.LogInformation("RAG search for '{Query}' returned {Count} source(s): {Docs}",
                query, sources.Count, string.Join(", ", sources.Select(s => s.DocId)));

            return sources;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG search failed for query '{Query}'.", query);
            return Array.Empty<KnowledgeSource>();
        }
    }

    /// <summary>
    /// Exposes the knowledge search as an <see cref="AIFunction"/> tool the agent can invoke to
    /// pull additional context while reasoning. The deterministic citation list shown to the user
    /// still comes from the pre-retrieval performed by the grounded agent.
    /// </summary>
    public AIFunction AsAIFunction()
    {
        return AIFunctionFactory.Create(
            async (string query, CancellationToken cancellationToken) =>
            {
                var sources = await SearchAsync(query, top: 3, cancellationToken);
                if (sources.Count == 0)
                {
                    return "No matching runbook or policy was found in the knowledge base.";
                }

                return string.Join("\n\n", sources.Select(s =>
                    $"[{s.DocId}] {s.Title}\n{s.Snippet}"));
            },
            name: "search_runbooks",
            description: "Search the operational knowledge base (runbooks, alert-severity policy, " +
                         "escalation matrix, report templates) for procedures relevant to a metric, " +
                         "incident, or action. Returns matching documents with their document ids.");
    }

    private static string BuildSnippet(string text)
    {
        var normalized = string.Join(" ", text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        const int maxLength = 280;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "…";
    }
}
