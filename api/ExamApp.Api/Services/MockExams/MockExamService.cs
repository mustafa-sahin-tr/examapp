using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.MockExams;

public interface IMockExamService
{
    Task<List<ExamTemplateDto>> ListTemplatesAsync(CancellationToken ct);
    Task<ExamTemplateDto> SaveTemplateAsync(ExamTemplateDto template, int? id, int userId, CancellationToken ct);
    Task<MockExamDetailDto?> CreateAsync(CreateMockExamDto request, int userId, CancellationToken ct);
    Task<List<MockExamListItemDto>> ListMineAsync(int userId, bool isAdmin, CancellationToken ct);
    Task<MockExamDetailDto?> GetAsync(int id, int userId, bool isAdmin, CancellationToken ct);
    Task<MockExamDetailDto?> SetSectionQuestionsAsync(int id, int sectionId, IReadOnlyList<int> questionIds, int userId, bool isAdmin, CancellationToken ct);
    Task<MockExamDetailDto?> PublishAsync(int id, bool openToGrade, int userId, bool isAdmin, CancellationToken ct);
    Task<bool> DeleteDraftAsync(int id, int userId, bool isAdmin, CancellationToken ct);
    Task<List<StudentMockExamDto>> ListForStudentAsync(StudentProfileDto student, CancellationToken ct);
    Task<MockExamResultDto?> GetResultAsync(int id, StudentProfileDto student, CancellationToken ct);
}

/// <summary>İstek kurallara uymuyor; <see cref="Exception.Message"/> ekranın çevirdiği koddur.</summary>
public sealed class MockExamRuleException(string code) : Exception(code);

/// <summary>
/// Issue #693: deneme = şablon + oturum başına sıradan bir test kaydı. Süre sınırı, oturum, cevap kaydı ve puanlama testlerle aynı yoldan
/// geçer; burada yalnız kurulum, yayın denetimi ve ders başına net hesabı vardır. Normal test ve etkinlikler değişmez.
/// </summary>
public sealed class MockExamService(AppDbContext db, IQuestionTestLinkService links) : IMockExamService
{
    public const string WrongTemplate = "UnknownTemplate";
    public const string WrongGrade = "UnknownGrade";
    public const string TemplateInUse = "TemplateInUse";
    public const string CodeTaken = "TemplateCodeTaken";
    public const string AlreadyPublished = "AlreadyPublished";
    public const string TooManyQuestions = "TooManyQuestions";
    public const string QuestionTwice = "QuestionUsedTwice";
    public const string QuestionNotUsable = "QuestionNotUsable";

    // ---- şablonlar

    public async Task<List<ExamTemplateDto>> ListTemplatesAsync(CancellationToken ct)
    {
        var templates = await db.Set<ExamTemplate>().AsNoTracking()
            .Include(t => t.Sessions).ThenInclude(s => s.Sections)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);
        var used = await db.Set<MockExam>().AsNoTracking().Select(e => e.ExamTemplateId).Distinct().ToListAsync(ct);
        return templates.Select(t => ToDto(t, used.Contains(t.Id))).ToList();
    }

    public async Task<ExamTemplateDto> SaveTemplateAsync(ExamTemplateDto dto, int? id, int userId, CancellationToken ct)
    {
        var code = dto.Code.Trim().ToLowerInvariant();
        if (await db.Set<ExamTemplate>().AnyAsync(t => t.Code == code && t.Id != id, ct))
            throw new MockExamRuleException(CodeTaken);

        db.SetCurrentUser(userId);
        ExamTemplate template;
        if (id is int existingId)
        {
            template = await db.Set<ExamTemplate>().Include(t => t.Sessions).ThenInclude(s => s.Sections).FirstOrDefaultAsync(t => t.Id == existingId, ct)
                ?? throw new MockExamRuleException(WrongTemplate);
            // Kurulmuş denemelerin ders satırları ve sonuçları bu yapıya bağlıdır.
            if (await db.Set<MockExam>().AnyAsync(e => e.ExamTemplateId == existingId, ct))
                throw new MockExamRuleException(TemplateInUse);
            db.RemoveRange(template.Sessions.SelectMany(s => s.Sections));
            db.RemoveRange(template.Sessions);
            template.Sessions = new List<ExamTemplateSession>();
        }
        else
        {
            template = new ExamTemplate();
            db.Add(template);
        }

        template.Code = code;
        template.Name = dto.Name.Trim();
        template.WrongAnswersPerCorrect = dto.WrongAnswersPerCorrect;
        template.SourceReference = dto.SourceReference?.Trim();
        template.Sessions = dto.Sessions.Select((session, sessionIndex) => new ExamTemplateSession
        {
            Order = sessionIndex + 1,
            Name = session.Name.Trim(),
            DurationSeconds = session.DurationSeconds,
            Sections = session.Sections.Select((section, sectionIndex) => new ExamTemplateSection
            {
                Order = sectionIndex + 1,
                GroupName = string.IsNullOrWhiteSpace(section.GroupName) ? null : section.GroupName.Trim(),
                Name = section.Name.Trim(),
                QuestionCount = section.QuestionCount,
            }).ToList(),
        }).ToList();
        await db.SaveChangesAsync(ct);
        return ToDto(template, inUse: false);
    }

    private static ExamTemplateDto ToDto(ExamTemplate template, bool inUse) => new()
    {
        Id = template.Id,
        Code = template.Code,
        Name = template.Name,
        WrongAnswersPerCorrect = template.WrongAnswersPerCorrect,
        SourceReference = template.SourceReference,
        InUse = inUse,
        Sessions = template.Sessions.OrderBy(s => s.Order).Select(s => new ExamTemplateSessionDto
        {
            Id = s.Id,
            Name = s.Name,
            DurationSeconds = s.DurationSeconds,
            Sections = s.Sections.OrderBy(x => x.Order).Select(x => new ExamTemplateSectionDto
            {
                Id = x.Id,
                GroupName = x.GroupName,
                Name = x.Name,
                QuestionCount = x.QuestionCount,
            }).ToList(),
        }).ToList(),
    };

    // ---- kurulum

    public async Task<MockExamDetailDto?> CreateAsync(CreateMockExamDto request, int userId, CancellationToken ct)
    {
        var template = await db.Set<ExamTemplate>().AsNoTracking().Include(t => t.Sessions).FirstOrDefaultAsync(t => t.Id == request.ExamTemplateId, ct)
            ?? throw new MockExamRuleException(WrongTemplate);
        if (!await db.Grades.AnyAsync(g => g.Id == request.GradeId, ct))
            throw new MockExamRuleException(WrongGrade);

        var name = request.Name.Trim();
        var exam = new MockExam { ExamTemplateId = template.Id, Name = name, GradeId = request.GradeId };
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            exam.Id = 0;
            exam.Sessions = new List<MockExamSession>();
            db.SetCurrentUser(userId);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var session in template.Sessions.OrderBy(s => s.Order))
            {
                // Yayına kadar kapalı: taslak öğrenciye de başka öğretmene de görünmez.
                exam.Sessions.Add(new MockExamSession
                {
                    ExamTemplateSessionId = session.Id,
                    Worksheet = new Worksheet
                    {
                        Name = template.Sessions.Count > 1 ? $"{name} · {session.Name}" : name,
                        GradeId = request.GradeId,
                        Kind = WorksheetKind.Test,
                        MaxDurationSeconds = session.DurationSeconds,
                        TeacherSharing = WorksheetTeacherSharing.Private,
                        StudentVisibility = WorksheetStudentVisibility.Restricted,
                    },
                });
            }
            db.Add(exam);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        return await ReadDetailAsync(exam.Id, ct);
    }

    public Task<List<MockExamListItemDto>> ListMineAsync(int userId, bool isAdmin, CancellationToken ct) =>
        db.Set<MockExam>().AsNoTracking()
            .Where(e => isAdmin || e.CreateUserId == userId)
            .OrderByDescending(e => e.Id)
            .Select(e => new MockExamListItemDto { Id = e.Id, Name = e.Name, TemplateName = e.ExamTemplate.Name, GradeName = e.Grade.Name, PublishedAt = e.PublishedAt })
            .ToListAsync(ct);

    public async Task<MockExamDetailDto?> GetAsync(int id, int userId, bool isAdmin, CancellationToken ct) =>
        await CanEditAsync(id, userId, isAdmin, ct) ? await ReadDetailAsync(id, ct) : null;

    private Task<bool> CanEditAsync(int id, int userId, bool isAdmin, CancellationToken ct) =>
        db.Set<MockExam>().AsNoTracking().AnyAsync(e => e.Id == id && (isAdmin || e.CreateUserId == userId), ct);

    public async Task<MockExamDetailDto?> SetSectionQuestionsAsync(int id, int sectionId, IReadOnlyList<int> questionIds, int userId, bool isAdmin, CancellationToken ct)
    {
        if (!await CanEditAsync(id, userId, isAdmin, ct))
            return null;

        var exam = await db.Set<MockExam>().AsNoTracking().FirstAsync(e => e.Id == id, ct);
        if (exam.PublishedAt is not null)
            throw new MockExamRuleException(AlreadyPublished);

        var section = await db.Set<ExamTemplateSection>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId, ct);
        var session = section is null
            ? null
            : await db.Set<MockExamSession>().AsNoTracking().FirstOrDefaultAsync(s => s.MockExamId == id && s.ExamTemplateSessionId == section.ExamTemplateSessionId, ct);
        if (section is null || session is null)
            return null;

        var wanted = questionIds.Distinct().ToList();
        if (wanted.Count != questionIds.Count)
            throw new MockExamRuleException(QuestionTwice);
        if (wanted.Count > section.QuestionCount)
            throw new MockExamRuleException(TooManyQuestions);

        // Aynı soru iki derste sayılmaz; bütün oturumlara bakılır, soru başka oturumda da olmamalı.
        var otherSections = await db.Set<MockExamQuestion>().AsNoTracking()
            .Where(q => q.MockExamSession.MockExamId == id && q.ExamTemplateSectionId != sectionId)
            .Select(q => q.WorksheetQuestion.QuestionId)
            .ToListAsync(ct);
        if (wanted.Intersect(otherSections).Any())
            throw new MockExamRuleException(QuestionTwice);

        var current = await db.Set<MockExamQuestion>().AsNoTracking()
            .Where(q => q.MockExamSessionId == session.Id && q.ExamTemplateSectionId == sectionId)
            .Select(q => q.WorksheetQuestion.QuestionId)
            .ToListAsync(ct);

        // Bağlama kuralları (görünürlük, açık uçlu, lisans, yayın) testlerle aynı yerde; ya hepsi eklenir ya hiçbiri.
        var added = wanted.Except(current).ToList();
        if (added.Count > 0)
        {
            var linked = await links.LinkAsync(session.WorksheetId, added, userId, isAdmin, ct);
            if (linked is null || linked.ErrorCode is not null)
                throw new MockExamRuleException(QuestionNotUsable);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            db.SetCurrentUser(userId);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Silme yumuşaktır (AppDbContext): silinen bağ izleyicide kalır. Madde aynı izleyicide silinirse EF zorunlu ilişkinin koptuğunu söyler.
            var mapped = await db.Set<MockExamQuestion>()
                .Where(q => q.MockExamSessionId == session.Id && q.ExamTemplateSectionId == sectionId)
                .ToListAsync(ct);
            var itemIds = mapped.Select(q => q.WorksheetQuestionId).ToList();
            var questionOfItem = await db.TestQuestions.AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.QuestionId, ct);
            var dropped = mapped.Where(q => !wanted.Contains(questionOfItem[q.WorksheetQuestionId])).ToList();
            var droppedItemIds = dropped.Select(q => q.WorksheetQuestionId).ToList();
            db.RemoveRange(dropped);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            db.TestQuestions.RemoveRange(await db.TestQuestions.Where(i => droppedItemIds.Contains(i.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            var items = await db.TestQuestions.AsNoTracking()
                .Where(i => i.TestId == session.WorksheetId && wanted.Contains(i.QuestionId))
                .ToDictionaryAsync(i => i.QuestionId, i => i.Id, ct);
            var kept = (await db.Set<MockExamQuestion>()
                    .Where(q => q.MockExamSessionId == session.Id && q.ExamTemplateSectionId == sectionId)
                    .ToListAsync(ct))
                .ToDictionary(q => questionOfItem[q.WorksheetQuestionId]);
            for (var index = 0; index < wanted.Count; index++)
            {
                var questionId = wanted[index];
                if (kept.TryGetValue(questionId, out var row))
                {
                    row.Order = index + 1;
                }
                else
                {
                    db.Add(new MockExamQuestion { MockExamSessionId = session.Id, ExamTemplateSectionId = sectionId, WorksheetQuestionId = items[questionId], Order = index + 1 });
                }
            }
            await db.SaveChangesAsync(ct);

            await RenumberAsync(session.Id, session.WorksheetId, ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        return await ReadDetailAsync(id, ct);
    }

    /// <summary>Öğrenci maddeleri şablonun ders sırasıyla görür: Türkçe bitmeden matematik başlamaz.</summary>
    private async Task RenumberAsync(int sessionId, int worksheetId, CancellationToken ct)
    {
        var ordered = await db.Set<MockExamQuestion>()
            .Where(q => q.MockExamSessionId == sessionId)
            .OrderBy(q => q.Section.Order).ThenBy(q => q.Order)
            .Select(q => q.WorksheetQuestionId)
            .ToListAsync(ct);
        var items = await db.TestQuestions.Where(i => i.TestId == worksheetId).ToDictionaryAsync(i => i.Id, ct);
        var order = 0;
        foreach (var itemId in ordered)
            items[itemId].Order = ++order;
        await db.SaveChangesAsync(ct);
    }

    public async Task<MockExamDetailDto?> PublishAsync(int id, bool openToGrade, int userId, bool isAdmin, CancellationToken ct)
    {
        if (!await CanEditAsync(id, userId, isAdmin, ct))
            return null;

        var detail = (await ReadDetailAsync(id, ct))!;
        if (detail.PublishedAt is not null)
            throw new MockExamRuleException(AlreadyPublished);
        if (detail.Problems.Count > 0)
            return detail;

        db.SetCurrentUser(userId);
        var exam = await db.Set<MockExam>().Include(e => e.Sessions).ThenInclude(s => s.Worksheet).FirstAsync(e => e.Id == id, ct);
        exam.PublishedAt = DateTime.UtcNow;
        foreach (var session in exam.Sessions)
            session.Worksheet.StudentVisibility = openToGrade ? WorksheetStudentVisibility.Normal : WorksheetStudentVisibility.Restricted;
        await db.SaveChangesAsync(ct);
        return await ReadDetailAsync(id, ct);
    }

    public async Task<bool> DeleteDraftAsync(int id, int userId, bool isAdmin, CancellationToken ct)
    {
        if (!await CanEditAsync(id, userId, isAdmin, ct))
            return false;

        db.SetCurrentUser(userId);
        var exam = await db.Set<MockExam>().Include(e => e.Sessions).ThenInclude(s => s.Worksheet).FirstAsync(e => e.Id == id, ct);
        // Yayınlanmış denemenin oturumları ve sonuçları vardır.
        if (exam.PublishedAt is not null)
            throw new MockExamRuleException(AlreadyPublished);

        exam.IsDeleted = true;
        foreach (var session in exam.Sessions)
        {
            session.IsDeleted = true;
            session.Worksheet.IsDeleted = true;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<MockExamDetailDto?> ReadDetailAsync(int id, CancellationToken ct)
    {
        var exam = await db.Set<MockExam>().AsNoTracking()
            .Include(e => e.ExamTemplate)
            .Include(e => e.Grade)
            .Include(e => e.Sessions).ThenInclude(s => s.TemplateSession).ThenInclude(s => s.Sections)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null)
            return null;

        var sessionIds = exam.Sessions.Select(s => s.Id).ToList();
        var mapped = await db.Set<MockExamQuestion>().AsNoTracking()
            .Where(q => sessionIds.Contains(q.MockExamSessionId))
            .OrderBy(q => q.Order)
            .Select(q => new { q.ExamTemplateSectionId, q.WorksheetQuestion.QuestionId })
            .ToListAsync(ct);

        var detail = new MockExamDetailDto
        {
            Id = exam.Id,
            Name = exam.Name,
            ExamTemplateId = exam.ExamTemplateId,
            TemplateName = exam.ExamTemplate.Name,
            GradeId = exam.GradeId,
            GradeName = exam.Grade.Name,
            PublishedAt = exam.PublishedAt,
            Sessions = exam.Sessions.OrderBy(s => s.TemplateSession.Order).Select(s => new MockExamSessionDetailDto
            {
                Id = s.Id,
                Name = s.TemplateSession.Name,
                DurationSeconds = s.TemplateSession.DurationSeconds,
                WorksheetId = s.WorksheetId,
                Sections = s.TemplateSession.Sections.OrderBy(x => x.Order).Select(x => new MockExamSectionDetailDto
                {
                    SectionId = x.Id,
                    GroupName = x.GroupName,
                    Name = x.Name,
                    RequiredCount = x.QuestionCount,
                    QuestionIds = mapped.Where(q => q.ExamTemplateSectionId == x.Id).Select(q => q.QuestionId).ToList(),
                }).ToList(),
            }).ToList(),
        };
        detail.Problems = await FindProblemsAsync(detail, ct);
        return detail;
    }

    /// <summary>Yanlış soru sayısıyla yayın olmaz; soru eklendikten sonra yayından kalkmış ya da lisansı bitmiş olabilir.</summary>
    private async Task<List<MockExamProblemDto>> FindProblemsAsync(MockExamDetailDto detail, CancellationToken ct)
    {
        var sections = detail.Sessions.SelectMany(s => s.Sections).ToList();
        var problems = sections
            .Where(s => s.QuestionIds.Count != s.RequiredCount)
            .Select(s => new MockExamProblemDto { Code = "WrongCount", Section = s.Name, Required = s.RequiredCount, Actual = s.QuestionIds.Count })
            .ToList();

        var ids = sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await db.Questions.AsNoTracking()
            .Where(q => ids.Contains(q.Id))
            .Select(q => new { q.Id, q.IsUnpublished, q.LicenseEndsAt, q.CorrectAnswerId, q.InteractionType })
            .ToDictionaryAsync(q => q.Id, ct);
        var now = DateTime.UtcNow;
        foreach (var section in sections)
        {
            foreach (var id in section.QuestionIds)
            {
                // Silinmiş soru (filtre) ya da kurallara uymayan soru: net hesabı yanlış olur.
                if (!questions.TryGetValue(id, out var question))
                    problems.Add(new MockExamProblemDto { Code = "Unpublished", Section = section.Name, QuestionId = id });
                else if (question.IsUnpublished)
                    problems.Add(new MockExamProblemDto { Code = "Unpublished", Section = section.Name, QuestionId = id });
                else if (question.LicenseEndsAt <= now)
                    problems.Add(new MockExamProblemDto { Code = "LicenseEnded", Section = section.Name, QuestionId = id });
                else if (question.CorrectAnswerId is null && question.InteractionType is null or Interactions.McqGrader.Type)
                    problems.Add(new MockExamProblemDto { Code = "NoCorrectAnswer", Section = section.Name, QuestionId = id });
            }
        }
        return problems;
    }

    // ---- öğrenci

    private IQueryable<MockExam> VisibleTo(StudentProfileDto student, DateTime now)
    {
        var assigned = db.ActiveAssignmentsFor(student.Id, student.GradeId, student.SchoolId, now).Select(a => a.WorksheetId);
        return db.Set<MockExam>().AsNoTracking().Where(e => e.PublishedAt != null && e.Sessions.Any(s =>
            (e.GradeId == student.GradeId && s.Worksheet.StudentVisibility == WorksheetStudentVisibility.Normal) || assigned.Contains(s.WorksheetId)));
    }

    public async Task<List<StudentMockExamDto>> ListForStudentAsync(StudentProfileDto student, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var exams = await VisibleTo(student, now)
            .Include(e => e.ExamTemplate)
            .Include(e => e.Sessions).ThenInclude(s => s.TemplateSession).ThenInclude(s => s.Sections)
            .OrderByDescending(e => e.Id)
            .ToListAsync(ct);

        var worksheetIds = exams.SelectMany(e => e.Sessions).Select(s => s.WorksheetId).ToList();
        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == student.Id && worksheetIds.Contains(i.WorksheetId))
            .Select(i => new { i.Id, i.WorksheetId, i.Status, i.StartTime, i.MaxDurationSeconds })
            .ToListAsync(ct);

        return exams.Select(exam => new StudentMockExamDto
        {
            Id = exam.Id,
            Name = exam.Name,
            TemplateName = exam.ExamTemplate.Name,
            Sessions = exam.Sessions.OrderBy(s => s.TemplateSession.Order).Select(session =>
            {
                var instance = instances.FirstOrDefault(i => i.WorksheetId == session.WorksheetId);
                var status = instance is null ? null : (WorksheetInstanceStatus?)TestTimeLimit.EffectiveStatus(instance.Status, instance.StartTime, instance.MaxDurationSeconds, now);
                return new StudentMockExamSessionDto
                {
                    Name = session.TemplateSession.Name,
                    WorksheetId = session.WorksheetId,
                    DurationSeconds = session.TemplateSession.DurationSeconds,
                    QuestionCount = session.TemplateSession.Sections.Sum(s => s.QuestionCount),
                    InstanceId = instance?.Id,
                    Status = status switch
                    {
                        null => "NotStarted",
                        WorksheetInstanceStatus.Started => "InProgress",
                        _ => "Finished",
                    },
                };
            }).ToList(),
        }).ToList();
    }

    public async Task<MockExamResultDto?> GetResultAsync(int id, StudentProfileDto student, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var exam = await VisibleTo(student, now)
            .Include(e => e.ExamTemplate)
            .Include(e => e.Sessions).ThenInclude(s => s.TemplateSession).ThenInclude(s => s.Sections)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null)
            return null;

        var worksheetIds = exam.Sessions.Select(s => s.WorksheetId).ToList();
        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == student.Id && worksheetIds.Contains(i.WorksheetId))
            .Select(i => new { i.Id, i.WorksheetId, i.Status, i.StartTime, i.MaxDurationSeconds })
            .ToListAsync(ct);
        // Süren oturumun sonucu gösterilmez: öğrenci sınav sürerken doğrusunu öğrenmesin.
        var finished = instances
            .Where(i => TestTimeLimit.EffectiveStatus(i.Status, i.StartTime, i.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .ToList();
        var finishedIds = finished.Select(i => i.Id).ToList();
        var finishedWorksheets = finished.Select(i => i.WorksheetId).ToHashSet();

        // Oturum satırından okuma kuralı (#684): sonradan kaldırılan madde de sayılır.
        var rows = db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems().Where(r => finishedIds.Contains(r.WorksheetInstanceId));
        var correct = await CountBySectionAsync(rows.Where(WorksheetScoring.IsCorrectRow), ct);
        var wrong = await CountBySectionAsync(rows.Where(WorksheetScoring.IsWrongRow), ct);
        var blank = await CountBySectionAsync(rows.Where(WorksheetScoring.IsBlankRow), ct);

        var penalty = exam.ExamTemplate.WrongAnswersPerCorrect;
        var scores = new List<MockExamScoreDto>();
        foreach (var session in exam.Sessions.OrderBy(s => s.TemplateSession.Order))
        {
            var done = finishedWorksheets.Contains(session.WorksheetId);
            foreach (var section in session.TemplateSession.Sections.OrderBy(s => s.Order))
            {
                var c = done ? correct.GetValueOrDefault(section.Id) : 0;
                var w = done ? wrong.GetValueOrDefault(section.Id) : 0;
                scores.Add(new MockExamScoreDto
                {
                    GroupName = section.GroupName,
                    Name = section.Name,
                    QuestionCount = section.QuestionCount,
                    Correct = c,
                    Wrong = w,
                    Blank = done ? blank.GetValueOrDefault(section.Id) : 0,
                    Net = Net(c, w, penalty),
                    Pending = !done,
                });
            }
        }

        return new MockExamResultDto
        {
            Id = exam.Id,
            Name = exam.Name,
            TemplateName = exam.ExamTemplate.Name,
            WrongAnswersPerCorrect = penalty,
            Complete = scores.All(s => !s.Pending),
            Sections = scores,
            Correct = scores.Sum(s => s.Correct),
            Wrong = scores.Sum(s => s.Wrong),
            Blank = scores.Sum(s => s.Blank),
            // Yuvarlanmış ders netleri toplanmaz: toplam, sınavdaki gibi ham sayılardan hesaplanır.
            Net = Net(scores.Sum(s => s.Correct), scores.Sum(s => s.Wrong), penalty),
        };
    }

    /// <summary>Doğru − yanlış / ceza; ceza 0 ise yanlış düşülmez.</summary>
    internal static decimal Net(int correct, int wrong, int wrongAnswersPerCorrect) =>
        Math.Round(wrongAnswersPerCorrect > 0 ? correct - (decimal)wrong / wrongAnswersPerCorrect : correct, 2, MidpointRounding.AwayFromZero);

    private Task<Dictionary<int, int>> CountBySectionAsync(IQueryable<WorksheetInstanceQuestion> rows, CancellationToken ct) =>
        rows.Join(db.Set<MockExamQuestion>(), r => r.WorksheetQuestionId, m => m.WorksheetQuestionId, (r, m) => m.ExamTemplateSectionId)
            .GroupBy(sectionId => sectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, ct);
}
