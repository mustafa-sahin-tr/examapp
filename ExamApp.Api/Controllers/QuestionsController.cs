using ExamApp.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
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
                CategoryName = q.Subject.Name,
                q.Point,
                Answers = q.Answers.Select(a => new
                {
                    a.Id,
                    a.Text,
                    a.ImageUrl
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (question == null)
        {
            return NotFound(new { message = "Soru bulunamadı!" });
        }

        return Ok(question);
    }

    [HttpPost]
    public async Task<IActionResult> CreateQuestion([FromBody] QuestionDto questionDto)
    {
        try
        {
            var question = new Question
            {
                Text = questionDto.Text,
                SubText = questionDto.SubText,
                // Category = questionDto.Category,
                Point = questionDto.Point,
                CorrectAnswer = questionDto.CorrectAnswer,
                SubjectId = questionDto.SubjectId,
                TopicId = questionDto.TopicId,
                SubTopicId = questionDto.SubTopicId,
            };

            // Soru Resmini MinIO'ya yükleyelim
            if (!string.IsNullOrEmpty(questionDto.Image))
            {
                byte[] imageBytes = Convert.FromBase64String(questionDto.Image.Split(',')[1]);
                await using var imageStream = new MemoryStream(imageBytes);
                question.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"questions/{Guid.NewGuid()}.jpg");
            }

            // Şıkların Resimlerini MinIO'ya Yükleyelim
            foreach (var answerDto in questionDto.Answers)
            {
                var answer = new Answer
                {
                    Text = answerDto.Text
                };

                if (!string.IsNullOrEmpty(answerDto.Image))
                {
                    byte[] imageBytes = Convert.FromBase64String(answerDto.Image.Split(',')[1]);
                    await using var imageStream = new MemoryStream(imageBytes);
                    answer.ImageUrl = await _minioService.UploadFileAsync(imageStream, $"answers/{Guid.NewGuid()}.jpg");
                }

                question.Answers.Add(answer);
            }

            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Soru başarıyla kaydedildi!", questionId = question.Id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
