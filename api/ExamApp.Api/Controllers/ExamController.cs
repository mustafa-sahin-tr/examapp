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

[Route("api/worksheet")]
[ApiController]
public class ExamController : BaseController
{
    private readonly ImageHelper _imageHelper;

    private readonly IMinIoService _minioService;
    public ExamController(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService)
        : base(context)
    {
        _imageHelper = imageHelper;
        _minioService = minioService;
    }
    

    [HttpGet("{id}")]
    public async Task<IActionResult> GetWorksheet(int id)
    {
        var test = await _context.Worksheets  
            .Where(t => t.Id == id)
            .Select(t => new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest != null ? t.BookTest.BookId : null,
                QuestionCount = t.WorksheetQuestions.Count() // ✅ Soru sayısını ekledik
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
        var student = await GetAuthenticatedStudentAsync();
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
                join tiq in _context.TestInstanceQuestions on tg.Id equals tiq.WorksheetInstanceId 
                where test.GradeId == student.GradeId
                select new
                {
                    test.Id,
                    test.Name,
                    test.Description,
                    test.ImageUrl,
                    tg.EndTime,
                    
                    test.MaxDurationSeconds,
                    TotalQuestions = test.WorksheetQuestions.Count,
                    InstanceStatus = tg != null ? (int?)tg.Status : -1,
                    StartTime = tg != null ? tg.StartTime : (DateTime?)null,
                    TestInstanceId = tg != null ? tg.Id : -1
                }).ToList();
        
        return Ok(response);
    }

    [HttpGet("CompletedTests")]    
    [AuthorizeRole(UserRole.Student)]
    public async Task<IActionResult> GetCompletedTests(int pageNumber = 1, int pageSize = 10)
    {
        var query = await _context.TestInstances
            .Where(wi => wi.StudentId == AuthenticatedStudentId && wi.Status == WorksheetInstanceStatus.Completed)
            .Select(wi => new
            {
                wi.Id,
                wi.Worksheet.Name,
                wi.Worksheet.ImageUrl,
                wi.EndTime,
                wi.StartTime,
                TotalQuestions = wi.WorksheetInstanceQuestions.Count(),

                // Doğru cevapları hesapla (SelectedAnswerId == CorrectAnswerId)
                CorrectAnswers = wi.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    wi.Worksheet.WorksheetQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId 
                        && wq.Question.CorrectAnswerId == wiq.SelectedAnswerId)
                ),

                // Yanlış cevapları hesapla
                WrongAnswers = wi.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    wi.Worksheet.WorksheetQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId 
                        && wq.Question.CorrectAnswerId != wiq.SelectedAnswerId)
                )
            })
            .ToListAsync(); // 🔥 Burada `ToListAsync()` çağırarak veriyi hafızaya alıyoruz.

        // 🔥 EF Core ile `DurationMinutes` hesaplaması yapılmadığı için C# tarafında işliyoruz.
        var results = query.Select(wi => new CompletedTestDto
        {
            Id = wi.Id,
            Name = wi.Name,
            ImageUrl = wi.ImageUrl,
            CompletedDate = wi.EndTime ?? DateTime.UtcNow,

            // ✅ C# tarafında farkı hesaplıyoruz.
            DurationMinutes = wi.EndTime.HasValue ? 
                (int)(wi.EndTime.Value - wi.StartTime).TotalMinutes : 0,

            TotalQuestions = wi.TotalQuestions,
            CorrectAnswers = wi.CorrectAnswers,
            WrongAnswers = wi.WrongAnswers,
            Score = (wi.CorrectAnswers * 100) / (wi.TotalQuestions > 0 ? wi.TotalQuestions : 1) // Bölme hatası olmaması için
        }).OrderByDescending(wi => wi.CompletedDate)
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .ToList();

        return Ok(new { totalCount = results.Count, items = results, pageNumber, pageSize });
    }

    [HttpGet("latest")]    
    public async Task<IActionResult> GetLatestWorksheetsAsync(int pageNumber = 1, int pageSize = 10)
    {
         // 🔹 Token’dan UserId'yi al
        var user = await GetAuthenticatedUserAsync();

        var query = _context.Worksheets.AsQueryable();

        var tests = await query
            .OrderByDescending(t => t.CreateTime) // Sıralama için
            .Skip((pageNumber - 1) * pageSize) // Sayfalama için
            .Take(pageSize)            
            .Select(t => new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest != null ? t.BookTest.BookId : null,
                QuestionCount = t.WorksheetQuestions.Count() // ✅ Soru sayısını ekledik
            })
            .ToListAsync();      

        return Ok(tests);  
    }


    [HttpGet("list")]    
    public async Task<IActionResult> GetWorksheetsAsync(string? search = null, int pageNumber = 1, int pageSize = 10)
    {
         // 🔹 Token’dan UserId'yi al
        var user = await GetAuthenticatedUserAsync();

        var query = _context.Worksheets.AsQueryable();
        if (!string.IsNullOrEmpty(search))
        {
            string normalizedSearch = search.ToLower(new CultureInfo("tr-TR"));
            query = query.Where(t => 
                EF.Functions.Like(t.Name.ToLower(), $"%{normalizedSearch}%") ||
                (t.Subtitle != null && EF.Functions.Like(t.Subtitle.ToLower(), $"%{normalizedSearch}%")) ||
                EF.Functions.Like(t.Description.ToLower(), $"%{normalizedSearch}%")
            );
        }

        var totalCount = await query.CountAsync(); // Toplam kayıt sayısı

        var tests = await query
            .OrderBy(t => t.Name) // Sıralama için
            .Skip((pageNumber - 1) * pageSize) // Sayfalama için
            .Take(pageSize)            
            .Select(t => new WorksheetDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                GradeId = t.GradeId,
                SubjectId = t.SubjectId,
                MaxDurationSeconds = t.MaxDurationSeconds,
                IsPracticeTest = t.IsPracticeTest,
                Subtitle = t.Subtitle,
                ImageUrl = t.ImageUrl,
                BadgeText = t.BadgeText,
                BookTestId = t.BookTestId,
                BookId = t.BookTest != null ? t.BookTest.BookId : null,
                QuestionCount = t.WorksheetQuestions.Count() // ✅ Soru sayısını ekledik
            })
            .ToListAsync();

        return Ok(new Paged<WorksheetDto>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = tests
        });
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
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cevap başarıyla kaydedildi." });
    }

    [Authorize]
    [HttpPost("start-test/{testId}")]
    public async Task<IActionResult> StartTest(int testId)
    {
        var student = await GetAuthenticatedStudentAsync();

        // Öğrenci bu testi daha önce başlatmış mı kontrol et
        var existingInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.StudentId == student.Id && ti.WorksheetId == testId
                && ti.EndTime == null
                && ti.Student.UserId == student.UserId );

        if (existingInstance != null)
        {
            return Ok(new { testInstanceId = existingInstance.Id, message = "Bu testi zaten başlattınız ve devam ediyorsunuz!" });
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
        var user = await GetAuthenticatedUserAsync();

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
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == user.Id);

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

    [HttpGet("test-canvas-instance/{testInstanceId}")]
    public async Task<IActionResult> GetTestCanvasInstanceQuestions(int testInstanceId)
    {
        var user = await GetAuthenticatedUserAsync();

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
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == user.Id);

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
                        tiq.WorksheetQuestion.Question.Passage?.ImageUrl,
                        tiq.WorksheetQuestion.Question.Passage?.X,
                        tiq.WorksheetQuestion.Question.Passage?.Y,
                        tiq.WorksheetQuestion.Question.Passage?.Width,
                        tiq.WorksheetQuestion.Question.Passage?.Height
                    } : null,
                    tiq.WorksheetQuestion.Question.PracticeCorrectAnswer,
                    tiq.WorksheetQuestion.Question.AnswerColCount,
                    tiq.WorksheetQuestion.Question.IsCanvasQuestion,
                    tiq.WorksheetQuestion.Question.X,
                    tiq.WorksheetQuestion.Question.Y,
                    tiq.WorksheetQuestion.Question.Width,
                    tiq.WorksheetQuestion.Question.Height,
                    Answers = tiq.WorksheetQuestion.Question.Answers.Select(a => new
                    {
                        a.Id,
                        a.Text,                        
                        a.ImageUrl,
                        a.X,
                        a.Y,
                        a.Width,
                        a.Height
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
        var user = await GetAuthenticatedUserAsync();

        var testInstanceQuestion = await _context.TestInstanceQuestions
            .FirstOrDefaultAsync(tiq => tiq.WorksheetInstanceId == dto.TestInstanceId && 
                tiq.Id == dto.TestQuestionId
                && tiq.WorksheetInstance.Student.UserId == user.Id);

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
        var user = await GetAuthenticatedUserAsync();

        var testInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == user.Id);

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
    public async Task<IActionResult> CreateOrUpdateAsync([FromBody] ExamDto examDto)
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
                examination.Subtitle = examDto.Subtitle;
                examination.BookTestId = examDto.BookTestId;

                // 📌 Eğer yeni resim varsa, güncelle
                if (!string.IsNullOrEmpty(examDto.ImageUrl) &&
                    _imageHelper.IsBase64String(examDto.ImageUrl)) 
                {
                    byte[] imageBytes = Convert.FromBase64String(examDto.ImageUrl.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    examination.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.jpg","exams");
                }

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
                    IsPracticeTest = examDto.IsPracticeTest,
                    Subtitle = examDto.Subtitle,
                    BookTestId = examDto.BookTestId
                };

                // 📌 Eğer yeni resim varsa, güncelle
                if (!string.IsNullOrEmpty(examDto.ImageUrl) &&
                    _imageHelper.IsBase64String(examDto.ImageUrl)) 
                {
                    byte[] imageBytes = Convert.FromBase64String(examDto.ImageUrl.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    examination.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.jpg","exams");
                }

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
