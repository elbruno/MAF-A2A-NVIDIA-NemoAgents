using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace MafActionAgent.Rag;

/// <summary>
/// Lightweight read-only catalog of the local Markdown knowledge base. Maps each document id
/// (from the <c>doc_id</c> frontmatter) to its title, category, and on-disk file so the Web UI can
/// list every indexed document and open the source behind a grounded citation.
/// </summary>
public sealed class KnowledgeCatalog
{
    private readonly ILogger<KnowledgeCatalog> _logger;
    private readonly Lazy<IReadOnlyDictionary<string, Entry>> _entries;

    public KnowledgeCatalog(ILogger<KnowledgeCatalog> logger)
    {
        _logger = logger;
        _entries = new Lazy<IReadOnlyDictionary<string, Entry>>(LoadEntries);
    }

    private sealed record Entry(string DocId, string Title, string Category, string FilePath);

    /// <summary>Lists every indexed document, ordered by category then title.</summary>
    public IReadOnlyList<KnowledgeDocInfo> ListDocs() =>
        _entries.Value.Values
            .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .Select(e => new KnowledgeDocInfo { DocId = e.DocId, Title = e.Title, Category = e.Category })
            .ToList();

    /// <summary>Returns the raw Markdown content for a document id, or null if it is unknown.</summary>
    public KnowledgeDocContent? GetDoc(string docId)
    {
        if (string.IsNullOrWhiteSpace(docId) || !_entries.Value.TryGetValue(docId, out var entry))
        {
            return null;
        }

        try
        {
            var raw = File.ReadAllText(entry.FilePath);
            return new KnowledgeDocContent
            {
                DocId = entry.DocId,
                Title = entry.Title,
                Category = entry.Category,
                Markdown = StripFrontmatter(raw)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read knowledge document '{DocId}'.", docId);
            return null;
        }
    }

    private IReadOnlyDictionary<string, Entry> LoadEntries()
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var knowledgeDir = Path.Combine(AppContext.BaseDirectory, "knowledge");

        if (!Directory.Exists(knowledgeDir))
        {
            _logger.LogWarning("Knowledge directory '{Dir}' not found; the document catalog is empty.", knowledgeDir);
            return entries;
        }

        foreach (var file in Directory.EnumerateFiles(knowledgeDir, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var raw = File.ReadAllText(file);
                var metadata = ParseFrontmatter(raw);
                var docId = metadata.GetValueOrDefault("doc_id", Path.GetFileNameWithoutExtension(file));
                var title = metadata.GetValueOrDefault("title", docId);
                var category = metadata.GetValueOrDefault("category", "knowledge");
                entries[docId] = new Entry(docId, title, category, file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to catalog knowledge file '{File}'.", file);
            }
        }

        _logger.LogInformation("Knowledge catalog loaded {Count} document(s).", entries.Count);
        return entries;
    }

    private static Dictionary<string, string> ParseFrontmatter(string raw)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = raw.Replace("\r\n", "\n").TrimStart('\uFEFF', ' ', '\n');

        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return metadata;
        }

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return metadata;
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

        return metadata;
    }

    private static string StripFrontmatter(string raw)
    {
        var text = raw.Replace("\r\n", "\n").TrimStart('\uFEFF', ' ', '\n');
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return text.Trim();
        }

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return text.Trim();
        }

        var bodyStart = text.IndexOf('\n', end + 1);
        return bodyStart >= 0 ? text[(bodyStart + 1)..].Trim() : string.Empty;
    }
}
