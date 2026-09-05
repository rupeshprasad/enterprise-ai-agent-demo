namespace EnterpriseAgent.Api.Models;

public sealed record ToolCallTrace(
    string Tool,
    object Input,
    string Status,
    object? Result);
