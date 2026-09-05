using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Rag;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class KnowledgeRefreshTests
{
    [Fact]
    public async Task RefreshAsync_ReplacesIndexWithPolicyChunks()
    {
        var environment = new TestEnvironment();
        var loader = new DocumentLoader(environment, NullLogger<DocumentLoader>.Instance);
        var chunker = new TextChunker();
        var expectedCount = (await loader.LoadAsync(CancellationToken.None))
            .SelectMany(chunker.Chunk)
            .Count();
        var vectorStore = new VectorStore();
        var service = new RagService(
            loader,
            chunker,
            new EmbeddingService(new FakeAIClient()),
            vectorStore,
            NullLogger<RagService>.Instance);

        var count = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedCount, vectorStore.Count);
    }

    private sealed class FakeAIClient : IAIClient
    {
        public Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<AIToolSelection?> SelectToolAsync(
            string prompt,
            IReadOnlyCollection<AIToolDefinition> tools,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AIToolSelection?>(null);

        public Task<float[]> EmbedAsync(
            string text,
            AIEmbeddingPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { 1f, text.Length / 1000f });
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        private static readonly string RootPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        public string ApplicationName { get; set; } = "EnterpriseAgent.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.Combine(RootPath, "src", "EnterpriseAgent.Api");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
