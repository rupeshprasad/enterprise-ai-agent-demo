using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Rag;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseAgent.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController(
    RagService ragService,
    ILogger<KnowledgeController> logger) : ControllerBase
{
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
