namespace EnterpriseAgent.Api.Models;

public sealed record PolicyDocumentResponse(
    string FileName,
    string Content,
    DateTimeOffset LastModifiedUtc);

public sealed record UpdatePolicyDocumentRequest(string Content);

public sealed record PolicyUpdateResponse(
    string Status,
    string Message);
