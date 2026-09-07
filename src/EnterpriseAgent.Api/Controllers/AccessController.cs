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
    /// <summary>Lists support representatives available in the demo.</summary>
    /// <remarks>Reads representatives from customerAccess in enterprise-data.json and creates a display name from each user ID.</remarks>
    /// <response code="200">The configured representative IDs and display names.</response>
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyCollection<SupportRepresentativeOption>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<SupportRepresentativeOption>> GetUsers() =>
        Ok(authorizationService.GetSupportRepresentatives());

    /// <summary>Lists customers and access assignments for a representative.</summary>
    /// <remarks>
    /// Returns every customer with name, verification status, and a flag indicating whether the
    /// representative is authorized to access that customer's records.
    /// </remarks>
    /// <param name="userId">The representative ID whose customer access should be displayed.</param>
    /// <response code="200">The complete customer-access matrix for the representative.</response>
    /// <response code="400">The user ID is not configured.</response>
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

    /// <summary>Grants or removes a representative's access to one customer.</summary>
    /// <remarks>
    /// Updates customerAccess in enterprise-data.json immediately. Subsequent agent requests use the
    /// new authorization assignment without an API restart.
    /// </remarks>
    /// <param name="request">The representative ID, customer ID, and desired access state.</param>
    /// <param name="cancellationToken">Stops the update if the client disconnects.</param>
    /// <response code="200">The representative's updated customer-access matrix.</response>
    /// <response code="400">The representative or customer is unknown, or the request is invalid.</response>
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

    /// <summary>Adds a customer to the demo directory.</summary>
    /// <remarks>Creates customer-master and verification records in enterprise-data.json. Access is not assigned automatically.</remarks>
    /// <param name="request">Customer ID, name, country, and initial verification status.</param>
    /// <param name="cancellationToken">Stops the operation if the client disconnects.</param>
    /// <response code="201">The customer was created.</response>
    /// <response code="400">The values are invalid or the customer ID already exists.</response>
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

    /// <summary>Lists customers for the Manage Customers screen.</summary>
    /// <remarks>Combines customer-master and verification records into one management view.</remarks>
    /// <response code="200">All customers with their current verification status.</response>
    [HttpGet("customers")]
    [ProducesResponseType<IReadOnlyCollection<CustomerManagementRecord>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<CustomerManagementRecord>> GetCustomers() =>
        Ok(authorizationService.GetCustomers());

    /// <summary>Updates an existing customer and verification status.</summary>
    /// <remarks>Persists name, country, and verification changes to enterprise-data.json. The customer ID cannot be changed.</remarks>
    /// <param name="customerId">The immutable ID of the customer to update.</param>
    /// <param name="request">The replacement customer values and verification status.</param>
    /// <param name="cancellationToken">Stops the update if the client disconnects.</param>
    /// <response code="200">The updated combined customer record.</response>
    /// <response code="400">The customer was not found or the submitted values are invalid.</response>
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

    /// <summary>Deletes a customer from the demo.</summary>
    /// <remarks>
    /// Removes the customer master record, verification record, and all representative access
    /// assignments for that customer from enterprise-data.json.
    /// </remarks>
    /// <param name="customerId">The ID of the customer to delete.</param>
    /// <param name="cancellationToken">Stops the deletion if the client disconnects.</param>
    /// <response code="204">The customer and related demo records were deleted.</response>
    /// <response code="400">The customer was not found.</response>
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
