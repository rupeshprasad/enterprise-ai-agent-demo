using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Rag;

public sealed class RagService(
    DocumentLoader documentLoader,
    TextChunker textChunker,
    EmbeddingService embeddingService,
    VectorStore vectorStore,
    ILogger<RagService> logger)
{
    private static readonly SemaphoreSlim InitializeLock = new(1, 1);

    public async Task<IReadOnlyCollection<RagSearchResult>> SearchAsync(
        string query,
        int top,
        CancellationToken cancellationToken)
    {
        await EnsureIndexAsync(cancellationToken);
        if (vectorStore.Count == 0)
        {
            logger.LogWarning("RAG search could not run because the knowledge base is empty.");
            return [];
        }

        var queryEmbedding = await embeddingService.EmbedQueryAsync(query, cancellationToken);
        var results = vectorStore.Search(queryEmbedding, top);
        logger.LogInformation(
            "RAG search completed. QueryLength={QueryLength}, ResultCount={ResultCount}, TopSection={TopSection}.",
            query.Length,
            results.Count,
            results.FirstOrDefault()?.Chunk.Section);
        return results;
    }

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        await InitializeLock.WaitAsync(cancellationToken);
        try
        {
            return await BuildIndexAsync(cancellationToken);
        }
        finally
        {
            InitializeLock.Release();
        }
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (vectorStore.Count > 0)
        {
            return;
        }

        await InitializeLock.WaitAsync(cancellationToken);
        try
        {
            if (vectorStore.Count > 0)
            {
                return;
            }

            await BuildIndexAsync(cancellationToken);
        }
        finally
        {
            InitializeLock.Release();
        }
    }

    private async Task<int> BuildIndexAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Knowledge-base indexing started.");
        var documents = await documentLoader.LoadAsync(cancellationToken);
        var chunks = documents.SelectMany(textChunker.Chunk).ToArray();
        var indexed = await embeddingService.EmbedDocumentsAsync(chunks, cancellationToken);
        vectorStore.Replace(indexed);
        logger.LogInformation(
            "Knowledge-base indexing completed. DocumentCount={DocumentCount}, ChunkCount={ChunkCount}.",
            documents.Count,
            indexed.Count);
        return indexed.Count;
    }
}
