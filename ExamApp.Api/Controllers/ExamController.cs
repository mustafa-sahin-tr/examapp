using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Data;
using ExamApp.Api.Models;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Route("api/worksheet")]
[ApiController]
public class ExamController : ControllerBase
{
    private readonly AppDbContext _context;

    public ExamController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetWorksheet(int id)
    {
        var test = await _context.Worksheets  
            .Where(t => t.Id == id)          
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.IsPracticeTest,
                t.MaxDurationSeconds,
                t.GradeId                
            })
            .FirstOrDefaultAsync();

        if (test == null) return NotFound();
        return Ok(test);
    }

    // 🟢 GET /api/exam/tests - Sınıfa ait sınavları getir
    [Authorize]
    [HttpGet]    
    public async Task<IActionResult> GetWorksheetAndInstancessAsync(int gradeId)
    {
         // 🔹 Token’dan UserId'yi al
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null)
        {
            return BadRequest("User ID claim not found.");
        }
        var userId = int.Parse(userIdClaim);

        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Role != UserRole.Student)
        {
            return BadRequest("Invalid User ID or User is not a Student.");
        }

        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);

        var tests = await _context.Worksheets
            .Where(t => t.GradeId == student.GradeId)
            .ToListAsync();

        var testInstances = await _context.TestInstances
            .Where(ti => ti.StudentId == student.Id)
            .ToListAsync();

        var response = (from test in _context.Worksheets
                join testInstance in _context.TestInstances
                on test.Id equals testInstance.WorksheetId into testGroup
                from tg in testGroup.DefaultIfEmpty()
                where test.GradeId == student.GradeId
                select new
                {
                    test.Id,
                    test.Name,
                    test.Description,
                    test.MaxDurationSeconds,
                    TotalQuestions = test.WorksheetQuestions.Count,
                    InstanceStatus = tg != null ? (int?)tg.Status : -1,
                    StartTime = tg != null ? tg.StartTime : (DateTime?)null,
                    TestInstanceId = tg != null ? tg.Id : -1
                }).ToList();
        
        return Ok(response);
    }

    // 🟢 GET /api/exam/tests - Sınıfa ait sınavları getir
    [Authorize]
    [HttpGet("list")]    
    public async Task<IActionResult> GetWorksheetsAsync(int gradeId)
    {
        var tests = await _context.Worksheets  
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.IsPracticeTest,
                t.MaxDurationSeconds,
                t.GradeId                
            })          
            .ToListAsync();

        return Ok(tests);
    }


    // 🟢 GET /api/exam/questions - Sınav için soruları getir
    [HttpGet("questions")]
    public async Task<IActionResult> GetExamQuestions()
    {
        var questions = await _context.Questions
            .Include(q => q.Subject)
            .Select(q => new
            {
                q.Id,
                q.Text,
                q.SubText,
                q.ImageUrl,
                CategoryName = q.Subject.Name,
                q.Point
            }).ToListAsync();

        return Ok(questions);
    }

    // 🟢 POST /api/exam/submit-answer - Öğrencinin cevabını kaydet
    [HttpPost("submit-answer")]
    public async Task<IActionResult> SubmitAnswer([FromBody] SubmitAnswerDto dto)
    {
        // var answer = new AnswerRecord
        // {
        //     QuestionId = dto.QuestionId,
        //     UserId = 1,  // TODO: Auth'dan al
        //     SelectedAnswer = dto.SelectedAnswer,
        //     TimeSpent = dto.TimeSpent,
        //     CreatedAt = DateTime.UtcNow
        // };

        // _context.AnswerRecords.Add(answer);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cevap başarıyla kaydedildi." });
    }

    [Authorize]
    [HttpPost("start-test/{testId}")]
    public async Task<IActionResult> StartTest(int testId)
    {
        // 🔹 Token’dan UserId'yi al
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null)
        {
            return BadRequest("User ID claim not found.");
        }
        var userId = int.Parse(userIdClaim);

        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Role != UserRole.Student)
        {
            return BadRequest("Invalid User ID or User is not a Student.");
        }

        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);

        if (student == null)
        {
            return BadRequest("Öğrenci bulunamadı!");
        }

        // Öğrenci bu testi daha önce başlatmış mı kontrol et
        var existingInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.StudentId == student.Id && ti.WorksheetId == testId
                && ti.EndTime == null
                && ti.Student.UserId == userId );

        if (existingInstance != null)
        {
            return BadRequest(new { message = "Bu testi zaten başlattınız ve devam ediyorsunuz!" });
        }

        var testInstance = new WorksheetInstance
        {
            StudentId = student.Id,
            WorksheetId = testId,
            StartTime = DateTime.UtcNow,
            WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>(),
            Status = WorksheetInstanceStatus.Started
        };

        // Teste ait soruları TestQuestion tablosundan çekiyoruz
        var testQuestions = await _context.TestQuestions
            .Where(tq => tq.TestId == testId)
            .OrderBy(tq => tq.Order)
            .ToListAsync();

        foreach (var tq in testQuestions)
        {
            testInstance.WorksheetInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetQuestionId = tq.Id,
                IsCorrect = false,
                TimeTaken = 0
            });
        }

        _context.TestInstances.Add(testInstance);
        await _context.SaveChangesAsync();

        return Ok(new { testInstanceId = testInstance.Id, message = "Test başlatıldı!" });
    }


    [HttpGet("test-instance/{testInstanceId}")]
    public async Task<IActionResult> GetTestInstanceQuestions(int testInstanceId)
    {
        // 🔹 Token’dan UserId'yi al
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null)
        {
            return Unauthorized();
        }        
        // 🔹 Token’dan UserId'yi al
        var userId = int.Parse(userIdClaim);
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Role != UserRole.Student)
        {
            return BadRequest("Invalid User ID or User is not a Student.");
        }

        var testInstance = await _context.TestInstances
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Passage)
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == userId);

        if (testInstance == null)
        {
            return NotFound(new { message = "Test bulunamadı!" });
        }        

        var response = new
        {
            Id = testInstance.Id,
            TestName = testInstance.Worksheet.Name,            
            Status = testInstance.Status,
            MaxDurationSeconds = testInstance.Worksheet.MaxDurationSeconds,
            testInstance.Worksheet.IsPracticeTest,
            TestInstanceQuestions = testInstance.WorksheetInstanceQuestions.Select(tiq => new
            {
                Id = tiq.Id,
                Order = tiq.WorksheetQuestion.Order,                
                Question = new {
                    tiq.WorksheetQuestion.Question.Id,
                    tiq.WorksheetQuestion.Question.Text,
                    tiq.WorksheetQuestion.Question.SubText,
                    tiq.WorksheetQuestion.Question.ImageUrl,
                    tiq.WorksheetQuestion.Question.IsExample,
                    Passage = tiq.WorksheetQuestion.Question.PassageId.HasValue ? new {
                        tiq.WorksheetQuestion.Question.Passage?.Id,
                        tiq.WorksheetQuestion.Question.Passage?.Title,
                        tiq.WorksheetQuestion.Question.Passage?.Text,
                        tiq.WorksheetQuestion.Question.Passage?.ImageUrl
                    } : null,
                    tiq.WorksheetQuestion.Question.PracticeCorrectAnswer,
                    tiq.WorksheetQuestion.Question.AnswerColCount,
                    Answers = tiq.WorksheetQuestion.Question.Answers.Select(a => new
                    {
                        a.Id,
                        a.Text,                        
                        a.ImageUrl
                    }).ToList() 
                } ,
                SelectedAnswerId = tiq.SelectedAnswerId // Önceden seçilen cevap
            }).ToList()
        };

        return Ok(response);
    }    

    [Authorize]
    [HttpPost("save-answer")]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDto dto)
    {
        // 🔹 Token’dan UserId'yi al
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Role != UserRole.Student)
        {
            return BadRequest("Invalid User ID or User is not a Student.");
        }

        var testInstanceQuestion = await _context.TestInstanceQuestions
            .FirstOrDefaultAsync(tiq => tiq.WorksheetInstanceId == dto.TestInstanceId && 
                tiq.Id == dto.TestQuestionId
                && tiq.WorksheetInstance.Student.UserId == userId);

        if (testInstanceQuestion == null)
        {
            return NotFound(new { message = "Soru bulunamadı!" });
        }

        testInstanceQuestion.SelectedAnswerId = dto.SelectedAnswerId;
        // testInstanceQuestion.IsCorrect = await _context.Questions
        //     .Where(q => q.Id == dto.QuestionId)
        //     .Select(q => q.CorrectAnswer == dto.SelectedAnswerId)
        //     .FirstOrDefaultAsync();

        testInstanceQuestion.TimeTaken = dto.TimeTaken;

        _context.TestInstanceQuestions.Update(testInstanceQuestion);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cevap kaydedildi!" });
    }    

    [Authorize]
    [HttpPut("end-test/{testInstanceId}")]
    public async Task<IActionResult> EndTest(int testInstanceId)
    {
        // 🔹 Token’dan UserId'yi al
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Role != UserRole.Student)
        {
            return BadRequest("Invalid User ID or User is not a Student.");
        }

        var testInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == userId);

        if (testInstance == null)
            return NotFound(new { message = "Test bulunamadı." });

        if (testInstance.Status != WorksheetInstanceStatus.Started)
            return BadRequest(new { message = $"Bu test zaten {testInstance.Status} durumunda." });

        testInstance.Status = WorksheetInstanceStatus.Completed;
        testInstance.EndTime = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Test başarıyla tamamlandı." });
    }


    [HttpPost]
    public async Task<IActionResult> CreateOrUpdateQuestion([FromBody] ExamDto examDto)
    {
        try
        {
            Worksheet examination;

            // 📌 Eğer ID varsa, veritabanından o soruyu bulup güncelle
            if (examDto.Id > 0)
            {
                examination = await _context.Worksheets.FindAsync(examDto.Id);

                if (examination == null)
                {
                    return NotFound(new { error = "Test bulunamadı!" });
                }                

                examination.Name = examDto.Name;
                examination.Description = examDto.Description;
                examination.GradeId = examDto.GradeId;
                examination.MaxDurationSeconds = examDto.MaxDurationSeconds;
                examination.IsPracticeTest = examDto.IsPracticeTest;
                _context.Worksheets.Update(examination);                
            }
            else
            {
                // 📌 Yeni Soru Oluştur (INSERT)
                examination = new Worksheet
                {
                    Name = examDto.Name,
                    Description = examDto.Description,
                    GradeId = examDto.GradeId,
                    MaxDurationSeconds = examDto.MaxDurationSeconds,
                    IsPracticeTest = examDto.IsPracticeTest
                };

                _context.Worksheets.Add(examination);
            }

            // 📌 Değişiklikleri Kaydet
            await _context.SaveChangesAsync();

            return Ok(new { message = examDto.Id > 0 ? "Test başarıyla güncellendi!" : "Test başarıyla kaydedildi!", examId = examination.Id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

}
