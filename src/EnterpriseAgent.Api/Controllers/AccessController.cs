using EnterpriseAgent.Api.Models;
using EnterpriseAgent.Api.Security;
using EnterpriseAgent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseAgent.Api.Controllers;

[ApiController]
[Route("api/access")]
public sealed class AccessController(
    AuthorizationService authorizationService,
    ChatHistoryService chatHistoryService,
    ILogger<AccessController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CustomerAccessOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyCollection<CustomerAccessOption>> Get([FromQuery] string userId)
    {
        if (!chatHistoryService.IsSupportedUser(userId))
        {
            return BadRequest(new { message = "Unknown demo user." });
        }

        return Ok(authorizationService.GetCustomerAccess(userId));
    }

    [HttpPut]
    [ProducesResponseType<IReadOnlyCollection<CustomerAccessOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyCollection<CustomerAccessOption>>> Update(
        [FromBody] UpdateCustomerAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (!chatHistoryService.IsSupportedUser(request.UserId))
        {
            return BadRequest(new { message = "Unknown demo user." });
        }

        try
        {
            var access = await authorizationService.SetCustomerAccessAsync(
                request.UserId,
                request.CustomerId,
                request.HasAccess,
                cancellationToken);
            logger.LogInformation(
                "Demo customer access changed. UserId={UserId}, CustomerId={CustomerId}, HasAccess={HasAccess}.",
                request.UserId,
                request.CustomerId,
                request.HasAccess);
            return Ok(access);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("customers")]
    [ProducesResponseType<Customer>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Customer>> AddCustomer(
        [FromBody] AddCustomerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = await authorizationService.AddCustomerAsync(request, cancellationToken);
            logger.LogInformation("Demo customer added. CustomerId={CustomerId}.", customer.CustomerId);
            return StatusCode(StatusCodes.Status201Created, customer);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("customers")]
    [ProducesResponseType<IReadOnlyCollection<CustomerManagementRecord>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<CustomerManagementRecord>> GetCustomers() =>
        Ok(authorizationService.GetCustomers());

    [HttpPut("customers/{customerId}")]
    [ProducesResponseType<CustomerManagementRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerManagementRecord>> UpdateCustomer(
        string customerId,
        [FromBody] AddCustomerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authorizationService.UpdateCustomerAsync(customerId, request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpDelete("customers/{customerId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteCustomer(string customerId, CancellationToken cancellationToken)
    {
        try
        {
            await authorizationService.DeleteCustomerAsync(customerId, cancellationToken);
            logger.LogInformation("Demo customer deleted. CustomerId={CustomerId}.", customerId);
            return NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
