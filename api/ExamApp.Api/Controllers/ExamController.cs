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
    [Authorize(Roles ="Student")]
    public async Task<IActionResult> GetWorksheetAndInstancessAsync(int gradeId)
    {
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if(profile == null) {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }        
        var result = await _examService.GetWorksheetAndInstancesAsync(new Student{Id = profile.ProfileId}, gradeId);
        return Ok(result);
    }


    [HttpGet("CompletedTests")]    
    [Authorize(Roles ="Student")]
    public async Task<IActionResult> GetCompletedTests(int pageNumber = 1, int pageSize = 10)
    {
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if(profile == null) {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        }        
        var result = await _examService.GetCompletedTestsAsync(new Student{Id = profile.ProfileId}, pageNumber, pageSize);
        return Ok(result);
    }

    [HttpGet("latest")]    
    public async Task<IActionResult> GetLatestWorksheetsAsync(int pageNumber = 1, int pageSize = 10)
    {
        var result = await _examService.GetLatestWorksheetsAsync(pageNumber, pageSize);
        return Ok(result);
    }


    [Authorize(Roles ="Student,Teacher")]
    [HttpGet("list")]
    public async Task<IActionResult> GetWorksheetsAsync(
        int? id = 0,
        string? search = null,
        [FromQuery] List<int>? subjectIds = null,
        int? gradeId = null,
        int pageNumber = 1,
        int pageSize = 10,
        int bookTestId = 0)
    {
        var filterDto = new ExamFilterDto
        {
            id = id,
            search = search,
            subjectIds = subjectIds,
            gradeId = gradeId,
            pageNumber = pageNumber,
            pageSize = pageSize,
            bookTestId = bookTestId
        };

        var user = await GetAuthenticatedUserAsync();         
        Paged<WorksheetDto> result = null;
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if(profile == null) {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        } 
        if (user.Role == UserRole.Student) {
            result = await _examService.GetWorksheetsForStudentsAsync(filterDto, user, new Student{Id = profile.ProfileId});
        }
        else if(user.Role == UserRole.Teacher) {
            result = await _examService.GetWorksheetsForTeacherAsync(filterDto,user,new Teacher{Id = profile.ProfileId});
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
        if(profile == null) {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        } 

        try
        {
            var result = await _examService.StartTestAsync(testId, new Student{Id = profile.ProfileId});
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
        var response = await _examService.SaveAnswer(dto, user.Id);
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
    
    
    [HttpGet("student/statistics")]
    public async Task<IActionResult> GetGroupedStudentStatistics()
    {
        var user = await GetAuthenticatedUserAsync();
        var profile = await _userProfileCacheService.GetAsync(KeyCloakId);
        if(profile == null) {
            return BadRequest("Öğrenci Bilgisine ulaşılamadı");
        } 
        var result = await _examService.GetGroupedStudentStatistics(profile.ProfileId);
        return Ok(result);
    }

}
