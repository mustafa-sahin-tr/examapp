using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TargetGoals;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #695: ekran menüyü ve kartı buna göre gösterir. Ayar kapalıyken diğer bütün uçlar 404 döner.</summary>
[ApiController]
[Route("api/target-goals/status")]
[Authorize]
public class TargetGoalsStatusController(IOptionsMonitor<TargetGoalsOptions> options) : BaseController
{
    [HttpGet]
    public IActionResult Get() => Ok(new TargetGoalsStatusDto { Enabled = options.CurrentValue.Enabled });
}

/// <summary>Issue #695: taban puanları admin resmî kaynaktan girer: tek satır ya da önizleme + onaylı CSV.</summary>
[ApiController]
[Route("api/admin/target-programs")]
[Authorize(Roles = "Admin")]
[RequireTargetGoals]
public class AdminTargetProgramsController(ITargetProgramService service) : BaseController
{
    private const int MaxTake = 200;
    private const long MaxFileBytes = 10 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? exam = null, [FromQuery] string? scoreType = null, [FromQuery] int? year = null, [FromQuery] string? city = null,
        [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await service.SearchForAdminAsync(new(exam, scoreType, year, city, search, Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake)), ct));

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveTargetProgramDto request, CancellationToken ct) =>
        RunAsync(async userId => Ok(await service.CreateAsync(request, userId, ct)));

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] SaveTargetProgramDto request, CancellationToken ct) =>
        RunAsync(async userId => await service.UpdateAsync(id, request, userId, ct) is { } saved ? Ok(saved) : NotFound());

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        RunAsync(async userId => await service.DeleteAsync(id, userId, ct) ? NoContent() : NotFound());

    /// <summary>Hiçbir şey yazmaz.</summary>
    [HttpPost("import/preview")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public Task<IActionResult> Preview([FromForm] TargetProgramImportForm form, CancellationToken ct) =>
        RunAsync(async _ => Ok(await service.PreviewImportAsync(await ReadAsync(form, ct), ct)));

    /// <summary>Önizlenen dosya yeniden gönderilir ve aynı kurallarla yazılır; sunucu önizleme durumu tutmaz.</summary>
    [HttpPost("import/confirm")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public Task<IActionResult> Confirm([FromForm] TargetProgramImportForm form, CancellationToken ct) =>
        RunAsync(async userId => Ok(await service.ConfirmImportAsync(await ReadAsync(form, ct), userId, ct)));

    private static async Task<TargetProgramImport> ReadAsync(TargetProgramImportForm form, CancellationToken ct)
    {
        if (form.File.Length > MaxFileBytes)
            throw new TargetGoalRuleException("CsvTooLarge");

        await using var stream = form.File.OpenReadStream();
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, ct);
        return new(form.Exam, form.Year, form.Source, content.ToArray());
    }

    private async Task<IActionResult> RunAsync(Func<int, Task<IActionResult>> action)
    {
        var user = await GetAuthenticatedUserAsync(HttpContext.RequestAborted);
        if (!IsResolvedUser(user))
            return Unauthorized();
        try
        {
            return await action(user.Id);
        }
        catch (TargetGoalRuleException ex)
        {
            return BadRequest(new { errorCode = ex.Message });
        }
    }
}

/// <summary>Issue #695: öğrencinin kendi hedefi. Öğrenci kimliği yalnız token'dan gelir.</summary>
[ApiController]
[Route("api/student/me/target-goal")]
[Authorize(Roles = "Student")]
[RequireTargetGoals]
public class StudentTargetGoalController(
    ITargetGoalService goals,
    ITargetProgramService programs,
    IStudentService students,
    IStringLocalizer<Messages> localizer) : BaseController
{
    private const int MaxTake = 50;

    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) =>
        WithStudentAsync(async studentId => Ok(await goals.GetAsync(studentId, ct)), ct);

    [HttpGet("programs")]
    [EnableRateLimiting(TaxonomySearchRateLimiting.Policy)]
    public async Task<IActionResult> Search(
        [FromQuery] string? exam = null, [FromQuery] string? scoreType = null, [FromQuery] int? year = null, [FromQuery] string? city = null,
        [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await programs.SearchAsync(new(exam, scoreType, year, city, search, Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake)), ct));

    [HttpPut]
    public Task<IActionResult> Set([FromBody] SetTargetGoalDto request, CancellationToken ct) =>
        WithStudentAsync(async studentId => await goals.SetAsync(studentId, request.TargetProgramId, ct) ? Ok(await goals.GetAsync(studentId, ct)) : NotFound(), ct);

    [HttpDelete]
    public Task<IActionResult> Clear(CancellationToken ct) =>
        WithStudentAsync(async studentId =>
        {
            await goals.ClearAsync(studentId, ct);
            return NoContent();
        }, ct);

    private async Task<IActionResult> WithStudentAsync(Func<int, Task<IActionResult>> action, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });
        return await action(student.Id);
    }
}

/// <summary>Issue #695: çocuğun hedefi ve farkı. Başka velinin ya da Active olmayan bağlantının çocuğu 404.</summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequireTargetGoals]
public class ParentTargetGoalController(
    IParentTargetGoalService service,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("{studentId:int}/target-goal")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> Get(int studentId, CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });

        var result = await service.GetAsync(user.Id, studentId, ct);
        return result is null
            ? NotFound(new { message = localizer["parentLinks.errors.childNotFound"].Value, errorCode = ParentLinkErrorCodes.NotFound })
            : Ok(result);
    }
}
