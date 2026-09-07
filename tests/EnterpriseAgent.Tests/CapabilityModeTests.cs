using System.Text.Json;
using EnterpriseAgent.Api.Agents;
using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Rag;
using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class CapabilityModeTests
{
    [Theory]
    [InlineData(DemoCapabilityMode.LlmOnly, "Who is the President of the USA?")]
    [InlineData(DemoCapabilityMode.Rag, "Give me PowerShell code to check whether a file exists.")]
    [InlineData(DemoCapabilityMode.RagAndTools, "Explain quantum computing.")]
    [InlineData(DemoCapabilityMode.FullAgent, "Write a Python program.")]
    public async Task OutOfScopeQuestion_IsRejectedBeforeAnyAiOrEnterpriseCapability(
        DemoCapabilityMode mode,
        string question)
    {
        var fixture = CreateFixture();

        var response = await fixture.Agent.SendAsync("demo-user", question, mode, CancellationToken.None);

        Assert.Equal(0, fixture.Ai.SendCalls);
        Assert.Equal(0, fixture.Ai.ToolSelectionCalls);
        Assert.Equal(0, fixture.Ai.EmbeddingCalls);
        Assert.All(fixture.Tools, tool => Assert.Equal(0, tool.ExecutionCount));
        Assert.Contains("only help with authorized customer", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Request rejected: outside enterprise support scope", response.Activity!);
    }

    [Fact]
    public async Task LlmOnly_DoesNotSearchKnowledgeOrExposeTools()
    {
        var fixture = CreateFixture();

        var response = await fixture.Agent.SendAsync(
            "demo-user", "Can customer ABC123 place an order?", DemoCapabilityMode.LlmOnly, CancellationToken.None);

        Assert.Equal(0, fixture.Ai.ToolSelectionCalls);
        Assert.Equal(0, fixture.Ai.EmbeddingCalls);
        Assert.All(fixture.Tools, tool => Assert.Equal(0, tool.ExecutionCount));
        Assert.Empty(response.Sources!);
        Assert.Contains("RAG: disabled", response.Activity!);
    }

    [Fact]
    public async Task Rag_SearchesKnowledgeButDoesNotExposeReadOrActionTools()
    {
        var fixture = CreateFixture();

        var response = await fixture.Agent.SendAsync(
            "demo-user", "Can customer ABC123 place an order?", DemoCapabilityMode.Rag, CancellationToken.None);

        Assert.True(fixture.Ai.EmbeddingCalls > 0);
        Assert.Equal(0, fixture.Ai.ToolSelectionCalls);
        Assert.All(fixture.Tools, tool => Assert.Equal(0, tool.ExecutionCount));
        Assert.NotEmpty(response.Sources!);
        Assert.Contains("Read tools: disabled", response.Activity!);
    }

    [Fact]
    public async Task RagAndTools_AllowsReadToolsButRejectsAction()
    {
        var fixture = CreateFixture();

        var investigation = await fixture.Agent.SendAsync(
            "demo-user", "Why can't customer ABC123 place an order?", DemoCapabilityMode.RagAndTools, CancellationToken.None);
        var rejectedAction = await fixture.Agent.SendAsync(
            "demo-user", "Create a verification review request for ABC123", DemoCapabilityMode.RagAndTools, CancellationToken.None);

        Assert.Equal(1, fixture.Tools.Single(tool => tool.Name == "GetCustomer").ExecutionCount);
        Assert.Equal(1, fixture.Tools.Single(tool => tool.Name == "GetVerificationStatus").ExecutionCount);
        Assert.Equal(0, fixture.Tools.Single(tool => tool.Name == "CreateVerificationReviewRequest").ExecutionCount);
        Assert.Equal(2, investigation.ToolCalls!.Count);
        Assert.Contains("requires Full Agent mode", rejectedAction.Answer);
    }

    [Fact]
    public async Task FullAgent_AllowsActionTool()
    {
        var fixture = CreateFixture();

        var response = await fixture.Agent.SendAsync(
            "demo-user", "Create a verification review request for ABC123", DemoCapabilityMode.FullAgent, CancellationToken.None);

        Assert.Equal(1, fixture.Tools.Single(tool => tool.Name == "CreateVerificationReviewRequest").ExecutionCount);
        Assert.Contains("VR-TEST", response.Answer);
        Assert.Contains("Actions: enabled", response.Activity!);
    }

    [Theory]
    [InlineData("Can you create a submit request for Jordan?")]
    [InlineData("Please open a ticket for Jordan.")]
    [InlineData("Raise a review request on behalf of Jordan.")]
    public async Task FullAgent_NaturalActionPhrases_CreateReviewTicket(string message)
    {
        var fixture = CreateFixture();

        var response = await fixture.Agent.SendAsync(
            "demo-user", message, DemoCapabilityMode.FullAgent, CancellationToken.None);

        Assert.Equal(1, fixture.Tools.Single(tool => tool.Name == "CreateVerificationReviewRequest").ExecutionCount);
        Assert.Contains("created successfully", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ticket number: VR-TEST", response.Answer);
    }

    [Fact]
    public async Task FullAgent_ResolutionRequest_InstructsAiToOfferReviewWithoutCreatingIt()
    {
        var fixture = CreateFixture();

        await fixture.Agent.SendAsync(
            "demo-user", "Help resolve Jordan Lee so they can place an order.", DemoCapabilityMode.FullAgent, CancellationToken.None);

        Assert.Equal(0, fixture.Tools.Single(tool => tool.Name == "CreateVerificationReviewRequest").ExecutionCount);
        Assert.Contains("Offer to create the ticket", fixture.Ai.LastSendPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customer-service contact information", fixture.Ai.LastSendPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bold markers", fixture.Ai.LastSendPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NumericCustomerStatusQuestion_UsesVerificationToolDeterministically()
    {
        var fixture = CreateFixture();

        var response = await fixture.Agent.SendAsync(
            "demo-user", "What is the status of 004?", DemoCapabilityMode.FullAgent, CancellationToken.None);

        Assert.Equal(0, fixture.Ai.ToolSelectionCalls);
        Assert.Equal(1, fixture.Tools.Single(tool => tool.Name == "GetVerificationStatus").ExecutionCount);
        Assert.Contains("verification status", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerNameStatusQuestion_UsesVerificationToolDeterministically()
    {
        var fixture = CreateFixture();

        await fixture.Agent.SendAsync(
            "demo-user", "What is the status of Aisha Patel?", DemoCapabilityMode.FullAgent, CancellationToken.None);

        Assert.Equal(0, fixture.Ai.ToolSelectionCalls);
        Assert.Equal(1, fixture.Tools.Single(tool => tool.Name == "GetVerificationStatus").ExecutionCount);
    }

    [Fact]
    public async Task SwitchingFromFullAgentToLlmOnly_DoesNotReuseToolsOrRetrievedContext()
    {
        var fixture = CreateFixture();
        await fixture.Agent.SendAsync(
            "demo-user", "Why can't customer ABC123 place an order?", DemoCapabilityMode.FullAgent, CancellationToken.None);
        var toolCountBeforeSwitch = fixture.Tools.Sum(tool => tool.ExecutionCount);
        var embeddingsBeforeSwitch = fixture.Ai.EmbeddingCalls;

        var response = await fixture.Agent.SendAsync(
            "demo-user", "Can customer ABC123 place an order?", DemoCapabilityMode.LlmOnly, CancellationToken.None);

        Assert.Equal(toolCountBeforeSwitch, fixture.Tools.Sum(tool => tool.ExecutionCount));
        Assert.Equal(embeddingsBeforeSwitch, fixture.Ai.EmbeddingCalls);
        Assert.Empty(response.ToolCalls!);
        Assert.Empty(response.Sources!);
    }

    private static Fixture CreateFixture()
    {
        var ai = new TrackingAIClient();
        var tools = new[]
        {
            new TrackingTool("GetCustomer", new Customer("ABC123", "Acme", "US")),
            new TrackingTool("GetVerificationStatus", new VerificationRecord("ABC123", "Pending")),
            new TrackingTool("CreateVerificationReviewRequest", new VerificationReviewRequest("VR-TEST", "ABC123", "Created", DateTimeOffset.UtcNow))
        };
        var environment = new TestEnvironment();
        var rag = new RagService(
            new DocumentLoader(environment, NullLogger<DocumentLoader>.Instance),
            new TextChunker(),
            new EmbeddingService(ai),
            new VectorStore(),
            NullLogger<RagService>.Instance);
        var repository = new CustomerDataRepository(
            environment,
            NullLogger<CustomerDataRepository>.Instance);
        var agent = new AgentService(ai, tools, rag, repository, NullLogger<AgentService>.Instance);
        return new Fixture(agent, ai, tools);
    }

    private sealed record Fixture(AgentService Agent, TrackingAIClient Ai, TrackingTool[] Tools);

    private sealed class TrackingTool(string name, object result) : ICustomerTool
    {
        public string Name { get; } = name;
        public string Description => $"Test tool {Name}";
        public int ExecutionCount { get; private set; }

        public Task<object?> ExecuteAsync(string userId, string customerId, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult<object?>(result);
        }
    }

    private sealed class TrackingAIClient : IAIClient
    {
        public int SendCalls { get; private set; }
        public int ToolSelectionCalls { get; private set; }
        public int EmbeddingCalls { get; private set; }
        public string LastSendPrompt { get; private set; } = string.Empty;

        public Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            LastSendPrompt = prompt;
            return Task.FromResult("A grounded test response.");
        }

        public Task<AIToolSelection?> SelectToolAsync(
            string prompt,
            IReadOnlyCollection<AIToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            ToolSelectionCalls++;
            return Task.FromResult<AIToolSelection?>(new(
                "InvestigateOrderEligibility",
                JsonSerializer.SerializeToElement(new { customerId = "ABC123" })));
        }

        public Task<float[]> EmbedAsync(
            string text,
            AIEmbeddingPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            EmbeddingCalls++;
            return Task.FromResult(text.Contains("Pending", StringComparison.OrdinalIgnoreCase)
                ? new[] { 1f, 0f }
                : new[] { 0f, 1f });
        }
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
