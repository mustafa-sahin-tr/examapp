using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #440: öğrencinin davet ettiği veli — YALNIZ servis principal'ı (exam API, <c>exam-service</c> client_credentials;
/// <c>Service</c> policy). Gateway <c>/api/auth/parent-accounts</c> yolunu engeller (child-accounts deseni). Davet / bağlantı / rıza / kilit
/// kararları exam API'dedir. Security review M2: davet anında hesap açılmaz (<c>lookup</c> salt okunur); hesap yalnız aktivasyonda
/// (<c>activate</c>) açılır. Parola gövdede gelir, loglanmaz, dönmez.
/// </summary>
[ApiController]
[Route("api/auth/parent-accounts")]
[Authorize(Policy = "Service")]
public class ParentAccountsController : ControllerBase
{
    private readonly IParentAccountService _service;

    public ParentAccountsController(IParentAccountService service) => _service = service;

    /// <summary>200 <see cref="ParentAccountLookupResponse"/> (<c>ExistingParent</c> | <c>None</c> | <c>NotParentAccount</c>); 400; 502.</summary>
    [HttpPost("lookup")]
    [ProducesResponseType(typeof(ParentAccountLookupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup([FromBody] ParentAccountLookupRequest? request, CancellationToken ct)
    {
        var result = await _service.LookupAsync(request?.Email, ct);
        return result.Outcome == ParentAccountOutcomeKind.Success ? Ok(result.Lookup) : Map(result);
    }

    /// <summary>
    /// 201 <see cref="CreateParentAccountResponse"/>; 400 <c>InvalidEmail</c> / <c>InvalidName</c> / <c>PasswordRejected</c>;
    /// 409 <c>EmailAlreadyRegistered</c>; 502.
    /// </summary>
    [HttpPost("activate")]
    [ProducesResponseType(typeof(CreateParentAccountResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Activate([FromBody] CreateParentAccountRequest? request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new ParentAccountErrorResponse { ErrorCode = ParentAccountErrorCodes.InvalidEmail });

        var result = await _service.CreateAsync(request, ct);
        return result.Outcome == ParentAccountOutcomeKind.Success ? StatusCode(StatusCodes.Status201Created, result.Created) : Map(result);
    }

    /// <summary>Security C1: 204; 409 <c>EmailMismatch</c> / <c>NotParentAccount</c>; 400; 502.</summary>
    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmParentEmailRequest? request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new ParentAccountErrorResponse { ErrorCode = ParentAccountErrorCodes.InvalidEmail });

        var result = await _service.ConfirmEmailAsync(request, ct);
        return result.Outcome == ParentAccountOutcomeKind.Success ? NoContent() : Map(result);
    }

    private IActionResult Map(ParentAccountResult result)
    {
        var body = new ParentAccountErrorResponse { ErrorCode = result.ErrorCode ?? ParentAccountErrorCodes.Unavailable };
        return result.Outcome switch
        {
            ParentAccountOutcomeKind.Invalid => BadRequest(body),
            ParentAccountOutcomeKind.Conflict => Conflict(body),
            _ => StatusCode(StatusCodes.Status502BadGateway, body)
        };
    }
}
