using EnterpriseAgent.Api.Agents;
using EnterpriseAgent.Api.AI;
using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseAgent.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(
    AgentService agentService,
    ChatHistoryService chatHistoryService,
    ILogger<ChatController> logger) : ControllerBase
{
    [HttpGet("history")]
    [ProducesResponseType<IReadOnlyCollection<ChatHistoryEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyCollection<ChatHistoryEntry>>> GetHistory(
        [FromQuery] string userId,
        CancellationToken cancellationToken)
    {
        if (!chatHistoryService.IsSupportedUser(userId))
        {
            return BadRequest(new ChatResponse("Unknown demo user."));
        }

        return Ok(await chatHistoryService.GetAsync(userId, cancellationToken));
    }

    [HttpDelete("history")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearHistory(
        [FromQuery] string userId,
        CancellationToken cancellationToken)
    {
        if (!chatHistoryService.IsSupportedUser(userId))
        {
            return BadRequest(new ChatResponse("Unknown demo user."));
        }

        await chatHistoryService.ClearAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatResponse>> Send(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!chatHistoryService.IsSupportedUser(request.UserId))
        {
            return BadRequest(new ChatResponse("Unknown demo user."));
        }

        await chatHistoryService.AppendAsync(
            request.UserId,
            new ChatHistoryEntry("user", request.Message, DateTimeOffset.UtcNow),
            cancellationToken);

        try
        {
            logger.LogInformation(
                "Chat request received. UserId={UserId}, DemoMode={DemoMode}, MessageLength={MessageLength}.",
                request.UserId,
                request.DemoMode,
                request.Message.Length);
            var response = await agentService.SendAsync(
                request.UserId,
                request.Message,
                request.DemoMode,
                cancellationToken);
            logger.LogInformation(
                "Chat request completed. UserId={UserId}, ToolCallCount={ToolCallCount}.",
                request.UserId,
                response.ToolCalls?.Count ?? 0);
            await SaveAssistantResponseAsync(request.UserId, response, "assistant", cancellationToken);
            return Ok(response);
        }
        catch (AIConfigurationException exception)
        {
            logger.LogError(exception, "Chat request failed because AI configuration is invalid.");
            var response = new ChatResponse(exception.Message);
            await SaveAssistantResponseAsync(request.UserId, response, "error", cancellationToken);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response);
        }
        catch (AIProviderException exception)
        {
            logger.LogError(exception, "Chat request failed because the AI provider was unavailable.");
            var response = new ChatResponse("The AI provider is currently unavailable. Please try again.");
            await SaveAssistantResponseAsync(request.UserId, response, "error", cancellationToken);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response);
        }
    }

    private Task SaveAssistantResponseAsync(
        string userId,
        ChatResponse response,
        string role,
        CancellationToken cancellationToken) =>
        chatHistoryService.AppendAsync(
            userId,
            new ChatHistoryEntry(
                role,
                response.Answer,
                DateTimeOffset.UtcNow,
                response.ToolCalls,
                response.Sources,
                response.DemoMode,
                response.Activity),
            cancellationToken);
}
