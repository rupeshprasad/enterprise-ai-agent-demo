namespace EnterpriseAgent.Api.Models;

public sealed record ChatResponse(
    string Answer,
    IReadOnlyCollection<ToolCallTrace>? ToolCalls = null,
    IReadOnlyCollection<RagSource>? Sources = null);
