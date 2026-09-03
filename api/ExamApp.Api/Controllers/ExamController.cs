using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Data;
using ExamApp.Api.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Globalization;
using ExamApp.Api.Helpers;
using ExamApp.Api.Controllers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;

[Route("api/worksheet")]
[ApiController]
public class ExamController : BaseController
{


    private readonly IExamService _examService;
    private readonly IStudentService _studentService;
    private readonly IWorksheetAssignmentService _assignmentService;
    private readonly ITestSessionService _testSession;
    private readonly IWorksheetAuthoringService _authoring;
    private readonly IWorksheetDetailService _worksheetDetail;
    private readonly IWorksheetReminderService _reminderService;
    public ExamController(IMinIoService minioService, IExamService examService,
            IStudentService studentService,
            IWorksheetAssignmentService assignmentService,
            ITestSessionService testSession,
            IWorksheetAuthoringService authoring,
            IWorksheetDetailService worksheetDetail,
            IWorksheetReminderService reminderService
            )
        : base()
    {
        _examService = examService;
        _studentService = studentService;
        _assignmentService = assignmentService;
        _testSession = testSession;
        _authoring = authoring;
        _worksheetDetail = worksheetDetail;
        _reminderService = reminderService;
    }

    [HttpGet("{id:int}/reminder")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetWorksheetReminder(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var student = await _studentService.GetStudentProfile(user.Id);
        if (student == null)
            return Unauthorized("Öğrenci profili bulunamadı");

        // Frontend Observable<WorksheetReminderDto | null> bekliyor: yok durumunda da 200 + null.
        var result = await _reminderService.GetAsync(id, student.Id, ct);
        return Ok(result);
    }

    [HttpPut("{id:int}/reminder")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> UpsertWorksheetReminder(int id, [FromBody] UpsertWorksheetReminderRequestDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var student = await _studentService.GetStudentProfile(user.Id);
        if (student == null)
            return Unauthorized("Öğrenci profili bulunamadı");

        try
        {
            var result = await _reminderService.UpsertAsync(id, student.Id, user.KeycloakId, request.ScheduledFor, request.RemindBeforeMinutes, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}/reminder")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> DeleteWorksheetReminder(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var student = await _studentService.GetStudentProfile(user.Id);
        if (student == null)
            return Unauthorized("Öğrenci profili bulunamadı");

        await _reminderService.DeleteAsync(id, student.Id, ct);
        return NoContent();
    }


    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetWorksheet(int id)
    {
        var result = await _examService.GetWorksheetByIdAsync(id);
        if (result == null)
            return NotFound();

        return Ok(result);
    }


    [HttpGet("{id:int}/detail")]
    [Authorize(Roles = "Student,Teacher")]
    public async Task<IActionResult> GetWorksheetDetail(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        int? studentId = null;
        if (user.Role == UserRole.Student.ToString())
        {
            var student = await _studentService.GetStudentProfile(user.Id);
            studentId = student?.Id;
        }

        var result = await _worksheetDetail.GetWorksheetDetailAsync(id, user.Role, studentId, user.Id, ct);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    [HttpPost("from-mistakes/{instanceId:int}")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> CreateWorksheetFromMistakes(int instanceId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        var student = await _studentService.GetStudentProfile(user.Id);
        if (student == null)
        {
            return Unauthorized("Öğrenci profili bulunamadı");
        }

        try
        {
            var result = await _worksheetDetail.CreateWorksheetFromMistakesAsync(instanceId, student.Id, user.Id, ct);
            if (result == null)
                return NotFound(new { message = "Test oturumu bulunamadı." });

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("student-worksheets")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetWorksheetAndInstancessAsync(int gradeId)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        var student = await _studentService.GetStudentProfile(user.Id);

        var result = await _examService.GetWorksheetAndInstancesAsync(student, gradeId);
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpPost("assignments")]
    public async Task<IActionResult> AssignWorksheet([FromBody] WorksheetAssignmentRequestDto request)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _assignmentService.AssignWorksheetAsync(request, user.Id);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [Authorize(Roles = "Student")]
    [HttpGet("assignments/active")]
    public async Task<IActionResult> GetActiveAssignments()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        var student = await _studentService.GetStudentProfile(user.Id);
        var assignments = await _assignmentService.GetActiveAssignmentsForStudentAsync(student);
        return Ok(assignments);
    }

    [Authorize(Roles = "Teacher")]
    [HttpGet("{id}/assignments/overview")]
    public async Task<IActionResult> GetAssignmentsOverview(int id)
    {
        var user = await GetAuthenticatedUserAsync();
        var overview = await _assignmentService.GetWorksheetAssignmentsForTeacherAsync(id, user.Id);
        return Ok(overview);
    }


    [HttpGet("CompletedTests")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetCompletedTests(int pageNumber = 1, int pageSize = 10)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        var student = await _studentService.GetStudentProfile(user.Id);
        var result = await _testSession.GetCompletedTestsAsync(student, pageNumber, pageSize);
        return Ok(result);
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestWorksheetsAsync(int pageNumber = 1, int pageSize = 10)
    {
        var result = await _examService.GetLatestWorksheetsAsync(pageNumber, pageSize);
        return Ok(result);
    }


    [Authorize(Roles = "Student,Teacher")]
    [HttpGet("popular")]
    public async Task<IActionResult> GetPopularWorksheetsAsync(int? gradeId = null, int pageNumber = 1, int pageSize = 10, int sinceDays = 30)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        // Öğrenci ise kendi sınıfına göre filtrele (istekte gradeId gelmediyse)
        int? effectiveGradeId = gradeId;
        if (effectiveGradeId == null && user.Role == UserRole.Student.ToString())
        {
            var student = await _studentService.GetStudentProfile(user.Id);
            effectiveGradeId = student?.GradeId;
        }

        var result = await _examService.GetPopularWorksheetsAsync(effectiveGradeId, pageNumber, pageSize, sinceDays);
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
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }
        if (user.Role == UserRole.Student.ToString())
        {
            var student = await _studentService.GetStudentProfile(user.Id);
            result = await _examService.GetWorksheetsForStudentsAsync(filterDto, student);
        }
        else if (user.Role == UserRole.Teacher.ToString())
        {
            // var teacher = await _teacherService.GetTeacherProfile(user.Id);
            result = await _examService.GetWorksheetsForTeacherAsync(filterDto, user);
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
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        var student = await _studentService.GetStudentProfile(user.Id);


        try
        {
            var result = await _testSession.StartTestAsync(testId, student);
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

        var result = await _testSession.GetTestInstanceQuestionsAsync(testInstanceId, user.Id);

        if (result == null)
            return NotFound(new { message = "Test bulunamadı!" });

        return Ok(result);
    }



    [HttpGet("test-canvas-instance-result/{testInstanceId}")]
    public async Task<IActionResult> GetTestCanvasInstanceResults(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _testSession.GetCanvasTestResultAsync(testInstanceId, user.Id, true);
        return Ok(response);
    }


    [HttpGet("test-canvas-instance/{testInstanceId}")]
    public async Task<IActionResult> GetTestCanvasInstanceQuestions(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _testSession.GetCanvasTestResultAsync(testInstanceId, user.Id);
        return Ok(response);
    }


    [Authorize]
    [HttpPost("save-answer")]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDto dto)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _testSession.SaveAnswer(dto, user);
        return Ok(response);
    }

    [Authorize]
    [HttpPut("end-test/{testInstanceId}")]
    public async Task<IActionResult> EndTest(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();
        var response = await _testSession.EndTest(testInstanceId, user.Id);
        return Ok(response);
    }
    [HttpPost]
    public async Task<IActionResult> CreateOrUpdateAsync([FromBody] ExamDto examDto)
    {
        var response = await _authoring.CreateOrUpdateAsync(examDto, 0);
        return Ok(response);
    }

    [HttpPost("bulk-import")]
    [Authorize]
    public async Task<IActionResult> BulkImportExams([FromBody] BulkExamCreateDto bulkExamDto)
    {
        try
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _authoring.CreateBulkExamsAsync(bulkExamDto, user.Id);
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
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }
        var student = await _studentService.GetStudentProfile(user.Id);


        var result = await _examService.GetGroupedStudentStatistics(student.Id);
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
            var user = await GetAuthenticatedUserAsync();
            if (user == null)
            {
                return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
            }
            var result = await _authoring.DeleteWorksheetAsync(id, user.Id);

            if (!result.Success)
            {
                if (result.NotFound)
                {
                    return NotFound(result.Message);
                }
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

    [HttpPut("{id}/background-image")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateWorksheetBackgroundImage(int id, [FromForm] IFormFile file)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
        }

        var result = await _authoring.UpdateWorksheetBackgroundImageAsync(id, file, user.Id);
        if (!result.Success)
        {
            if (result.NotFound)
            {
                return NotFound(result);
            }
            return BadRequest(result);
        }

        return Ok(result);
    }

}
