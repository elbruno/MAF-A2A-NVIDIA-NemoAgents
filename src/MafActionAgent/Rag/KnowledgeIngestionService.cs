using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MafActionAgent.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace MafActionAgent.Rag;

/// <summary>
/// Hosted service that ingests the local markdown knowledge base into the in-memory vector store
/// at startup: reads each <c>knowledge/*.md</c> file, parses its YAML frontmatter (doc_id/title/category),
/// splits the body into heading-based chunks, embeds them locally, and upserts them.
/// </summary>
public sealed class KnowledgeIngestionService : IHostedService
{
    private readonly VectorStoreCollection<string, KnowledgeDocument> _collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<KnowledgeIngestionService> _logger;

    public KnowledgeIngestionService(
        VectorStoreCollection<string, KnowledgeDocument> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<KnowledgeIngestionService> logger)
    {
        _collection = collection;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var knowledgeDir = Path.Combine(AppContext.BaseDirectory, "knowledge");
        if (!Directory.Exists(knowledgeDir))
        {
            _logger.LogWarning("Knowledge directory '{Dir}' not found. RAG will return no sources.", knowledgeDir);
            return;
        }

        var documents = new List<KnowledgeDocument>();

        foreach (var file in Directory.EnumerateFiles(knowledgeDir, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var raw = await File.ReadAllTextAsync(file, cancellationToken);
                var (metadata, body) = ParseFrontmatter(raw);

                var docId = metadata.GetValueOrDefault("doc_id", Path.GetFileNameWithoutExtension(file));
                var title = metadata.GetValueOrDefault("title", docId);
                var category = metadata.GetValueOrDefault("category", "knowledge");

                var chunks = ChunkByHeadings(body);
                var index = 0;
                foreach (var chunk in chunks)
                {
                    documents.Add(new KnowledgeDocument
                    {
                        Id = $"{docId}#{index++}",
                        DocId = docId,
                        Title = title,
                        Category = category,
                        // Prepend the title so each chunk carries enough context for retrieval.
                        Text = $"{title}\n\n{chunk}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest knowledge file '{File}'.", file);
            }
        }

        if (documents.Count == 0)
        {
            _logger.LogWarning("No knowledge chunks were produced from '{Dir}'.", knowledgeDir);
            return;
        }

        try
        {
            await _collection.EnsureCollectionExistsAsync(cancellationToken);
            await ElBruno.LocalEmbeddings.VectorData.Extensions.VectorStoreCollectionExtensions
                .UpsertBatchWithEmbeddingAsync(
                    _collection,
                    _embeddingGenerator,
                    documents,
                    doc => doc.Text,
                    (doc, embedding) => doc.Vector = embedding.Vector,
                    cancellationToken);

            _logger.LogInformation(
                "Ingested {Count} knowledge chunk(s) from {Docs} document(s) into the vector store.",
                documents.Count, documents.Select(d => d.DocId).Distinct().Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert knowledge chunks into the vector store.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static (Dictionary<string, string> Metadata, string Body) ParseFrontmatter(string raw)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = raw.Replace("\r\n", "\n").TrimStart('\uFEFF', ' ', '\n');

        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (metadata, text);
        }

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return (metadata, text);
        }

        var frontmatter = text.Substring(4, end - 4);
        foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0)
            {
                metadata[key] = value;
            }
        }

        var bodyStart = text.IndexOf('\n', end + 1);
        var body = bodyStart >= 0 ? text[(bodyStart + 1)..] : string.Empty;
        return (metadata, body.Trim());
    }

    private static IReadOnlyList<string> ChunkByHeadings(string body)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks
            .Where(c => c.Length > 0)
            .ToList();
    }
}
