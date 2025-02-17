using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
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
            .Where(q => q.Id == id)
            .Select(q => new
            {
                q.Id,
                q.Text,
                q.SubText,
                q.ImageUrl,
                q.SubjectId,
                q.TopicId,
                q.SubTopicId,
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
                q.AnswerColCount
            })
            .FirstOrDefaultAsync();

        if (question == null)
        {
            return NotFound(new { message = "Soru bulunamadı!" });
        }

        return Ok(question);
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
            .Where(tq => tq.TestId == testid)
            .Select(tq => new
            {                
                tq.Question.Id,
                tq.Question.Text,
                tq.Question.SubText,
                tq.Question.ImageUrl,
                tq.Question.SubjectId,
                tq.Question.TopicId,
                tq.Question.SubTopicId,
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
                tq.Question.AnswerColCount
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
            question = await _context.Questions.Include(q => q.Answers)
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
            question.SubTopicId = questionDto.SubTopicId;
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

            _context.Questions.Update(question);
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
                SubTopicId = questionDto.SubTopicId,
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

            if (correctAnswer != null)
            {
                question.CorrectAnswerId = correctAnswer.Id;
                _context.Questions.Update(question);
                await _context.SaveChangesAsync();
            }
        }

        return Ok(new { message = questionDto.Id > 0 ? "Soru başarıyla güncellendi!" : "Soru başarıyla kaydedildi!", questionId = question.Id });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}


    // [HttpPost]
    // public async Task<IActionResult> CreateQuestion([FromBody] QuestionDto questionDto)
    // {
    //     try
    //     {
    //         var question = new Question
    //         {
    //             Text = questionDto.Text,
    //             SubText = questionDto.SubText,
    //             // Category = questionDto.Category,
    //             Point = questionDto.Point,
    //             CorrectAnswer = questionDto.CorrectAnswer,
    //             SubjectId = questionDto.SubjectId,
    //             TopicId = questionDto.TopicId,
    //             SubTopicId = questionDto.SubTopicId,
    //         };

    //         // Soru Resmini MinIO'ya yükleyelim
    //         if (!string.IsNullOrEmpty(questionDto.Image))
    //         {
    //             byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
    //             await using var imageStream = new MemoryStream(imageBytes);
    //             question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{Guid.NewGuid()}.jpg");
    //         }

    //         // Şıkların Resimlerini MinIO'ya Yükleyelim
    //         foreach (var answerDto in questionDto.Answers)
    //         {
    //             var answer = new Answer
    //             {
    //                 Text = answerDto.Text
    //             };

    //             if (!string.IsNullOrEmpty(answerDto.Image))
    //             {
    //                 byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
    //                 await using var imageStream = new MemoryStream(imageBytes);
    //                 answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{Guid.NewGuid()}.jpg");
    //             }

    //             question.Answers.Add(answer);
    //         }

    //         _context.Questions.Add(question);
    //         await _context.SaveChangesAsync();

    //         return Ok(new { message = "Soru başarıyla kaydedildi!", questionId = question.Id });
    //     }
    //     catch (Exception ex)
    //     {
    //         return BadRequest(new { error = ex.Message });
    //     }
    // }
}
