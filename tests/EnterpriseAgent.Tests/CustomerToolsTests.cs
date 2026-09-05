using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class CustomerToolsTests
{
    private readonly CustomerDataRepository repository = new(
        new TestEnvironment(),
        NullLogger<CustomerDataRepository>.Instance);
    private readonly AuthorizationService authorization = new(
        NullLogger<AuthorizationService>.Instance);

    [Fact]
    public async Task GetCustomer_ReturnsFictionalCustomer()
    {
        var tool = new GetCustomerTool(repository, authorization);

        var result = await tool.ExecuteAsync("demo-user", "ABC123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Acme Manufacturing", result.ToString());
    }

    [Fact]
    public async Task GetVerificationStatus_ReturnsPending()
    {
        var tool = new GetVerificationStatusTool(repository, authorization);

        var result = await tool.ExecuteAsync("demo-user", "ABC123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Pending", result.ToString());
    }

    [Fact]
    public async Task GetOrderEligibility_ReturnsRestricted()
    {
        var tool = new GetOrderEligibilityTool(repository, authorization);

        var result = await tool.ExecuteAsync("demo-user", "ABC123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Restricted", result.ToString());
    }

    [Fact]
    public async Task GetVerificationStatus_RestrictedUserCannotAccessAbc123()
    {
        var tool = new GetVerificationStatusTool(repository, authorization);

        await Assert.ThrowsAsync<CustomerAccessDeniedException>(() =>
            tool.ExecuteAsync("restricted-user", "ABC123", CancellationToken.None));
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
