using System.Collections.Concurrent;
using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Services;

public sealed class VerificationReviewRequestService(
    ILogger<VerificationReviewRequestService> logger)
{
    private readonly ConcurrentDictionary<string, VerificationReviewRequest> requests =
        new(StringComparer.OrdinalIgnoreCase);
    private int lastRequestNumber = 1000;

    public VerificationReviewRequest Create(string customerId)
    {
        var requestNumber = Interlocked.Increment(ref lastRequestNumber);
        var request = new VerificationReviewRequest(
            $"VR-{requestNumber}",
            customerId,
            "Created",
            DateTimeOffset.UtcNow);
        requests[request.RequestId] = request;

        logger.LogInformation(
            "Verification review request {RequestId} created for CustomerId={CustomerId}.",
            request.RequestId,
            customerId);
        return request;
    }

    public VerificationReviewRequest? Get(string requestId) =>
        requests.GetValueOrDefault(requestId);
}
