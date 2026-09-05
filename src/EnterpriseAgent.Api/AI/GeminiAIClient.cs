using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EnterpriseAgent.Api.AI;

public sealed class GeminiAIClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    IConfiguration configuration,
    ILogger<GeminiAIClient> logger) : IAIClient
{
    private const string MissingKeyMessage =
        "Gemini API key is not configured. Set Gemini:ApiKey using .NET user-secrets " +
        "or set the GEMINI_API_KEY environment variable.";

    public async Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Gemini text-generation operation started. Model={Model}, PromptLength={PromptLength}.",
            options.Value.Model,
            prompt.Length);
        var requestPayload = new GeminiRequest([new GeminiContent([new GeminiPart(prompt)])]);
        var payload = await SendRequestAsync(
            () => CreateRequest(requestPayload, options.Value.Model),
            cancellationToken);
        var text = ReadResponseText(payload);
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("Gemini text-generation operation returned no text. Model={Model}.", options.Value.Model);
            throw new AIProviderException("Gemini returned an empty response.");
        }

        logger.LogInformation(
            "Gemini text-generation operation completed. Model={Model}, ResponseLength={ResponseLength}.",
            options.Value.Model,
            text.Length);
        return text;
    }

    public async Task<AIToolSelection?> SelectToolAsync(
        string prompt,
        IReadOnlyCollection<AIToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Gemini tool-selection operation started. Model={Model}, ToolCount={ToolCount}, PromptLength={PromptLength}.",
            options.Value.Model,
            tools.Count,
            prompt.Length);
        var declarations = tools.Select(tool => new GeminiFunctionDeclaration(
            tool.Name,
            tool.Description,
            tool.Parameters)).ToArray();
        var requestPayload = new GeminiToolRequest(
            [new GeminiContent([new GeminiPart(prompt)])],
            [new GeminiTool(declarations)]);
        var payload = await SendRequestAsync(
            () => CreateRequest(requestPayload, options.Value.Model),
            cancellationToken);

        var selection = ReadToolSelection(payload);
        logger.LogInformation(
            "Gemini tool-selection operation completed. Model={Model}, ToolSelected={ToolSelected}, ToolName={ToolName}.",
            options.Value.Model,
            selection is not null,
            selection?.Name);
        return selection;
    }

    public async Task<float[]> EmbedAsync(
        string text,
        AIEmbeddingPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.EmbeddingModel))
        {
            logger.LogError("Gemini embedding request cannot start because no embedding model is configured.");
            throw new AIConfigurationException(
                "Gemini embedding model is not configured. Set Gemini:EmbeddingModel.");
        }

        logger.LogInformation(
            "Gemini embedding operation started. Model={Model}, Purpose={Purpose}, TextLength={TextLength}.",
            options.Value.EmbeddingModel,
            purpose,
            text.Length);
        var taskType = purpose == AIEmbeddingPurpose.Document
            ? "RETRIEVAL_DOCUMENT"
            : "RETRIEVAL_QUERY";
        var modelName = $"models/{options.Value.EmbeddingModel}";
        var requestPayload = new GeminiEmbeddingRequest(
            modelName,
            new GeminiContent([new GeminiPart(text)]),
            taskType);
        var payload = await SendRequestAsync(
            () => CreateRequest(requestPayload, options.Value.EmbeddingModel, "embedContent"),
            cancellationToken);

        if (!payload.TryGetProperty("embedding", out var embedding) ||
            !embedding.TryGetProperty("values", out var values))
        {
            logger.LogWarning("Gemini embedding response did not contain values.");
            throw new AIProviderException("Gemini returned an invalid embedding response.");
        }

        var vector = values.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        logger.LogInformation(
            "Gemini embedding operation completed. Model={Model}, Dimensions={Dimensions}.",
            options.Value.EmbeddingModel,
            vector.Length);
        return vector;
    }

    private HttpRequestMessage CreateRequest<TRequest>(
        TRequest payload,
        string model,
        string operation = "generateContent")
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = configuration["GEMINI_API_KEY"];
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("Gemini request cannot start because no API key is configured.");
            throw new AIConfigurationException(MissingKeyMessage);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogError("Gemini request cannot start because no model is configured.");
            throw new AIConfigurationException("Gemini model is not configured. Set Gemini:Model.");
        }

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(model)}:{operation}");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(payload);
        return request;
    }

    private async Task<JsonElement> SendRequestAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, options.Value.MaxRetryAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = requestFactory();
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                }

                var shouldRetry = IsTransient(response.StatusCode) && attempt < maxAttempts;
                logger.LogWarning(
                    "Gemini request failed with HTTP status {StatusCode}. Attempt={Attempt}, WillRetry={WillRetry}.",
                    response.StatusCode,
                    attempt,
                    shouldRetry);
                if (!shouldRetry)
                {
                    throw new AIProviderException("Gemini request failed.");
                }
            }
            catch (AIProviderException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException &&
                !cancellationToken.IsCancellationRequested)
            {
                var shouldRetry = attempt < maxAttempts;
                logger.LogWarning(
                    exception,
                    "Gemini connectivity request could not be completed. Attempt={Attempt}, WillRetry={WillRetry}.",
                    attempt,
                    shouldRetry);
                if (!shouldRetry)
                {
                    throw new AIProviderException("Gemini request could not be completed.", exception);
                }
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Gemini returned an invalid JSON response.");
                throw new AIProviderException("Gemini returned an invalid response.", exception);
            }

            var delay = TimeSpan.FromMilliseconds(
                Math.Max(0, options.Value.RetryDelayMilliseconds) * attempt);
            await Task.Delay(delay, cancellationToken);
        }

        throw new AIProviderException("Gemini request failed.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static AIToolSelection? ReadToolSelection(JsonElement payload)
    {
        if (!payload.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (!part.TryGetProperty("functionCall", out var functionCall))
            {
                continue;
            }

            return new AIToolSelection(
                functionCall.GetProperty("name").GetString()!,
                functionCall.GetProperty("args").Clone());
        }

        return null;
    }

    private static string? ReadResponseText(JsonElement payload)
    {
        if (!payload.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var content = candidates[0].GetProperty("content");
        var parts = content.GetProperty("parts");
        return parts.GetArrayLength() == 0 ? null : parts[0].GetProperty("text").GetString();
    }

    private sealed record GeminiRequest(GeminiContent[] Contents);

    private sealed record GeminiToolRequest(GeminiContent[] Contents, GeminiTool[] Tools);

    private sealed record GeminiEmbeddingRequest(
        string Model,
        GeminiContent Content,
        string TaskType);

    private sealed record GeminiContent(GeminiPart[] Parts);

    private sealed record GeminiPart(string Text);

    private sealed record GeminiTool(GeminiFunctionDeclaration[] FunctionDeclarations);

    private sealed record GeminiFunctionDeclaration(
        string Name,
        string Description,
        JsonElement Parameters);
}
