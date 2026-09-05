namespace EnterpriseAgent.Api.Models;

public sealed record CustomerAccessOption(
    string CustomerId,
    string CustomerName,
    bool HasAccess);

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
