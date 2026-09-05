using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;

namespace EnterpriseAgent.Api.Tools;

public sealed class GetVerificationStatusTool(
    CustomerDataRepository repository,
    AuthorizationService authorizationService) : ICustomerTool
{
    public string Name => "GetVerificationStatus";

    public string Description => "Gets the current verification or KYC status for a specific customer.";

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
        return await repository.GetVerificationAsync(customerId, cancellationToken);
    }
}
