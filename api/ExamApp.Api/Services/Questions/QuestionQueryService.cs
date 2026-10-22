using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
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

    public async Task<List<PassageDto>> GetLastTenPassages(int userId, bool isAdmin, CancellationToken ct = default)
    {
        if (!isAdmin && userId <= 0)
            return new List<PassageDto>();

        var query = _context.Passage.AsNoTracking();
        if (!isAdmin)
            query = query.Where(p => p.CreateUserId == userId);

        return await query
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
            .ToListAsync(ct);
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

    public async Task<QuestionBankPageDto> SearchBankAsync(QuestionBankQuery filter, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var page = new QuestionBankPageDto { Skip = Math.Max(0, filter.Skip), Take = filter.EffectiveTake };
        if (!isAdmin && userId <= 0)
            return page;

        // Kendi soruları kimliği çözülmüş kullanıcıya göre; kimliksiz admin "mine" kapsamında hiçbir şeye sahip değildir.
        if (!filter.IsAllScope && userId <= 0)
            return page;

        // Sayılan / görünürlük veren test üyelikleri: admin için silinmemiş tüm testlerinki, öğretmen için görebildiği
        // testlerinki (CanViewQuestionAsync ile aynı predicate). Test silinince üyelik satırları silinmediği için admin
        // dalında da testin varlığı (global soft-delete filtresi) aranır.
        IQueryable<WorksheetQuestion> visibleMemberships = _context.TestQuestions
            .Where(tq => _context.Worksheets.Any(w => w.Id == tq.TestId));
        if (!isAdmin)
        {
            var schoolId = await _context.ResolveTeacherSchoolIdAsync(userId, ct);
            var visibleWorksheets = _context.Worksheets.Where(WorksheetAccess.VisibleToTeacherPredicate(_context, userId, schoolId));
            visibleMemberships = visibleMemberships.Where(tq => visibleWorksheets.Any(w => w.Id == tq.TestId));
        }

        // Eski (CreateUserId 0/null) sorularda sahip, soruyu içeren kopya olmayan testin sahibidir (CanModifyQuestionAsync).
        // Bu dal yalnız kullanıcının gerçekten böyle sorusu varsa sorguya eklenir: yoksa "kendi sorularım" koşulu tek başına
        // CreateUserId eşitliğidir ve IX_Questions_CreateUserId'yi kullanabilir (OR'lu alt sorgu index'i engeller).
        var legacyOwnedIds = _context.TestQuestions
            .Where(tq => tq.Worksheet.SourceWorksheetId == null && tq.Worksheet.CreateUserId == userId
                && (tq.Question.CreateUserId == null || tq.Question.CreateUserId == 0))
            .Select(tq => tq.QuestionId);
        var hasLegacyOwned = userId > 0 && await legacyOwnedIds.AnyAsync(ct);

        var query = _context.Questions.AsNoTracking();
        if (!filter.IsAllScope)
        {
            query = hasLegacyOwned
                ? query.Where(q => q.CreateUserId == userId || legacyOwnedIds.Contains(q.Id))
                : query.Where(q => q.CreateUserId == userId);
        }
        else if (!isAdmin)
        {
            var visibleQuestionIds = visibleMemberships.Select(tq => tq.QuestionId);
            query = hasLegacyOwned
                ? query.Where(q => q.CreateUserId == userId || legacyOwnedIds.Contains(q.Id) || visibleQuestionIds.Contains(q.Id))
                : query.Where(q => q.CreateUserId == userId || visibleQuestionIds.Contains(q.Id));
        }

        if (filter.GradeId is { } gradeId)
            query = query.Where(q => q.TopicId != null && q.Topic.GradeId == gradeId);
        if (filter.SubjectId is { } subjectId)
            query = query.Where(q => q.SubjectId == subjectId);
        if (filter.TopicId is { } topicId)
            query = query.Where(q => q.TopicId == topicId);
        var subTopicIds = filter.SubTopicIds?.Where(id => id > 0).Distinct().ToArray() ?? [];
        if (subTopicIds.Length > 0)
            query = query.Where(q => q.QuestionSubTopics.Any(qst => subTopicIds.Contains(qst.SubTopicId)));
        if (filter.MinDifficulty is { } minDifficulty)
            query = query.Where(q => q.DifficultyLevel >= minDifficulty);
        if (filter.MaxDifficulty is { } maxDifficulty)
            query = query.Where(q => q.DifficultyLevel <= maxDifficulty);
        if (filter.ParsedSource is { } source)
            query = query.Where(q => q.ClassificationSource == source);
        if (filter.Classified is { } classified)
            query = classified
                ? query.Where(q => q.SubjectId != null && q.TopicId != null)
                : query.Where(q => q.SubjectId == null || q.TopicId == null);
        if (filter.InTest is { } inTest)
            query = inTest
                ? query.Where(q => visibleMemberships.Any(tq => tq.QuestionId == q.Id))
                : query.Where(q => !visibleMemberships.Any(tq => tq.QuestionId == q.Id));
        if (filter.HasCorrectAnswer is { } hasCorrectAnswer)
            query = hasCorrectAnswer
                ? query.Where(q => q.CorrectAnswerId != null)
                : query.Where(q => q.CorrectAnswerId == null);

        page.Total = await query.CountAsync(ct);
        if (page.Total == 0 || page.Skip >= page.Total)
            return page;

        page.Items = await query
            .OrderByDescending(q => q.Id)
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(q => new QuestionBankItemDto
            {
                Id = q.Id,
                ImageUrl = q.ImageUrl,
                SubjectId = q.SubjectId,
                SubjectName = q.SubjectId != null ? q.Subject.Name : null,
                TopicId = q.TopicId,
                TopicName = q.TopicId != null ? q.Topic.Name : null,
                GradeId = q.TopicId != null ? (int?)q.Topic.GradeId : null,
                SubTopics = q.QuestionSubTopics
                    .Where(qst => qst.SubTopic != null)
                    .Select(qst => new SubTopicDto
                    {
                        Id = qst.SubTopic.Id,
                        Name = qst.SubTopic.Name,
                        TopicId = qst.SubTopic.TopicId
                    })
                    .ToList(),
                DifficultyLevel = q.DifficultyLevel,
                ClassificationSource = q.ClassificationSource,
                IsClassified = q.SubjectId != null && q.TopicId != null,
                HasCorrectAnswer = q.CorrectAnswerId != null,
                TestCount = visibleMemberships.Count(tq => tq.QuestionId == q.Id),
                IsOwn = (userId > 0 && q.CreateUserId == userId) || (hasLegacyOwned && legacyOwnedIds.Contains(q.Id)),
                CanLink = q.CreateUserId > 0 || (hasLegacyOwned && legacyOwnedIds.Contains(q.Id)),
                CreateTime = q.CreateTime
            })
            .ToListAsync(ct);

        return page;
    }
}
