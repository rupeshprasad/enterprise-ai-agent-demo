using System.Text.Json;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class AuthorizationServiceTests
{
    [Fact]
    public async Task AccessChange_IsPersistedAndAppliedWithoutRestart()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "enterprise-agent-access-tests", Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(testRoot, "src", "EnterpriseAgent.Api");
        var dataDirectory = Path.Combine(testRoot, "data");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(dataDirectory);
        var dataPath = Path.Combine(dataDirectory, "enterprise-data.json");
        var data = new EnterpriseData(
            [new Customer("XYZ789", "Northstar", "CA")],
            [new CustomerAccessAssignment("demo-user", [])],
            [new VerificationRecord("XYZ789", "Rejected")]);
        await File.WriteAllTextAsync(dataPath, JsonSerializer.Serialize(data));

        try
        {
            var service = new AuthorizationService(
                new TestEnvironment(contentRoot),
                NullLogger<AuthorizationService>.Instance);
            Assert.False(service.CanAccessCustomer("demo-user", "XYZ789"));

            var updated = await service.SetCustomerAccessAsync(
                "demo-user", "XYZ789", true, CancellationToken.None);

            Assert.True(service.CanAccessCustomer("demo-user", "XYZ789"));
            Assert.Contains(updated, customer => customer.CustomerId == "XYZ789" && customer.HasAccess);
        }
        finally
        {
            Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task CustomerManagement_CreateUpdateAndDelete_PersistsConsistentData()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "enterprise-agent-customer-tests", Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(testRoot, "src", "EnterpriseAgent.Api");
        var dataDirectory = Path.Combine(testRoot, "data");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(dataDirectory);
        var dataPath = Path.Combine(dataDirectory, "enterprise-data.json");
        var data = new EnterpriseData([], [new CustomerAccessAssignment("demo-user", [])], []);
        await File.WriteAllTextAsync(dataPath, JsonSerializer.Serialize(data));

        try
        {
            var service = new AuthorizationService(
                new TestEnvironment(contentRoot),
                NullLogger<AuthorizationService>.Instance);

            var customer = await service.AddCustomerAsync(
                new AddCustomerRequest("ghi012", "Example Industries", "us", "Pending"),
                CancellationToken.None);
            var saved = JsonSerializer.Deserialize<EnterpriseData>(
                await File.ReadAllTextAsync(dataPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Equal("GHI012", customer.CustomerId);
            Assert.Contains(saved!.CustomerMaster, item => item.CustomerId == "GHI012");
            Assert.Contains(saved.Verification, item => item.CustomerId == "GHI012" && item.VerificationStatus == "Pending");
            Assert.False(service.CanAccessCustomer("demo-user", "GHI012"));

            var updated = await service.UpdateCustomerAsync(
                "GHI012",
                new AddCustomerRequest("GHI012", "Example Industries Updated", "ca", "Approved"),
                CancellationToken.None);
            await service.SetCustomerAccessAsync("demo-user", "GHI012", true, CancellationToken.None);
            Assert.Equal("Example Industries Updated", updated.Name);
            Assert.Equal("Approved", updated.VerificationStatus);
            Assert.True(service.CanAccessCustomer("demo-user", "GHI012"));

            await service.DeleteCustomerAsync("GHI012", CancellationToken.None);
            var afterDelete = JsonSerializer.Deserialize<EnterpriseData>(
                await File.ReadAllTextAsync(dataPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain(afterDelete!.CustomerMaster, item => item.CustomerId == "GHI012");
            Assert.DoesNotContain(afterDelete.Verification, item => item.CustomerId == "GHI012");
            Assert.DoesNotContain(afterDelete.CustomerAccess.SelectMany(item => item.CustomerIds), id => id == "GHI012");
        }
        finally
        {
            Directory.Delete(testRoot, true);
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
