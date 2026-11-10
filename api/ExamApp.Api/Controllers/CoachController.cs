using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Coaching;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #700: koç paneli. Kapsam dışı öğrenci 404: varlığı sızdırılmaz. Hedef ve notlarda öğretmen yalnız kendi satırlarını görür.
/// Gateway'de <c>/api/exam/{everything}</c> joker route'u kapsar.
/// </summary>
[ApiController]
[Route("api/teacher/coach/students")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
[Authorize(Roles = "Teacher")]
[RequiresEntitlement(EntitlementFeatures.CoachPanel, InPlanOnly = true)] // #1088: koç paneli Essential+; öğrencinin hedef listesi açık kalır
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeacherCoachController(
    ICoachService coach,
    ISchoolAccessPolicy schoolAccess,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> ListStudents(CancellationToken ct) =>
        Ok(await coach.ListStudentsAsync(await GetSchoolScopeAsync(ct), ct));

    [HttpGet("{studentId:int}/overview")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public Task<IActionResult> GetOverview(int studentId, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => Ok(await coach.GetOverviewAsync(teacher, studentId, ct)), ct);

    [HttpGet("{studentId:int}/goals")]
    public Task<IActionResult> ListGoals(int studentId, [FromQuery] DateOnly? weekStart, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => Ok(await coach.ListGoalsAsync(teacher, studentId, weekStart, ct)), ct);

    [HttpPost("{studentId:int}/goals")]
    public Task<IActionResult> AddGoal(int studentId, [FromBody] SaveCoachGoalDto request, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => ToResult(await coach.AddGoalAsync(teacher, studentId, request, ct)), ct);

    [HttpPut("{studentId:int}/goals/{id:int}")]
    public Task<IActionResult> UpdateGoal(int studentId, int id, [FromBody] SaveCoachGoalDto request, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => ToResult(await coach.UpdateGoalAsync(teacher, studentId, id, request, ct)), ct);

    [HttpDelete("{studentId:int}/goals/{id:int}")]
    public Task<IActionResult> DeleteGoal(int studentId, int id, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => await coach.DeleteGoalAsync(teacher, studentId, id, ct) ? NoContent() : NotFound(), ct);

    [HttpGet("{studentId:int}/notes")]
    public Task<IActionResult> ListNotes(int studentId, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => Ok(await coach.ListNotesAsync(teacher, studentId, ct)), ct);

    [HttpPost("{studentId:int}/notes")]
    public Task<IActionResult> AddNote(int studentId, [FromBody] SaveCoachNoteDto request, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => ToResult(await coach.AddNoteAsync(teacher, studentId, request, ct)), ct);

    [HttpPut("{studentId:int}/notes/{id:int}")]
    public Task<IActionResult> UpdateNote(int studentId, int id, [FromBody] SaveCoachNoteDto request, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => ToResult(await coach.UpdateNoteAsync(teacher, studentId, id, request, ct)), ct);

    [HttpDelete("{studentId:int}/notes/{id:int}")]
    public Task<IActionResult> DeleteNote(int studentId, int id, CancellationToken ct) =>
        AsCoachAsync(studentId, async teacher => await coach.DeleteNoteAsync(teacher, studentId, id, ct) ? NoContent() : NotFound(), ct);

    private async Task<IActionResult> AsCoachAsync(int studentId, Func<int, Task<IActionResult>> action, CancellationToken ct)
    {
        var scope = await GetSchoolScopeAsync(ct);
        if (!await schoolAccess.CanAccessAsTeacherOrTutorAsync(scope, studentId, ct))
            return NotFound(new { message = localizer["student.profileNotFound"].Value });
        return await action(scope.UserId);
    }

    private IActionResult ToResult<T>(CoachSaveResult<T>? result) where T : class => result switch
    {
        null => NotFound(),
        { Error: CoachSaveError.TooManyGoals or CoachSaveError.TooManyNotes } => Conflict(new { errorCode = result.Error.ToString() }),
        { Error: not CoachSaveError.None } => BadRequest(new { errorCode = result.Error.ToString() }),
        _ => Ok(result.Value),
    };
}

/// <summary>Issue #700: öğrenci bu haftanın hedeflerini görür; koçun notları hiçbir öğrenci yanıtına girmez.</summary>
[ApiController]
[Route("api/student/me/coach-goals")]
[Authorize(Roles = "Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StudentCoachGoalsController(
    ICoachService coach,
    IStudentService students,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        return Ok(await coach.ListGoalsForStudentAsync(student.Id, ct));
    }

    /// <summary>Issue #893: "yaptım" işareti. Başka öğrencinin ya da bu hafta dışındaki hedef 404.</summary>
    [HttpPut("{goalId:int}/done")]
    public async Task<IActionResult> SetDone(int goalId, [FromBody] SetCoachGoalDoneDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        var goal = await coach.SetGoalDoneAsync(student.Id, goalId, request.Done, ct);
        return goal is null ? NotFound() : Ok(goal);
    }
}

/// <summary>Issue #700: velinin haftalık özeti. Başka velinin ya da Active olmayan bağlantının çocuğu 404.</summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentCoachSummaryController(
    IParentCoachSummaryService service,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("{studentId:int}/coach-summary")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> Get(int studentId, CancellationToken ct)
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
