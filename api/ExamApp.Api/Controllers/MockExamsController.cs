using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #693: sınav şablonları (tanımı admin) ve denemeler (kurulum öğretmen ve admin, çözüm öğrenci).</summary>
[ApiController]
[Route("api/mock-exams")]
public class MockExamsController : BaseController
{
    private const string AuthoringRoles = "Teacher,Admin";
    private readonly IMockExamService _service;
    private readonly IStudentService _students;

    public MockExamsController(IMockExamService service, IStudentService students)
    {
        _service = service;
        _students = students;
    }

    [HttpGet("templates")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> ListTemplates(CancellationToken ct) => Ok(await _service.ListTemplatesAsync(ct));

    [HttpPost("templates")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateTemplate([FromBody] ExamTemplateDto template, CancellationToken ct) => SaveTemplateAsync(template, null, ct);

    [HttpPut("templates/{id:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> UpdateTemplate(int id, [FromBody] ExamTemplateDto template, CancellationToken ct) => SaveTemplateAsync(template, id, ct);

    private async Task<IActionResult> SaveTemplateAsync(ExamTemplateDto template, int? id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await RunAsync(async () => Ok(await _service.SaveTemplateAsync(template, id, user.Id, ct)));
    }

    [HttpGet]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? Ok(await _service.ListMineAsync(user.Id, IsAdmin, ct)) : Unauthorized();
    }

    [HttpPost]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> Create([FromBody] CreateMockExamDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await RunAsync(async () => Ok(await _service.CreateAsync(request, user.Id, ct)));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await _service.GetAsync(id, user.Id, IsAdmin, ct) is { } detail ? Ok(detail) : NotFound();
    }

    [HttpPut("{id:int}/sections/{sectionId:int}/questions")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> SetSectionQuestions(int id, int sectionId, [FromBody] SetSectionQuestionsDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await RunAsync(async () =>
            await _service.SetSectionQuestionsAsync(id, sectionId, request.QuestionIds, user.Id, IsAdmin, ct) is { } detail ? Ok(detail) : NotFound());
    }

    /// <summary>Sorun varsa yayınlanmaz: 400 ve <c>problems</c> listesi.</summary>
    [HttpPost("{id:int}/publish")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> Publish(int id, [FromBody] PublishMockExamDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await RunAsync(async () =>
        {
            var detail = await _service.PublishAsync(id, request.OpenToGrade, user.Id, IsAdmin, ct);
            if (detail is null)
                return NotFound();
            return detail.PublishedAt is null ? BadRequest(new { errorCode = "CannotPublish", problems = detail.Problems }) : Ok(detail);
        });
    }

    /// <summary>Issue #1031: sınıfın Free öğrencilerine açık tek deneme. Yalnız yayımlanmış ve sınıfa açık deneme seçilir.</summary>
    [HttpPut("{id:int}/free-trial")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetFreeTrial(int id, [FromBody] SetMockExamFreeTrialDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await RunAsync(async () => await _service.SetFreeTrialAsync(id, request.IsFreeTrial, user.Id, ct) is { } detail ? Ok(detail) : NotFound());
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> DeleteDraft(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        return await RunAsync(async () => await _service.DeleteDraftAsync(id, user.Id, IsAdmin, ct) ? NoContent() : NotFound());
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> ListForStudent(CancellationToken ct)
    {
        var student = await CurrentStudentAsync(ct);
        return student is null ? Unauthorized() : Ok(await _service.ListForStudentAsync(student, ct));
    }

    [HttpGet("{id:int}/result")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetResult(int id, CancellationToken ct)
    {
        var student = await CurrentStudentAsync(ct);
        if (student is null)
            return Unauthorized();
        return await _service.GetResultAsync(id, student, ct) is { } result ? Ok(result) : NotFound();
    }

    private async Task<StudentProfileDto?> CurrentStudentAsync(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? await _students.GetStudentProfile(user.Id) : null;
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (MockExamRuleException ex)
        {
            return BadRequest(new { errorCode = ex.Message });
        }
    }
}
