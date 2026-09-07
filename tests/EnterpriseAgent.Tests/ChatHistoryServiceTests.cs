using System.Text.Json;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class ChatHistoryServiceTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "EnterpriseAgent.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task History_IsStoredAndClearedSeparatelyForEachUser()
    {
        var contentRoot = Path.Combine(testRoot, "src", "EnterpriseAgent.Api");
        Directory.CreateDirectory(contentRoot);
        var dataDirectory = Path.Combine(testRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        var enterpriseData = new EnterpriseData(
            [],
            [new CustomerAccessAssignment("alex", []), new CustomerAccessAssignment("priya", [])],
            []);
        File.WriteAllText(
            Path.Combine(dataDirectory, "enterprise-data.json"),
            JsonSerializer.Serialize(enterpriseData, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var environment = new TestEnvironment(contentRoot);
        var authorization = new AuthorizationService(
            environment,
            NullLogger<AuthorizationService>.Instance);
        var service = new ChatHistoryService(
            environment,
            authorization,
            NullLogger<ChatHistoryService>.Instance);

        await service.AppendAsync(
            "alex",
            new ChatHistoryEntry("user", "demo message", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await service.AppendAsync(
            "priya",
            new ChatHistoryEntry("user", "restricted message", DateTimeOffset.UtcNow),
            CancellationToken.None);

        var demoHistory = await service.GetAsync("alex", CancellationToken.None);
        var restrictedHistory = await service.GetAsync("priya", CancellationToken.None);

        Assert.Single(demoHistory);
        Assert.Equal("demo message", demoHistory.Single().Text);
        Assert.Single(restrictedHistory);
        Assert.Equal("restricted message", restrictedHistory.Single().Text);

        await service.ClearAsync("alex", CancellationToken.None);

        Assert.Empty(await service.GetAsync("alex", CancellationToken.None));
        Assert.Single(await service.GetAsync("priya", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "EnterpriseAgent.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
