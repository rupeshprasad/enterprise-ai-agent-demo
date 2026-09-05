using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;

namespace EnterpriseAgent.Api.Tools;

public sealed class GetCustomerTool(
    CustomerDataRepository repository,
    AuthorizationService authorizationService) : ICustomerTool
{
    public string Name => "GetCustomer";

    public string Description => "Gets the current profile for a specific enterprise customer.";

    public Task<object?> ExecuteAsync(
        string userId,
        string customerId,
        CancellationToken cancellationToken)
    {
        authorizationService.EnsureCanAccessCustomer(userId, customerId);
        return GetAsync(customerId, cancellationToken);
    }

    private async Task<object?> GetAsync(string customerId, CancellationToken cancellationToken) =>
        await repository.GetCustomerAsync(customerId, cancellationToken);
}
