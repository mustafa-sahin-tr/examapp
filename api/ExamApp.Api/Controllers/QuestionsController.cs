using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[ApiController]
[Route("api/questions")]
public class QuestionsController : BaseController
{
    private readonly IMinIoService _minioService;

    private readonly ImageHelper _imageHelper;  

    public QuestionsController(AppDbContext context, IMinIoService minioService, ImageHelper imageHelper)
        : base(context)
    {        
        _minioService = minioService;
        _imageHelper = imageHelper;
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuestionById(int id)
    {
        var question = await _context.Questions
            .Include(q => q.Answers)
            .Include(q => q.Subject)
            .Include(q => q.QuestionSubTopics)
                .ThenInclude(qst => qst.SubTopic)
            .Where(q => q.Id == id)
            .Select(q => new
            {
                q.Id,
                q.Text,
                q.SubText,
                q.ImageUrl,
                q.SubjectId,
                q.TopicId,                
                CategoryName = q.Subject.Name,
                q.Point,
                Answers = q.Answers.Select(a => new
                {
                    a.Id,
                    a.Text,
                    a.ImageUrl
                }).ToList(),
                q.IsExample,
                q.PracticeCorrectAnswer,
                Passage = q.PassageId.HasValue ? new {
                    q.Passage.Id,
                    q.Passage.Title,
                    q.Passage.Text, 
                    q.Passage.ImageUrl
                } : null,
                q.CorrectAnswer,
                q.AnswerColCount,
                Subtopics = q.QuestionSubTopics.Select(qst => new
                {
                    qst.SubTopicId,
                    qst.SubTopic.Name
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (question == null)
        {
            return NotFound(new { message = "Soru bulunamadı!" });
        }

        return Ok(question);
    }

    [HttpGet("passages")]
    public async Task<IActionResult> GetLastTenPassages() {
        var passages = await _context.Passage
            .OrderByDescending(p => p.Id)
            .Take(10)
            .Select(p => new {
                p.Id,
                p.Title,
                p.Text,
                p.ImageUrl
            })
            .ToListAsync();

        return Ok(passages);
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        try
        {
            var question = await _context.Questions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound(new { message = "Soru bulunamadı!" });
            }

            _context.Answers.RemoveRange(question.Answers);
            _context.Questions.Remove(question);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Soru başarıyla silindi!" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("bytest/{testid}")]
    public async Task<IActionResult> GetQuestionByTestId(int testid)
    {
        var questionList = await _context.TestQuestions
            .Include(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.Subject)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.Passage)      
            .Include(tq => tq.Question)
                .ThenInclude(q => q.QuestionSubTopics)
                    .ThenInclude(qst => qst.SubTopic)
            .Where(tq => tq.TestId == testid)
            .Select(tq => new
            {                
                tq.Question.Id,
                tq.Question.Text,
                tq.Question.SubText,
                tq.Question.ImageUrl,
                tq.Question.SubjectId,
                tq.Question.TopicId,
                CategoryName = tq.Question.Subject.Name,
                tq.Question.Point,
                Answers = tq.Question.Answers.Select(a => new
                {
                    a.Id,
                    a.Text,
                    a.ImageUrl
                }).ToList(),
                tq.Question.IsExample,
                tq.Question.PracticeCorrectAnswer,
                Passage = tq.Question.PassageId.HasValue ? new {
                    tq.Question.Passage.Id,
                    tq.Question.Passage.Title,
                    tq.Question.Passage.Text, 
                    tq.Question.Passage.ImageUrl
                } : null,
                tq.Question.CorrectAnswer,
                tq.Question.AnswerColCount,
                SubTopics = tq.Question.QuestionSubTopics.Select(qst => new
                {
                    qst.SubTopicId,
                    qst.SubTopic.Name
                }).ToList()
            })
            .ToListAsync();

        if (questionList == null)
        {
            return NotFound(new { message = "Soru bulunamadı!" });
        }

        return Ok(questionList);
    }



    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateOrUpdateQuestion([FromBody] QuestionDto questionDto)
{
    try
    {
        Question question;

        // 📌 Eğer ID varsa, veritabanından o soruyu bulup güncelle
        if (questionDto.Id > 0)
        {
            question = await _context.Questions
                    .Include(q => q.Answers)
                    .FirstOrDefaultAsync(q => q.Id == questionDto.Id) ?? throw new InvalidOperationException("Soru bulunamadı!");

            if (question == null)
            {
                return NotFound(new { error = "Soru bulunamadı!" });
            }

            question.Text = questionDto.Text;
            question.SubText = questionDto.SubText;
            question.Point = questionDto.Point;                
            question.SubjectId = questionDto.SubjectId;
            question.TopicId = questionDto.TopicId;
            question.AnswerColCount = questionDto.AnswerColCount;

            // 📌 Eğer yeni resim varsa, güncelle
            if (!string.IsNullOrEmpty(questionDto.Image) && 
                _imageHelper.IsBase64String(questionDto.Image)) 
            {
                byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                await using var imageStream = new MemoryStream(imageBytes);
                question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{questionDto.TestId}/{Guid.NewGuid()}.jpg");
            }

            // 📌 Cevapları Güncelle
            question.Answers.Clear(); // Önce mevcut şıkları temizle
            if (questionDto.IsExample) 
            {
                question.IsExample = true;
                question.PracticeCorrectAnswer = questionDto.PracticeCorrectAnswer;                    
            }
            else 
            {
                List<Answer> answers = new();
                Answer? correctAnswer = null;

                foreach (var answerDto in questionDto.Answers.Where(a => !string.IsNullOrEmpty(a.Text) || !string.IsNullOrEmpty(a.Image)))
                {
                    var answer = new Answer
                    {
                        Text = answerDto.Text,
                        QuestionId = question.Id // Foreign Key Set
                    };

                    if (!string.IsNullOrEmpty(answerDto.Image) && 
                        _imageHelper.IsBase64String(answerDto.Image))
                    {
                        byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                        await using var imageStream = new MemoryStream(imageBytes);
                        answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{questionDto.Id}/{Guid.NewGuid()}.jpg");
                    }

                    answers.Add(answer);
                    if (answerDto.IsCorrect)
                    {
                        correctAnswer = answer;
                    }
                }

                _context.Answers.AddRange(answers);
                await _context.SaveChangesAsync(); // 📌 Önce Answer'ları kaydet!

                if (correctAnswer != null)
                {
                    question.CorrectAnswerId = correctAnswer.Id;
                }
            }            

            if(questionDto.Passage != null) {
                if(questionDto.Passage.Id > 0) {
                    var passage = await _context.Passage
                    .FirstOrDefaultAsync(p => p.Id == questionDto.Passage.Id) ?? throw new InvalidOperationException("Kapsam bulunamadı!");
                } 
                else {
                    var passage = new Passage {
                        Title = questionDto.Passage.Title,
                        Text = questionDto.Passage.Text,
                        ImageUrl = questionDto.Passage.ImageUrl
                    };
                    _context.Passage.Add(passage);                    
                    question.PassageId = passage.Id;
                }
            }            
                    
            _context.Questions.Update(question);

            // Update QuestionSubTopics
            var existingSubTopics = await _context.QuestionSubTopics
                .Where(qst => qst.QuestionId == questionDto.Id)
                .ToListAsync();
            _context.QuestionSubTopics.RemoveRange(existingSubTopics);

            // var newSubTopics = questionDto.QuestionSubTopics.Select(qst => new QuestionSubTopic
            // {
            //     QuestionId = questionDto.Id.Value,
            //     SubTopicId = qst.SubtopicId
            // }).ToList();
            // _context.QuestionSubTopics.AddRange(newSubTopics);

            await _context.SaveChangesAsync();
        }
        else
        {
            // 📌 Yeni Soru Oluştur (INSERT)
            question = new Question
            {
                Text = questionDto.Text,
                SubText = questionDto.SubText,
                Point = questionDto.Point,                    
                SubjectId = questionDto.SubjectId,
                TopicId = questionDto.TopicId,
                AnswerColCount = questionDto.AnswerColCount
            };

            if (!string.IsNullOrEmpty(questionDto.Image) &&
                _imageHelper.IsBase64String(questionDto.Image))
            {
                byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                await using var imageStream = new MemoryStream(imageBytes);
                question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{questionDto.TestId}/{Guid.NewGuid()}.jpg");
            }


            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

            List<Answer> answers = new();
            Answer? correctAnswer = null;

            if (questionDto.IsExample) 
            {
                question.IsExample = true;
                question.PracticeCorrectAnswer = questionDto.PracticeCorrectAnswer;                    
            }
            else 
            {

                foreach (var answerDto in questionDto.Answers.Where(a => 
                            !string.IsNullOrEmpty(a.Text) || !string.IsNullOrEmpty(a.Image)))
                {
                    var answer = new Answer
                    {
                        Text = answerDto.Text
                    };

                    if (!string.IsNullOrEmpty(answerDto.Image) && 
                        _imageHelper.IsBase64String(answerDto.Image))
                    {
                        byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                        await using var imageStream = new MemoryStream(imageBytes);
                        answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{questionDto.Id}/{Guid.NewGuid()}.jpg");
                    }

                    answers.Add(answer);
                    if (answerDto.IsCorrect)
                    {
                        correctAnswer = answer;
                    }
                }

                foreach (var answer in answers)
                {
                    answer.QuestionId = question.Id;
                }

                _context.Answers.AddRange(answers);
                await _context.SaveChangesAsync();
            }

            if (correctAnswer != null)
            {
                question.CorrectAnswerId = correctAnswer.Id;
                _context.Questions.Update(question);
                await _context.SaveChangesAsync();
            }

            if (questionDto.TestId.HasValue)
            {
                var orderCount = await _context.TestQuestions
                    .Where(tq => tq.TestId == questionDto.TestId.Value)
                    .CountAsync();
                // ** WorksheetQuestions tablosuna ekleme **
                var worksheetQuestion = new WorksheetQuestion
                {
                    TestId = questionDto.TestId.Value,
                    QuestionId = question.Id,
                    Order = orderCount + 1 // Varsayılan sıralama
                };

                _context.TestQuestions.Add(worksheetQuestion);
                await _context.SaveChangesAsync();
            }

            if(questionDto.Passage != null) {
                if(questionDto.Passage.Id > 0) {
                    var passage = await _context.Passage
                    .FirstOrDefaultAsync(p => p.Id == questionDto.Passage.Id) ?? throw new InvalidOperationException("Kapsam bulunamadı!");

                    question.PassageId = passage.Id;
                    _context.Questions.Update(question);
                    await _context.SaveChangesAsync();

                } 
                else {

                    var passage = new Passage {
                        Title = questionDto.Passage.Title,
                        Text = questionDto.Passage.Text
                    };
                    // 📌 Eğer yeni resim varsa, güncelle
                    if (!string.IsNullOrEmpty(questionDto.Passage.ImageUrl) && 
                        _imageHelper.IsBase64String(questionDto.Passage.ImageUrl)) 
                    {
                        byte[] imageBytes = Convert.FromBase64String(questionDto.Passage.ImageUrl.Split(',')[1]);
                        await using var imageStream = new MemoryStream(imageBytes);
                        passage.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"passages/{questionDto.TestId}/{Guid.NewGuid()}.jpg");
                    }

                    _context.Passage.Add(passage);
                    await _context.SaveChangesAsync();

                    question.PassageId = passage.Id;
                    _context.Questions.Update(question);
                    await _context.SaveChangesAsync();
                }
            }

            // Add QuestionSubTopics
            // var newSubTopics = .Select(qst => new QuestionSubTopic
            // {
            //     QuestionId = question.Id,
            //     SubTopicId = qst.SubtopicId
            // }).ToList();
            // _context.QuestionSubTopics.AddRange(newSubTopics);

            await _context.SaveChangesAsync();
        }

        return Ok(new { message = questionDto.Id > 0 ? "Soru başarıyla güncellendi!" : "Soru başarıyla kaydedildi!", questionId = question.Id });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}


    [HttpPost("save")]
    public async Task<IActionResult> KaydetSoruSeti([FromBody] BulkQuestionCreateDto soruDto)
    {
        if (soruDto == null)
        {
            return BadRequest("Geçersiz veri.");
        }

        using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            try
            {
                string imageUrl = string.Empty;
                // 🔹 1. Resmi MinIO'ya kaydet ve ImageUrl olarak kaydet
                if (!string.IsNullOrEmpty(soruDto.ImageData) && 
                        _imageHelper.IsBase64String(soruDto.ImageData))
                {
                    byte[] imageBytes = Convert.FromBase64String(soruDto.ImageData.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    imageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{Guid.NewGuid()}.jpg");
                }
                
                // 🔹 2. Passage'ları kaydet
                var passages = soruDto.Passages.Select(p => new Passage
                {
                    X = p.X,
                    Y = p.Y,
                    Width = p.Width,
                    Height = p.Height,
                    Title = p.Id,
                    IsCanvasQuestion = true
                }).ToList();

                _context.Passage.AddRange(passages);
                await _context.SaveChangesAsync();

                // 🔹 3. Soruları kaydet
                foreach (var questionDto in soruDto.Questions)
                {
                    var question = new Question
                    {
                        // Text = questionDto.Name,
                        ImageUrl = imageUrl,
                        X = questionDto.X,
                        Y = questionDto.Y,
                        Width = questionDto.Width,
                        Height = questionDto.Height,
                        IsExample = questionDto.IsExample,
                        PracticeCorrectAnswer = questionDto.IsExample ? questionDto.ExampleAnswer : null,
                        IsCanvasQuestion = true,
                        SubjectId = soruDto.Header.SubjectId ?? 0,
                        TopicId = soruDto.Header.TopicId ?? 0,
                        PassageId = passages.FirstOrDefault(p => p.Title == questionDto.PassageId)?.Id ?? null
                    };

                    if (soruDto.Header.Subtopics != null && soruDto.Header.Subtopics.Any())
                    {
                        question.QuestionSubTopics = soruDto.Header.Subtopics.Select(subTopicId => new QuestionSubTopic
                        {
                            SubTopicId = subTopicId
                            // QuestionId will be set automatically when the question is saved
                        }).ToList();
                    }

                    _context.Questions.Add(question);
                    await _context.SaveChangesAsync();

                    // 🔹 4. Şıkları kaydet
                    if (!questionDto.IsExample)
                    {
                        var answers = questionDto.Answers.Select(a => new Answer
                        {
                            QuestionId = question.Id,
                            Text = a.Label,
                            X = a.X,
                            Y = a.Y,
                            Width = a.Width,
                            Height = a.Height,        
                            IsCanvasQuestion = true                    
                        }).ToList();

                        var correctLabel = questionDto.Answers.FirstOrDefault(a => a.IsCorrect)?.Label;

                        if(correctLabel == null) {
                            throw new InvalidOperationException("Doğru cevap belirtilmemiş." + questionDto.Name);
                        }

                        _context.Answers.AddRange(answers);
                        await _context.SaveChangesAsync();

                        // 🔹 Doğru cevabı kaydet (ilk doğru olanı seç)
                        question.CorrectAnswerId = answers.FirstOrDefault(a => a.Text == correctLabel)?.Id;
                        await _context.SaveChangesAsync();
                    }

                    if (soruDto.Header.TestId.HasValue)
                    {
                        var orderCount = await _context.TestQuestions
                            .Where(tq => tq.TestId == soruDto.Header.TestId.Value)
                            .CountAsync();
                        // ** WorksheetQuestions tablosuna ekleme **
                        var worksheetQuestion = new WorksheetQuestion
                        {
                            TestId = soruDto.Header.TestId.Value,
                            QuestionId = question.Id,
                            Order = orderCount + 1 // Varsayılan sıralama
                        };

                        _context.TestQuestions.Add(worksheetQuestion);
                        await _context.SaveChangesAsync();
                    }
                }

                await transaction.CommitAsync();
                return Ok(new { message = "Sorular başarıyla kaydedildi!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { error = "Veri kaydedilirken hata oluştu.", details = ex.Message });
            }
        }
    }


    
}
