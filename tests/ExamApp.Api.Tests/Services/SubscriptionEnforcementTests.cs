using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1031: plan sınırlarının uygulanması. AI sınıflandırma: sınır doluysa soru kaydedilir, olay yazılmaz. Deneme: Free
/// öğrenci yalnız sınıfın ücretsiz denemesini başlatır; atanan deneme öğrencinin planına sayılmaz.
/// </summary>
public class SubscriptionEnforcementTests : IDisposable
{
    private const int TeacherUserId = 71;
    private const int FreeClassificationLimit = 20; // SubscriptionPlanSeed
    private const int StudentUserId = 400;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static EntitlementService Entitlements(AppDbContext ctx) => new(ctx, TimeProvider.System);

    // ---- AI sınıflandırma

    private static BulkQuestionCreateDto Bulk(int questionCount) => new()
    {
        ImageData = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100)),
        Passages = new List<BulkPassageDto>(),
        Header = new HeaderInfo(),
        Questions = Enumerable.Range(0, questionCount).Select(i => new BulkQuestionDto
        {
            Name = $"soru {i}",
            InteractionType = "OpenText",
            InteractionPlan = """{"version":1,"type":"openText","minWords":10,"maxWords":80,"lines":6}""",
            InteractionSolution = """{"sampleAnswer":"a","rubric":"b"}""",
            Answers = new List<BulkAnswerDto>(),
            X = 0, Y = 0, Width = 10, Height = 10,
        }).ToList(),
    };

    private async Task<QuestionSavedDto> SaveBulkAsync(int questionCount, EntitlementSubject? owner)
    {
        await using var ctx = _db.NewContext();
        var service = new QuestionService(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), entitlements: Entitlements(ctx));
        var result = await service.SaveBulkQuestion(Bulk(questionCount), TeacherUserId, owner);
        result.Success.ShouldBeTrue(result.Message);
        return result.ShouldBeOfType<QuestionSavedDto>();
    }

    private async Task UseAsync(int userId, string feature, int count)
    {
        await using var ctx = _db.NewContext();
        ctx.EntitlementUsages.AddRange(Enumerable.Range(0, count).Select(i => new EntitlementUsage
        {
            UserId = userId,
            FeatureKey = feature,
            ReferenceKey = $"seed:{userId}:{i}",
            UsedAtUtc = DateTime.UtcNow,
        }));
        await ctx.SaveChangesAsync();
    }

    private async Task<(int Questions, int Events, int Usages)> ClassificationCountsAsync()
    {
        await using var ctx = _db.NewContext();
        var eventName = OutboxEventRegistry.NameFor<QuestionCreatedEvent>();
        return (await ctx.Questions.CountAsync(),
            await ctx.OutboxMessages.CountAsync(m => m.Type == eventName),
            await ctx.EntitlementUsages.CountAsync(u => u.FeatureKey == EntitlementFeatures.AiClassification && u.ReferenceKey.StartsWith("question:")));
    }

    [Fact]
    public async Task Classification_under_the_limit_sends_every_question_and_counts_them()
    {
        var result = await SaveBulkAsync(2, EntitlementSubject.Create(TeacherUserId));

        result.ClassificationSkipped.ShouldBe(0);
        (await ClassificationCountsAsync()).ShouldBe((2, 2, 2));
    }

    [Fact]
    public async Task Classification_reaching_the_limit_saves_all_questions_but_sends_only_the_remaining_allowance()
    {
        await UseAsync(TeacherUserId, EntitlementFeatures.AiClassification, FreeClassificationLimit - 1);

        var result = await SaveBulkAsync(3, EntitlementSubject.Create(TeacherUserId));

        result.ClassificationSkipped.ShouldBe(2);
        (await ClassificationCountsAsync()).ShouldBe((3, 1, 1));
    }

    [Fact]
    public async Task Classification_at_the_limit_saves_the_question_without_an_event()
    {
        await UseAsync(TeacherUserId, EntitlementFeatures.AiClassification, FreeClassificationLimit);

        var result = await SaveBulkAsync(1, EntitlementSubject.Create(TeacherUserId));

        result.ClassificationSkipped.ShouldBe(1);
        (await ClassificationCountsAsync()).ShouldBe((1, 0, 0));
    }

    [Theory]
    [InlineData(EntitlementRoles.Admin)]
    [InlineData(EntitlementRoles.QuestionTrainer)]
    public async Task Classification_is_unlimited_for_exempt_roles(string role)
    {
        await UseAsync(TeacherUserId, EntitlementFeatures.AiClassification, FreeClassificationLimit);

        var result = await SaveBulkAsync(2, EntitlementSubject.Create(TeacherUserId, role));

        result.ClassificationSkipped.ShouldBe(0);
        (await ClassificationCountsAsync()).ShouldBe((2, 2, 2));
    }

    [Fact]
    public async Task Classification_without_an_owner_is_not_metered()
    {
        // Kitap ve hızlı ekleme işleri: maliyet kendi özelliklerine yazılır.
        await UseAsync(TeacherUserId, EntitlementFeatures.AiClassification, FreeClassificationLimit);

        var result = await SaveBulkAsync(2, owner: null);

        result.ClassificationSkipped.ShouldBe(0);
        (await ClassificationCountsAsync()).ShouldBe((2, 2, 0));
    }

    // ---- deneme

    private sealed record MockWorld(int StudentId, int GradeId, int ExamId, int Worksheet1, int Worksheet2);

    private static async Task<MockExam> AddExamAsync(AppDbContext ctx, int gradeId, ExamTemplate template, bool isFreeTrial = false, string name = "Deneme")
    {
        var exam = new MockExam
        {
            ExamTemplateId = template.Id,
            Name = name,
            GradeId = gradeId,
            PublishedAt = DateTime.UtcNow,
            OpenToGrade = true,
            IsFreeTrial = isFreeTrial,
        };
        foreach (var session in template.Sessions)
        {
            exam.Sessions.Add(new MockExamSession
            {
                ExamTemplateSessionId = session.Id,
                Worksheet = new Worksheet
                {
                    Name = $"{name} {session.Name}",
                    Description = "",
                    GradeId = gradeId,
                    MaxDurationSeconds = 600,
                    StudentVisibility = WorksheetStudentVisibility.Restricted,
                },
            });
        }

        ctx.Add(exam);
        await ctx.SaveChangesAsync();
        return exam;
    }

    private async Task<MockWorld> SeedMockExamAsync(bool isFreeTrial = false)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8" };
        var student = new Student { UserId = StudentUserId, StudentNumber = "S1", SchoolName = "Sch", Grade = grade };
        var template = new ExamTemplate
        {
            Code = "t1031",
            Name = "Şablon",
            Sessions =
            {
                new ExamTemplateSession { Name = "Sözel", Order = 1, DurationSeconds = 600 },
                new ExamTemplateSession { Name = "Sayısal", Order = 2, DurationSeconds = 600 },
            },
        };
        ctx.AddRange(student, template);
        await ctx.SaveChangesAsync();
        var exam = await AddExamAsync(ctx, grade.Id, template, isFreeTrial);
        var sessions = exam.Sessions.OrderBy(s => s.Id).ToList();
        return new MockWorld(student.Id, grade.Id, exam.Id, sessions[0].WorksheetId, sessions[1].WorksheetId);
    }

    private async Task AssignPlanAsync(string planKey)
    {
        await using var ctx = _db.NewContext();
        (await Entitlements(ctx).AssignPlanAsync(StudentUserId, planKey, null, null, 1)).ShouldBe(AssignPlanError.None);
    }

    private async Task<TestStartResultDto> StartAsync(MockWorld world, int worksheetId)
    {
        await using var ctx = _db.NewContext();
        var service = new TestSessionService(ctx, entitlements: Entitlements(ctx));
        return await service.StartTestAsync(worksheetId, new StudentProfileDto { Id = world.StudentId, GradeId = world.GradeId });
    }

    private async Task<int> MockUsagesAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.EntitlementUsages.CountAsync(u => u.FeatureKey == EntitlementFeatures.MockExam && u.UserId == StudentUserId);
    }

    private async Task<int> InstancesAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstances.CountAsync();
    }

    [Fact]
    public async Task Free_student_cannot_start_an_open_mock_exam()
    {
        var world = await SeedMockExamAsync();

        var ex = await Should.ThrowAsync<EntitlementLimitException>(() => StartAsync(world, world.Worksheet1));

        ex.FeatureKey.ShouldBe(EntitlementFeatures.MockExam);
        ex.Check.PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
        (await InstancesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Free_student_can_start_the_free_trial_exam_without_using_the_quota()
    {
        var world = await SeedMockExamAsync(isFreeTrial: true);

        (await StartAsync(world, world.Worksheet1)).Success.ShouldBeTrue();

        (await MockUsagesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Assigned_mock_exam_stays_open_and_is_not_billed_to_the_student()
    {
        var world = await SeedMockExamAsync();
        await using (var seed = _db.NewContext())
        {
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = world.Worksheet1,
                StudentId = world.StudentId,
                StartAt = DateTime.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        (await StartAsync(world, world.Worksheet1)).Success.ShouldBeTrue();

        (await MockUsagesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Trial_plan_counts_one_use_per_exam_and_blocks_at_the_limit()
    {
        var world = await SeedMockExamAsync();
        await AssignPlanAsync(SubscriptionPlan.TrialKey);
        await UseAsync(StudentUserId, EntitlementFeatures.MockExam, 1); // Trial sınırı 2

        (await StartAsync(world, world.Worksheet1)).Success.ShouldBeTrue();
        (await MockUsagesAsync()).ShouldBe(2);

        // Aynı denemenin ikinci oturumu sınır dolu olsa da açılır, yeniden sayılmaz.
        (await StartAsync(world, world.Worksheet2)).Success.ShouldBeTrue();
        (await MockUsagesAsync()).ShouldBe(2);

        int otherWorksheet;
        await using (var ctx = _db.NewContext())
        {
            var template = await ctx.Set<ExamTemplate>().Include(t => t.Sessions).FirstAsync(t => t.Code == "t1031");
            var other = await AddExamAsync(ctx, world.GradeId, template, name: "Başka");
            otherWorksheet = other.Sessions.First().WorksheetId;
        }

        await Should.ThrowAsync<EntitlementLimitException>(() => StartAsync(world, otherWorksheet));
    }

    [Fact]
    public async Task Resuming_a_started_session_is_not_blocked_after_the_plan_ends()
    {
        var world = await SeedMockExamAsync();
        await AssignPlanAsync(SubscriptionPlan.TrialKey);
        var started = await StartAsync(world, world.Worksheet1);
        await using (var ctx = _db.NewContext())
            (await Entitlements(ctx).EndPlanAsync(StudentUserId, 1)).ShouldBeTrue();

        var resumed = await StartAsync(world, world.Worksheet1);

        resumed.InstanceId.ShouldBe(started.InstanceId);
    }

    [Fact]
    public async Task Student_pro_is_unlimited()
    {
        var world = await SeedMockExamAsync();
        await AssignPlanAsync(SubscriptionPlan.StudentProKey);
        await UseAsync(StudentUserId, EntitlementFeatures.MockExam, 50);

        (await StartAsync(world, world.Worksheet1)).Success.ShouldBeTrue();
    }

    private MockExamService NewMockExamService(AppDbContext ctx) => new(ctx, Substitute.For<IQuestionTestLinkService>());

    [Fact]
    public async Task Free_trial_choice_replaces_the_previous_exam_of_the_grade()
    {
        var world = await SeedMockExamAsync(isFreeTrial: true);
        int otherId;
        await using (var ctx = _db.NewContext())
        {
            var template = await ctx.Set<ExamTemplate>().Include(t => t.Sessions).FirstAsync(t => t.Code == "t1031");
            otherId = (await AddExamAsync(ctx, world.GradeId, template, name: "Başka")).Id;
        }

        await using (var ctx = _db.NewContext())
            (await NewMockExamService(ctx).SetFreeTrialAsync(otherId, true, 1, default)).ShouldNotBeNull().IsFreeTrial.ShouldBeTrue();

        await using var read = _db.NewContext();
        (await read.Set<MockExam>().Where(e => e.IsFreeTrial).Select(e => e.Id).SingleAsync()).ShouldBe(otherId);
    }

    [Fact]
    public async Task Free_trial_requires_a_published_open_exam()
    {
        var world = await SeedMockExamAsync();
        await using (var ctx = _db.NewContext())
        {
            var exam = await ctx.Set<MockExam>().FirstAsync(e => e.Id == world.ExamId);
            exam.OpenToGrade = false;
            await ctx.SaveChangesAsync();
        }

        await using var service = _db.NewContext();
        var ex = await Should.ThrowAsync<MockExamRuleException>(() => NewMockExamService(service).SetFreeTrialAsync(world.ExamId, true, 1, default));
        ex.Message.ShouldBe(MockExamService.FreeTrialNotOpen);
    }
}
