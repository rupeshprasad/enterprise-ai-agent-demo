using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;

namespace EnterpriseAgent.Api.Tools;

public sealed class GetCustomerTool(
    CustomerDataRepository repository,
    AuthorizationService authorizationService) : ICustomerTool
{
    public string Name => "GetCustomer";

    public string Description => "Gets the current profile for a specific enterprise customer.";

    public async Task<object?> ExecuteAsync(
        string userId,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customerId = await repository.ResolveCustomerIdAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return null;
        }

        authorizationService.EnsureCanAccessCustomer(userId, customerId);
        return await repository.GetCustomerAsync(customerId, cancellationToken);
    }
}
