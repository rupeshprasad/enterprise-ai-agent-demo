using System.Text.Json;
using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Security;

public sealed class AuthorizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly ILogger<AuthorizationService> logger;
    private readonly string enterpriseDataPath;
    private readonly SemaphoreSlim updateLock = new(1, 1);

    public AuthorizationService(
        IWebHostEnvironment environment,
        ILogger<AuthorizationService> logger)
    {
        this.logger = logger;
        enterpriseDataPath = Path.GetFullPath(
            Path.Combine(environment.ContentRootPath, "..", "..", "data", "enterprise-data.json"));

        try
        {
            var enterpriseData = ReadEnterpriseData();
            logger.LogInformation(
                "Customer authorization directory loaded. UserCount={UserCount}.",
                enterpriseData.CustomerAccess.Count);
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
        {
            logger.LogCritical(
                exception,
                "Customer authorization directory could not be loaded from {AccessFilePath}.",
                enterpriseDataPath);
            throw new InvalidOperationException(
                "Customer authorization configuration is unavailable or invalid.",
                exception);
        }
    }

    public bool CanAccessCustomer(string userId, string customerId)
    {
        var customerAccess = CreateAccessLookup(ReadEnterpriseData());
        var allowed = customerAccess.TryGetValue(userId, out var customers) &&
            customers.Contains(customerId);
        logger.LogInformation(
            "Customer authorization evaluated. UserId={UserId}, CustomerId={CustomerId}, Allowed={Allowed}.",
            userId,
            customerId,
            allowed);
        return allowed;
    }

    public IReadOnlyCollection<CustomerAccessOption> GetCustomerAccess(string userId)
    {
        var data = ReadEnterpriseData();
        var lookup = CreateAccessLookup(data);
        var assigned = lookup.GetValueOrDefault(userId) ?? [];
        return data.CustomerMaster
            .Select(customer => new CustomerAccessOption(
                customer.CustomerId,
                customer.Name,
                assigned.Contains(customer.CustomerId)))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<CustomerAccessOption>> SetCustomerAccessAsync(
        string userId,
        string customerId,
        bool hasAccess,
        CancellationToken cancellationToken)
    {
        await updateLock.WaitAsync(cancellationToken);
        try
        {
            var data = ReadEnterpriseData();
            if (!data.CustomerMaster.Any(customer =>
                    string.Equals(customer.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Unknown customer {customerId}.", nameof(customerId));
            }

            var assignments = data.CustomerAccess.ToList();
            var existing = assignments.FirstOrDefault(assignment =>
                string.Equals(assignment.UserId, userId, StringComparison.OrdinalIgnoreCase));
            var customerIds = new HashSet<string>(
                existing?.CustomerIds ?? [],
                StringComparer.OrdinalIgnoreCase);
            if (hasAccess)
            {
                customerIds.Add(customerId);
            }
            else
            {
                customerIds.Remove(customerId);
            }

            var replacement = new CustomerAccessAssignment(userId, customerIds.Order().ToArray());
            if (existing is null)
            {
                assignments.Add(replacement);
            }
            else
            {
                assignments[assignments.IndexOf(existing)] = replacement;
            }

            var updated = data with { CustomerAccess = assignments };
            await WriteEnterpriseDataAsync(updated, cancellationToken);
            return GetCustomerAccess(userId);
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task<Customer> AddCustomerAsync(
        AddCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = request.CustomerId.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var country = request.Country.Trim().ToUpperInvariant();
        var verificationStatus = request.VerificationStatus.Trim();
        if (string.IsNullOrWhiteSpace(customerId) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(country) ||
            string.IsNullOrWhiteSpace(verificationStatus))
        {
            throw new ArgumentException("All customer fields are required.");
        }

        await updateLock.WaitAsync(cancellationToken);
        try
        {
            var data = ReadEnterpriseData();
            if (data.CustomerMaster.Any(customer =>
                    string.Equals(customer.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Customer {customerId} already exists.", nameof(request.CustomerId));
            }

            var customer = new Customer(customerId, name, country);
            var customers = data.CustomerMaster.Append(customer).ToArray();
            var verifications = data.Verification.Append(new VerificationRecord(
                customerId,
                verificationStatus)).ToArray();
            await WriteEnterpriseDataAsync(
                data with { CustomerMaster = customers, Verification = verifications },
                cancellationToken);
            return customer;
        }
        finally
        {
            updateLock.Release();
        }
    }

    public IReadOnlyCollection<CustomerManagementRecord> GetCustomers()
    {
        var data = ReadEnterpriseData();
        return data.CustomerMaster.Select(customer =>
        {
            var verification = data.Verification.FirstOrDefault(item =>
                string.Equals(item.CustomerId, customer.CustomerId, StringComparison.OrdinalIgnoreCase));
            return new CustomerManagementRecord(
                customer.CustomerId,
                customer.Name,
                customer.Country,
                verification?.VerificationStatus ?? "Unavailable");
        }).ToArray();
    }

    public async Task<CustomerManagementRecord> UpdateCustomerAsync(
        string customerId,
        AddCustomerRequest request,
        CancellationToken cancellationToken)
    {
        await updateLock.WaitAsync(cancellationToken);
        try
        {
            var data = ReadEnterpriseData();
            var existing = data.CustomerMaster.FirstOrDefault(customer =>
                string.Equals(customer.CustomerId, customerId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                throw new ArgumentException($"Unknown customer {customerId}.", nameof(customerId));
            }

            var name = request.Name.Trim();
            var country = request.Country.Trim().ToUpperInvariant();
            var verificationStatus = request.VerificationStatus.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(country) ||
                string.IsNullOrWhiteSpace(verificationStatus))
            {
                throw new ArgumentException("All customer fields are required.");
            }

            var customers = data.CustomerMaster
                .Select(customer => customer == existing
                    ? new Customer(existing.CustomerId, name, country)
                    : customer)
                .ToArray();
            var verification = new VerificationRecord(
                existing.CustomerId,
                verificationStatus);
            var verifications = data.Verification
                .Where(item => !string.Equals(item.CustomerId, existing.CustomerId, StringComparison.OrdinalIgnoreCase))
                .Append(verification)
                .ToArray();
            await WriteEnterpriseDataAsync(
                data with { CustomerMaster = customers, Verification = verifications },
                cancellationToken);
            return new CustomerManagementRecord(
                existing.CustomerId, name, country, verificationStatus);
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task DeleteCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        await updateLock.WaitAsync(cancellationToken);
        try
        {
            var data = ReadEnterpriseData();
            if (!data.CustomerMaster.Any(customer =>
                    string.Equals(customer.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Unknown customer {customerId}.", nameof(customerId));
            }

            var customers = data.CustomerMaster
                .Where(customer => !string.Equals(customer.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var verifications = data.Verification
                .Where(item => !string.Equals(item.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var assignments = data.CustomerAccess
                .Select(assignment => assignment with
                {
                    CustomerIds = assignment.CustomerIds
                        .Where(id => !string.Equals(id, customerId, StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                })
                .ToArray();
            await WriteEnterpriseDataAsync(
                data with
                {
                    CustomerMaster = customers,
                    Verification = verifications,
                    CustomerAccess = assignments
                },
                cancellationToken);
        }
        finally
        {
            updateLock.Release();
        }
    }

    public void EnsureCanAccessCustomer(string userId, string customerId)
    {
        if (!CanAccessCustomer(userId, customerId))
        {
            throw new CustomerAccessDeniedException(userId, customerId);
        }
    }

    private EnterpriseData ReadEnterpriseData()
    {
        try
        {
            return JsonSerializer.Deserialize<EnterpriseData>(
                File.ReadAllText(enterpriseDataPath),
                JsonOptions) ?? throw new JsonException("Enterprise data is empty.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Unable to read authorization data from {DataFile}.", enterpriseDataPath);
            throw new InvalidOperationException("Customer authorization data is unavailable.", exception);
        }
    }

    private async Task WriteEnterpriseDataAsync(EnterpriseData data, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{enterpriseDataPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, enterpriseDataPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IReadOnlyDictionary<string, HashSet<string>> CreateAccessLookup(EnterpriseData data) =>
        data.CustomerAccess.ToDictionary(
            assignment => assignment.UserId,
            assignment => new HashSet<string>(assignment.CustomerIds, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
}

public sealed class CustomerAccessDeniedException(string userId, string customerId)
    : Exception($"User {userId} is not authorized to access customer {customerId}.")
{
    public string UserId { get; } = userId;

    public string CustomerId { get; } = customerId;
}
