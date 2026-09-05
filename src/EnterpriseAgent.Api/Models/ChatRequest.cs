using System.ComponentModel.DataAnnotations;

namespace EnterpriseAgent.Api.Models;

public sealed record ChatRequest(
    [Required] string UserId,
    [Required] string Message);
