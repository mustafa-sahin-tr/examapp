using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Data;
using ExamApp.Api.Models;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

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
    [HttpGet]    
    public async Task<IActionResult> GetTestsAsync(int gradeId)
    {
        var tests = await _context.Tests
            .Where(t => t.GradeId == gradeId)
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

    [HttpPost("start-test")]
    public async Task<IActionResult> StartTest([FromBody] StartTestDto dto)
    {
        // Öğrenci bu testi daha önce başlatmış mı kontrol et
        var existingInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.StudentId == dto.StudentId && ti.TestId == dto.TestId && ti.EndTime == null);

        if (existingInstance != null)
        {
            return BadRequest(new { message = "Bu testi zaten başlattınız ve devam ediyorsunuz!" });
        }

        var testInstance = new TestInstance
        {
            StudentId = dto.StudentId,
            TestId = dto.TestId,
            StartTime = DateTime.UtcNow,
            TestInstanceQuestions = new List<TestInstanceQuestion>()
        };

        // Teste ait soruları TestQuestion tablosundan çekiyoruz
        var testQuestions = await _context.TestQuestions
            .Where(tq => tq.TestId == dto.TestId)
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
        var testInstance = await _context.TestInstances
            .Include(ti => ti.Test)
            .Include(ti => ti.TestInstanceQuestions)
                .ThenInclude(tiq => tiq.TestQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId);

        if (testInstance == null)
        {
            return NotFound(new { message = "Test bulunamadı!" });
        }

        var response = new
        {
            Id = testInstance.Id,
            TestName = testInstance.Test.Name,
            TestInstanceQuestions = testInstance.TestInstanceQuestions.Select(tiq => new
            {
                Id = tiq.Id,
                Question = new {
                    tiq.TestQuestion.Question.Id,
                    tiq.TestQuestion.Question.Text,
                    tiq.TestQuestion.Question.SubText,
                    tiq.TestQuestion.Question.ImageUrl,
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


    [HttpPost("save-answer")]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDto dto)
    {
        var testInstanceQuestion = await _context.TestInstanceQuestions
            .FirstOrDefaultAsync(tiq => tiq.TestInstanceId == dto.TestInstanceId && tiq.TestQuestionId == dto.TestQuestionId);

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



}
