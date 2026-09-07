using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseAgent.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AIController(
    IAIClient aiClient,
    ILogger<AIController> logger) : ControllerBase
{
    /// <summary>Tests connectivity to the configured Gemini model.</summary>
    /// <remarks>
    /// Sends a small deterministic prompt to Gemini. Use this operation to verify that the API key,
    /// model name, network connectivity, and provider configuration are working before using chat.
    /// </remarks>
    /// <response code="200">Gemini responded successfully.</response>
    /// <response code="503">Gemini configuration is invalid or the provider is unavailable.</response>
    [HttpGet("connectivity")]
    [ProducesResponseType<ConnectivityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConnectivityResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConnectivityResponse>> CheckConnectivity(
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Gemini connectivity check started.");
            var response = await aiClient.SendAsync(
                "Reply with exactly: Gemini connection successful.",
                cancellationToken);
            logger.LogInformation("Gemini connectivity check completed successfully.");
            return Ok(new ConnectivityResponse("Success", response));
        }
        catch (AIConfigurationException exception)
        {
            logger.LogError(exception, "Gemini connectivity check failed due to invalid configuration.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ConnectivityResponse("ConfigurationError", exception.Message));
        }
        catch (AIProviderException exception)
        {
            logger.LogError(exception, "Gemini connectivity check failed because the provider was unavailable.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ConnectivityResponse("ProviderError", "Gemini is currently unavailable."));
        }
    }
}
