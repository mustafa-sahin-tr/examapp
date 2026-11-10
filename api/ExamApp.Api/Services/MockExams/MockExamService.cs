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

    /// <summary>Issue #1031: denemeyi sınıfının ücretsiz denemesi yapar (sınıfta önceki seçim kalkar) ya da çıkarır; deneme yoksa null.</summary>
    Task<MockExamDetailDto?> SetFreeTrialAsync(int id, bool isFreeTrial, int userId, CancellationToken ct);
    Task<bool> DeleteDraftAsync(int id, int userId, bool isAdmin, CancellationToken ct);
    Task<List<StudentMockExamDto>> ListForStudentAsync(StudentProfileDto student, CancellationToken ct);
    Task<MockExamResultDto?> GetResultAsync(int id, StudentProfileDto student, CancellationToken ct);

    /// <summary>Issue #695: her şablon kodu için öğrencinin bütün oturumlarını bitirdiği en son deneme. Yetki çağıranın sorumluluğundadır.</summary>
    Task<List<MockExamResultDto>> GetLatestCompleteResultsAsync(int studentId, IReadOnlyCollection<string> templateCodes, CancellationToken ct);

    /// <summary>Bütün oturumları bitmiş son <paramref name="take"/> deneme, eskiden yeniye; öğrencinin hedefe yaklaşımı için.</summary>
    Task<List<MockExamResultDto>> GetCompleteResultHistoryAsync(int studentId, IReadOnlyCollection<string> templateCodes, int take, CancellationToken ct);
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
    public const string InvalidTemplate = "InvalidTemplate";
    public const string WrongGrade = "UnknownGrade";
    public const string TemplateInUse = "TemplateInUse";
    public const string OfficialTemplate = "OfficialTemplate";

    /// <summary>
    /// #917: kılavuzdan girilen resmî şablonlar (LGS, YKS TYT/AYT) kilitli; değişiklik kopyası üzerinde yapılır.
    /// Kodlar kurulum verisindekiyle aynı ve benzersiz olduğundan başka şablon bu kodu alamaz.
    /// </summary>
    public static readonly IReadOnlySet<string> OfficialCodes = new HashSet<string>(StringComparer.Ordinal) { "lgs", "tyt", "ayt" };
    public const string CodeTaken = "TemplateCodeTaken";
    public const string AlreadyPublished = "AlreadyPublished";
    public const string TooManyQuestions = "TooManyQuestions";
    public const string QuestionTwice = "QuestionUsedTwice";
    public const string QuestionNotUsable = "QuestionNotUsable";
    public const string FreeTrialNotOpen = "FreeTrialNotOpen";

    // ---- şablonlar

    public async Task<List<ExamTemplateDto>> ListTemplatesAsync(CancellationToken ct)
    {
        var templates = await db.Set<ExamTemplate>().AsNoTracking()
            .Include(t => t.Sessions).ThenInclude(s => s.Sections)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);
        var used = await db.Set<MockExam>().AsNoTracking()
            .GroupBy(e => e.ExamTemplateId)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TemplateId, x => x.Count, ct);
        return templates.Select(t => ToDto(t, used.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<ExamTemplateDto> SaveTemplateAsync(ExamTemplateDto dto, int? id, int userId, CancellationToken ct)
    {
        if (dto.Sessions.Any(s => s is null || s.Sections is null || s.Sections.Any(x => x is null)))
            throw new MockExamRuleException(InvalidTemplate);

        var code = dto.Code.Trim().ToLowerInvariant();
        if (await db.Set<ExamTemplate>().AnyAsync(t => t.Code == code && t.Id != id, ct))
            throw new MockExamRuleException(CodeTaken);

        db.SetCurrentUser(userId);
        ExamTemplate template;
        if (id is int existingId)
        {
            template = await db.Set<ExamTemplate>().Include(t => t.Sessions).ThenInclude(s => s.Sections).FirstOrDefaultAsync(t => t.Id == existingId, ct)
                ?? throw new MockExamRuleException(WrongTemplate);
            if (OfficialCodes.Contains(template.Code))
                throw new MockExamRuleException(OfficialTemplate);
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
        return ToDto(template, mockExamCount: 0);
    }

    private static ExamTemplateDto ToDto(ExamTemplate template, int mockExamCount) => new()
    {
        Id = template.Id,
        Code = template.Code,
        Name = template.Name,
        WrongAnswersPerCorrect = template.WrongAnswersPerCorrect,
        SourceReference = template.SourceReference,
        InUse = mockExamCount > 0,
        IsOfficial = OfficialCodes.Contains(template.Code),
        MockExamCount = mockExamCount,
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
            .Select(e => new MockExamListItemDto { Id = e.Id, Name = e.Name, TemplateName = e.ExamTemplate.Name, GradeName = e.Grade.Name, PublishedAt = e.PublishedAt, IsFreeTrial = e.IsFreeTrial })
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

        // Bağlama sıfır ve eksi kimlikleri sessizce atar: liste eksik yazılırdı.
        if (questionIds.Any(q => q <= 0))
            throw new MockExamRuleException(QuestionNotUsable);

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
            // Bağı kalmış ama maddesi silinmiş satır da düşer.
            var dropped = mapped.Where(q => !questionOfItem.TryGetValue(q.WorksheetQuestionId, out var questionId) || !wanted.Contains(questionId)).ToList();
            var droppedItemIds = dropped.Select(q => q.WorksheetQuestionId).ToList();
            db.RemoveRange(dropped);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            // Yarıda kalmış eski bir kayıt bağsız madde bırakmış olabilir: öğrenciye sayılmayan soru gitmesin.
            var allMapped = db.Set<MockExamQuestion>().Where(q => q.MockExamSessionId == session.Id).Select(q => q.WorksheetQuestionId);
            db.TestQuestions.RemoveRange(await db.TestQuestions
                .Where(i => i.TestId == session.WorksheetId && (droppedItemIds.Contains(i.Id) || (!allMapped.Contains(i.Id) && !wanted.Contains(i.QuestionId))))
                .ToListAsync(ct));
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
        {
            if (items.TryGetValue(itemId, out var item))
                item.Order = ++order;
        }
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
        exam.OpenToGrade = openToGrade;
        await db.SaveChangesAsync(ct);
        return await ReadDetailAsync(id, ct);
    }

    public async Task<MockExamDetailDto?> SetFreeTrialAsync(int id, bool isFreeTrial, int userId, CancellationToken ct)
    {
        var exam = await db.Set<MockExam>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null)
            return null;
        if (isFreeTrial && (exam.PublishedAt is null || !exam.OpenToGrade))
            throw new MockExamRuleException(FreeTrialNotOpen);

        db.SetCurrentUser(userId);
        if (isFreeTrial)
        {
            // Sınıf başına tek deneme (tekil indeks): önce eskisi kalkar.
            var previous = await db.Set<MockExam>().Where(e => e.GradeId == exam.GradeId && e.Id != id && e.IsFreeTrial).ToListAsync(ct);
            previous.ForEach(e => e.IsFreeTrial = false);
            await db.SaveChangesAsync(ct);
        }

        exam.IsFreeTrial = isFreeTrial;
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
            .Select(q => new { q.ExamTemplateSectionId, q.WorksheetQuestion.QuestionId, q.WorksheetQuestion.Question.Text, q.WorksheetQuestion.Question.ImageUrl })
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
            OpenToGrade = exam.OpenToGrade,
            IsFreeTrial = exam.IsFreeTrial,
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
                    Questions = mapped.Where(q => q.ExamTemplateSectionId == x.Id)
                        .Select(q => new MockExamQuestionCardDto { Id = q.QuestionId, Text = q.Text, ImageUrl = q.ImageUrl }).ToList(),
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
            .Select(q => new { q.Id, q.IsUnpublished, q.LicenseEndsAt, q.CorrectAnswerId, q.InteractionType, q.IsExample })
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
                // Örnek soru oturumda cevabını gösterir.
                else if (question.IsExample)
                    problems.Add(new MockExamProblemDto { Code = "ExampleQuestion", Section = section.Name, QuestionId = id });
                else if (question.CorrectAnswerId is null && question.InteractionType is null or Interactions.McqGrader.Type)
                    problems.Add(new MockExamProblemDto { Code = "NoCorrectAnswer", Section = section.Name, QuestionId = id });
            }
        }

        // Bağı olmayan madde öğrenciye gider ama hiçbir derse sayılmaz.
        var worksheetIds = detail.Sessions.Select(s => s.WorksheetId).ToList();
        var mappedItems = db.Set<MockExamQuestion>().AsNoTracking().Select(q => q.WorksheetQuestionId);
        var unmapped = await db.TestQuestions.AsNoTracking()
            .Where(i => worksheetIds.Contains(i.TestId) && !mappedItems.Contains(i.Id))
            .Select(i => i.QuestionId)
            .ToListAsync(ct);
        problems.AddRange(unmapped.Select(id => new MockExamProblemDto { Code = "Unmapped", Section = string.Empty, QuestionId = id }));
        return problems;
    }

    // ---- öğrenci

    private IQueryable<MockExam> VisibleTo(StudentProfileDto student, DateTime now)
    {
        // Başladığı deneme öğrencinin kalır: atama bitse ya da okul değişse de geçmiş sonucu görür (bkz. school-change.md).
        var assigned = db.ActiveAssignmentsFor(student.Id, student.GradeId, student.SchoolId, now).Select(a => a.WorksheetId);
        var started = db.TestInstances.Where(i => i.StudentId == student.Id).Select(i => i.WorksheetId);
        return db.Set<MockExam>().AsNoTracking().Where(e => e.PublishedAt != null && (
            (e.OpenToGrade && e.GradeId == student.GradeId)
            || e.Sessions.Any(s => assigned.Contains(s.WorksheetId) || started.Contains(s.WorksheetId))));
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

        var assigned = await db.ActiveAssignmentsFor(student.Id, student.GradeId, student.SchoolId, now)
            .Where(a => worksheetIds.Contains(a.WorksheetId))
            .Select(a => a.WorksheetId)
            .ToListAsync(ct);

        return exams.Select(exam => new StudentMockExamDto
        {
            Id = exam.Id,
            Name = exam.Name,
            TemplateName = exam.ExamTemplate.Name,
            IsFreeTrial = exam.IsFreeTrial,
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
                    CanStart = instance is not null || assigned.Contains(session.WorksheetId) || (exam.OpenToGrade && exam.GradeId == student.GradeId),
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

        var result = await BuildResultAsync(exam, student.Id, now, ct);
        result.Ranking = await RankingAsync(exam, result, now, ct);
        if (result.Complete)
            result.Questions = await QuestionResultsAsync(exam, student.Id, now, ct);
        return result;
    }

    public async Task<List<MockExamResultDto>> GetLatestCompleteResultsAsync(int studentId, IReadOnlyCollection<string> templateCodes, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var latestIds = (await CompleteAttemptsAsync(studentId, templateCodes, now, ct))
            .GroupBy(e => e.Code)
            .Select(g => g.MaxBy(e => e.FinishedAt).Id)
            .ToList();

        var exams = await db.Set<MockExam>().AsNoTracking()
            .Include(e => e.ExamTemplate)
            .Include(e => e.Sessions).ThenInclude(s => s.TemplateSession).ThenInclude(s => s.Sections)
            .Where(e => latestIds.Contains(e.Id))
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        var results = new List<MockExamResultDto>(exams.Count);
        foreach (var exam in exams)
        {
            var result = await BuildResultAsync(exam, studentId, now, ct);
            result.Ranking = await RankingAsync(exam, result, now, ct);
            results.Add(result);
        }
        return results;
    }

    public async Task<List<MockExamResultDto>> GetCompleteResultHistoryAsync(int studentId, IReadOnlyCollection<string> templateCodes, int take, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var recent = (await CompleteAttemptsAsync(studentId, templateCodes, now, ct))
            .OrderByDescending(e => e.FinishedAt)
            .Take(take)
            .ToList();
        var ids = recent.Select(e => e.Id).ToList();

        var exams = await db.Set<MockExam>().AsNoTracking()
            .Include(e => e.ExamTemplate)
            .Include(e => e.Sessions).ThenInclude(s => s.TemplateSession).ThenInclude(s => s.Sections)
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct);

        var results = new List<MockExamResultDto>(exams.Count);
        foreach (var attempt in recent.AsEnumerable().Reverse())
        {
            var result = await BuildResultAsync(exams.First(e => e.Id == attempt.Id), studentId, now, ct);
            result.FinishedAt = attempt.FinishedAt;
            results.Add(result);
        }
        return results;
    }

    private async Task<List<(int Id, string Code, DateTime FinishedAt)>> CompleteAttemptsAsync(
        int studentId, IReadOnlyCollection<string> templateCodes, DateTime now, CancellationToken ct)
    {
        var sessions = db.Set<MockExamSession>().Where(s => s.MockExam.PublishedAt != null && templateCodes.Contains(s.MockExam.ExamTemplate.Code));
        var attempts = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == studentId)
            .Join(sessions, i => i.WorksheetId, s => s.WorksheetId, (i, s) => new
            {
                s.MockExamId,
                s.MockExam.ExamTemplate.Code,
                SessionCount = s.MockExam.Sessions.Count,
                i.Status, i.StartTime, i.EndTime, i.MaxDurationSeconds,
            })
            .ToListAsync(ct);

        return attempts
            .GroupBy(a => a.MockExamId)
            .Where(g => g.Count() == g.First().SessionCount
                && g.All(a => TestTimeLimit.EffectiveStatus(a.Status, a.StartTime, a.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started))
            .Select(g => (g.Key, g.First().Code, g.Max(a => a.EndTime ?? a.StartTime.AddSeconds(a.MaxDurationSeconds ?? 0))))
            .ToList();
    }

    private async Task<MockExamResultDto> BuildResultAsync(MockExam exam, int studentId, DateTime now, CancellationToken ct)
    {
        var worksheetIds = exam.Sessions.Select(s => s.WorksheetId).ToList();
        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == studentId && worksheetIds.Contains(i.WorksheetId))
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
        var blank = await CountBySectionAsync(rows.Where(WorksheetScoring.IsBlankRow), ct);
        // Yanlış = toplam − doğru − boş (WorksheetScoring): sorusu sonradan silinen cevaplı satır yanlış sayılır, net şişmez.
        var total = await CountBySectionAsync(rows, ct);

        var penalty = exam.ExamTemplate.WrongAnswersPerCorrect;
        var scores = new List<MockExamScoreDto>();
        foreach (var session in exam.Sessions.OrderBy(s => s.TemplateSession.Order))
        {
            var done = finishedWorksheets.Contains(session.WorksheetId);
            foreach (var section in session.TemplateSession.Sections.OrderBy(s => s.Order))
            {
                var c = done ? correct.GetValueOrDefault(section.Id) : 0;
                var w = done ? total.GetValueOrDefault(section.Id) - c - blank.GetValueOrDefault(section.Id) : 0;
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
            TemplateCode = exam.ExamTemplate.Code,
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

    /// <summary>
    /// #1094: denemeyi bütün oturumlarıyla bitiren öğrenciler arasında öğrencinin yeri; sonucu tamam değilse null. Netler
    /// <see cref="BuildResultAsync"/> ile aynı kuralla (oturum satırı, #684; yanlış = toplam − doğru − boş) toplu hesaplanır.
    /// </summary>
    private async Task<MockExamRankingDto?> RankingAsync(MockExam exam, MockExamResultDto result, DateTime now, CancellationToken ct)
    {
        if (!result.Complete)
            return null;

        var worksheetIds = exam.Sessions.Select(s => s.WorksheetId).ToList();
        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => worksheetIds.Contains(i.WorksheetId))
            .Select(i => new { i.Id, i.StudentId, i.WorksheetId, i.Status, i.StartTime, i.MaxDurationSeconds })
            .ToListAsync(ct);
        // Bütün oturumları bitmiş öğrenciler (süren ya da başlanmamış oturumu olan sayılmaz).
        var finished = instances
            .Where(i => TestTimeLimit.EffectiveStatus(i.Status, i.StartTime, i.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .GroupBy(i => i.StudentId)
            .Where(g => g.Select(i => i.WorksheetId).Distinct().Count() == worksheetIds.Count)
            .SelectMany(g => g)
            .ToList();
        var studentOf = finished.ToDictionary(i => i.Id, i => i.StudentId);
        var finishedIds = studentOf.Keys.ToList();

        var rows = db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems().Where(r => finishedIds.Contains(r.WorksheetInstanceId));
        var correct = await CountByInstanceAsync(rows.Where(WorksheetScoring.IsCorrectRow), ct);
        var blank = await CountByInstanceAsync(rows.Where(WorksheetScoring.IsBlankRow), ct);
        var total = await CountByInstanceAsync(rows, ct);

        var penalty = exam.ExamTemplate.WrongAnswersPerCorrect;
        var nets = finishedIds
            .GroupBy(id => studentOf[id])
            .Select(g =>
            {
                var c = g.Sum(id => correct.GetValueOrDefault(id));
                var w = g.Sum(id => total.GetValueOrDefault(id)) - c - g.Sum(id => blank.GetValueOrDefault(id));
                return Net(c, w, penalty);
            })
            .ToList();

        var takers = nets.Count;
        if (takers < MockExamRankingRules.MinTakers)
            return new MockExamRankingDto { Takers = takers };
        var rank = 1 + nets.Count(n => n > result.Net);
        return new MockExamRankingDto { Takers = takers, Rank = rank, TopPercent = (int)Math.Ceiling(100m * rank / takers) };
    }

    /// <summary>#1094: maddeler ders ve ders içi sırayla; başarı yalnız bu denemenin oturumlarından (QuestionAnswerStatsReader).</summary>
    private async Task<List<MockExamQuestionResultDto>> QuestionResultsAsync(MockExam exam, int studentId, DateTime now, CancellationToken ct)
    {
        var sections = exam.Sessions
            .SelectMany(s => s.TemplateSession.Sections.Select(section => (Section: section, SessionOrder: s.TemplateSession.Order)))
            .ToDictionary(x => x.Section.Id);
        var items = await db.Set<MockExamQuestion>().AsNoTracking().WithRemovedWorksheetItems()
            .Where(m => m.MockExamSession.MockExamId == exam.Id)
            .Select(m => new { m.ExamTemplateSectionId, m.Order, m.WorksheetQuestionId, m.WorksheetQuestion.QuestionId })
            .ToListAsync(ct);

        var worksheetIds = exam.Sessions.Select(s => s.WorksheetId).ToList();
        var itemIds = items.Select(i => i.WorksheetQuestionId).ToList();
        var answers = await db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => r.WorksheetInstance.StudentId == studentId && worksheetIds.Contains(r.WorksheetInstance.WorksheetId))
            .Where(r => itemIds.Contains(r.WorksheetQuestionId))
            .Select(r => new { r.WorksheetQuestionId, r.SelectedAnswerId, HasPayload = r.AnswerPayload != null, r.IsCorrect })
            .ToListAsync(ct);
        var answerOf = answers.GroupBy(a => a.WorksheetQuestionId).ToDictionary(g => g.Key, g => g.First());

        var questionIds = items.Select(i => i.QuestionId).Distinct().ToList();
        var questions = await db.Questions.AsNoTracking().IgnoreQueryFilters()
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.CorrectAnswerId, q.IsDeleted })
            .ToDictionaryAsync(q => q.Id, ct);
        var stats = await QuestionAnswerStatsReader.ReadAsync(db, questionIds, now, worksheetIds, ct);

        return items
            .Where(i => sections.ContainsKey(i.ExamTemplateSectionId))
            .OrderBy(i => sections[i.ExamTemplateSectionId].SessionOrder)
            .ThenBy(i => sections[i.ExamTemplateSectionId].Section.Order)
            .ThenBy(i => i.Order)
            .Select(i =>
            {
                var section = sections[i.ExamTemplateSectionId].Section;
                var question = questions.GetValueOrDefault(i.QuestionId);
                var outcome = answerOf.GetValueOrDefault(i.WorksheetQuestionId) is not { } a || WorksheetScoring.IsBlank(a.SelectedAnswerId, a.HasPayload)
                    ? "blank"
                    : WorksheetScoring.IsCorrect(a.SelectedAnswerId, question?.CorrectAnswerId, a.HasPayload, a.IsCorrect,
                        questionKnown: question is { IsDeleted: false }) ? "correct" : "wrong";
                return new MockExamQuestionResultDto
                {
                    GroupName = section.GroupName,
                    SectionName = section.Name,
                    Number = i.Order,
                    Outcome = outcome,
                    Stats = stats.GetValueOrDefault(i.QuestionId),
                };
            })
            .ToList();
    }

    private Task<Dictionary<int, int>> CountByInstanceAsync(IQueryable<WorksheetInstanceQuestion> rows, CancellationToken ct) =>
        rows.Join(db.Set<MockExamQuestion>(), r => r.WorksheetQuestionId, m => m.WorksheetQuestionId, (r, m) => r.WorksheetInstanceId)
            .GroupBy(id => id)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

    /// <summary>Doğru − yanlış / ceza; ceza 0 ise yanlış düşülmez.</summary>
    internal static decimal Net(int correct, int wrong, int wrongAnswersPerCorrect) =>
        Math.Round(wrongAnswersPerCorrect > 0 ? correct - (decimal)wrong / wrongAnswersPerCorrect : correct, 2, MidpointRounding.AwayFromZero);

    private Task<Dictionary<int, int>> CountBySectionAsync(IQueryable<WorksheetInstanceQuestion> rows, CancellationToken ct) =>
        rows.Join(db.Set<MockExamQuestion>(), r => r.WorksheetQuestionId, m => m.WorksheetQuestionId, (r, m) => m.ExamTemplateSectionId)
            .GroupBy(sectionId => sectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, ct);
}
