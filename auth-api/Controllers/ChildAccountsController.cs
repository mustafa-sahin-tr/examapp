using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #438: velinin panelinden açılan çocuk hesabı — YALNIZ servis principal'ı (exam API, <c>exam-service</c>
/// client_credentials; <c>Service</c> policy). Gateway <c>/api/auth/child-accounts</c> yolunu engeller (users/lookup deseni).
/// Veli yetkisi / bağlantı / tavan / rıza kararları exam API'dedir; bu uç yalnız kimlik deposunu (Keycloak + identity) yönetir
/// ve yalnız <c>parent_managed=true</c> işaretli hesaplara dokunur. Parola gövdede gelir, loglanmaz, dönmez.
/// </summary>
[ApiController]
[Route("api/auth/child-accounts")]
[Authorize(Policy = "Service")]
public class ChildAccountsController : ControllerBase
{
    private readonly IChildAccountService _service;

    public ChildAccountsController(IChildAccountService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(typeof(CreateChildAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ChildAccountErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ChildAccountErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ChildAccountErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateChildAccountRequest? request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new ChildAccountErrorResponse { ErrorCode = ChildAccountErrorCodes.InvalidUsername });

        var result = await _service.CreateAsync(request, ct);
        return result.Outcome == ChildAccountOutcome.Success
            ? StatusCode(StatusCodes.Status201Created, result.Created)
            : Map(result);
    }

    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> Delete(int userId, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(userId, ct);
        return result.Outcome == ChildAccountOutcome.Success ? NoContent() : Map(result);
    }

    [HttpPut("{userId:int}/password")]
    public async Task<IActionResult> ResetPassword(int userId, [FromBody] ResetChildAccountPasswordRequest? request, CancellationToken ct)
    {
        var result = await _service.ResetPasswordAsync(userId, request?.Password, ct);
        return result.Outcome == ChildAccountOutcome.Success ? NoContent() : Map(result);
    }

    private IActionResult Map(ChildAccountResult result)
    {
        var body = new ChildAccountErrorResponse { ErrorCode = result.ErrorCode ?? ChildAccountErrorCodes.Unavailable };
        return result.Outcome switch
        {
            ChildAccountOutcome.Invalid => BadRequest(body),
            ChildAccountOutcome.Conflict => Conflict(body),
            ChildAccountOutcome.NotFound => NotFound(body),
            _ => StatusCode(StatusCodes.Status502BadGateway, body)
        };
    }
}
