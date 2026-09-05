using System.Text.Json;
using EnterpriseAgent.Api.Agents;
using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Rag;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class AgentIntegrationTests
{
    [Fact]
    public async Task OrderInvestigation_CombinesCustomerToolsAndPendingPolicy()
    {
        using var environment = new TestDataEnvironment();
        var aiClient = new DeterministicAIClient();
        var repository = new CustomerDataRepository(
            environment,
            NullLogger<CustomerDataRepository>.Instance);
        var authorization = new AuthorizationService(
            environment,
            NullLogger<AuthorizationService>.Instance);
        ICustomerTool[] tools =
        [
            new GetCustomerTool(repository, authorization),
            new GetVerificationStatusTool(repository, authorization)
        ];
        var ragService = new RagService(
            new DocumentLoader(environment, NullLogger<DocumentLoader>.Instance),
            new TextChunker(),
            new EmbeddingService(aiClient),
            new VectorStore(),
            NullLogger<RagService>.Instance);
        var agent = new AgentService(
            aiClient,
            tools,
            ragService,
            NullLogger<AgentService>.Instance);

        var response = await agent.SendAsync(
            "demo-user",
            "Why can't customer ABC123 place an order?",
            CancellationToken.None);

        Assert.Contains(response.ToolCalls!, call => call.Tool == "GetCustomer" && call.Status == "Success");
        Assert.Contains(response.ToolCalls!, call => call.Tool == "GetVerificationStatus" && call.Status == "Success");
        Assert.Contains(response.Sources!, source => source.Section == "Pending Verification");
    }

    private sealed class DeterministicAIClient : IAIClient
    {
        public Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult("ABC123 is restricted because verification is Pending under the ordering policy.");

        public Task<AIToolSelection?> SelectToolAsync(
            string prompt,
            IReadOnlyCollection<AIToolDefinition> tools,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AIToolSelection?>(new AIToolSelection(
                "InvestigateOrderEligibility",
                JsonSerializer.SerializeToElement(new { customerId = "ABC123" })));

        public Task<float[]> EmbedAsync(
            string text,
            AIEmbeddingPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(text.Contains("Pending", StringComparison.OrdinalIgnoreCase)
                ? new[] { 1f, 0f }
                : new[] { 0f, 1f });
    }

}
