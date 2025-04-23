using System;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class QuestionService : IQuestionService
{
    
    private readonly AppDbContext _context;

    private readonly ImageHelper _imageHelper;
    private readonly IMinIoService _minioService;
    public QuestionService(AppDbContext context, ImageHelper imageHelper, IMinIoService minioService)
    {
        _imageHelper = imageHelper;
        _context = context;
        _minioService = minioService;
    }
    
    public async Task<QuestionDto?> GetQuestionById(int id)
    {
        var question = await _context.Questions
            .Include(q => q.Answers)
            .Include(q => q.Subject)
            .Include(q => q.QuestionSubTopics)
                .ThenInclude(qst => qst.SubTopic)
            .Where(q => q.Id == id)
            .Select(q => new QuestionDto
            {
                Id = q.Id,
                Text = q.Text,
                SubText = q.SubText,
                ImageUrl = q.ImageUrl,
                SubjectId = q.SubjectId,
                TopicId = q.TopicId,                
                CategoryName = q.Subject.Name,
                Point = q.Point,
                X = q.X,
                Y = q.Y,
                Width = q.Width,
                Height = q.Height,
                Answers = q.Answers.Select(a => new AnswerDto
                {
                    Id = a.Id,
                    Text = a.Text,
                    ImageUrl = a.ImageUrl,
                    X = a.X,
                    Y = a.Y,
                    Width = a.Width,
                    Height = a.Height,

                }).ToList(),
                IsExample = q.IsExample,
                PracticeCorrectAnswer = q.PracticeCorrectAnswer,
                Passage = q.PassageId.HasValue ? new PassageDto {
                    Id = q.Passage.Id,
                    Title = q.Passage.Title,
                    Text = q.Passage.Text, 
                    ImageUrl = q.Passage.ImageUrl,
                    X = q.Passage.X,
                    Y = q.Passage.Y,
                    Width = q.Passage.Width,
                    Height = q.Passage.Height,
                } : null,
                CorrectAnswerId = q.CorrectAnswerId,
                AnswerColCount = q.AnswerColCount
                // Subtopics = q.QuestionSubTopics.Select(qst => new Sub
                // {
                //     qst.SubTopicId,
                //     qst.SubTopic.Name
                // }).ToList()
            })
            .FirstOrDefaultAsync();        

        return question;
    }
}
