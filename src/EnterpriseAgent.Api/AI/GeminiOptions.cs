namespace EnterpriseAgent.Api.AI;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string? ApiKey { get; init; }

    public string Model { get; init; } = "gemini-3.5-flash-lite";

    public string EmbeddingModel { get; init; } = "gemini-embedding-2";

    public int RequestTimeoutSeconds { get; init; } = 60;

    public int MaxRetryAttempts { get; init; } = 3;

    public int RetryDelayMilliseconds { get; init; } = 1000;
}
