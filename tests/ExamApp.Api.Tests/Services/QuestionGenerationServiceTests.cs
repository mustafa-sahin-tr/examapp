using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.QuestionGeneration;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

public class QuestionGenerationServiceTests : IDisposable
{
    private const int TeacherId = 42;
    private readonly TestDb _db = TestDb.Create();
    private readonly IQuestionDraftGenerator _generator = Substitute.For<IQuestionDraftGenerator>();
    private readonly InMemoryFixedWindowCounterStore _store = new(TimeProvider.System);
    private readonly LabelingActor _actor = new("teacher-1", "Öğretmen");
    private readonly LabelingAiOptions _options = new()
    {
        Models = [new LabelingAiModelOption { Id = "claude-opus-5-5", DisplayName = "Opus", InputUsdPerMTok = 4, OutputUsdPerMTok = 20, IsDefault = true }],
    };
    private int _outcomeId;

    public QuestionGenerationServiceTests()
    {
        _generator.IsConfigured.Returns(true);
        SeedOutcome("5. Sınıf");
    }

    public void Dispose() => _db.Dispose();

    private void SeedOutcome(string gradeName)
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = gradeName };
        var subject = new Subject { Name = $"Matematik {gradeName}" };
        ctx.AddRange(grade, subject);
        ctx.SaveChanges();
        var outcome = new LearningOutcome { Code = $"MAT.{grade.Id}.1.1", Description = "Doğal sayıları karşılaştırabilme", GradeId = grade.Id, SubjectId = subject.Id, CurriculumVersion = CurriculumVersion.Maarif };
        ctx.LearningOutcomes.Add(outcome);
        ctx.SaveChanges();
        _outcomeId = outcome.Id;
    }

    private QuestionGenerationService Service(AppDbContext ctx) => new(ctx, _generator, _store, Options.Create(_options));

    private void ClaudeReturns(params GeneratedQuestion[] questions) =>
        _generator.GenerateAsync(Arg.Any<QuestionDraftPrompt>(), Arg.Any<LabelingAiModelOption>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((new GeneratedQuestionSet(questions.ToList()), new LabelingAiUsage(1000, 500))));

    private async Task<QuestionDraftsResponseDto> GenerateAsync(int count = 3)
    {
        await using var ctx = _db.NewContext();
        return (await Service(ctx).GenerateAsync(new GenerateQuestionDraftsDto { LearningOutcomeId = _outcomeId, Count = count, DifficultyLevel = 4 }, _actor, default))!;
    }

    private static GeneratedQuestion Valid(string text = "Hangisi en büyüktür?") => new(text, ["10", "20", "30", "40"], 3, "40 en büyük sayıdır.");

    private static QuestionDraftDto Draft(string text = "Hangisi en büyüktür?") => new() { Text = text, Options = ["10", "20", "30", "40"], CorrectIndex = 3 };

    private async Task<SavedQuestionDraftsDto?> SaveAsync(AppDbContext ctx, params QuestionDraftDto[] questions) =>
        await Service(ctx).SaveAsync(new SaveQuestionDraftsDto { LearningOutcomeId = _outcomeId, DifficultyLevel = 4, Questions = questions.ToList() }, _actor, TeacherId, default);

    [Fact]
    public async Task Drafts_write_nothing_and_stay_correct_after_the_options_are_shuffled()
    {
        ClaudeReturns(Enumerable.Range(0, 3).Select(i => Valid($"Soru {i}")).ToArray());

        var response = await GenerateAsync();

        response.OutcomeDescription.ShouldBe("Doğal sayıları karşılaştırabilme");
        response.Drafts.Count.ShouldBe(3);
        response.Drafts.ShouldAllBe(d => d.Options[d.CorrectIndex] == "40");
        await using var ctx = _db.NewContext();
        (await ctx.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Claude_is_given_the_outcome_grade_and_option_count()
    {
        ClaudeReturns(Valid());

        await GenerateAsync(1);

        await _generator.Received(1).GenerateAsync(
            Arg.Is<QuestionDraftPrompt>(p => p.OutcomeDescription == "Doğal sayıları karşılaştırabilme" && p.GradeName == "5. Sınıf" && p.Count == 1 && p.OptionCount == 4 && p.DifficultyLevel == 4),
            Arg.Is<LabelingAiModelOption>(m => m.Id == "claude-opus-5-5"), Arg.Any<CancellationToken>());
    }

    public static TheoryData<GeneratedQuestion> BrokenQuestions() => new()
    {
        new GeneratedQuestion("", ["a", "b", "c", "d"], 0, ""),
        new GeneratedQuestion("Soru?", ["a", "b", "c"], 0, ""),
        new GeneratedQuestion("Soru?", ["a", "a", "c", "d"], 0, ""),
        new GeneratedQuestion("Soru?", ["a", "", "c", "d"], 0, ""),
        new GeneratedQuestion("Soru?", ["a", "b", "c", "d"], 4, ""),
        new GeneratedQuestion("Soru?", ["a", "b", "c", "d"], -1, ""),
        new GeneratedQuestion("Yukarıdaki grafiğe göre hangisi doğrudur?", ["a", "b", "c", "d"], 0, ""),
        new GeneratedQuestion("Tabloda verilen en büyük değer hangisidir?", ["a", "b", "c", "d"], 0, ""),
        new GeneratedQuestion("Aşağıdaki şekli inceleyiniz. Kaç kenarı vardır?", ["a", "b", "c", "d"], 0, ""),
        new GeneratedQuestion("Verilen resme göre hangisi doğrudur?", ["a", "b", "c", "d"], 0, ""),
        new GeneratedQuestion("Which is true about the picture above?", ["a", "b", "c", "d"], 0, ""),
        new GeneratedQuestion("Şekilde verilen üçgenin alanı kaçtır?", ["a", "b", "c", "d"], 0, ""),
    };

    [Theory]
    [MemberData(nameof(BrokenQuestions))]
    public async Task Broken_draft_is_counted_and_not_shown(GeneratedQuestion broken)
    {
        ClaudeReturns(broken, Valid());

        var response = await GenerateAsync();

        response.Drafts.ShouldHaveSingleItem().Text.ShouldBe("Hangisi en büyüktür?");
        response.Rejected.ShouldBe(1);
    }

    [Theory]
    [InlineData("Periyodik tabloda aynı grupta olan elementlerin ortak özelliği nedir?")]
    [InlineData("Daha büyük ölçekli haritada hangisi daha ayrıntılı gösterilir?")]
    [InlineData("Aşağıdaki şekilde sıralanan sayılardan hangisi en büyüktür?")]
    [InlineData("Aşağıdaki resmi kurumlardan hangisi eğitimle ilgilidir?")]
    [InlineData("Verilen görsel sanatlar türlerinden hangisi Türk sanatıdır?")]
    public void Ordinary_wording_is_not_taken_as_a_reference_to_a_visual(string text) =>
        QuestionGenerationService.Problem(new QuestionDraftDto { Text = text, Options = ["a", "b", "c", "d"], CorrectIndex = 0 }, 4, checkVisuals: true).ShouldBeNull();

    [Fact]
    public void Options_are_really_shuffled_and_the_correct_one_is_followed()
    {
        var question = new GeneratedQuestion("Soru?", ["doğru", "b", "c", "d"], 0, "");

        var drafts = Enumerable.Range(0, 200).Select(_ => QuestionGenerationService.ToDraft(question)).ToList();

        drafts.ShouldAllBe(d => d.Options[d.CorrectIndex] == "doğru");
        drafts.Select(d => d.CorrectIndex).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task Claude_returning_no_list_counts_as_no_drafts()
    {
        _generator.GenerateAsync(Arg.Any<QuestionDraftPrompt>(), Arg.Any<LabelingAiModelOption>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((new GeneratedQuestionSet(null!), new LabelingAiUsage(1, 1))));

        (await GenerateAsync()).Drafts.ShouldBeEmpty();
    }

    [Fact]
    public async Task More_drafts_than_requested_are_not_shown()
    {
        ClaudeReturns(Valid("1"), Valid("2"), Valid("3"));

        var response = await GenerateAsync(1);

        response.Drafts.Count.ShouldBe(1);
        response.Rejected.ShouldBe(2);
    }

    [Theory]
    [InlineData("1. Sınıf", 4)]
    [InlineData("8. Sınıf", 4)]
    [InlineData("9. Sınıf", 5)]
    [InlineData("12. Sınıf", 5)]
    [InlineData("Hazırlık", 5)]
    public void Option_count_follows_the_exam_format(string grade, int expected) =>
        QuestionGenerationService.OptionCountFor(grade).ShouldBe(expected);

    [Fact]
    public async Task Unknown_outcome_returns_nothing_and_does_not_call_claude()
    {
        await using var ctx = _db.NewContext();

        var response = await Service(ctx).GenerateAsync(new GenerateQuestionDraftsDto { LearningOutcomeId = 9999 }, _actor, default);

        response.ShouldBeNull();
        await _generator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Missing_api_key_is_reported()
    {
        _generator.IsConfigured.Returns(false);

        await Should.ThrowAsync<LabelingAiUnavailableException>(() => GenerateAsync());
    }

    [Fact]
    public async Task Hourly_limit_stops_the_call()
    {
        _options.LiveCallsPerHour = 1;
        ClaudeReturns(Valid());
        await GenerateAsync(1);

        await Should.ThrowAsync<LabelingRateLimitException>(() => GenerateAsync(1));

        await _generator.ReceivedWithAnyArgs(1).GenerateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Budget_is_charged_with_the_real_cost_after_a_good_call()
    {
        ClaudeReturns(Valid());

        await GenerateAsync(1);

        (await _store.PeekAsync(LabelingAiQuota.UserBudgetKey(_actor))).Count.ShouldBe(14_000); // 1000 x $4 + 500 x $20 / MTok
    }

    [Fact]
    public async Task Limit_reached_gives_the_reserved_budget_back()
    {
        _options.LiveCallsPerHour = 0;

        await Should.ThrowAsync<LabelingRateLimitException>(() => GenerateAsync(1));

        (await _store.PeekAsync(LabelingAiQuota.UserBudgetKey(_actor))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Timeout_keeps_the_reservation_because_claude_may_still_charge()
    {
        _generator.GenerateAsync(Arg.Any<QuestionDraftPrompt>(), Arg.Any<LabelingAiModelOption>(), Arg.Any<CancellationToken>())
            .Returns<Task<(GeneratedQuestionSet, LabelingAiUsage)>>(_ => throw new TaskCanceledException());

        await Should.ThrowAsync<QuestionDraftGenerationException>(() => GenerateAsync(1));

        (await _store.PeekAsync(LabelingAiQuota.UserBudgetKey(_actor))).Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Each_saved_question_counts_against_the_hourly_limit()
    {
        _options.LiveCallsPerHour = 1;
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingRateLimitException>(() => SaveAsync(ctx, Draft("Birinci?"), Draft("İkinci?")));

        (await ctx.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Failed_save_gives_the_quota_back()
    {
        await using (var ctx = _db.NewContext(new FailOnSecondOutcomeLink()))
            await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(ctx, Draft("Birinci?"), Draft("İkinci?")));

        (await _store.PeekAsync(LabelingAiQuota.UserCallsKey(_actor))).Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("a<b ise hangisi doğrudur?", "a&lt;b ise hangisi doğrudur?")]
    [InlineData("<script>alert(1)</script> doğru mu?", "&lt;script&gt;alert(1)&lt;/script&gt; doğru mu?")]
    [InlineData("Birinci satır\nİkinci satır", "Birinci satır<br>İkinci satır")]
    public async Task Text_stays_readable_in_the_player_which_renders_html(string typed, string stored)
    {
        await using (var ctx = _db.NewContext())
            await SaveAsync(ctx, Draft(typed.Replace("\\n", "\n")));

        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync()).Text.ShouldBe(stored);
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u200B \uFEFF")]
    public void Invisible_characters_do_not_pass_as_content(string invisible)
    {
        QuestionGenerationService.Problem(new QuestionDraftDto { Text = invisible, Options = ["a", "b"], CorrectIndex = 0 }, null, false).ShouldNotBeNull();
        QuestionGenerationService.Problem(new QuestionDraftDto { Text = "Soru?", Options = ["a", "a" + invisible], CorrectIndex = 0 }, null, false).ShouldNotBeNull();
    }

    [Fact]
    public void Control_characters_and_empty_entries_are_refused()
    {
        QuestionGenerationService.Problem(new QuestionDraftDto { Text = "So\0ru?", Options = ["a", "b"], CorrectIndex = 0 }, null, false).ShouldNotBeNull();
        QuestionGenerationService.Problem(null, null, false).ShouldNotBeNull();
    }

    [Fact]
    public async Task Reviewer_decides_when_the_visual_rule_is_wrong()
    {
        await using var ctx = _db.NewContext();

        var saved = await SaveAsync(ctx, Draft("Tabloda verilen değerlerden hangisi doğrudur?"));

        saved!.QuestionIds.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Daily_budget_counts_what_was_spent()
    {
        _options.Models[0].OutputUsdPerMTok = 20_000;
        _options.DailyBudgetUsdPerUser = 1m;
        _options.DailyBudgetUsdTotal = 1m;

        await Should.ThrowAsync<LabelingRateLimitException>(() => GenerateAsync(1));

        await _generator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Failed_call_is_reported_and_only_paid_tokens_stay_in_the_budget()
    {
        _generator.GenerateAsync(Arg.Any<QuestionDraftPrompt>(), Arg.Any<LabelingAiModelOption>(), Arg.Any<CancellationToken>())
            .Returns<Task<(GeneratedQuestionSet, LabelingAiUsage)>>(_ => throw new LabelingAiException("truncated", usage: new LabelingAiUsage(1000, 1000)));

        await Should.ThrowAsync<QuestionDraftGenerationException>(() => GenerateAsync());

        var spent = await _store.PeekAsync(LabelingAiQuota.UserBudgetKey(_actor));
        spent.Count.ShouldBe(24_000); // 1000 x $4 + 1000 x $20 / MTok = $0.024
    }

    [Fact]
    public async Task Saved_question_is_a_text_question_linked_to_its_outcome()
    {
        await using (var ctx = _db.NewContext())
            (await SaveAsync(ctx, Draft())).ShouldNotBeNull();

        await using var check = _db.NewContext();
        var question = await check.Questions.AsNoTracking().Include(q => q.Answers).SingleAsync();
        question.Text.ShouldBe("Hangisi en büyüktür?");
        question.ImageUrl.ShouldBeNullOrEmpty();
        question.IsCanvasQuestion.ShouldBeFalse();
        question.InteractionType.ShouldBe("mcq");
        question.SourceType.ShouldBe(QuestionSourceType.Generated);
        question.DifficultyLevel.ShouldBe(4);
        question.CreateUserId.ShouldBe(TeacherId);
        question.Answers.OrderBy(a => a.Order).Select(a => a.Text).ShouldBe(["10", "20", "30", "40"]);
        question.Answers.Single(a => a.Id == question.CorrectAnswerId).Text.ShouldBe("40");
        (await check.QuestionLearningOutcomes.SingleAsync()).LearningOutcomeId.ShouldBe(_outcomeId);
    }

    [Fact]
    public async Task Saving_queues_classification_like_any_new_question()
    {
        await using (var ctx = _db.NewContext())
            await SaveAsync(ctx, Draft());

        await using var check = _db.NewContext();
        var outbox = await check.OutboxMessages.AsNoTracking().SingleAsync();
        outbox.Type.ShouldBe(OutboxEventRegistry.NameFor<QuestionCreatedEvent>());
        outbox.Content.ShouldContain("Hangisi");
    }

    [Fact]
    public async Task One_invalid_question_stops_the_whole_save()
    {
        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<QuestionDraftInvalidException>(() => SaveAsync(ctx, Draft(), new QuestionDraftDto { Text = "Soru?", Options = ["a", "a"], CorrectIndex = 0 }));

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Reviewer_may_change_the_number_of_options()
    {
        await using var ctx = _db.NewContext();

        var saved = await SaveAsync(ctx, new QuestionDraftDto { Text = "Soru?", Options = ["a", "b", "c"], CorrectIndex = 1 });

        saved!.QuestionIds.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Failure_on_a_later_question_leaves_nothing_behind()
    {
        await using (var ctx = _db.NewContext(new FailOnSecondOutcomeLink()))
            await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(ctx, Draft("Birinci?"), Draft("İkinci?")));

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Retried_save_writes_each_question_once()
    {
        var interceptor = new FailFirstCommit();
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
        {
            var saved = await SaveAsync(ctx, Draft("Birinci?"), Draft("İkinci?"));
            saved!.QuestionIds.Count.ShouldBe(2);
        }

        interceptor.Failed.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(2);
        (await check.Answers.CountAsync()).ShouldBe(8);
        (await check.OutboxMessages.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Question_pool_shows_the_text_and_options_without_the_correct_answer()
    {
        await using (var ctx = _db.NewContext())
            await SaveAsync(ctx, Draft());

        await using var read = _db.NewContext();
        var item = (await new QuestionQueryService(read).SearchBankAsync(new QuestionBankQuery { Scope = "all" }, TeacherId, true)).Items.ShouldHaveSingleItem();

        item.Text.ShouldBe("Hangisi en büyüktür?");
        item.AnswerTexts.ShouldBe(["10", "20", "30", "40"]);
    }

    private async Task<int> SubTopicOfSubjectAsync(int? subjectId)
    {
        await using var ctx = _db.NewContext();
        var outcome = await ctx.LearningOutcomes.AsNoTracking().SingleAsync(o => o.Id == _outcomeId);
        if (subjectId is null)
        {
            var other = new Subject { Name = "Başka ders" };
            ctx.Add(other);
            await ctx.SaveChangesAsync();
            subjectId = other.Id;
        }
        var topic = new Topic { Name = "Konu", SubjectId = subjectId.Value, GradeId = outcome.GradeId };
        ctx.Add(topic);
        await ctx.SaveChangesAsync();
        var subTopic = new SubTopic { Name = "Alt konu", TopicId = topic.Id };
        ctx.Add(subTopic);
        await ctx.SaveChangesAsync();
        return subTopic.Id;
    }

    [Fact]
    public async Task Classifier_adds_the_topic_and_keeps_the_reviewers_difficulty()
    {
        int questionId, subjectId;
        await using (var ctx = _db.NewContext())
            questionId = (await SaveAsync(ctx, Draft()))!.QuestionIds[0];
        await using (var ctx = _db.NewContext())
            subjectId = (await ctx.Questions.AsNoTracking().SingleAsync()).SubjectId!.Value;
        var subTopicId = await SubTopicOfSubjectAsync(subjectId);

        await using (var ctx = _db.NewContext())
            (await new QuestionClassificationService(ctx).UpdateQuestionClassification(questionId, subTopicIds: [subTopicId], classificationSourceStr: "AI", difficulty: 9)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var question = await check.Questions.AsNoTracking().SingleAsync();
        question.TopicId.ShouldNotBeNull();
        question.DifficultyLevel.ShouldBe(4);
    }

    [Fact]
    public async Task Classifier_cannot_move_the_question_to_another_subject()
    {
        int questionId, subjectId;
        await using (var ctx = _db.NewContext())
            questionId = (await SaveAsync(ctx, Draft()))!.QuestionIds[0];
        await using (var ctx = _db.NewContext())
            subjectId = (await ctx.Questions.AsNoTracking().SingleAsync()).SubjectId!.Value;
        var otherSubTopic = await SubTopicOfSubjectAsync(null);

        await using (var ctx = _db.NewContext())
            await new QuestionClassificationService(ctx).UpdateQuestionClassification(questionId, subTopicIds: [otherSubTopic], classificationSourceStr: "AI");

        await using var check = _db.NewContext();
        var question = await check.Questions.AsNoTracking().SingleAsync();
        question.SubjectId.ShouldBe(subjectId);
        question.TopicId.ShouldBeNull();
    }

    [Fact]
    public async Task A_person_can_still_change_a_generated_question()
    {
        int questionId;
        await using (var ctx = _db.NewContext())
            questionId = (await SaveAsync(ctx, Draft()))!.QuestionIds[0];
        var otherSubTopic = await SubTopicOfSubjectAsync(null);

        await using (var ctx = _db.NewContext())
            (await new QuestionClassificationService(ctx).UpdateQuestionClassification(questionId, subTopicIds: [otherSubTopic], classificationSourceStr: "Human", difficulty: 9)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var question = await check.Questions.AsNoTracking().SingleAsync();
        question.TopicId.ShouldNotBeNull();
        question.DifficultyLevel.ShouldBe(9);
    }

    [Fact]
    public async Task Unknown_outcome_saves_nothing()
    {
        await using var ctx = _db.NewContext();

        var saved = await Service(ctx).SaveAsync(new SaveQuestionDraftsDto { LearningOutcomeId = 9999, Questions = [Draft()] }, _actor, TeacherId, default);

        saved.ShouldBeNull();
        (await ctx.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public void Instruction_carries_the_outcome_and_the_format()
    {
        var text = ClaudeQuestionDraftGenerator.Instruction(new QuestionDraftPrompt("MAT.5.1.1", "Doğal sayıları karşılaştırabilme", "5. Sınıf", "Matematik", 3, 4, 6));

        text.ShouldContain("MAT.5.1.1");
        text.ShouldContain("Doğal sayıları karşılaştırabilme");
        text.ShouldContain("3 soru");
        text.ShouldContain("4 şık");
    }

    private sealed class FailOnSecondOutcomeLink : SaveChangesInterceptor
    {
        private int _links;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<QuestionLearningOutcome>().Any(e => e.State == EntityState.Added) && ++_links == 2)
                throw new DbUpdateException("simulated failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        public bool Failed { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Failed)
                return ValueTask.FromResult(result);
            Failed = true;
            throw new TransientTestException("simulated commit failure");
        }
    }
}
