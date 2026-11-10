using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Coaching;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #696: öğrencinin kendi kazanım seviyeleri. Öğrenci kimliği yalnız token'dan gelir.</summary>
[ApiController]
[Route("api/student/me/learning-outcome-mastery")]
[Authorize(Roles = "Student")]
[RequiresEntitlement(EntitlementFeatures.Analytics, InPlanOnly = true)] // #1089
public class StudentLearningOutcomeMasteryController(
    ILearningOutcomeMasteryService service,
    IStudentService students,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> Get([FromQuery] int? subjectId = null, [FromQuery] string? curriculumVersion = null, CancellationToken ct = default)
    {
        if (!MasteryQuery.TryParseCurriculum(curriculumVersion, out var curriculum))
            return BadRequest();

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        return Ok(await service.GetAsync(student.Id, DateTime.UtcNow, subjectId, curriculum, ct));
    }
}

/// <summary>Issue #696: çocuğun kazanım seviyeleri. Başka velinin ya da Active olmayan bağlantının çocuğu 404.</summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentLearningOutcomeMasteryController(
    IParentLearningOutcomeMasteryService service,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("{studentId:int}/learning-outcome-mastery")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> Get(int studentId, [FromQuery] int? subjectId = null, [FromQuery] string? curriculumVersion = null,
        [FromServices] IParentChildAccess access = null!, [FromServices] IEntitlementService entitlements = null!, CancellationToken ct = default)
    {
        if (!MasteryQuery.TryParseCurriculum(curriculumVersion, out var curriculum))
            return BadRequest();

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        // Issue #1089: kazanım seviyeleri çocuğun planında `analytics` ister.
        if (await ChildEntitlements.DenyUnlessChildHasAsync(access, entitlements, user.Id, studentId, EntitlementFeatures.Analytics, ct) is { } denied)
            return denied;

        var result = await service.GetAsync(user.Id, studentId, subjectId, curriculum, ct);
        return result is null
            ? NotFound(new { message = localizer["parentLinks.errors.childNotFound"].Value, errorCode = ParentLinkErrorCodes.NotFound })
            : Ok(result);
    }
}

/// <summary>
/// Issue #696 (kullanıcı kararı 2026-11-05): öğretmen kendi okulundaki öğrencinin ya da özel ders verdiği (Approved Booking)
/// öğrencinin kazanım seviyesini görür. Kapsam dışı öğrenci 404: varlığı sızdırılmaz.
/// </summary>
[ApiController]
[Route("api/teacher/students")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeacherLearningOutcomeMasteryController(
    ILearningOutcomeMasteryService service,
    ISchoolAccessPolicy schoolAccess,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("{studentId:int}/learning-outcome-mastery")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> Get(int studentId, [FromQuery] int? subjectId = null, [FromQuery] string? curriculumVersion = null, CancellationToken ct = default)
    {
        if (!MasteryQuery.TryParseCurriculum(curriculumVersion, out var curriculum))
            return BadRequest();

        var scope = await GetSchoolScopeAsync(ct);
        if (!await schoolAccess.CanAccessAsTeacherOrTutorAsync(scope, studentId, ct))
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        return Ok(await service.GetAsync(studentId, DateTime.UtcNow, subjectId, curriculum, ct));
    }
}

internal static class MasteryQuery
{
    /// <summary>Boş parametre iki müfredatı da döner; yalnız geçersiz değer reddedilir.</summary>
    public static bool TryParseCurriculum(string? value, out CurriculumVersion? curriculum)
    {
        curriculum = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!CurriculumVersionParser.TryParse(value, out var parsed))
            return false;
        curriculum = parsed;
        return true;
    }
}
