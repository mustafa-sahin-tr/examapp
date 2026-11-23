using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #85: admin dashboard özet sayaçları (GET /api/admin/dashboard/summary).
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private DashboardService NewService(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabase_AllCountsAreZeroAndRatioIsZero()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetSummaryAsync();

        result.TeacherCount.ShouldBe(0);
        result.StudentCount.ShouldBe(0);
        result.WorksheetCount.ShouldBe(0);
        result.QuestionCount.ShouldBe(0);
        result.AiClassifiedQuestionCount.ShouldBe(0);
        result.AiClassifiedRatio.ShouldBe(0);
        result.LastAiClassificationAt.ShouldBeNull();
        result.FailedOutboxCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetSummaryAsync_SystemStatus_LastAiClassificationAndFailedOutbox_issue887()
    {
        var aiAt = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Add(new Question { Text = "ai", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "human", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.OutboxMessages.Add(new ExamApp.Foundation.Persistence.OutboxMessage { Type = "T", Content = "{}", Error = "boom" });
            ctx.OutboxMessages.Add(new ExamApp.Foundation.Persistence.OutboxMessage { Type = "T", Content = "{}", Error = "old", ProcessedAt = aiAt });
            ctx.OutboxMessages.Add(new ExamApp.Foundation.Persistence.OutboxMessage { Type = "T", Content = "{}" });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = _db.NewContext())
        {
            // Denetim yakalayıcısı zaman damgasını kendisi yazar; sınıflandırma anı elle sabitlenir.
            await ctx.Questions.Where(q => q.Text == "ai").ExecuteUpdateAsync(u => u.SetProperty(q => q.UpdateTime, aiAt));
            await ctx.Questions.Where(q => q.Text == "human").ExecuteUpdateAsync(u => u.SetProperty(q => q.UpdateTime, aiAt.AddDays(1)));
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.LastAiClassificationAt.ShouldBe(aiAt);
        result.FailedOutboxCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSummaryAsync_MixedClassificationSources_CountsOnlyAiAndComputesRatio()
    {
        await using (var ctx = _db.NewContext())
        {
            // 2 AI, 1 Human, 1 null (unclassified) => 4 total, AI ratio = 2/4 = 0.5
            ctx.Questions.Add(new Question { Text = "q1", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "q2", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "q3", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.Questions.Add(new Question { Text = "q4", Point = 1, ClassificationSource = null });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.QuestionCount.ShouldBe(4);
        result.AiClassifiedQuestionCount.ShouldBe(2);
        result.AiClassifiedRatio.ShouldBe(0.5);
    }

    [Fact]
    public async Task GetSummaryAsync_NoAiClassifiedQuestions_RatioIsZeroNotNaN()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Add(new Question { Text = "q1", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.Questions.Add(new Question { Text = "q2", Point = 1, ClassificationSource = null });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.QuestionCount.ShouldBe(2);
        result.AiClassifiedQuestionCount.ShouldBe(0);
        result.AiClassifiedRatio.ShouldBe(0);
        double.IsNaN(result.AiClassifiedRatio).ShouldBeFalse();
    }

    [Fact]
    public async Task GetSummaryAsync_SeedsTeachersStudentsWorksheetsAndQuestions_ReturnsExactCounts()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.Teachers.Add(new Teacher { UserId = 1 });
            ctx.Teachers.Add(new Teacher { UserId = 2 });
            ctx.Teachers.Add(new Teacher { UserId = 3 });

            ctx.Students.Add(new Student { UserId = 10, StudentNumber = "a", SchoolName = "s" });
            ctx.Students.Add(new Student { UserId = 11, StudentNumber = "b", SchoolName = "s" });

            ctx.Worksheets.Add(new Worksheet { Name = "WS1", Description = "", GradeId = grade.Id });

            ctx.Questions.Add(new Question { Text = "q1", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "q2", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.Questions.Add(new Question { Text = "q3", Point = 1, ClassificationSource = ClassificationSource.AI });

            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.TeacherCount.ShouldBe(3);
        result.StudentCount.ShouldBe(2);
        result.WorksheetCount.ShouldBe(1);
        result.QuestionCount.ShouldBe(3);
        result.AiClassifiedQuestionCount.ShouldBe(2);
        result.AiClassifiedRatio.ShouldBe(2.0 / 3.0, 0.0001);
    }

    [Fact]
    public async Task GetSummaryAsync_IsNotScopedByCurrentUser_CountsAllTeachersAcrossOwners()
    {
        // Dashboard is an admin-wide aggregate, unlike per-teacher services — it must not
        // apply any SetCurrentUser-based row filtering.
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(1);
            ctx.Teachers.Add(new Teacher { UserId = 1 });
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(2);
            ctx.Teachers.Add(new Teacher { UserId = 2 });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.TeacherCount.ShouldBe(2);
    }

    // ---- Issue #1315: sınıflandırma kırılımı ve yeniden sınıflandırma ----

    private sealed record Taxonomy(int GradeId, int SubjectId, int TopicId, int SubTopicId);

    private static async Task<Taxonomy> SeedTaxonomyAsync(AppDbContext ctx)
    {
        var grade = new Grade { Name = "5" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Add(topic);
        await ctx.SaveChangesAsync();
        var subTopic = new SubTopic { Name = "Kesir karşılaştırma", TopicId = topic.Id };
        ctx.Add(subTopic);
        await ctx.SaveChangesAsync();
        return new Taxonomy(grade.Id, subject.Id, topic.Id, subTopic.Id);
    }

    private static Question Q(string text, ClassificationSource? source, Taxonomy? classifiedAs = null) => new()
    {
        Text = text,
        Point = 1,
        ClassificationSource = source,
        SubjectId = classifiedAs?.SubjectId,
        TopicId = classifiedAs?.TopicId,
    };

    [Fact]
    public async Task GetSummaryAsync_ClassificationBreakdown_SeparatesFiveGroups_issue1315()
    {
        await using (var ctx = _db.NewContext())
        {
            var tax = await SeedTaxonomyAsync(ctx);
            ctx.Questions.AddRange(
                Q("ai1", ClassificationSource.AI, tax), Q("ai2", ClassificationSource.AI, tax),
                Q("book", ClassificationSource.Book, tax),
                Q("human", ClassificationSource.Human, tax), Q("legacy", null, tax),
                Q("aiNoTopic", ClassificationSource.AI),
                Q("never", ClassificationSource.Human), Q("neverNull", null));
            // #690: yayından kaldırılmış / lisansı bitmiş sorular gruplara girmez.
            var withdrawn = Q("withdrawn", ClassificationSource.AI);
            withdrawn.IsUnpublished = true;
            var expired = Q("expired", null);
            expired.LicenseEndsAt = DateTime.UtcNow.AddDays(-1);
            ctx.Questions.AddRange(withdrawn, expired);
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.QuestionCount.ShouldBe(10);
        result.Classification.Unpublished.ShouldBe(2);

        result.Classification.Ai.ShouldBe(2);
        result.Classification.Book.ShouldBe(1);
        result.Classification.Teacher.ShouldBe(2);
        result.Classification.AiNoTopic.ShouldBe(1);
        result.Classification.NotClassified.ShouldBe(2);
        result.Classification.ClassifiedRatio.ShouldBe(5.0 / 8.0, 0.0001);
    }

    [Fact]
    public async Task QueueClassificationRetryAsync_NotClassified_QueuesOnlyPublishedTopiclessNonAiQuestionsWithoutSubTopics_issue1315()
    {
        int neverId, nullId, withGradeId;
        Taxonomy tax;
        await using (var ctx = _db.NewContext())
        {
            tax = await SeedTaxonomyAsync(ctx);
            var never = Q("never", ClassificationSource.Human);
            var nullSource = Q("null", null);
            var inTest = Q("inTest", ClassificationSource.Book);
            var withSubTopic = Q("subTopic", ClassificationSource.Book);
            withSubTopic.QuestionSubTopics.Add(new QuestionSubTopic { SubTopicId = tax.SubTopicId });
            var withdrawn = Q("withdrawn", ClassificationSource.Human);
            withdrawn.IsUnpublished = true;
            ctx.Questions.AddRange(never, nullSource, inTest, withSubTopic, withdrawn,
                Q("aiNoTopic", ClassificationSource.AI), Q("classified", ClassificationSource.Human, tax));
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = tax.GradeId };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = inTest.Id, Order = 1 });
            await ctx.SaveChangesAsync();
            (neverId, nullId, withGradeId) = (never.Id, nullSource.Id, inTest.Id);
        }

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).QueueClassificationRetryAsync(ClassificationRetryGroup.NotClassified);
            result.Queued.ShouldBe(3);
            result.Remaining.ShouldBe(0);
        }

        await using var check = _db.NewContext();
        var events = (await check.OutboxMessages.AsNoTracking().ToListAsync())
            .Select(m =>
            {
                m.Type.ShouldBe(ExamApp.Foundation.Contracts.OutboxEventRegistry.NameFor<ExamApp.Foundation.Contracts.QuestionCreatedEvent>());
                return System.Text.Json.JsonSerializer.Deserialize<ExamApp.Foundation.Contracts.QuestionCreatedEvent>(m.Content)!;
            })
            .ToList();
        events.Select(e => e.QuestionId).OrderBy(id => id).ShouldBe(new[] { neverId, nullId, withGradeId }.OrderBy(id => id));
        events.ShouldAllBe(e => e.ClassificationSource == null && !e.TaxonomyLocked);
        events.Single(e => e.QuestionId == withGradeId).GradeId.ShouldBe(tax.GradeId);
        events.Single(e => e.QuestionId == neverId).GradeId.ShouldBeNull();
    }

    [Fact]
    public async Task QueueClassificationRetryAsync_AiNoTopic_QueuesOnlyAiQuestionsWithoutTopic_issue1315()
    {
        int aiNoTopicId;
        await using (var ctx = _db.NewContext())
        {
            var tax = await SeedTaxonomyAsync(ctx);
            var aiNoTopic = Q("aiNoTopic", ClassificationSource.AI);
            ctx.Questions.AddRange(aiNoTopic, Q("ai", ClassificationSource.AI, tax), Q("never", ClassificationSource.Human));
            await ctx.SaveChangesAsync();
            aiNoTopicId = aiNoTopic.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).QueueClassificationRetryAsync(ClassificationRetryGroup.AiNoTopic);
            result.Queued.ShouldBe(1);
        }

        await using var check = _db.NewContext();
        var message = await check.OutboxMessages.AsNoTracking().SingleAsync();
        var @event = System.Text.Json.JsonSerializer.Deserialize<ExamApp.Foundation.Contracts.QuestionCreatedEvent>(message.Content)!;
        @event.QuestionId.ShouldBe(aiNoTopicId);
        // Tüketici kaynağı "AI" olan olayı atlar; yeniden deneme kaynağı boş gönderir.
        @event.ClassificationSource.ShouldBeNull();
    }

    [Fact]
    public async Task QueueClassificationRetryAsync_MoreThanBatch_QueuesBatchAndReportsRemaining_issue1315()
    {
        await using (var ctx = _db.NewContext())
        {
            for (var i = 0; i < DashboardService.ClassificationRetryBatchSize + 5; i++)
                ctx.Questions.Add(Q($"q{i}", null));
            await ctx.SaveChangesAsync();
        }

        await using var run = _db.NewContext();
        var result = await NewService(run).QueueClassificationRetryAsync(ClassificationRetryGroup.NotClassified);

        result.Queued.ShouldBe(DashboardService.ClassificationRetryBatchSize);
        result.Remaining.ShouldBe(5);
    }

    public void Dispose() => _db.Dispose();
}
