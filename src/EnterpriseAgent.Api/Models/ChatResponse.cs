namespace EnterpriseAgent.Api.Models;

public sealed record ChatResponse(
    string Answer,
    IReadOnlyCollection<ToolCallTrace>? ToolCalls = null,
    IReadOnlyCollection<RagSource>? Sources = null,
    DemoCapabilityMode DemoMode = DemoCapabilityMode.FullAgent,
    IReadOnlyCollection<string>? Activity = null);
