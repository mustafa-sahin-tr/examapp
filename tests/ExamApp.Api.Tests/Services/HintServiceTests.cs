using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Hints;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1113: adım adım ipucu — yalnız yanlış / boş soruda, Pro'da; soru başına bir kez üretilir ve paylaşılır, adım başına bir kullanım,
/// cevabı açık eden set düşer, bütçe doluysa Claude çağrılmaz. SQLite, sahte üretici.
/// </summary>
public class HintServiceTests : IDisposable
{
    private const int AdminUser = 1;
    private const int ProUser = 9401;
    private const int OtherProUser = 9402;
    private const int BasicUser = 9403;

    private readonly TestDb _db = TestDb.Create();
    private readonly FakeGenerator _generator = new();
    private readonly InMemoryFixedWindowCounterStore _store = new();
    private int _questionId, _proStudentId, _otherStudentId, _basicStudentId, _gradeId;

    public HintServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
        var pro = new Student { UserId = ProUser, StudentNumber = "p1" };
        var other = new Student { UserId = OtherProUser, StudentNumber = "p2" };
        var basic = new Student { UserId = BasicUser, StudentNumber = "b1" };
        ctx.Students.AddRange(pro, other, basic);
        var question = new Question { Text = "12 + 30 kaçtır?", Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(question);
        ctx.SaveChanges();
        var answers = new[] { "40", "42 elma", "44", "46" }.Select((t, i) => new Answer { QuestionId = question.Id, Text = t, Order = i }).ToList();
        ctx.Answers.AddRange(answers);
        ctx.SaveChanges();
        question.CorrectAnswerId = answers[1].Id;
        ctx.SaveChanges();
        (_questionId, _proStudentId, _otherStudentId, _basicStudentId) = (question.Id, pro.Id, other.Id, basic.Id);
    }

    public void Dispose() => _db.Dispose();

    private async Task SetupAsync()
    {
        await AnswerWrongAsync(_proStudentId);
        await AnswerWrongAsync(_otherStudentId);
        await AnswerWrongAsync(_basicStudentId);
        await AssignAsync(ProUser, SubscriptionPlan.StudentProKey);
        await AssignAsync(OtherProUser, SubscriptionPlan.StudentProKey);
        await AssignAsync(BasicUser, SubscriptionPlan.StudentBasicKey);
    }

    private async Task AnswerWrongAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        var at = DateTime.UtcNow.AddHours(-1);
        var session = new PracticeSession { StudentId = studentId, GradeId = _gradeId, StartTime = at, Status = PracticeSessionStatus.Ended };
        session.Questions.Add(new PracticeSessionQuestion { QuestionId = _questionId, ShownAt = at, AnsweredAt = at, IsCorrect = false, TimeTaken = 5 });
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    private async Task AssignAsync(int userId, string planKey)
    {
        await using var ctx = _db.NewContext();
        (await new EntitlementService(ctx, TimeProvider.System).AssignPlanAsync(userId, planKey, null, null, AdminUser)).ShouldBe(AssignPlanError.None);
    }

    private async Task<HintResult> NextAsync(int userId, LabelingAiOptions? options = null)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx, options).RevealNextAsync(EntitlementSubject.Create(userId, "Student"), new LabelingActor($"kc-{userId}", null),
            _questionId, CancellationToken.None);
    }

    private async Task<HintResult> GetAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).GetAsync(EntitlementSubject.Create(userId, "Student"), _questionId, CancellationToken.None);
    }

    private HintService Service(AppDbContext ctx, LabelingAiOptions? options = null) =>
        new(ctx, _generator, new EntitlementService(ctx, TimeProvider.System), new QuestionClassificationService(ctx), new NoImages(), _store,
            Microsoft.Extensions.Options.Options.Create(options ?? AiOptions()), TimeProvider.System, NullLogger<HintService>.Instance);

    private static LabelingAiOptions AiOptions(decimal budget = 10m) => new()
    {
        DailyBudgetUsdPerUser = budget,
        DailyBudgetUsdTotal = 100m,
        Models = [new LabelingAiModelOption { Id = "model", DisplayName = "Model", IsDefault = true, InputUsdPerMTok = 3m, OutputUsdPerMTok = 15m }],
    };

    private async Task<int> UsageAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.EntitlementUsages.CountAsync(u => u.UserId == userId && u.FeatureKey == EntitlementFeatures.StepHints);
    }

    [Fact]
    public async Task Steps_open_one_by_one_each_counts_once_and_the_set_is_shared()
    {
        await SetupAsync();

        var first = await NextAsync(ProUser);
        first.Error.ShouldBe(HintError.None);
        first.State!.ShouldSatisfyAllConditions(
            s => s.TotalSteps.ShouldBe(3),
            s => s.Steps.Select(x => x.Text).ShouldBe(["Önce birlikleri topla."]),
            s => s.Remaining.ShouldBe(299));
        _generator.LastPrompt!.ShouldSatisfyAllConditions(
            p => p.CorrectLetter.ShouldBe("B"), p => p.CorrectText.ShouldBe("42 elma"), p => p.Text!.ShouldContain("12 + 30"));

        (await NextAsync(ProUser)).State!.Steps.Count.ShouldBe(2);
        (await NextAsync(ProUser)).State!.Steps.Count.ShouldBe(3);
        // Adımlar bitti: hak düşmez.
        (await NextAsync(ProUser)).State!.Steps.Count.ShouldBe(3);
        (await UsageAsync(ProUser)).ShouldBe(3);
        (await GetAsync(ProUser)).State!.Steps.Count.ShouldBe(3);

        (await NextAsync(OtherProUser)).State!.Steps.Count.ShouldBe(1);
        _generator.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Only_wrong_or_blank_questions_on_a_pro_plan_open_hints()
    {
        await SetupAsync();
        (await NextAsync(BasicUser)).Error.ShouldBe(HintError.NotInPlan);

        // Yalnız doğru cevabı olan öğrenci.
        await using (var ctx = _db.NewContext())
        {
            var student = new Student { UserId = 9404, StudentNumber = "c1" };
            ctx.Students.Add(student);
            await ctx.SaveChangesAsync();
            var at = DateTime.UtcNow;
            var session = new PracticeSession { StudentId = student.Id, GradeId = _gradeId, StartTime = at, Status = PracticeSessionStatus.Ended };
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = _questionId, ShownAt = at, AnsweredAt = at, IsCorrect = true, TimeTaken = 5 });
            ctx.PracticeSessions.Add(session);
            await ctx.SaveChangesAsync();
        }
        await AssignAsync(9404, SubscriptionPlan.StudentProKey);
        (await NextAsync(9404)).Error.ShouldBe(HintError.NotEligible);
        (await GetAsync(9404)).Error.ShouldBe(HintError.NotEligible);
        _generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_set_that_reveals_the_answer_is_dropped_and_never_regenerated()
    {
        await SetupAsync();
        _generator.Steps = ["Birlikleri topla.", "Sonuç 42 elma olur, işaretle."];

        var result = await NextAsync(ProUser);
        result.State!.ShouldSatisfyAllConditions(s => s.Unavailable.ShouldBeTrue(), s => s.Steps.ShouldBeEmpty());
        (await NextAsync(ProUser)).State!.Unavailable.ShouldBeTrue();
        _generator.Calls.ShouldBe(1);
        (await UsageAsync(ProUser)).ShouldBe(0);
    }

    [Fact]
    public async Task A_full_daily_budget_stops_the_call()
    {
        await SetupAsync();

        await Should.ThrowAsync<LabelingRateLimitException>(() => NextAsync(ProUser, AiOptions(budget: 0.0001m)));
        _generator.Calls.ShouldBe(0);
        await using var ctx = _db.NewContext();
        (await ctx.QuestionHintSets.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("Doğru cevap B şıkkıdır.", true)]
    [InlineData("Cevap: (B)", true)]
    [InlineData("Cevap A değil, tekrar dene.", false)]
    [InlineData("Toplam 42 Elma!", true)]
    [InlineData("B şıkkına bakmadan önce birlikleri topla.", false)]
    public void Leak_guard_catches_the_letter_and_the_option_text(string step, bool leaks) =>
        HintLeakGuard.Leaks(step, "B", "42 elma").ShouldBe(leaks);

    [Fact]
    public void Leak_guard_ignores_short_option_texts() => HintLeakGuard.Leaks("Önce 12 ile 30'u topla.", "C", "12").ShouldBeFalse();

    private sealed class FakeGenerator : IHintGenerator
    {
        public List<string> Steps { get; set; } = ["Önce birlikleri topla.", "Sonra onlukları topla.", "İki sonucu birleştir."];
        public int Calls { get; private set; }
        public HintPrompt? LastPrompt { get; private set; }
        public bool IsConfigured => true;

        public Task<(GeneratedHints Result, LabelingAiUsage Usage)> GenerateAsync(HintPrompt prompt, LabelingAiModelOption model, CancellationToken ct)
        {
            Calls++;
            LastPrompt = prompt;
            return Task.FromResult((new GeneratedHints(Steps, false), new LabelingAiUsage(1000, 200)));
        }
    }

    private sealed class NoImages : IMinIoService
    {
        public Task<Stream?> GetFileStreamAsync(string fileUrl) => Task.FromResult<Stream?>(null);
        public Task<string> UploadFileAsync(Stream fileStream, string fileName, string bucketName = null!, string contentType = null!) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(string fileUrl, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteFileByUrlAsync(string fileUrl) => throw new NotSupportedException();
        public Task EnsureBucketAsync(string bucketName, string? anonymousReadPolicyJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListBucketNamesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetBucketPolicyAsync(string bucketName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ObjectExistsAsync(string bucketName, string objectName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
