using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Data;
using ExamApp.Api.Models;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Route("api/exam")]
[ApiController]
public class ExamController : ControllerBase
{
    private readonly AppDbContext _context;

    public ExamController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("test/{testId}")]
    public async Task<IActionResult> GetTestWithAnswers(int testId, int studentId)
    {
        var test = await _context.TestQuestions
            .Include(t => t.Question).ThenInclude(q => q.Subject)        
            .Include(t => t.Test)
            .Where(t => t.TestId == testId)
            .Select(t => new
            {
                t.Test.Id,
                t.Question.Subject.Name,
                t.Question,
                // PreviousAnswers = _context.AnswerRecords
                //     .Where(a => a. == studentId && a.TestId == testId)
                //     .ToDictionary(a => a.QuestionId, a => a.SelectedOption)
            })
            .FirstOrDefaultAsync();

        if (test == null) return NotFound();
        return Ok(test);
    }

    // 🟢 GET /api/exam/tests - Sınıfa ait sınavları getir
    [Authorize]
    [HttpGet]    
    public async Task<IActionResult> GetTestsAsync(int gradeId)
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

        var tests = await _context.Tests
            .Where(t => t.GradeId == student.GradeId)
            .ToListAsync();

        var testInstances = await _context.TestInstances
            .Where(ti => ti.StudentId == student.Id)
            .ToListAsync();

        var response = (from test in _context.Tests
                join testInstance in _context.TestInstances
                on test.Id equals testInstance.TestId into testGroup
                from tg in testGroup.DefaultIfEmpty()
                where test.GradeId == student.GradeId
                select new
                {
                    test.Id,
                    test.Name,
                    test.Description,
                    test.MaxDurationSeconds,
                    TotalQuestions = test.TestQuestions.Count,
                    InstanceStatus = tg != null ? (int?)tg.Status : -1,
                    StartTime = tg != null ? tg.StartTime : (DateTime?)null,
                    TestInstanceId = tg != null ? tg.Id : -1
                }).ToList();
        
        return Ok(response);
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
            .FirstOrDefaultAsync(ti => ti.StudentId == student.Id && ti.TestId == testId
                && ti.EndTime == null
                && ti.Student.UserId == userId );

        if (existingInstance != null)
        {
            return BadRequest(new { message = "Bu testi zaten başlattınız ve devam ediyorsunuz!" });
        }

        var testInstance = new TestInstance
        {
            StudentId = student.Id,
            TestId = testId,
            StartTime = DateTime.UtcNow,
            TestInstanceQuestions = new List<TestInstanceQuestion>(),
            Status = TestInstanceStatus.Started
        };

        // Teste ait soruları TestQuestion tablosundan çekiyoruz
        var testQuestions = await _context.TestQuestions
            .Where(tq => tq.TestId == testId)
            .OrderBy(tq => tq.Order)
            .ToListAsync();

        foreach (var tq in testQuestions)
        {
            testInstance.TestInstanceQuestions.Add(new TestInstanceQuestion
            {
                TestQuestionId = tq.Id,
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
            .Include(ti => ti.Test)
            .Include(ti => ti.TestInstanceQuestions)
                .ThenInclude(tiq => tiq.TestQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .Include(ti => ti.TestInstanceQuestions)
                .ThenInclude(tiq => tiq.TestQuestion)
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
            TestName = testInstance.Test.Name,            
            Status = testInstance.Status,
            MaxDurationSeconds = testInstance.Test.MaxDurationSeconds,
            testInstance.Test.IsPracticeTest,
            TestInstanceQuestions = testInstance.TestInstanceQuestions.Select(tiq => new
            {
                Id = tiq.Id,
                Order = tiq.TestQuestion.Order,                
                Question = new {
                    tiq.TestQuestion.Question.Id,
                    tiq.TestQuestion.Question.Text,
                    tiq.TestQuestion.Question.SubText,
                    tiq.TestQuestion.Question.ImageUrl,
                    tiq.TestQuestion.Question.IsExample,
                    Passage = tiq.TestQuestion.Question.PassageId.HasValue ? new {
                        tiq.TestQuestion.Question.Passage?.Id,
                        tiq.TestQuestion.Question.Passage?.Title,
                        tiq.TestQuestion.Question.Passage?.Text,
                        tiq.TestQuestion.Question.Passage?.ImageUrl
                    } : null,
                    tiq.TestQuestion.Question.PracticeCorrectAnswer,
                    tiq.TestQuestion.Question.AnswerColCount,
                    Answers = tiq.TestQuestion.Question.Answers.Select(a => new
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
            .FirstOrDefaultAsync(tiq => tiq.TestInstanceId == dto.TestInstanceId && 
                tiq.TestQuestionId == dto.TestQuestionId
                && tiq.TestInstance.Student.UserId == userId);

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

        if (testInstance.Status != TestInstanceStatus.Started)
            return BadRequest(new { message = $"Bu test zaten {testInstance.Status} durumunda." });

        testInstance.Status = TestInstanceStatus.Completed;
        testInstance.EndTime = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Test başarıyla tamamlandı." });
    }



}
