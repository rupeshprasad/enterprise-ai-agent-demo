using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class CustomerToolsTests : IDisposable
{
    private readonly TestDataEnvironment environment = new();
    private readonly CustomerDataRepository repository;
    private readonly AuthorizationService authorization;

    public CustomerToolsTests()
    {
        repository = new CustomerDataRepository(
            environment,
            NullLogger<CustomerDataRepository>.Instance);
        authorization = new AuthorizationService(
            environment,
            NullLogger<AuthorizationService>.Instance);
    }

    public void Dispose() => environment.Dispose();

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
    public async Task GetVerificationStatus_RestrictedUserCannotAccessAbc123()
    {
        var tool = new GetVerificationStatusTool(repository, authorization);

        await Assert.ThrowsAsync<CustomerAccessDeniedException>(() =>
            tool.ExecuteAsync("restricted-user", "ABC123", CancellationToken.None));
    }

    [Fact]
    public async Task GetCustomer_RestrictedUserCanAccessAssignedCustomerFromDirectory()
    {
        var tool = new GetCustomerTool(repository, authorization);

        var result = await tool.ExecuteAsync("restricted-user", "DEF456", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Global Components", result.ToString());
    }

    [Fact]
    public async Task GetVerificationStatus_AcceptsExactCustomerName()
    {
        var tool = new GetVerificationStatusTool(repository, authorization);

        var result = await tool.ExecuteAsync("restricted-user", "Global Components", CancellationToken.None);

        var verification = Assert.IsType<EnterpriseAgent.Api.Models.VerificationRecord>(result);
        Assert.Equal("DEF456", verification.CustomerId);
        Assert.Equal("Approved", verification.VerificationStatus);
    }

    [Fact]
    public async Task ResolveCustomerId_DuplicateNameIsRejectedAsAmbiguous()
    {
        await authorization.AddCustomerAsync(
            new EnterpriseAgent.Api.Models.AddCustomerRequest("DUP001", "Duplicate Name", "US", "Pending"),
            CancellationToken.None);
        await authorization.AddCustomerAsync(
            new EnterpriseAgent.Api.Models.AddCustomerRequest("DUP002", "Duplicate Name", "US", "Approved"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CustomerReferenceAmbiguousException>(() =>
            repository.ResolveCustomerIdAsync("Duplicate Name", CancellationToken.None));

        Assert.Equal(["DUP001", "DUP002"], exception.MatchingCustomerIds);
    }

}
