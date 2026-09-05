namespace EnterpriseAgent.Api.AI;

public interface IAIClient
{
    Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default);

    Task<AIToolSelection?> SelectToolAsync(
        string prompt,
        IReadOnlyCollection<AIToolDefinition> tools,
        CancellationToken cancellationToken = default);

    Task<float[]> EmbedAsync(
        string text,
        AIEmbeddingPurpose purpose,
        CancellationToken cancellationToken = default);
}
