using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

/// <summary>Read-only question/passage projections. Extracted from QuestionService.</summary>
public class QuestionQueryService : IQuestionQueryService
{
    private readonly AppDbContext _context;

    public QuestionQueryService(AppDbContext context)
    {
        _context = context;
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
                BookName = q.BookName,
                SubjectId = q.SubjectId,
                TopicId = q.TopicId,
                CategoryName = q.Subject.Name,
                Point = q.Point,
                DifficultyLevel = q.DifficultyLevel,
                X = q.X,
                Y = q.Y,
                Width = q.Width,
                Height = q.Height,
                SanitizedHeight = q.SanitizedHeight,
                InteractionType = q.InteractionType,
                InteractionPlan = q.InteractionPlan,
                ShowPassageFirst = q.ShowPassageFirst,
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
                SubTopics = q.QuestionSubTopics
                    .Where(qst => qst.SubTopic != null)
                    .Select(qst => new SubTopicDto
                    {
                        Id = qst.SubTopic.Id,
                        Name = qst.SubTopic.Name,
                        TopicId = qst.SubTopic.TopicId
                    })
                    .ToList(),
                IsExample = q.IsExample,
                PracticeCorrectAnswer = q.PracticeCorrectAnswer,
                Passage = q.PassageId.HasValue ? new PassageDto
                {
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
            })
            .FirstOrDefaultAsync();



        return question;
    }

    public async Task<List<PassageDto>> GetLastTenPassages()
    {
        return await _context.Passage
            .OrderByDescending(p => p.Id)
            .Take(10)
            .Select(p => new PassageDto
            {
                Id = p.Id,
                Title = p.Title,
                Text = p.Text,
                ImageUrl = p.ImageUrl,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
            })
            .ToListAsync();
    }

    public async Task<List<QuestionDto>> GetQuestionByTestId(int testid)
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
            .Where(tq => tq.TestId == testid && !tq.IsDeleted)
            .OrderBy(tq => tq.Order)
            .Select(tq => new QuestionDto
            {
                Id = tq.Question.Id,
                Text = tq.Question.Text,
                SubText = tq.Question.SubText,
                ImageUrl = tq.Question.ImageUrl,
                BookName = tq.Question.BookName,
                SubjectId = tq.Question.SubjectId,
                TopicId = tq.Question.TopicId,
                CategoryName = tq.Question.Subject.Name,
                Point = tq.Question.Point,
                DifficultyLevel = tq.Question.DifficultyLevel,
                X = tq.Question.X,
                Y = tq.Question.Y,
                Width = tq.Question.Width,
                Height = tq.Question.Height,
                SanitizedHeight = tq.Question.SanitizedHeight,
                InteractionType = tq.Question.InteractionType,
                InteractionPlan = tq.Question.InteractionPlan,
                ShowPassageFirst = tq.Question.ShowPassageFirst,
                Answers = tq.Question.Answers.Select(a => new AnswerDto
                {
                    Id = a.Id,
                    Text = a.Text,
                    ImageUrl = a.ImageUrl,
                    X = a.X,
                    Y = a.Y,
                    Width = a.Width,
                    Height = a.Height,
                }).ToList(),
                SubTopics = tq.Question.QuestionSubTopics
                    .Where(qst => qst.SubTopic != null)
                    .Select(qst => new SubTopicDto
                    {
                        Id = qst.SubTopic.Id,
                        Name = qst.SubTopic.Name,
                        TopicId = qst.SubTopic.TopicId
                    })
                    .ToList(),
                IsExample = tq.Question.IsExample,
                PracticeCorrectAnswer = tq.Question.PracticeCorrectAnswer,
                Passage = tq.Question.PassageId.HasValue ? new PassageDto
                {
                    Id = tq.Question.Passage.Id,
                    Title = tq.Question.Passage.Title,
                    Text = tq.Question.Passage.Text,
                    ImageUrl = tq.Question.Passage.ImageUrl,
                    X = tq.Question.Passage.X,
                    Y = tq.Question.Passage.Y,
                    Width = tq.Question.Passage.Width,
                    Height = tq.Question.Passage.Height,
                } : null,
                CorrectAnswerId = tq.Question.CorrectAnswerId,
                AnswerColCount = tq.Question.AnswerColCount,
                Order = tq.Order,
                ClassificationSource = tq.Question.ClassificationSource ?? ClassificationSource.Human

            })
            .ToListAsync();



        return questionList;
    }
}
