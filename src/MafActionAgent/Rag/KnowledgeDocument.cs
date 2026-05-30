using System;
using Microsoft.Extensions.VectorData;

namespace MafActionAgent.Rag;

/// <summary>
/// A single embedded knowledge-base chunk stored in the in-memory vector store.
/// Vectors are produced locally (ElBruno.LocalEmbeddings, ONNX MiniLM-L6, 384 dims) — no cloud calls.
/// </summary>
public sealed class KnowledgeDocument
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string DocId { get; set; } = string.Empty;

    [VectorStoreData]
    public string Title { get; set; } = string.Empty;

    [VectorStoreData]
    public string Category { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreVector(384)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
