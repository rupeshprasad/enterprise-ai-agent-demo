namespace EnterpriseAgent.Api.Models;

public sealed record Customer(
    string CustomerId,
    string Name,
    string Country,
    string Status);

public sealed record VerificationRecord(
    string CustomerId,
    string VerificationStatus,
    DateOnly LastUpdated);

public sealed record OrderEligibility(
    string CustomerId,
    string SystemStatus,
    string? ReasonCode);
