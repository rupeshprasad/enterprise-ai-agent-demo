namespace EnterpriseAgent.Api.Models;

public sealed record ChatHistoryEntry(
    string Role,
    string Text,
    DateTimeOffset Timestamp,
    IReadOnlyCollection<ToolCallTrace>? ToolCalls = null,
    IReadOnlyCollection<RagSource>? Sources = null,
    DemoCapabilityMode DemoMode = DemoCapabilityMode.FullAgent,
    IReadOnlyCollection<string>? Activity = null);
