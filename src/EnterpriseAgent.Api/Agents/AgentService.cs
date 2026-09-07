using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Rag;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Tools;

namespace EnterpriseAgent.Api.Agents;

public sealed class AgentService(
    IAIClient aiClient,
    IEnumerable<ICustomerTool> customerTools,
    RagService ragService,
    CustomerDataRepository customerDataRepository,
    ILogger<AgentService> logger)
{
    private const string PolicySearchToolName = "SearchPolicy";
    private const string InvestigateOrderToolName = "InvestigateOrderEligibility";
    private const string CreateReviewRequestToolName = "CreateVerificationReviewRequest";
    private const string OutOfScopeResponse =
        "I can only help with authorized customer verification, ordering policy, customer eligibility, and verification review requests.";

    private const string ToolSelectionInstruction = """
        You are an enterprise customer-support agent. For customer-specific questions,
        select the single most relevant approved tool. Never invent customer data.
        Verification status is the only customer status in this demo. Treat questions such
        as "what is the status of 004?" as verification-status questions and use
        GetVerificationStatus, not GetCustomer.
        Use SearchPolicy for questions about internal ordering policy, rules, verification
        meaning, or what customers are allowed to do. Do not answer general knowledge,
        programming, system administration, or other non-enterprise questions.
        Use InvestigateOrderEligibility
        when the user asks whether or why a specific customer can or cannot place an order,
        or asks how to help or resolve a stuck customer's ordering or verification problem.
        Use CreateVerificationReviewRequest only when the user explicitly asks to create
        a verification review request. Never claim an action succeeded without its tool result.
        """;

    private readonly IReadOnlyDictionary<string, ICustomerTool> tools = customerTools.ToDictionary(
        tool => tool.Name,
        StringComparer.OrdinalIgnoreCase);

    public async Task<ChatResponse> SendAsync(
        string userId,
        string userMessage,
        CancellationToken cancellationToken) =>
        await SendAsync(userId, userMessage, DemoCapabilityMode.FullAgent, cancellationToken);

    public async Task<ChatResponse> SendAsync(
        string userId,
        string userMessage,
        DemoCapabilityMode demoMode,
        CancellationToken cancellationToken)
    {
        var capabilities = DemoCapabilities.For(demoMode);
        logger.LogInformation(
            "Applying demo capabilities. DemoMode={DemoMode}, RagEnabled={RagEnabled}, ReadToolsEnabled={ReadToolsEnabled}, ActionToolsEnabled={ActionToolsEnabled}.",
            demoMode,
            capabilities.RagEnabled,
            capabilities.ReadToolsEnabled,
            capabilities.ActionToolsEnabled);

        ChatResponse response;
        try
        {
            response = await SendCoreAsync(userId, userMessage, capabilities, cancellationToken);
        }
        catch (CustomerReferenceAmbiguousException exception)
        {
            logger.LogInformation(
                "Customer reference was ambiguous. Reference={CustomerReference}, MatchCount={MatchCount}.",
                exception.CustomerReference,
                exception.MatchingCustomerIds.Count);
            response = new ChatResponse(
                $"The customer name '{exception.CustomerReference}' matches multiple customers ({string.Join(", ", exception.MatchingCustomerIds)}). Please use a customer ID.",
                [],
                []);
        }
        return AddActivity(response, demoMode, capabilities);
    }

    private async Task<ChatResponse> SendCoreAsync(
        string userId,
        string userMessage,
        DemoCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Agent orchestration started. MessageLength={MessageLength}, AvailableToolCount={AvailableToolCount}.",
            userMessage.Length,
            tools.Count);

        if (!await IsEnterpriseSupportQuestionAsync(userMessage, cancellationToken))
        {
            logger.LogWarning(
                "Chat request rejected by enterprise scope guard. MessageLength={MessageLength}.",
                userMessage.Length);
            return new ChatResponse(
                OutOfScopeResponse,
                [],
                [],
                Activity: ["Request rejected: outside enterprise support scope"]);
        }

        if (!capabilities.RagEnabled)
        {
            var llmOnlyAnswer = await aiClient.SendAsync(
                $$"""
                You are a general-purpose assistant operating without access to private or current
                enterprise data. Do not claim to know customer records, verification status, order
                eligibility, internal policy, or action results. Clearly explain unavailable access
                when the question requires it.

                User: {{userMessage}}
                """,
                cancellationToken);
            return new ChatResponse(llmOnlyAnswer, [], []);
        }

        if (!capabilities.ReadToolsEnabled)
        {
            return await SearchPolicyWithoutCustomerToolsAsync(userMessage, stopwatch, cancellationToken);
        }

        if (TryReadExplicitReviewRequest(userMessage, out var reviewCustomerId))
        {
            if (!capabilities.ActionToolsEnabled)
            {
                logger.LogInformation("Action request rejected because actions are disabled in the selected demo mode.");
                return new ChatResponse(
                    "Creating a verification review request requires Full Agent mode. This mode allows information retrieval but does not permit actions.",
                    [],
                    []);
            }

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

        var allowedTools = tools.Values.Where(tool =>
            capabilities.ActionToolsEnabled ||
            !string.Equals(tool.Name, CreateReviewRequestToolName, StringComparison.OrdinalIgnoreCase));
        var definitions = allowedTools.Select(CreateDefinition)
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

        if (string.Equals(tool.Name, CreateReviewRequestToolName, StringComparison.OrdinalIgnoreCase) &&
            !capabilities.ActionToolsEnabled)
        {
            logger.LogWarning("AI requested action tool {ToolName} while actions are disabled.", tool.Name);
            return new ChatResponse(
                "That action requires Full Agent mode. No action was performed.",
                [],
                []);
        }

        var customerId = ReadCustomerReference(selection.Arguments);
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

    private async Task<ChatResponse> SearchPolicyWithoutCustomerToolsAsync(
        string userMessage,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching policy with customer tools disabled.");
        var results = await ragService.SearchAsync(userMessage, 2, cancellationToken);
        if (results.Count == 0)
        {
            return new ChatResponse("The policy knowledge base is currently unavailable or empty.", [], []);
        }

        var context = string.Join(
            "\n\n",
            results.Select(result =>
                $"Source: {result.Chunk.Source}\nSection: {result.Chunk.Section}\n{result.Chunk.Content}"));
        var answer = await aiClient.SendAsync(
            $$"""
            Answer using only the internal policy context below. Wrap every policy-derived statement using **bold markers** in the chat response. Cite the policy filename and section.
            Customer-specific tools are disabled, so never claim to know a named customer's current
            status or eligibility. If the question asks about a named customer, explain the applicable
            policy and clearly state that the customer's actual status is unavailable in this mode.

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
        return new ChatResponse(answer, [], sources);
    }

    private static ChatResponse AddActivity(
        ChatResponse response,
        DemoCapabilityMode demoMode,
        DemoCapabilities capabilities)
    {
        var activity = new List<string>
        {
            $"Demo mode: {GetModeLabel(demoMode)}",
            $"RAG: {(capabilities.RagEnabled ? "enabled" : "disabled")}",
            $"Read tools: {(capabilities.ReadToolsEnabled ? "enabled" : "disabled")}",
            $"Actions: {(capabilities.ActionToolsEnabled ? "enabled" : "disabled")}",
            "Authorization: enforced"
        };

        activity.AddRange(response.Activity ?? []);
        activity.AddRange((response.ToolCalls ?? []).Select(call =>
            $"{call.Tool} called: {call.Status}"));
        activity.AddRange((response.Sources ?? [])
            .Select(source => $"Knowledge search: {source.File} · {source.Section}")
            .Distinct(StringComparer.OrdinalIgnoreCase));
        activity.Add("Final response generated");

        return response with { DemoMode = demoMode, Activity = activity };
    }

    private static string GetModeLabel(DemoCapabilityMode mode) => mode switch
    {
        DemoCapabilityMode.LlmOnly => "LLM Only",
        DemoCapabilityMode.Rag => "LLM + RAG",
        DemoCapabilityMode.RagAndTools => "LLM + RAG + Tools",
        DemoCapabilityMode.FullAgent => "Full Agent",
        _ => mode.ToString()
    };

    private static AIToolDefinition CreateDefinition(ICustomerTool tool) => new(
        tool.Name,
        tool.Description,
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                customerReference = new
                {
                    type = "string",
                    description = "An exact customer ID or exact customer name, such as 004 or Aisha Patel."
                }
            },
            required = new[] { "customerReference" }
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
        "Investigates whether or why a specific customer can place an order using current customer, verification, and policy data.",
        CreateCustomerIdSchema());

    private static JsonElement CreateCustomerIdSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            customerReference = new
            {
                type = "string",
                description = "An exact customer ID or exact customer name, such as 004 or Aisha Patel."
            }
        },
        required = new[] { "customerReference" }
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
            Wrap every policy-derived statement using **bold markers** in the chat response.
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
        var customerId = ReadCustomerReference(selection.Arguments);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            logger.LogError("Order investigation request did not contain a customerId.");
            throw new AIProviderException("Gemini returned a malformed order investigation request.");
        }

        var toolNames = new[] { "GetCustomer", "GetVerificationStatus" };
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
        var policyQuery = $"{verification.VerificationStatus} verification customer ordering eligibility resolution review request customer service";
        var policyResults = await ragService.SearchAsync(policyQuery, 4, cancellationToken);
        var policyContext = string.Join(
            "\n\n",
            policyResults.Select(result =>
                $"Source: {result.Chunk.Source}\nSection: {result.Chunk.Section}\n{result.Chunk.Content}"));
        var structuredData = JsonSerializer.Serialize(toolResults);
        var answer = await aiClient.SendAsync(
            $$"""
            Determine the customer's current ordering eligibility using only the approved customer
            and verification results plus the retrieved policy below. Explain the verification status
            and the applicable policy. The retrieved policy is authoritative for current ordering
            permission and limits. If the customer cannot place the requested order and the policy
            provides resolution or support options, clearly explain every relevant option, including
            customer-service contact information and that a representative can use this agent to
            create a verification review ticket. Offer to create the ticket, but do not claim it was
            created and do not create it unless the user explicitly asks for that action. Cite the
            policy filename and section. Wrap every policy-derived statement using **bold markers**
            in the chat response. Never invent missing facts.

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
        var customer = await customerDataRepository.GetCustomerAsync(
            request.CustomerId,
            cancellationToken);
        var customerDisplay = customer is null
            ? $"Customer ID: {request.CustomerId}"
            : $"{customer.Name} (Customer ID: {request.CustomerId})";
        return new ChatResponse(
            $"The verification review ticket was created successfully. Ticket number: {request.RequestId}. Customer: {customerDisplay}.",
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

        var customer = await customerDataRepository.GetCustomerAsync(
            verification.CustomerId,
            cancellationToken);
        var customerDisplay = customer is null
            ? $"Customer ID: {verification.CustomerId}"
            : $"{customer.Name} (Customer ID: {verification.CustomerId})";

        logger.LogInformation(
            "Verification lookup completed for CustomerId={CustomerId} in {ElapsedMilliseconds} ms.",
            customerId,
            stopwatch.ElapsedMilliseconds);
        return new ChatResponse(
            $"{customerDisplay} has a verification status of {verification.VerificationStatus}.",
            [trace]);
    }

    private static bool TryReadExplicitReviewRequest(string message, out string customerId)
    {
        customerId = string.Empty;
        var hasExplicitActionIntent = Regex.IsMatch(
            message,
            @"\b(create|submit|open|raise|file)\b.*\b(request|ticket)\b",
            RegexOptions.IgnoreCase);
        if (!hasExplicitActionIntent)
        {
            return false;
        }

        var customerReference = ReadCustomerReferenceFromMessage(message);
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return false;
        }

        customerId = customerReference;
        return true;
    }

    private static bool TryReadVerificationStatusRequest(string message, out string customerId)
    {
        customerId = string.Empty;
        if (!message.Contains("status", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var customerReference = ReadCustomerReferenceFromMessage(message);
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return false;
        }

        customerId = customerReference;
        return true;
    }

    private static string? ReadCustomerReferenceFromMessage(string message)
    {
        var match = Regex.Match(
            message,
            @"(?:status\s+(?:of|for)|(?:request|ticket)\s+(?:for|on\s+behalf\s+of)|customer)\s+(?<reference>[A-Za-z0-9][A-Za-z0-9 _-]*?)\s*[?.!]*$",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["reference"].Value.Trim() : null;
    }

    private async Task<bool> IsEnterpriseSupportQuestionAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var containsExplicitlyUnsupportedTopic = Regex.IsMatch(
            message,
            @"\b(powershell|python|javascript|programming|source code|script|president|prime minister|weather|recipe|sports|quantum)\b",
            RegexOptions.IgnoreCase);
        var containsEnterpriseTopic = Regex.IsMatch(
            message,
            @"\b(customer|verification|verify|verified|kyc|order|ordering|eligib(?:le|ility)|policy|review request|access|status)\b|\buser[_-]?\d+\b",
            RegexOptions.IgnoreCase);
        if (containsExplicitlyUnsupportedTopic)
        {
            return false;
        }

        if (containsEnterpriseTopic)
        {
            return true;
        }

        var containsSupportIntent = Regex.IsMatch(
            message,
            @"\b(stuck|help|resolve|resolution|option|options|problem|issue|support)\b",
            RegexOptions.IgnoreCase);
        var containsExplicitActionIntent = Regex.IsMatch(
            message,
            @"\b(create|submit|open|raise|file)\b.*\b(request|ticket)\b",
            RegexOptions.IgnoreCase);
        return (containsSupportIntent || containsExplicitActionIntent) &&
            await customerDataRepository.ContainsKnownCustomerReferenceAsync(message, cancellationToken);
    }

    private static ChatResponse CreateAccessDeniedResponse(string toolName, string customerId) =>
        new(
            $"You are not authorized to access customer {customerId}.",
            [new ToolCallTrace(toolName, new { customerId }, "AccessDenied", null)],
            []);

    private static string? ReadCustomerReference(JsonElement arguments) =>
        ReadStringArgument(arguments, "customerReference") ??
        ReadStringArgument(arguments, "customerId");

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
