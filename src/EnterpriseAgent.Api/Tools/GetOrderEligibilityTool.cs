using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;

namespace EnterpriseAgent.Api.Tools;

public sealed class GetOrderEligibilityTool(
    CustomerDataRepository repository,
    AuthorizationService authorizationService) : ICustomerTool
{
    public string Name => "GetOrderEligibility";

    public string Description => "Gets the current order eligibility and reason code for a specific customer.";

    public Task<object?> ExecuteAsync(
        string userId,
        string customerId,
        CancellationToken cancellationToken)
    {
        authorizationService.EnsureCanAccessCustomer(userId, customerId);
        return GetAsync(customerId, cancellationToken);
    }

    private async Task<object?> GetAsync(string customerId, CancellationToken cancellationToken) =>
        await repository.GetOrderEligibilityAsync(customerId, cancellationToken);
}
