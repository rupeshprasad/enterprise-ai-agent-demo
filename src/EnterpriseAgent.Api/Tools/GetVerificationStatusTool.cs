using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;

namespace EnterpriseAgent.Api.Tools;

public sealed class GetVerificationStatusTool(
    CustomerDataRepository repository,
    AuthorizationService authorizationService) : ICustomerTool
{
    public string Name => "GetVerificationStatus";

    public string Description => "Gets the current verification or KYC status for a specific customer.";

    public Task<object?> ExecuteAsync(
        string userId,
        string customerId,
        CancellationToken cancellationToken)
    {
        authorizationService.EnsureCanAccessCustomer(userId, customerId);
        return GetAsync(customerId, cancellationToken);
    }

    private async Task<object?> GetAsync(string customerId, CancellationToken cancellationToken) =>
        await repository.GetVerificationAsync(customerId, cancellationToken);
}
