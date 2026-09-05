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
