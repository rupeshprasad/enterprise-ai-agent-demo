using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Rag;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Tools;

namespace EnterpriseAgent.Api.Agents;

public sealed class AgentService(
    IAIClient aiClient,
    IEnumerable<ICustomerTool> customerTools,
    RagService ragService,
    ILogger<AgentService> logger)
{
    private const string PolicySearchToolName = "SearchPolicy";
    private const string InvestigateOrderToolName = "InvestigateOrderEligibility";
    private const string CreateReviewRequestToolName = "CreateVerificationReviewRequest";

    private const string ToolSelectionInstruction = """
        You are an enterprise customer-support agent. For customer-specific questions,
        select the single most relevant approved tool. Never invent customer data.
        Use SearchPolicy for questions about internal ordering policy, rules, verification
        meaning, or what customers are allowed to do. If a general question needs neither
        policy nor a customer record, do not call a tool. Use InvestigateOrderEligibility
        when the user asks whether or why a specific customer can or cannot place an order.
        Use CreateVerificationReviewRequest only when the user explicitly asks to create
        a verification review request. Never claim an action succeeded without its tool result.
        """;

    private readonly IReadOnlyDictionary<string, ICustomerTool> tools = customerTools.ToDictionary(
        tool => tool.Name,
        StringComparer.OrdinalIgnoreCase);

    public async Task<ChatResponse> SendAsync(
        string userId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Agent orchestration started. MessageLength={MessageLength}, AvailableToolCount={AvailableToolCount}.",
            userMessage.Length,
            tools.Count);

        if (TryReadExplicitReviewRequest(userMessage, out var reviewCustomerId))
        {
            return await CreateReviewRequestAsync(
                userId,
                reviewCustomerId,
                stopwatch,
                cancellationToken);
        }

        if (TryReadVerificationStatusRequest(userMessage, out var verificationCustomerId))
        {
            return await GetVerificationStatusAsync(
                userId,
                verificationCustomerId,
                stopwatch,
                cancellationToken);
        }

        var definitions = tools.Values.Select(CreateDefinition)
            .Append(CreatePolicySearchDefinition())
            .Append(CreateInvestigateOrderDefinition())
            .ToArray();
        var selection = await aiClient.SelectToolAsync(
            $"{ToolSelectionInstruction}\n\nUser: {userMessage}",
            definitions,
            cancellationToken);

        if (selection is null)
        {
            logger.LogInformation("Gemini selected no tool; generating a general response.");
            var generalAnswer = await aiClient.SendAsync(
                $"{ToolSelectionInstruction}\n\nUser: {userMessage}",
                cancellationToken);
            stopwatch.Stop();
            logger.LogInformation(
                "Agent orchestration completed without a tool in {ElapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);
            return new ChatResponse(generalAnswer, []);
        }

        logger.LogInformation("Gemini selected tool {ToolName}.", selection.Name);

        if (string.Equals(selection.Name, PolicySearchToolName, StringComparison.OrdinalIgnoreCase))
        {
            return await SearchPolicyAsync(userMessage, selection, stopwatch, cancellationToken);
        }

        if (string.Equals(selection.Name, InvestigateOrderToolName, StringComparison.OrdinalIgnoreCase))
        {
            return await InvestigateOrderAsync(userId, userMessage, selection, stopwatch, cancellationToken);
        }

        if (!tools.TryGetValue(selection.Name, out var tool))
        {
            logger.LogError("Gemini requested unregistered tool {ToolName}.", selection.Name);
            throw new AIProviderException("Gemini requested an unknown tool.");
        }

        var customerId = ReadCustomerId(selection.Arguments);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            logger.LogError("Gemini tool request for {ToolName} did not contain a customerId.", tool.Name);
            throw new AIProviderException("Gemini returned a malformed tool request.");
        }

        logger.LogInformation(
            "Executing tool {ToolName} for CustomerId={CustomerId}.",
            tool.Name,
            customerId);
        object? result;
        try
        {
            result = await tool.ExecuteAsync(userId, customerId, cancellationToken);
        }
        catch (CustomerAccessDeniedException)
        {
            logger.LogWarning(
                "Tool {ToolName} denied access for UserId={UserId}, CustomerId={CustomerId}.",
                tool.Name,
                userId,
                customerId);
            return CreateAccessDeniedResponse(tool.Name, customerId);
        }
        var status = result is null ? "NotFound" : "Success";
        logger.LogInformation(
            "Tool {ToolName} completed with Status={ToolStatus} for CustomerId={CustomerId}.",
            tool.Name,
            status,
            customerId);
        var resultJson = result is null
            ? JsonSerializer.Serialize(new { customerId, status = "NotFound" })
            : JsonSerializer.Serialize(result);

        var answer = await aiClient.SendAsync(
            $$"""
            You are an enterprise customer-support AI agent.
            Answer the user's question using only the approved tool result below.
            Never invent missing data. If the status is NotFound, say the customer was not found.

            User question: {{userMessage}}
            Tool called: {{tool.Name}}
            Tool result: {{resultJson}}
            """,
            cancellationToken);

        stopwatch.Stop();
        logger.LogInformation(
            "Agent orchestration completed with tool {ToolName} in {ElapsedMilliseconds} ms.",
            tool.Name,
            stopwatch.ElapsedMilliseconds);

        return new ChatResponse(
            answer,
            [new ToolCallTrace(tool.Name, new { customerId }, status, result)]);
    }

    private static AIToolDefinition CreateDefinition(ICustomerTool tool) => new(
        tool.Name,
        tool.Description,
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                customerId = new
                {
                    type = "string",
                    description = "The enterprise customer identifier, such as ABC123."
                }
            },
            required = new[] { "customerId" }
        }));

    private static AIToolDefinition CreatePolicySearchDefinition() => new(
        PolicySearchToolName,
        "Searches the current internal customer-ordering policy and returns grounded source sections.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "A concise semantic search query for the policy knowledge base."
                }
            },
            required = new[] { "query" }
        }));

    private static AIToolDefinition CreateInvestigateOrderDefinition() => new(
        InvestigateOrderToolName,
        "Investigates whether or why a specific customer can place an order using current customer, verification, eligibility, and policy data.",
        CreateCustomerIdSchema());

    private static JsonElement CreateCustomerIdSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            customerId = new
            {
                type = "string",
                description = "The enterprise customer identifier, such as ABC123."
            }
        },
        required = new[] { "customerId" }
    });

    private async Task<ChatResponse> SearchPolicyAsync(
        string userMessage,
        AIToolSelection selection,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var query = ReadStringArgument(selection.Arguments, "query") ?? userMessage;
        logger.LogInformation(
            "Searching policy knowledge base. QueryLength={QueryLength}.",
            query.Length);
        var results = await ragService.SearchAsync(query, 2, cancellationToken);
        if (results.Count == 0)
        {
            logger.LogWarning("Policy search returned no results.");
            return new ChatResponse(
                "The policy knowledge base is currently unavailable or empty.",
                [],
                []);
        }

        var context = string.Join(
            "\n\n",
            results.Select(result =>
                $"Source: {result.Chunk.Source}\nSection: {result.Chunk.Section}\n{result.Chunk.Content}"));
        var answer = await aiClient.SendAsync(
            $$"""
            Answer the user's question using only the retrieved internal policy context.
            Cite the policy filename and section in the answer. If the context does not
            answer the question, say that the information is unavailable.

            User question: {{userMessage}}

            Retrieved policy context:
            {{context}}
            """,
            cancellationToken);

        var sources = results.Select(result => new RagSource(
            result.Chunk.Source,
            result.Chunk.Section,
            result.Chunk.Content,
            Math.Round(result.Score, 4))).ToArray();
        stopwatch.Stop();
        logger.LogInformation(
            "Agent orchestration completed with policy search in {ElapsedMilliseconds} ms. SourceCount={SourceCount}.",
            stopwatch.ElapsedMilliseconds,
            sources.Length);
        return new ChatResponse(answer, [], sources);
    }

    private async Task<ChatResponse> InvestigateOrderAsync(
        string userId,
        string userMessage,
        AIToolSelection selection,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var customerId = ReadCustomerId(selection.Arguments);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            logger.LogError("Order investigation request did not contain a customerId.");
            throw new AIProviderException("Gemini returned a malformed order investigation request.");
        }

        var toolNames = new[] { "GetCustomer", "GetVerificationStatus", "GetOrderEligibility" };
        var traces = new List<ToolCallTrace>(toolNames.Length);
        var toolResults = new Dictionary<string, object?>();
        foreach (var toolName in toolNames)
        {
            if (!tools.TryGetValue(toolName, out var tool))
            {
                logger.LogError("Required investigation tool {ToolName} is not registered.", toolName);
                throw new InvalidOperationException($"Required tool {toolName} is not registered.");
            }

            logger.LogInformation(
                "Executing investigation tool {ToolName} for CustomerId={CustomerId}.",
                tool.Name,
                customerId);
            object? result;
            try
            {
                result = await tool.ExecuteAsync(userId, customerId, cancellationToken);
            }
            catch (CustomerAccessDeniedException)
            {
                logger.LogWarning(
                    "Investigation denied access for UserId={UserId}, CustomerId={CustomerId}.",
                    userId,
                    customerId);
                traces.Add(new ToolCallTrace(tool.Name, new { customerId }, "AccessDenied", null));
                return new ChatResponse(
                    $"You are not authorized to access customer {customerId}.",
                    traces,
                    []);
            }
            var status = result is null ? "NotFound" : "Success";
            toolResults[tool.Name] = result;
            traces.Add(new ToolCallTrace(tool.Name, new { customerId }, status, result));
            logger.LogInformation(
                "Investigation tool {ToolName} completed with Status={ToolStatus} for CustomerId={CustomerId}.",
                tool.Name,
                status,
                customerId);
        }

        if (toolResults.Values.Any(result => result is null))
        {
            stopwatch.Stop();
            return new ChatResponse(
                $"Customer {customerId} or required customer data was not found.",
                traces,
                []);
        }

        var verification = (VerificationRecord)toolResults["GetVerificationStatus"]!;
        var policyQuery = $"{verification.VerificationStatus} verification customer ordering policy";
        var policyResults = await ragService.SearchAsync(policyQuery, 2, cancellationToken);
        var policyContext = string.Join(
            "\n\n",
            policyResults.Select(result =>
                $"Source: {result.Chunk.Source}\nSection: {result.Chunk.Section}\n{result.Chunk.Content}"));
        var structuredData = JsonSerializer.Serialize(toolResults);
        var answer = await aiClient.SendAsync(
            $$"""
            Answer the user's customer-order question using only the approved tool results
            and retrieved policy below. Explain the current customer status and the applicable
            policy. The retrieved policy is authoritative for current ordering permission
            and limits; if an eligibility-system status conflicts with a newer policy, clearly
            explain that discrepancy. Cite the policy filename and section. Never invent missing facts.

            User question: {{userMessage}}
            Customer tool results: {{structuredData}}

            Retrieved policy context:
            {{policyContext}}
            """,
            cancellationToken);
        var sources = policyResults.Select(result => new RagSource(
            result.Chunk.Source,
            result.Chunk.Section,
            result.Chunk.Content,
            Math.Round(result.Score, 4))).ToArray();

        stopwatch.Stop();
        logger.LogInformation(
            "Order investigation completed for CustomerId={CustomerId} in {ElapsedMilliseconds} ms. ToolCallCount={ToolCallCount}, SourceCount={SourceCount}.",
            customerId,
            stopwatch.ElapsedMilliseconds,
            traces.Count,
            sources.Length);
        return new ChatResponse(answer, traces, sources);
    }

    private async Task<ChatResponse> CreateReviewRequestAsync(
        string userId,
        string customerId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (!tools.TryGetValue(CreateReviewRequestToolName, out var tool))
        {
            logger.LogError("Required action tool {ToolName} is not registered.", CreateReviewRequestToolName);
            throw new InvalidOperationException($"Required tool {CreateReviewRequestToolName} is not registered.");
        }

        logger.LogInformation(
            "Executing action tool {ToolName} for CustomerId={CustomerId}.",
            tool.Name,
            customerId);
        object? result;
        try
        {
            result = await tool.ExecuteAsync(userId, customerId, cancellationToken);
        }
        catch (CustomerAccessDeniedException)
        {
            logger.LogWarning(
                "Action tool {ToolName} denied access for UserId={UserId}, CustomerId={CustomerId}.",
                tool.Name,
                userId,
                customerId);
            return CreateAccessDeniedResponse(tool.Name, customerId);
        }
        var status = result is null ? "NotFound" : "Success";
        var trace = new ToolCallTrace(tool.Name, new { customerId }, status, result);

        stopwatch.Stop();
        if (result is not VerificationReviewRequest request)
        {
            logger.LogWarning(
                "Action tool {ToolName} did not create a request for CustomerId={CustomerId}.",
                tool.Name,
                customerId);
            return new ChatResponse($"Customer {customerId} was not found.", [trace]);
        }

        logger.LogInformation(
            "Action tool {ToolName} completed with RequestId={RequestId} in {ElapsedMilliseconds} ms.",
            tool.Name,
            request.RequestId,
            stopwatch.ElapsedMilliseconds);
        return new ChatResponse(
            $"Verification review request {request.RequestId} has been created for customer {customerId}.",
            [trace]);
    }

    private async Task<ChatResponse> GetVerificationStatusAsync(
        string userId,
        string customerId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        const string toolName = "GetVerificationStatus";
        if (!tools.TryGetValue(toolName, out var tool))
        {
            logger.LogError("Required tool {ToolName} is not registered.", toolName);
            throw new InvalidOperationException($"Required tool {toolName} is not registered.");
        }

        object? result;
        try
        {
            result = await tool.ExecuteAsync(userId, customerId, cancellationToken);
        }
        catch (CustomerAccessDeniedException)
        {
            logger.LogWarning(
                "Tool {ToolName} denied access for UserId={UserId}, CustomerId={CustomerId}.",
                toolName,
                userId,
                customerId);
            return CreateAccessDeniedResponse(toolName, customerId);
        }

        stopwatch.Stop();
        var status = result is null ? "NotFound" : "Success";
        var trace = new ToolCallTrace(toolName, new { customerId }, status, result);
        if (result is not VerificationRecord verification)
        {
            return new ChatResponse($"Customer {customerId} was not found.", [trace]);
        }

        logger.LogInformation(
            "Verification lookup completed for CustomerId={CustomerId} in {ElapsedMilliseconds} ms.",
            customerId,
            stopwatch.ElapsedMilliseconds);
        return new ChatResponse(
            $"Customer {customerId}'s verification status is {verification.VerificationStatus} (last updated {verification.LastUpdated:yyyy-MM-dd}).",
            [trace]);
    }

    private static bool TryReadExplicitReviewRequest(string message, out string customerId)
    {
        customerId = string.Empty;
        if (!message.Contains("verification review request", StringComparison.OrdinalIgnoreCase) ||
            !message.Contains("create", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = Regex.Match(message, @"\b[A-Za-z]{3}\d{3}\b");
        if (!match.Success)
        {
            return false;
        }

        customerId = match.Value.ToUpperInvariant();
        return true;
    }

    private static bool TryReadVerificationStatusRequest(string message, out string customerId)
    {
        customerId = string.Empty;
        if (!message.Contains("verification status", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = Regex.Match(message, @"\b[A-Za-z]{3}\d{3}\b");
        if (!match.Success)
        {
            return false;
        }

        customerId = match.Value.ToUpperInvariant();
        return true;
    }

    private static ChatResponse CreateAccessDeniedResponse(string toolName, string customerId) =>
        new(
            $"You are not authorized to access customer {customerId}.",
            [new ToolCallTrace(toolName, new { customerId }, "AccessDenied", null)],
            []);

    private static string? ReadCustomerId(JsonElement arguments)
        => ReadStringArgument(arguments, "customerId");

    private static string? ReadStringArgument(JsonElement arguments, string argumentName)
    {
        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}
