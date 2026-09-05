using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Rag;

public sealed class DocumentLoader(
    IWebHostEnvironment environment,
    ILogger<DocumentLoader> logger)
{
    private readonly string policyPath = Path.GetFullPath(
        Path.Combine(environment.ContentRootPath, "..", "..", "data", "policies"));

    public async Task<IReadOnlyCollection<PolicyDocument>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(policyPath))
        {
            logger.LogWarning("Policy directory was not found at {PolicyPath}.", policyPath);
            return [];
        }

        var files = Directory.GetFiles(policyPath, "*.md", SearchOption.TopDirectoryOnly);
        var documents = new List<PolicyDocument>(files.Length);
        foreach (var file in files)
        {
            documents.Add(new PolicyDocument(
                Path.GetFileName(file),
                await File.ReadAllTextAsync(file, cancellationToken)));
        }

        logger.LogInformation(
            "Loaded {DocumentCount} policy documents from {PolicyPath}.",
            documents.Count,
            policyPath);
        return documents;
    }
}
