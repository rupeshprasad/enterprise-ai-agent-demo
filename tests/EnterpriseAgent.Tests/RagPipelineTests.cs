using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Rag;

namespace EnterpriseAgent.Tests;

public sealed class RagPipelineTests
{
    [Fact]
    public void Chunk_SplitsMarkdownBySecondLevelHeading()
    {
        var document = new PolicyDocument(
            "policy.md",
            """
            # Policy

            ## Approved
            Approved customers may order.

            ## Pending Verification
            Pending customers cannot order.
            Additional review is required.
            """);

        var chunks = new TextChunker().Chunk(document).ToArray();

        Assert.Equal(2, chunks.Length);
        Assert.Equal("Approved", chunks[0].Section);
        Assert.Equal("Pending Verification", chunks[1].Section);
        Assert.Equal(
            "Pending customers cannot order. Additional review is required.",
            chunks[1].Content);
    }

    [Fact]
    public void Search_ReturnsMostSimilarVectorFirst()
    {
        var store = new VectorStore();
        store.Replace(
        [
            new IndexedPolicyChunk(
                new PolicyChunk("policy.md", "Pending", "Pending rule"),
                [1f, 0f]),
            new IndexedPolicyChunk(
                new PolicyChunk("policy.md", "Approved", "Approved rule"),
                [0f, 1f])
        ]);

        var results = store.Search([0.9f, 0.1f], top: 1);

        var result = Assert.Single(results);
        Assert.Equal("Pending", result.Chunk.Section);
        Assert.True(result.Score > 0.9);
    }
}
