using EnterpriseAgent.Api.Models;
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
        var service = new ChatHistoryService(
            new TestEnvironment(contentRoot),
            NullLogger<ChatHistoryService>.Instance);

        await service.AppendAsync(
            "demo-user",
            new ChatHistoryEntry("user", "demo message", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await service.AppendAsync(
            "restricted-user",
            new ChatHistoryEntry("user", "restricted message", DateTimeOffset.UtcNow),
            CancellationToken.None);

        var demoHistory = await service.GetAsync("demo-user", CancellationToken.None);
        var restrictedHistory = await service.GetAsync("restricted-user", CancellationToken.None);

        Assert.Single(demoHistory);
        Assert.Equal("demo message", demoHistory.Single().Text);
        Assert.Single(restrictedHistory);
        Assert.Equal("restricted message", restrictedHistory.Single().Text);

        await service.ClearAsync("demo-user", CancellationToken.None);

        Assert.Empty(await service.GetAsync("demo-user", CancellationToken.None));
        Assert.Single(await service.GetAsync("restricted-user", CancellationToken.None));
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
