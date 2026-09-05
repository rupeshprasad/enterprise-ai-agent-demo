using EnterpriseAgent.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseAgent.Tests;

public sealed class VerificationReviewRequestTests
{
    [Fact]
    public void Create_ReturnsUniqueInMemoryRequestThatCanBeRetrieved()
    {
        var service = new VerificationReviewRequestService(
            NullLogger<VerificationReviewRequestService>.Instance);

        var first = service.Create("ABC123");
        var second = service.Create("ABC123");

        Assert.Equal("VR-1001", first.RequestId);
        Assert.Equal("Created", first.Status);
        Assert.Equal("ABC123", first.CustomerId);
        Assert.Equal(first, service.Get(first.RequestId));
        Assert.NotEqual(first.RequestId, second.RequestId);
    }
}
