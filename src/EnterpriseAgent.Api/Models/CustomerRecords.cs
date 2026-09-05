namespace EnterpriseAgent.Api.Models;

public sealed record Customer(
    string CustomerId,
    string Name,
    string Country);

public sealed record VerificationRecord(
    string CustomerId,
    string VerificationStatus);

public sealed record CustomerAccessAssignment(
    string UserId,
    IReadOnlyCollection<string> CustomerIds);

public sealed record EnterpriseData(
    IReadOnlyCollection<Customer> CustomerMaster,
    IReadOnlyCollection<CustomerAccessAssignment> CustomerAccess,
    IReadOnlyCollection<VerificationRecord> Verification);
