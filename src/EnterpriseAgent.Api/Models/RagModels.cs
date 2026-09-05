namespace EnterpriseAgent.Api.Models;

public sealed record PolicyDocument(string Source, string Content);

public sealed record PolicyChunk(string Source, string Section, string Content);

public sealed record IndexedPolicyChunk(PolicyChunk Chunk, float[] Embedding);

public sealed record RagSearchResult(PolicyChunk Chunk, double Score);

public sealed record RagSource(
    string File,
    string Section,
    string Snippet,
    double Score);
