using EnterpriseAgent.Api.Services;
using EnterpriseAgent.Api.Security;

namespace EnterpriseAgent.Api.Tools;

public sealed class CreateVerificationReviewRequestTool(
    VerificationReviewRequestService reviewRequestService,
    CustomerDataRepository customerRepository,
    AuthorizationService authorizationService) : ICustomerTool
{
    public string Name => "CreateVerificationReviewRequest";

    public string Description =>
        "Creates a safe, in-memory verification review request for a specific customer.";

    public async Task<object?> ExecuteAsync(
        string userId,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customerId = await customerRepository.ResolveCustomerIdAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return null;
        }

        authorizationService.EnsureCanAccessCustomer(userId, customerId);
        var customer = await customerRepository.GetCustomerAsync(customerId, cancellationToken);
        return customer is null ? null : reviewRequestService.Create(customerId);
    }
}
