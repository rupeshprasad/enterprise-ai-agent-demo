using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Rag;

public sealed class EmbeddingService(IAIClient aiClient)
{
    public async Task<IReadOnlyCollection<IndexedPolicyChunk>> EmbedDocumentsAsync(
        IEnumerable<PolicyChunk> chunks,
        CancellationToken cancellationToken)
    {
        var indexed = new List<IndexedPolicyChunk>();
        foreach (var chunk in chunks)
        {
            var text = $"{chunk.Section}\n{chunk.Content}";
            var vector = await aiClient.EmbedAsync(text, AIEmbeddingPurpose.Document, cancellationToken);
            indexed.Add(new IndexedPolicyChunk(chunk, vector));
        }

        return indexed;
    }

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken) =>
        aiClient.EmbedAsync(query, AIEmbeddingPurpose.Query, cancellationToken);
}
