using ExamApp.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[ApiController]
[Route("api/questions")]
public class QuestionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMinIoService _minioService;

    public QuestionsController(AppDbContext context, IMinIoService minioService)
    {
        _context = context;
        _minioService = minioService;
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

    bool IsBase64String(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        // Base64 formatını tespit etmek için regex
        string base64Pattern = @"^data:image\/(png|jpeg|jpg|gif|bmp|webp);base64,[A-Za-z0-9+/=]+$";
        return Regex.IsMatch(input, base64Pattern);
    }

     bool IsValidImageUrl(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        // URL olup olmadığını anlamak için regex
        string urlPattern = @"^(http|https):\/\/.*\.(jpeg|jpg|png|gif|bmp|webp)(\?.*)?$";
        return Regex.IsMatch(input, urlPattern, RegexOptions.IgnoreCase);
    }

    [HttpPost]
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
                question.CorrectAnswer = questionDto.CorrectAnswer;
                question.SubjectId = questionDto.SubjectId;
                question.TopicId = questionDto.TopicId;
                question.SubTopicId = questionDto.SubTopicId;
                question.AnswerColCount = questionDto.AnswerColCount;

                // 📌 Eğer yeni resim varsa, güncelle
                if (!string.IsNullOrEmpty(questionDto.Image) && IsBase64String(questionDto.Image)) 
                {
                    byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{Guid.NewGuid()}.jpg");
                }

                // 📌 Cevapları Güncelle
                question.Answers.Clear(); // Önce mevcut şıkları temizle
                if(questionDto.IsExample) // eğer is Example ise answer olmasına gerek yok 
                {
                    question.IsExample = true;
                    question.PracticeCorrectAnswer = questionDto.PracticeCorrectAnswer;
                    question.CorrectAnswer = 0;
                }
                else {
                    foreach (var answerDto in questionDto.Answers.Where(a => !string.IsNullOrEmpty(a.Text) || !string.IsNullOrEmpty(a.Image)))
                    {
                        var answer = new Answer
                        {
                            Text = answerDto.Text
                        };

                        if (!string.IsNullOrEmpty(answerDto.Image)  && IsBase64String(answerDto.Image))
                        {
                            byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                            await using var imageStream = new MemoryStream(imageBytes);
                            answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{Guid.NewGuid()}.jpg");
                        }

                        question.Answers.Add(answer);
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
                    CorrectAnswer = questionDto.CorrectAnswer,
                    SubjectId = questionDto.SubjectId,
                    TopicId = questionDto.TopicId,
                    SubTopicId = questionDto.SubTopicId,
                    AnswerColCount = questionDto.AnswerColCount
                };

                // 📌 Eğer resim varsa, MinIO'ya yükleyelim
                if (!string.IsNullOrEmpty(questionDto.Image) && IsBase64String(questionDto.Image))
                {
                    byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{Guid.NewGuid()}.jpg");
                }

                // 📌 Şıkları ekleyelim
                if(questionDto.IsExample) // eğer is Example ise answer olmasına gerek yok 
                {
                    question.IsExample = true;
                    question.PracticeCorrectAnswer = questionDto.PracticeCorrectAnswer;
                    question.CorrectAnswer = 0;
                }
                else {
                    foreach (var answerDto in questionDto.Answers.Where(a => 
                                !string.IsNullOrEmpty(a.Text) || !string.IsNullOrEmpty(a.Image)))
                    {
                        var answer = new Answer
                        {
                            Text = answerDto.Text
                        };

                        if (!string.IsNullOrEmpty(answerDto.Image) && IsBase64String(answerDto.Image))
                        {
                            byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                            await using var imageStream = new MemoryStream(imageBytes);
                            answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{Guid.NewGuid()}.jpg");
                        }

                        question.Answers.Add(answer);
                    }
                }
                _context.Questions.Add(question);
            }

            // 📌 Değişiklikleri Kaydet
            await _context.SaveChangesAsync();

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
