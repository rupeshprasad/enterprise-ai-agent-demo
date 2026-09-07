namespace EnterpriseAgent.Api.Models;

public sealed record CustomerAccessOption(
    string CustomerId,
    string CustomerName,
    string VerificationStatus,
    bool HasAccess);

public sealed record SupportRepresentativeOption(
    string UserId,
    string DisplayName);

public sealed record UpdateCustomerAccessRequest(
    string UserId,
    string CustomerId,
    bool HasAccess);

public sealed record AddCustomerRequest(
    string CustomerId,
    string Name,
    string Country,
    string VerificationStatus);

public sealed record CustomerManagementRecord(
    string CustomerId,
    string Name,
    string Country,
    string VerificationStatus);
