namespace EnterpriseAgent.Api.Models;

public sealed record VerificationReviewRequest(
    string RequestId,
    string CustomerId,
    string Status,
    DateTimeOffset CreatedAt);
