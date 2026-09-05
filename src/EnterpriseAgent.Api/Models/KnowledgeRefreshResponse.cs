namespace EnterpriseAgent.Api.Models;

public sealed record KnowledgeRefreshResponse(string Status, int ChunksIndexed, string Message);
