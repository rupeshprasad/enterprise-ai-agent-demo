namespace EnterpriseAgent.Api.Security;

public sealed class AuthorizationService(ILogger<AuthorizationService> logger)
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> CustomerAccess =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["demo-user"] = new(StringComparer.OrdinalIgnoreCase) { "ABC123", "DEF456" },
            ["restricted-user"] = new(StringComparer.OrdinalIgnoreCase) { "DEF456" }
        };

    public bool CanAccessCustomer(string userId, string customerId)
    {
        var allowed = CustomerAccess.TryGetValue(userId, out var customers) &&
            customers.Contains(customerId);
        logger.LogInformation(
            "Customer authorization evaluated. UserId={UserId}, CustomerId={CustomerId}, Allowed={Allowed}.",
            userId,
            customerId,
            allowed);
        return allowed;
    }

    public void EnsureCanAccessCustomer(string userId, string customerId)
    {
        if (!CanAccessCustomer(userId, customerId))
        {
            throw new CustomerAccessDeniedException(userId, customerId);
        }
    }
}

public sealed class CustomerAccessDeniedException(string userId, string customerId)
    : Exception($"User {userId} is not authorized to access customer {customerId}.")
{
    public string UserId { get; } = userId;

    public string CustomerId { get; } = customerId;
}
