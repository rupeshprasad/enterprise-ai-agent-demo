using System.Text.Json;

namespace EnterpriseAgent.Api.AI;

public sealed record AIToolDefinition(
    string Name,
    string Description,
    JsonElement Parameters);

public sealed record AIToolSelection(
    string Name,
    JsonElement Arguments);

public enum AIEmbeddingPurpose
{
    Document,
    Query
}
