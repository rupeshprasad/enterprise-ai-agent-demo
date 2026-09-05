namespace EnterpriseAgent.Api.Tools;

public interface ICustomerTool
{
    string Name { get; }

    string Description { get; }

    Task<object?> ExecuteAsync(
        string userId,
        string customerId,
        CancellationToken cancellationToken);
}
