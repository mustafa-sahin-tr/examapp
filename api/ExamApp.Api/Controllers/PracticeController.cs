using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// "Soru Çöz" pratik oturumu (issue #62). Gateway'de mevcut <c>/api/exam/{everything}</c> joker route'u
/// bu controller'ı da kapsar; ayrı Ocelot kaydı gerekmez.
/// </summary>
[Route("api/practice")]
[ApiController]
[Authorize(Roles = "Student")]
public class PracticeController : BaseController
{
    private readonly IPracticeSessionService _practice;
    private readonly IStudentService _studentService;

    public PracticeController(IPracticeSessionService practice, IStudentService studentService)
        : base()
    {
        _practice = practice;
        _studentService = studentService;
    }

    /// <summary>Yeni pratik oturumu açar. Gövde boş/eksikse: öğrencinin sınıfı + tüm dersler.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession([FromBody] PracticeSessionStartDto? request, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        try
        {
            var result = await _practice.StartAsync(student!, request ?? new PracticeSessionStartDto(), ct);
            return CreatedAtAction(nameof(GetSession), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Öğrencinin geçmiş pratik oturumları, en yeni önce. Sayfalı (<see cref="Paged{T}"/>).</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.ListAsync(student!.Id, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Oturumdaki soruların tek tek sonucu (doğru şık dahil); geçmiş oturum incelemesi.</summary>
    [HttpGet("sessions/{id:int}/review")]
    public async Task<IActionResult> GetSessionReview(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.GetReviewAsync(id, student!.Id, ct);
        if (result == null)
            return NotFound(new { message = "Pratik oturumu bulunamadı" });

        return Ok(result);
    }

    [HttpGet("sessions/{id:int}")]
    public async Task<IActionResult> GetSession(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.GetAsync(id, student!.Id, ct);
        if (result == null)
            return NotFound(new { message = "Pratik oturumu bulunamadı" });

        return Ok(result);
    }

    /// <summary>
    /// Oturumda henüz gösterilmemiş rastgele bir soru. Havuz bittiğinde 200 +
    /// <c>{ question: null, poolExhausted: true }</c> döner (404 değil).
    /// </summary>
    [HttpGet("sessions/{id:int}/next")]
    public async Task<IActionResult> NextQuestion(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        try
        {
            var result = await _practice.NextQuestionAsync(id, student!.Id, ct);
            if (result == null)
                return NotFound(new { message = "Pratik oturumu bulunamadı" });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{id:int}/answer")]
    public async Task<IActionResult> SubmitAnswer(int id, [FromBody] PracticeAnswerSubmitDto dto, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        try
        {
            var result = await _practice.SubmitAnswerAsync(id, student!.Id, dto, ct);
            if (result == null)
                return NotFound(new { message = "Pratik oturumu bulunamadı" });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("sessions/{id:int}/end")]
    public async Task<IActionResult> EndSession(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.EndAsync(id, student!.Id, ct);
        if (result == null)
            return NotFound(new { message = "Pratik oturumu bulunamadı" });

        return Ok(result);
    }

    // --- helpers ---

    private async Task<(StudentProfileDto? Student, IActionResult? Error)> ResolveStudentAsync()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return (null, Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu"));

        var student = await _studentService.GetStudentProfile(user.Id);
        if (student == null)
            return (null, Unauthorized("Öğrenci profili bulunamadı"));

        return (student, null);
    }
}
