using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Rag;
using EnterpriseAgent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseAgent.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController(
    RagService ragService,
    PolicyDocumentService policyDocumentService,
    ILogger<KnowledgeController> logger) : ControllerBase
{
    /// <summary>Returns the editable customer-ordering policy.</summary>
    /// <remarks>Loads the current Markdown document directly from the demo policy file.</remarks>
    /// <response code="200">The policy filename, content, and last-modified time.</response>
    [HttpGet("policy")]
    [ProducesResponseType<PolicyDocumentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PolicyDocumentResponse>> GetPolicy(CancellationToken cancellationToken)
    {
        var content = await policyDocumentService.ReadAsync(cancellationToken);
        return Ok(new PolicyDocumentResponse(
            "customer-ordering-policy.md",
            content,
            policyDocumentService.GetLastModifiedUtc()));
    }

    /// <summary>Updates the customer-ordering policy file.</summary>
    /// <remarks>
    /// Atomically saves the submitted Markdown to the fixed demo policy file. It deliberately does
    /// not refresh the RAG index; call POST /api/knowledge/refresh as a separate presentation step.
    /// </remarks>
    /// <param name="request">The complete replacement Markdown content.</param>
    /// <param name="cancellationToken">Stops the operation if the client disconnects.</param>
    /// <response code="200">The policy was saved. The existing knowledge index remains active.</response>
    /// <response code="400">The submitted policy was empty.</response>
    [HttpPut("policy")]
    [ProducesResponseType<PolicyUpdateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PolicyUpdateResponse>> UpdatePolicy(
        [FromBody] UpdatePolicyDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await policyDocumentService.WriteAsync(request.Content, cancellationToken);
            return Ok(new PolicyUpdateResponse(
                "Success",
                "Policy saved. Click Refresh knowledge when you are ready to update the agent."));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid policy", Detail = exception.Message });
        }
    }

    /// <summary>Rebuilds the in-memory policy knowledge index.</summary>
    /// <remarks>
    /// Reloads Markdown policy documents, creates fresh Gemini embeddings, and replaces the current
    /// vector index. Call this after changing a policy file; an API restart is not required.
    /// </remarks>
    /// <response code="200">The knowledge index was refreshed and the indexed chunk count is returned.</response>
    /// <response code="503">Embedding configuration is invalid or the provider is unavailable.</response>
    [HttpPost("refresh")]
    [ProducesResponseType<KnowledgeRefreshResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<KnowledgeRefreshResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<KnowledgeRefreshResponse>> Refresh(
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Knowledge-base refresh requested.");
            var count = await ragService.RefreshAsync(cancellationToken);
            logger.LogInformation(
                "Knowledge-base refresh completed. ChunkCount={ChunkCount}.",
                count);
            return Ok(new KnowledgeRefreshResponse(
                "Success",
                count,
                $"Knowledge base refreshed successfully. {count} chunks indexed."));
        }
        catch (AIConfigurationException exception)
        {
            logger.LogError(exception, "Knowledge-base refresh failed due to AI configuration.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new KnowledgeRefreshResponse("ConfigurationError", 0, exception.Message));
        }
        catch (AIProviderException exception)
        {
            logger.LogError(exception, "Knowledge-base refresh failed because embeddings were unavailable.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new KnowledgeRefreshResponse(
                    "ProviderError",
                    0,
                    "The embedding provider is currently unavailable."));
        }
    }
}
