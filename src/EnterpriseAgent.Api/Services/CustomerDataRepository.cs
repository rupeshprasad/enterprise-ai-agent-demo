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
        FindAsync(
            customerId,
            data => data.CustomerMaster,
            item => item.CustomerId,
            cancellationToken);

    public Task<VerificationRecord?> GetVerificationAsync(
        string customerId,
        CancellationToken cancellationToken) =>
        FindAsync(
            customerId,
            data => data.Verification,
            item => item.CustomerId,
            cancellationToken);

    public async Task<string?> ResolveCustomerIdAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(dataPath, "enterprise-data.json");
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var data = JsonSerializer.Deserialize<EnterpriseData>(json, JsonOptions)
            ?? throw new JsonException("Enterprise data is empty.");
        var exactId = data.CustomerMaster.FirstOrDefault(customer =>
            string.Equals(customer.CustomerId, customerReference.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exactId is not null)
        {
            return exactId.CustomerId;
        }

        var nameMatches = data.CustomerMaster.Where(customer =>
            string.Equals(customer.Name, customerReference.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nameMatches.Length > 1)
        {
            throw new CustomerReferenceAmbiguousException(
                customerReference,
                nameMatches.Select(customer => customer.CustomerId).ToArray());
        }

        return nameMatches.SingleOrDefault()?.CustomerId;
    }

    public async Task<bool> ContainsKnownCustomerReferenceAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(dataPath, "enterprise-data.json");
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var data = JsonSerializer.Deserialize<EnterpriseData>(json, JsonOptions)
            ?? throw new JsonException("Enterprise data is empty.");
        return data.CustomerMaster.Any(customer =>
            System.Text.RegularExpressions.Regex.IsMatch(
                message,
                $@"\b(?:{System.Text.RegularExpressions.Regex.Escape(customer.CustomerId)}|{System.Text.RegularExpressions.Regex.Escape(customer.Name)})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private async Task<T?> FindAsync<T>(
        string customerId,
        Func<EnterpriseData, IReadOnlyCollection<T>> collectionSelector,
        Func<T, string> idSelector,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(dataPath, "enterprise-data.json");
        logger.LogDebug(
            "Loading {RecordType} records from {DataFile} for CustomerId={CustomerId}.",
            typeof(T).Name,
            filePath,
            customerId);

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var enterpriseData = JsonSerializer.Deserialize<EnterpriseData>(json, JsonOptions)
                ?? throw new JsonException("Enterprise data is empty.");
            var records = collectionSelector(enterpriseData);
            var result = records.SingleOrDefault(item =>
                string.Equals(idSelector(item), customerId, StringComparison.OrdinalIgnoreCase));

            logger.LogInformation(
                "Customer data lookup completed. RecordType={RecordType}, CustomerId={CustomerId}, " +
                "Found={Found}, AvailableRecordCount={RecordCount}.",
                typeof(T).Name,
                customerId,
                result is not null,
                records.Count);
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

public sealed class CustomerReferenceAmbiguousException(
    string customerReference,
    IReadOnlyCollection<string> matchingCustomerIds)
    : Exception($"Customer name '{customerReference}' matches multiple customer IDs.")
{
    public string CustomerReference { get; } = customerReference;
    public IReadOnlyCollection<string> MatchingCustomerIds { get; } = matchingCustomerIds;
}
