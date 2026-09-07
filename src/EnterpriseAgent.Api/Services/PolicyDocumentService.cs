namespace EnterpriseAgent.Api.Services;

public sealed class PolicyDocumentService(
    IWebHostEnvironment environment,
    ILogger<PolicyDocumentService> logger)
{
    private readonly SemaphoreSlim updateLock = new(1, 1);
    private readonly string policyPath = Path.GetFullPath(Path.Combine(
        environment.ContentRootPath,
        "..",
        "..",
        "data",
        "policies",
        "customer-ordering-policy.md"));

    public async Task<string> ReadAsync(CancellationToken cancellationToken) =>
        await File.ReadAllTextAsync(policyPath, cancellationToken);

    public DateTimeOffset GetLastModifiedUtc() => File.GetLastWriteTimeUtc(policyPath);

    public async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Policy content cannot be empty.", nameof(content));
        }

        await updateLock.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = $"{policyPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
                File.Move(temporaryPath, policyPath, true);
                logger.LogInformation("Policy document updated. PolicyFile={PolicyFile}.", Path.GetFileName(policyPath));
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            updateLock.Release();
        }
    }
}
