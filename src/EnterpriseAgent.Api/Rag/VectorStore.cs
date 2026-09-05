using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Rag;

public sealed class VectorStore
{
    private readonly object syncRoot = new();
    private IndexedPolicyChunk[] index = [];

    public int Count
    {
        get
        {
            lock (syncRoot)
            {
                return index.Length;
            }
        }
    }

    public void Replace(IEnumerable<IndexedPolicyChunk> chunks)
    {
        var replacement = chunks.ToArray();
        lock (syncRoot)
        {
            index = replacement;
        }
    }

    public IReadOnlyCollection<RagSearchResult> Search(float[] query, int top = 2)
    {
        IndexedPolicyChunk[] snapshot;
        lock (syncRoot)
        {
            snapshot = index;
        }

        return snapshot
            .Where(item => item.Embedding.Length == query.Length)
            .Select(item => new RagSearchResult(item.Chunk, CosineSimilarity(query, item.Embedding)))
            .OrderByDescending(result => result.Score)
            .Take(top)
            .ToArray();
    }

    internal static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 0;
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        var denominator = Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
