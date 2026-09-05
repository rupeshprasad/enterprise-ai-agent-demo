using System.Net;
using System.Text;
using System.Text.Json;
using EnterpriseAgent.Api.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseAgent.Tests;

public sealed class GeminiAIClientTests
{
    [Fact]
    public async Task SendAsync_WithoutApiKey_ThrowsSafeConfigurationError()
    {
        var client = CreateClient(apiKey: null, new RecordingHandler());

        var exception = await Assert.ThrowsAsync<AIConfigurationException>(
            () => client.SendAsync("test"));

        Assert.Contains("Gemini API key is not configured", exception.Message);
        Assert.DoesNotContain("ApiKey=", exception.Message);
    }

    [Fact]
    public async Task SendAsync_SendsKeyInHeaderAndReturnsText()
    {
        const string secret = "test-secret-never-log";
        var handler = new RecordingHandler();
        var client = CreateClient(secret, handler);

        var result = await client.SendAsync("connectivity test");

        Assert.Equal("Gemini connection successful.", result);
        Assert.Equal(secret, handler.ApiKeyHeader);
        Assert.DoesNotContain(secret, handler.RequestUri!.ToString());
        Assert.DoesNotContain(secret, handler.RequestBody!);
    }

    [Fact]
    public async Task SelectToolAsync_ReturnsGeminiFunctionCall()
    {
        var handler = new RecordingHandler
        {
            ResponseJson = """
                {"candidates":[{"content":{"parts":[{"functionCall":{"name":"GetVerificationStatus","args":{"customerId":"ABC123"}}}]}}]}
                """
        };
        var client = CreateClient("test-secret", handler);
        var definition = new AIToolDefinition(
            "GetVerificationStatus",
            "Gets verification status.",
            JsonSerializer.SerializeToElement(new { type = "object" }));

        var selection = await client.SelectToolAsync("Check ABC123", [definition]);

        Assert.NotNull(selection);
        Assert.Equal("GetVerificationStatus", selection.Name);
        Assert.Equal("ABC123", selection.Arguments.GetProperty("customerId").GetString());
        Assert.Contains("functionDeclarations", handler.RequestBody!);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmbeddingVector()
    {
        var handler = new RecordingHandler
        {
            ResponseJson = "{\"embedding\":{\"values\":[0.1,0.2,0.3]}}"
        };
        var client = CreateClient("test-secret", handler);

        var vector = await client.EmbedAsync("policy text", AIEmbeddingPurpose.Document);

        Assert.Equal(3, vector.Length);
        Assert.Equal(0.2f, vector[1]);
        Assert.Contains("gemini-embedding-2:embedContent", handler.RequestUri!.ToString());
        Assert.Contains("RETRIEVAL_DOCUMENT", handler.RequestBody!);
    }

    [Fact]
    public async Task SendAsync_WhenProviderIsTemporarilyUnavailable_RetriesRequest()
    {
        var handler = new RecordingHandler { TransientFailuresBeforeSuccess = 1 };
        var client = CreateClient("test-secret", handler, retryDelayMilliseconds: 0);

        var result = await client.SendAsync("connectivity test");

        Assert.Equal("Gemini connection successful.", result);
        Assert.Equal(2, handler.CallCount);
    }

    private static GeminiAIClient CreateClient(
        string? apiKey,
        RecordingHandler handler,
        int retryDelayMilliseconds = 1000)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var options = Options.Create(new GeminiOptions
        {
            ApiKey = apiKey,
            RetryDelayMilliseconds = retryDelayMilliseconds
        });
        var configuration = new ConfigurationBuilder().Build();

        return new GeminiAIClient(
            httpClient,
            options,
            configuration,
            NullLogger<GeminiAIClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string ResponseJson { get; init; } =
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini connection successful.\"}]}}]}";

        public string? ApiKeyHeader { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        public int TransientFailuresBeforeSuccess { get; init; }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ApiKeyHeader = request.Headers.GetValues("x-goog-api-key").Single();
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (CallCount <= TransientFailuresBeforeSuccess)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    ResponseJson,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
