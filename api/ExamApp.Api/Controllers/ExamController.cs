using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Data;
using ExamApp.Api.Models;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Globalization;
using ExamApp.Api.Helpers;
using ExamApp.Api.Controllers;
using ExamApp.Api.Services.Interfaces;

[Route("api/worksheet")]
[ApiController]
public class ExamController : BaseController
{


    private readonly UserProfileCacheService _userProfileCacheService;
    private readonly IExamService _examService;
    public ExamController(IMinIoService minioService, IExamService examService, UserProfileCacheService userProfileCacheService)
        : base()
    {
        _examService = examService;
        _userProfileCacheService = userProfileCacheService;
    }


    [HttpGet("{id}")]
    public async Task<IActionResult> GetWorksheet(int id)
    {
        var result = await _examService.GetWorksheetByIdAsync(id);
        if (result == null)
            return NotFound();

        return Ok(result);
    }


    [HttpGet("student-worksheets")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetWorksheetAndInstancessAsync(int gradeId)
    {
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if (profile == null)
        {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }
        var result = await _examService.GetWorksheetAndInstancesAsync(new Student { Id = profile.ProfileId }, gradeId);
        return Ok(result);
    }


    [HttpGet("CompletedTests")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetCompletedTests(int pageNumber = 1, int pageSize = 10)
    {
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if (profile == null)
        {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }
        var result = await _examService.GetCompletedTestsAsync(new Student { Id = profile.ProfileId }, pageNumber, pageSize);
        return Ok(result);
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestWorksheetsAsync(int pageNumber = 1, int pageSize = 10)
    {
        var result = await _examService.GetLatestWorksheetsAsync(pageNumber, pageSize);
        return Ok(result);
    }


    [Authorize(Roles = "Student,Teacher")]
    [HttpGet("list")]
    public async Task<IActionResult> GetWorksheetsAsync(
        int? id = 0,
        string? search = null,
        [FromQuery] List<int>? subjectIds = null,
        [FromQuery] List<int>? gradeIds = null,
        int pageNumber = 1,
        int pageSize = 10,
        int bookTestId = 0)
    {
        var filterDto = new ExamFilterDto
        {
            id = id,
            search = search,
            subjectIds = subjectIds,
            gradeIds = gradeIds,
            pageNumber = pageNumber,
            pageSize = pageSize,
            bookTestId = bookTestId
        };

        Paged<WorksheetDto> result = null;
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if (profile == null)
        {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }
        if (profile.Role == UserRole.Student.ToString())
        {
            result = await _examService.GetWorksheetsForStudentsAsync(filterDto, profile);
        }
        else if (profile.Role == UserRole.Teacher.ToString())
        {
            result = await _examService.GetWorksheetsForTeacherAsync(filterDto, profile);
        }
        return Ok(result);
    }

    // 🟢 GET /api/exam/questions - Sınav için soruları getir
    [HttpGet("questions")]
    public async Task<IActionResult> GetExamQuestions()
    {
        var questions = await _examService.GetExamQuestionsAsync();
        return Ok(questions);
    }

    // // 🟢 POST /api/exam/submit-answer - Öğrencinin cevabını kaydet
    // [HttpPost("submit-answer")]
    // public async Task<IActionResult> SubmitAnswer([FromBody] SubmitAnswerDto dto)
    // {
    //     await _context.SaveChangesAsync();

    //     return Ok(new { message = "Cevap başarıyla kaydedildi." });
    // }

    [Authorize]
    [HttpPost("start-test/{testId}")]
    public async Task<IActionResult> StartTest(int testId)
    {
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if (profile == null)
        {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }

        try
        {
            var result = await _examService.StartTestAsync(testId, new Student { Id = profile.ProfileId });
            if (result == null)
                return NotFound(new { message = "Test bulunamadı!" });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("test-instance/{testInstanceId}")]
    public async Task<IActionResult> GetTestInstanceQuestions(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();

        var result = await _examService.GetTestInstanceQuestionsAsync(testInstanceId, user.Id);

        if (result == null)
            return NotFound(new { message = "Test bulunamadı!" });

        return Ok(result);
    }



    [HttpGet("test-canvas-instance-result/{testInstanceId}")]
    public async Task<IActionResult> GetTestCanvasInstanceResults(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _examService.GetCanvasTestResultAsync(testInstanceId, user.Id, true);
        return Ok(response);
    }


    [HttpGet("test-canvas-instance/{testInstanceId}")]
    public async Task<IActionResult> GetTestCanvasInstanceQuestions(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _examService.GetCanvasTestResultAsync(testInstanceId, user.Id);
        return Ok(response);
    }


    [Authorize]
    [HttpPost("save-answer")]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDto dto)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _examService.SaveAnswer(dto, user);
        return Ok(response);
    }

    [Authorize]
    [HttpPut("end-test/{testInstanceId}")]
    public async Task<IActionResult> EndTest(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _examService.EndTest(testInstanceId, user.Id);
        return Ok(response);
    }
    [HttpPost]
    public async Task<IActionResult> CreateOrUpdateAsync([FromBody] ExamDto examDto)
    {
        var response = await _examService.CreateOrUpdateAsync(examDto, 0);
        return Ok(response);
    }

    [HttpPost("bulk-import")]
    [Authorize]
    public async Task<IActionResult> BulkImportExams([FromBody] BulkExamCreateDto bulkExamDto)
    {
        try
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _examService.CreateBulkExamsAsync(bulkExamDto, user.Id);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new BulkExamResultDto
            {
                Success = false,
                Message = $"Bulk import failed: {ex.Message}",
                TotalProcessed = bulkExamDto.Exams.Count,
                SuccessCount = 0,
                FailureCount = bulkExamDto.Exams.Count
            });
        }
    }


    [HttpGet("student/statistics")]
    public async Task<IActionResult> GetGroupedStudentStatistics()
    {
        var user = await GetAuthenticatedUserAsync();
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if (profile == null)
        {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }
        var result = await _examService.GetGroupedStudentStatistics(profile.ProfileId);
        return Ok(result);
    }

    [HttpGet("grades")]
    public async Task<IActionResult> GetGrades()
    {
        var grades = await _examService.GetGradesAsync();
        return Ok(grades);
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteWorksheet(int id)
    {
        try
        {
            var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (profile == null)
            {
                return BadRequest("Öğrenci Bilgisine ulaşılamadı");
            }
            var result = await _examService.DeleteWorksheetAsync(id, profile.ProfileId);

            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(new { message = result.Message, success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Bir hata oluştu: " + ex.Message, success = false });
        }
    }

}
