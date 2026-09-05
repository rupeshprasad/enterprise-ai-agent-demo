using System.Text.Json;
using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Services;

public sealed class CustomerDataRepository(
    IWebHostEnvironment environment,
    ILogger<CustomerDataRepository> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string dataPath = Path.GetFullPath(
        Path.Combine(environment.ContentRootPath, "..", "..", "data"));

    public Task<Customer?> GetCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        FindAsync<Customer>("customers.json", customerId, item => item.CustomerId, cancellationToken);

    public Task<VerificationRecord?> GetVerificationAsync(
        string customerId,
        CancellationToken cancellationToken) =>
        FindAsync<VerificationRecord>(
            "verification.json",
            customerId,
            item => item.CustomerId,
            cancellationToken);

    public Task<OrderEligibility?> GetOrderEligibilityAsync(
        string customerId,
        CancellationToken cancellationToken) =>
        FindAsync<OrderEligibility>(
            "order-eligibility.json",
            customerId,
            item => item.CustomerId,
            cancellationToken);

    private async Task<T?> FindAsync<T>(
        string fileName,
        string customerId,
        Func<T, string> idSelector,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(dataPath, fileName);
        logger.LogDebug(
            "Loading {RecordType} records from {DataFile} for CustomerId={CustomerId}.",
            typeof(T).Name,
            filePath,
            customerId);

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var records = JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];
            var result = records.SingleOrDefault(item =>
                string.Equals(idSelector(item), customerId, StringComparison.OrdinalIgnoreCase));

            logger.LogInformation(
                "Customer data lookup completed. RecordType={RecordType}, CustomerId={CustomerId}, " +
                "Found={Found}, AvailableRecordCount={RecordCount}.",
                typeof(T).Name,
                customerId,
                result is not null,
                records.Length);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(
                exception,
                "Failed to load {RecordType} data from {DataFile}.",
                typeof(T).Name,
                filePath);
            throw;
        }
    }
}
