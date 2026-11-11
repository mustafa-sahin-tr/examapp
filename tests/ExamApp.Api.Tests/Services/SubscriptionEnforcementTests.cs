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

    private async Task AssignTeacherPlanAsync(string planKey)
    {
        await using var ctx = _db.NewContext();
        (await Entitlements(ctx).AssignPlanAsync(TeacherUserId, planKey, null, null, 1)).ShouldBe(AssignPlanError.None);
    }

    private async Task<int> AddTopicAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        return topic.Id;
    }

    [Fact]
    public async Task Basic_teacher_must_choose_the_topic_and_is_not_classified_issue1087()
    {
        await AssignTeacherPlanAsync(SubscriptionPlan.TeacherBasicKey);
        var owner = EntitlementSubject.Create(TeacherUserId);

        await using (var ctx = _db.NewContext())
        {
            var service = new QuestionService(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), entitlements: Entitlements(ctx));
            var rejected = (await service.SaveBulkQuestion(Bulk(1), TeacherUserId, owner)).ShouldBeOfType<QuestionSavedDto>();
            (rejected.Success, rejected.InvalidInput, rejected.ErrorCode).ShouldBe((false, true, "TopicRequired"));
        }
        (await ClassificationCountsAsync()).ShouldBe((0, 0, 0));

        var topicId = await AddTopicAsync();
        await using (var ctx = _db.NewContext())
        {
            var service = new QuestionService(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), entitlements: Entitlements(ctx));
            var bulk = Bulk(1);
            bulk.Header.TopicId = topicId;
            var saved = (await service.SaveBulkQuestion(bulk, TeacherUserId, owner)).ShouldBeOfType<QuestionSavedDto>();
            saved.Success.ShouldBeTrue(saved.Message);
            saved.ClassificationSkipped.ShouldBe(1);
        }
        (await ClassificationCountsAsync()).ShouldBe((1, 0, 0));
    }

    [Fact]
    public async Task Essential_teacher_may_leave_the_topic_to_the_classifier_issue1087()
    {
        await AssignTeacherPlanAsync(SubscriptionPlan.TeacherEssentialKey);

        await SaveBulkAsync(1, EntitlementSubject.Create(TeacherUserId));

        (await ClassificationCountsAsync()).ShouldBe((1, 1, 1));
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

    // ---- #1089: öğrencinin kendi seçtiği test / etkinlik

    private async Task<int> AddOpenWorksheetAsync(MockWorld world, WorksheetKind kind, string name)
    {
        await using var ctx = _db.NewContext();
        var worksheet = new Worksheet
        {
            Name = name,
            Description = "",
            GradeId = world.GradeId,
            Kind = kind,
            StudentVisibility = WorksheetStudentVisibility.Normal,
        };
        ctx.Add(worksheet);
        await ctx.SaveChangesAsync();
        return worksheet.Id;
    }

    private async Task<int> UsagesAsync(string feature)
    {
        await using var ctx = _db.NewContext();
        return await ctx.EntitlementUsages.CountAsync(u => u.FeatureKey == feature && u.UserId == StudentUserId);
    }

    [Fact]
    public async Task Self_chosen_test_counts_as_practice_and_activity_as_self_activity_issue1089()
    {
        var world = await SeedMockExamAsync();
        await AssignPlanAsync(SubscriptionPlan.StudentBasicKey);
        var test = await AddOpenWorksheetAsync(world, WorksheetKind.Test, "Test");
        var activity = await AddOpenWorksheetAsync(world, WorksheetKind.Activity, "Etkinlik");

        (await StartAsync(world, test)).Success.ShouldBeTrue();
        (await StartAsync(world, activity)).Success.ShouldBeTrue();
        (await StartAsync(world, test)).Success.ShouldBeTrue(); // kendi oturumuna dönmek yeniden sayılmaz

        (await UsagesAsync(EntitlementFeatures.Practice)).ShouldBe(1);
        (await UsagesAsync(EntitlementFeatures.SelfActivity)).ShouldBe(1);
    }

    [Fact]
    public async Task Basic_practice_limit_blocks_the_next_self_chosen_test_but_not_assigned_work_issue1089()
    {
        var world = await SeedMockExamAsync();
        await AssignPlanAsync(SubscriptionPlan.StudentBasicKey);
        await UseAsync(StudentUserId, EntitlementFeatures.Practice, 10); // Basic sınırı 10
        var test = await AddOpenWorksheetAsync(world, WorksheetKind.Test, "Test");
        var assigned = await AddOpenWorksheetAsync(world, WorksheetKind.Test, "Ödev");
        await using (var seed = _db.NewContext())
        {
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = assigned,
                StudentId = world.StudentId,
                StartAt = DateTime.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        var ex = await Should.ThrowAsync<EntitlementLimitException>(() => StartAsync(world, test));
        ex.FeatureKey.ShouldBe(EntitlementFeatures.Practice);
        ex.Check.Limit.ShouldBe(10);

        (await StartAsync(world, assigned)).Success.ShouldBeTrue();
        (await UsagesAsync(EntitlementFeatures.Practice)).ShouldBe(10);
    }

    // ---- #1109: tek ders planı öğrencinin kendi açtığı oturumu daraltır

    private async Task<(int Math, int Turkish)> AssignSingleSubjectPlanAsync()
    {
        await using var ctx = _db.NewContext();
        var math = new Subject { Name = "Matematik" };
        var turkish = new Subject { Name = "Türkçe" };
        ctx.AddRange(math, turkish);
        await ctx.SaveChangesAsync();
        var plan = await ctx.SubscriptionPlans.SingleAsync(p => p.Key == SubscriptionPlan.StudentProKey);
        ctx.UserSubscriptionPlans.Add(new UserSubscriptionPlan
        {
            UserId = StudentUserId, PlanId = plan.Id, ValidFromUtc = DateTime.UtcNow.AddDays(-1), ScopeSubjectId = math.Id,
        });
        await ctx.SaveChangesAsync();
        return (math.Id, turkish.Id);
    }

    private async Task SetSubjectAsync(int worksheetId, int subjectId)
    {
        await using var ctx = _db.NewContext();
        var worksheet = await ctx.Worksheets.AsTracking().SingleAsync(w => w.Id == worksheetId);
        worksheet.SubjectId = subjectId;
        await ctx.SaveChangesAsync();
    }

    private async Task AddQuestionAsync(int worksheetId, int subjectId)
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = "Q", SubjectId = subjectId, Point = 10, DifficultyLevel = 1 };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheetId, QuestionId = question.Id, Order = 1 });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Single_subject_plan_refuses_a_self_chosen_test_of_another_subject_but_not_assigned_work_issue1109()
    {
        var world = await SeedMockExamAsync();
        var (math, turkish) = await AssignSingleSubjectPlanAsync();
        var mathTest = await AddOpenWorksheetAsync(world, WorksheetKind.Test, "Mat");
        var turkishActivity = await AddOpenWorksheetAsync(world, WorksheetKind.Activity, "Türkçe");
        var assigned = await AddOpenWorksheetAsync(world, WorksheetKind.Test, "Ödev");
        await SetSubjectAsync(mathTest, math);
        await SetSubjectAsync(turkishActivity, turkish);
        await SetSubjectAsync(assigned, turkish);
        await using (var seed = _db.NewContext())
        {
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = assigned,
                StudentId = world.StudentId,
                StartAt = DateTime.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        var ex = await Should.ThrowAsync<StudyScopeException>(() => StartAsync(world, turkishActivity));
        (ex.ScopeSubjectId, ex.PlanKey).ShouldBe((math, SubscriptionPlan.StudentProKey));
        (await UsagesAsync(EntitlementFeatures.SelfActivity)).ShouldBe(0); // kapsam dışı istek hak harcamaz

        (await StartAsync(world, mathTest)).Success.ShouldBeTrue();
        (await StartAsync(world, assigned)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Single_subject_plan_opens_only_the_mock_exam_session_of_its_subject_issue1109()
    {
        var world = await SeedMockExamAsync();
        var (math, turkish) = await AssignSingleSubjectPlanAsync();
        await AddQuestionAsync(world.Worksheet1, turkish);
        await AddQuestionAsync(world.Worksheet1, math);
        await AddQuestionAsync(world.Worksheet2, math);

        (await Should.ThrowAsync<StudyScopeException>(() => StartAsync(world, world.Worksheet1))).ScopeSubjectId.ShouldBe(math);
        (await StartAsync(world, world.Worksheet2)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Free_student_keeps_self_study_until_the_lockout_issue1089()
    {
        var world = await SeedMockExamAsync();
        var activity = await AddOpenWorksheetAsync(world, WorksheetKind.Activity, "Etkinlik");

        (await StartAsync(world, activity)).Success.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, false, false, false, null, EntitlementFeatures.MixedMini, true)]
    [InlineData(false, true, false, false, null, EntitlementFeatures.StudyTools, false)]
    [InlineData(false, false, true, false, null, EntitlementFeatures.StudyTools, false)]
    [InlineData(false, false, false, true, null, EntitlementFeatures.StudyTools, false)]
    [InlineData(false, false, false, false, 5, EntitlementFeatures.StudyTools, false)]
    [InlineData(false, false, false, false, null, EntitlementFeatures.Practice, true)]
    public void Practice_session_kind_decides_the_plan_feature_issue1089(bool mixed, bool due, bool mistakes, bool saved, int? studyItemId,
        string feature, bool counted) =>
        ExamApp.Api.Controllers.PracticeController.PlanFeatureOf(new PracticeSessionStartDto
        {
            MixedReview = mixed, DueReview = due, Mistakes = mistakes, Saved = saved, StudyItemId = studyItemId,
        }).ShouldBe((feature, counted));
}
