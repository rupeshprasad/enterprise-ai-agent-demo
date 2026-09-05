namespace EnterpriseAgent.Api.Models;

public enum DemoCapabilityMode
{
    LlmOnly,
    Rag,
    RagAndTools,
    FullAgent
}

public sealed record DemoCapabilities(
    bool RagEnabled,
    bool ReadToolsEnabled,
    bool ActionToolsEnabled)
{
    public static DemoCapabilities For(DemoCapabilityMode mode) => mode switch
    {
        DemoCapabilityMode.LlmOnly => new(false, false, false),
        DemoCapabilityMode.Rag => new(true, false, false),
        DemoCapabilityMode.RagAndTools => new(true, true, false),
        DemoCapabilityMode.FullAgent => new(true, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown demo capability mode.")
    };
}
