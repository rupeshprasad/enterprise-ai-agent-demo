using System.Text.Json;
using EnterpriseAgent.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace EnterpriseAgent.Tests;

internal sealed class TestDataEnvironment : IWebHostEnvironment, IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "enterprise-agent-test-data",
        Guid.NewGuid().ToString("N"));

    public TestDataEnvironment()
    {
        ContentRootPath = Path.Combine(testRoot, "src", "EnterpriseAgent.Api");
        var dataDirectory = Path.Combine(testRoot, "data");
        var policyDirectory = Path.Combine(dataDirectory, "policies");
        Directory.CreateDirectory(ContentRootPath);
        Directory.CreateDirectory(policyDirectory);
        var data = new EnterpriseData(
            [
                new Customer("ABC123", "Acme Manufacturing", "US"),
                new Customer("DEF456", "Global Components", "US"),
                new Customer("XYZ789", "Northstar Electronics", "CA")
            ],
            [
                new CustomerAccessAssignment("demo-user", ["ABC123", "DEF456"]),
                new CustomerAccessAssignment("restricted-user", ["DEF456"])
            ],
            [
                new VerificationRecord("ABC123", "Pending"),
                new VerificationRecord("DEF456", "Approved"),
                new VerificationRecord("XYZ789", "Rejected")
            ]);
        File.WriteAllText(
            Path.Combine(dataDirectory, "enterprise-data.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.WriteAllText(
            Path.Combine(policyDirectory, "customer-ordering-policy.md"),
            "# Customer Ordering Policy\n\n## Pending Verification\nCustomers with Pending verification cannot place orders.");
    }

    public string ApplicationName { get; set; } = "EnterpriseAgent.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }
}
