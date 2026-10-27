using System.Collections.Concurrent;
using System.Drawing;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #580: cevap anahtarı okuma (eşleme, saklama, değiştirme, önbellek, aralık kontrolü), madde listesi / etiket eşlemesi,
/// etkinlik çözümü (tip ve mod başına, anahtar girdisi, doğrulama), mod başına önbellek ve yalnız değişen talimatın geçersiz olması,
/// kota/bütçe ve taslak çözüm onayı (onaylayan + zaman, düzeltme → Human).
/// </summary>
public class ActivitySolutionServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly LabelingActor Actor = new("kc-trainer-1", "Eğitmen");
    private static readonly LabelingActor Approver = new("kc-trainer-2", "Onaylayan");

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly InMemoryFixedWindowCounterStore _counters;
    private readonly FakeAi _ai = new();
    private readonly int _bookId;

    // Düz renkli sayfalar: kırpma hash'i yalnız boyuta bağlı.
    private readonly string _keyPage = Convert.ToBase64String(TestImages.Png(1000, 1400));
    private readonly string _activityPage = Convert.ToBase64String(TestImages.Png(800, 1000));

    public ActivitySolutionServiceTests()
    {
        _counters = new InMemoryFixedWindowCounterStore(_clock);
        using var ctx = _db.NewContext();
        var book = new Book { Name = "MatLab 5" };
        ctx.Books.Add(book);
        ctx.SaveChanges();
        ctx.BookPageOffsets.Add(new BookPageOffset { BookId = book.Id, FileFrom = 1, FileTo = 300, Offset = -2 });
        ctx.BookAnswerKeyRanges.Add(new BookAnswerKeyRange { BookId = book.Id, FileFrom = 290, FileTo = 300 });
        ctx.SaveChanges();
        _bookId = book.Id;
    }

    public void Dispose() => _db.Dispose();

    private static LabelingAiModelOption Model(string id = "claude-test") => new()
    {
        Id = id, DisplayName = "Test", InputUsdPerMTok = 3m, OutputUsdPerMTok = 15m, IsDefault = id == "claude-test",
    };

    private ActivitySolutionService NewService(AppDbContext ctx, int liveCallsPerHour = 60, decimal dailyBudgetUsdPerUser = 5m) => new(
        ctx,
        _ai,
        new BookRegistryService(ctx, _clock),
        InteractionGraderRegistry.Default,
        new ImageHelper(),
        _counters,
        Options.Create(new LabelingAiOptions
        {
            Models = [Model(), Model("claude-other")],
            LiveCallsPerHour = liveCallsPerHour,
            DailyBudgetUsdPerUser = dailyBudgetUsdPerUser,
            DailyBudgetUsdTotal = 25m,
            ActivityStructureRequestsPerMinute = 1000,
        }),
        _clock,
        NullLogger<ActivitySolutionService>.Instance);

    private async Task<T> WithService<T>(Func<ActivitySolutionService, Task<T>> action, int liveCallsPerHour = 60)
    {
        await using var ctx = _db.NewContext();
        return await action(NewService(ctx, liveCallsPerHour));
    }

    private AnswerKeyReadRequestDto KeyRead(int filePage = 295, bool force = false, string model = "claude-test") =>
        new() { BookId = _bookId, FilePage = filePage, ImageBase64 = _keyPage, Model = model, Force = force };

    private async Task<long> CallsCounted() => (await _counters.PeekAsync("labeling-ai:" + Actor.KeycloakId)).Count;

    private async Task<long> SpentMicroUsd() => (await _counters.PeekAsync("labeling-ai-usd:" + Actor.KeycloakId)).Count;

    private static string PlanFor(string type) => type switch
    {
        MatrixChoiceGrader.Type => ActivitySolutionDraftsTests.MatrixPlan,
        SlotFillGrader.Type => ActivitySolutionDraftsTests.SlotPlan,
        DragPlaceGrader.Type => ActivitySolutionDraftsTests.DragPlan,
        MatchGrader.Type => ActivitySolutionDraftsTests.MatchPlan,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private ActivitySolutionRequestDto Solve(string type, string mode = "ai", int width = 200, int height = 100,
        IReadOnlyList<int>? keyIds = null, bool force = false) => new()
    {
        ImageBase64 = _activityPage,
        X = 20, Y = 30, Width = width, Height = height,
        InteractionType = type,
        Plan = JsonDocument.Parse(PlanFor(type)).RootElement.Clone(),
        Model = "claude-test",
        Mode = mode,
        Force = force,
        Key = keyIds is null ? null : new ActivitySolutionKeyDto { EntryIds = keyIds.ToList() },
    };

    private async Task SavePromptAsync(string kind, string instruction)
    {
        await using var ctx = _db.NewContext();
        var structure = new ActivityStructureService(ctx, Substitute.For<IActivityStructureExtractor>(), InteractionGraderRegistry.Default,
            new ImageHelper(), _counters, Options.Create(new LabelingAiOptions { Models = [Model()] }), _clock,
            NullLogger<ActivityStructureService>.Instance);
        await structure.SavePromptAsync(kind, new ActivitySavePromptRequestDto { Instruction = instruction }, Actor);
    }

    // ================================================================== cevap anahtarı

    [Fact]
    public async Task ReadAnswerKey_PageOutsideAnswerKeyRange_Rejected_WithoutCallingClaude()
    {
        await Should.ThrowAsync<LabelingNotAnswerKeyPageException>(() => WithService(s => s.ReadAnswerKeyAsync(KeyRead(filePage: 12), Actor)));
        await Should.ThrowAsync<LabelingNotFoundException>(() =>
            WithService(s => s.ReadAnswerKeyAsync(new AnswerKeyReadRequestDto
            {
                BookId = 999, FilePage = 295, ImageBase64 = _keyPage, Model = "claude-test",
            }, Actor)));
        _ai.KeyCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ReadAnswerKey_Live_StoresEntries_AndVisualCrop_ThenServesFromCacheForFree()
    {
        var first = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));

        first.Origin.ShouldBe("live");
        first.PromptVersion.ShouldBe(0);
        first.EstimatedCostUsd.ShouldBe(FakeAi.CostUsd);
        first.Entries.Select(e => (e.FilePage, e.PrintedPage, e.ActivityLabel, e.ItemLabel, e.AnswerText, e.Visual)).ShouldBe([
            (295, 12, "Etkinlik 3", "a", "3/4", false),
            (295, 12, "Etkinlik 3", "b", "D", false),
            (295, 14, "Etkinlik 5", null, null, true),
        ]);
        first.Entries[2].Box.ShouldBe(new LabelingBoxDto(100, 200, 300, 150));
        _ai.LastKeyPrompt!.Instruction.ShouldBe(ActivityStructureSettings.DefaultAnswerKeyInstruction);

        await using (var ctx = _db.NewContext())
        {
            var visual = await ctx.LabelingAnswerKeyEntries.SingleAsync(e => e.Visual);
            visual.CropJpeg.ShouldNotBeNull();
            TestImages.Info(visual.CropJpeg!).Width.ShouldBe(300);
            visual.CropSha256.ShouldBe(QuestionCropHash.Of(visual.CropJpeg!));
            (await ctx.LabelingAnswerKeyEntries.Where(e => !e.Visual).AllAsync(e => e.CropJpeg == null)).ShouldBeTrue();
        }

        var spent = await SpentMicroUsd();
        var second = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));

        second.Origin.ShouldBe("cached");
        second.Entries.Select(e => e.Id).ShouldBe(first.Entries.Select(e => e.Id));
        _ai.KeyCalls.ShouldBe(1);
        (await CallsCounted()).ShouldBe(1);
        (await SpentMicroUsd()).ShouldBe(spent);
    }

    [Fact]
    public async Task ReadAnswerKey_LiveReadReplacesThePagesEntries_TeacherEditsSurviveCacheHits()
    {
        var first = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        var edited = await WithService(s => s.UpdateEntryAsync(first.Entries[0].Id,
            new AnswerKeyEntryUpdateRequestDto { PrintedPage = 12, ActivityLabel = "Etkinlik 3", ItemLabel = "a", AnswerText = "4/5" }, Actor));
        edited!.AnswerText.ShouldBe("4/5");
        edited.EditedByName.ShouldBe("Eğitmen");

        var cached = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        cached.Entries[0].AnswerText.ShouldBe("4/5");

        _ai.KeyEntries = [new AnswerKeyReadEntry(20, "Etkinlik 1", null, "7", false, null)];
        var overwrite = KeyRead(force: true);
        overwrite.ConfirmOverwrite = true; // düzeltme var: onaysız force 409 (aşağıdaki review testi)
        var forced = await WithService(s => s.ReadAnswerKeyAsync(overwrite, Actor));

        forced.Origin.ShouldBe("live");
        forced.Entries.Select(e => e.AnswerText).ShouldBe(["7"]);
        await using var ctx = _db.NewContext();
        (await ctx.LabelingAnswerKeyEntries.CountAsync()).ShouldBe(1);
        (await ctx.LabelingAnswerKeyPages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ReadAnswerKey_AnswerKeyPromptChange_InvalidatesOnlyItsCache()
    {
        await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        await SavePromptAsync("solveAi", "Different solve prompt.");
        (await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor))).Origin.ShouldBe("cached");

        await SavePromptAsync("answerKey", "Read the key, version two.");
        var again = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));

        again.Origin.ShouldBe("live");
        again.PromptVersion.ShouldBe(1);
        _ai.LastKeyPrompt!.Instruction.ShouldBe("Read the key, version two.");
    }

    [Fact]
    public async Task ReadAnswerKey_ClaudeFailure_ThrowsAndChargesPaidTokens()
    {
        _ai.FailKey = true;

        await Should.ThrowAsync<LabelingAiException>(() => WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor)));

        (await SpentMicroUsd()).ShouldBe((long)(FakeAi.CostUsd * 1_000_000m));
        await using var ctx = _db.NewContext();
        (await ctx.LabelingAnswerKeyPages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ReadAnswerKey_HourlyQuotaExhausted_Returns429_ButCachedReadStillWorks()
    {
        await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor), liveCallsPerHour: 1);

        (await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor), liveCallsPerHour: 1)).Origin.ShouldBe("cached");
        await Should.ThrowAsync<LabelingRateLimitException>(() =>
            WithService(s => s.ReadAnswerKeyAsync(KeyRead(filePage: 296), Actor), liveCallsPerHour: 1));
        _ai.KeyCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ListAnswerKey_FiltersByPrintedPage_AndMatchesLabelsNormalized()
    {
        await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));

        var all = await WithService(s => s.ListAnswerKeyAsync(_bookId, null, null, 0));
        all!.Total.ShouldBe(3);

        var page12 = await WithService(s => s.ListAnswerKeyAsync(_bookId, 12, null, 0));
        page12!.Entries.Select(e => e.ItemLabel).ShouldBe(["a", "b"]);

        (await WithService(s => s.ListAnswerKeyAsync(_bookId, null, "ETKİNLİK 3", 0)))!.Entries.Count.ShouldBe(2);
        (await WithService(s => s.ListAnswerKeyAsync(_bookId, 14, "5.", 0)))!.Entries.Single().Visual.ShouldBeTrue();
        (await WithService(s => s.ListAnswerKeyAsync(_bookId, null, "Alıştırma 3", 0)))!.Entries.ShouldBeEmpty();
        (await WithService(s => s.ListAnswerKeyAsync(999, null, null, 0))).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteEntry_RemovesOnlyThatEntry()
    {
        var read = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));

        (await WithService(s => s.DeleteEntryAsync(read.Entries[1].Id, Actor))).ShouldBeTrue();
        (await WithService(s => s.DeleteEntryAsync(read.Entries[1].Id, Actor))).ShouldBeFalse();
        (await WithService(s => s.ListAnswerKeyAsync(_bookId, null, null, 0)))!.Total.ShouldBe(2);
    }

    // ================================================================== çözüm

    public static TheoryData<string> Types => new()
    {
        MatrixChoiceGrader.Type, SlotFillGrader.Type, DragPlaceGrader.Type, MatchGrader.Type,
    };

    [Theory]
    [MemberData(nameof(Types))]
    public async Task SolveAi_PerType_ReturnsValidPendingAiSolution_ThenCached(string type)
    {
        var first = await WithService(s => s.SolveAsync(Solve(type), Actor));

        first.Error.ShouldBeNull();
        first.Origin.ShouldBe("live");
        first.Source.ShouldBe("AI");
        first.Status.ShouldBe("pending");
        first.PromptVersion.ShouldBe(0);
        first.Valid.ShouldBeTrue();
        first.ValidationErrors.ShouldBeEmpty();
        first.DraftSolutionId.ShouldNotBeNull();
        first.CropSha256.ShouldNotBeNull();
        first.Mode.ShouldBe("ai");
        _ai.SolveCalls.Single().Prompt.InteractionType.ShouldBe("solveAi");
        _ai.SolveCalls.Single().Type.ShouldBe(type);
        _ai.SolveCalls.Single().Key.ShouldBeEmpty();

        var second = await WithService(s => s.SolveAsync(Solve(type), Actor));
        second.Origin.ShouldBe("cached");
        second.DraftSolutionId.ShouldBe(first.DraftSolutionId);
        _ai.SolveCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SolveKey_SendsEntryTextAndVisualCrop_SourceKey_AndKeyChangeMissesTheCache()
    {
        var read = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        var ids = new[] { read.Entries[0].Id, read.Entries[2].Id };

        var first = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "key", keyIds: ids), Actor));

        first.Source.ShouldBe("Key");
        first.Status.ShouldBe("pending");
        first.Mode.ShouldBe("key");
        first.KeyEntryIds.ShouldBe(ids);
        var call = _ai.SolveCalls.Single();
        call.Prompt.InteractionType.ShouldBe("solveFromKey");
        call.Key.Select(k => k.AnswerText).ShouldBe(["3/4", null]);
        call.Key[1].CropJpeg.ShouldNotBeNull();
        call.Key[0].CropJpeg.ShouldBeNull();

        (await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "key", keyIds: ids), Actor))).Origin.ShouldBe("cached");

        // Anahtar metni değişince anahtar girdisinin hash'i değişir → yeniden çözülür.
        await WithService(s => s.UpdateEntryAsync(read.Entries[0].Id,
            new AnswerKeyEntryUpdateRequestDto { PrintedPage = 12, ActivityLabel = "Etkinlik 3", ItemLabel = "a", AnswerText = "1/2" }, Actor));
        (await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "key", keyIds: ids), Actor))).Origin.ShouldBe("live");
        _ai.SolveCalls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SolvePromptChange_InvalidatesOnlyThatModesCache()
    {
        var read = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        int[] ids = [read.Entries[0].Id];
        await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "ai"), Actor));
        await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "key", keyIds: ids), Actor));
        _ai.SolveCalls.Count.ShouldBe(2);

        await SavePromptAsync("solveAi", "Solve it, version two.");

        var ai = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "ai"), Actor));
        var key = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "key", keyIds: ids), Actor));
        var keyRead = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));

        ai.Origin.ShouldBe("live");
        ai.PromptVersion.ShouldBe(1);
        key.Origin.ShouldBe("cached");
        key.PromptVersion.ShouldBe(0);
        keyRead.Origin.ShouldBe("cached");
        _ai.SolveCalls.Count.ShouldBe(3);
        _ai.SolveCalls.Last().Prompt.Instruction.ShouldBe("Solve it, version two.");
    }

    [Fact]
    public async Task Solve_CacheIsPerModelAndPlan()
    {
        await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor));

        var otherModel = Solve(MatchGrader.Type);
        otherModel.Model = "claude-other";
        (await WithService(s => s.SolveAsync(otherModel, Actor))).Origin.ShouldBe("live");

        var otherPlan = Solve(MatchGrader.Type);
        otherPlan.Plan = JsonDocument.Parse(ActivitySolutionDraftsTests.MatchPlan.Replace("Bursa", "Bursa şehri")).RootElement.Clone();
        (await WithService(s => s.SolveAsync(otherPlan, Actor))).Origin.ShouldBe("live");
        _ai.SolveCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Solve_InvalidSolutionFromClaude_IsStoredButFlaggedInvalid()
    {
        _ai.SolutionFor = _ => """{"r1":["c1"]}"""; // r2 eksik

        var result = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor));

        result.Valid.ShouldBeFalse();
        result.ValidationErrors.ShouldBe([ActivitySolutionService.InvalidSolutionKey]);
        result.DraftSolutionId.ShouldNotBeNull();
        await Should.ThrowAsync<LabelingValidationException>(() =>
            WithService(s => s.ApproveSolutionAsync(result.DraftSolutionId!.Value, Approver)));
    }

    [Fact]
    public async Task Solve_ClaudeFailure_ReturnsErrorWithoutSaving_AndChargesPaidTokens()
    {
        _ai.FailSolve = true;

        var result = await WithService(s => s.SolveAsync(Solve(SlotFillGrader.Type), Actor));

        result.Error.ShouldBe(ActivitySolutionService.AiFailedKey);
        result.DraftSolutionId.ShouldBeNull();
        result.Solution.ShouldBeNull();
        result.EstimatedCostUsd.ShouldBe(FakeAi.CostUsd);
        (await SpentMicroUsd()).ShouldBe((long)(FakeAi.CostUsd * 1_000_000m));
        await using var ctx = _db.NewContext();
        (await ctx.LabelingActivitySolutions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Solve_HourlyQuota_429_RefusedRequestDoesNotEatTheQuota()
    {
        await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor), liveCallsPerHour: 1);

        await Should.ThrowAsync<LabelingRateLimitException>(() =>
            WithService(s => s.SolveAsync(Solve(MatchGrader.Type, width: 300), Actor), liveCallsPerHour: 1));

        (await CallsCounted()).ShouldBe(1);
        (await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor), liveCallsPerHour: 1)).Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task Solve_NotConfigured_503_OnlyWhenALiveCallIsNeeded()
    {
        await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor));
        _ai.IsConfigured = false;

        (await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor))).Origin.ShouldBe("cached");
        await Should.ThrowAsync<LabelingAiUnavailableException>(() =>
            WithService(s => s.SolveAsync(Solve(MatchGrader.Type, width: 300), Actor)));
    }

    public static TheoryData<string, Action<ActivitySolutionRequestDto>> InvalidRequests => new()
    {
        { "labeling.activity.unknownType", r => r.InteractionType = "mcq" },
        { "labeling.solution.invalidMode", r => r.Mode = "guess" },
        { "labeling.solution.invalidPlan", r => r.Plan = null },
        { "labeling.solution.invalidPlan", r => r.Plan = JsonDocument.Parse("""{"version":1,"type":"slotFill","slots":[]}""").RootElement.Clone() },
        { "labeling.solution.keyRequired", r => r.Mode = "key" },
        { "labeling.solution.keyEntryNotFound", r => { r.Mode = "key"; r.Key = new() { EntryIds = [12345] }; } },
        { "labeling.invalidRegion", r => r.Width = 0 },
        { "labeling.unknownModel", r => r.Model = "gpt" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Solve_InvalidRequest_400_WithoutCallingClaude(string key, Action<ActivitySolutionRequestDto> mutate)
    {
        var request = Solve(MatrixChoiceGrader.Type);
        mutate(request);

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => WithService(s => s.SolveAsync(request, Actor)));

        ex.Message.ShouldBe(key);
        _ai.SolveCalls.ShouldBeEmpty();
    }

    // ================================================================== onay

    [Fact]
    public async Task Approve_WithoutEdit_KeepsSource_RecordsApproverAndTime()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor));
        _clock.Now = Now.AddMinutes(5);

        var approved = await WithService(s => s.ApproveSolutionAsync(solved.DraftSolutionId!.Value, Approver));

        approved!.Status.ShouldBe("approved");
        approved.Source.ShouldBe("AI");
        approved.ApprovedByName.ShouldBe("Onaylayan");
        approved.ApprovedAt.ShouldBe(Now.AddMinutes(5).UtcDateTime);
        approved.PromptVersion.ShouldBe(0);
        await using var ctx = _db.NewContext();
        (await ctx.LabelingActivitySolutions.SingleAsync()).ApprovedByKeycloakId.ShouldBe("kc-trainer-2");

        // Kayıttan dönen çözüm onay durumunu taşır.
        (await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor))).Status.ShouldBe("approved");
    }

    [Fact]
    public async Task Edit_ResetsToPending_KeepsSource_ThenApproveMakesItHuman()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor));
        var id = solved.DraftSolutionId!.Value;
        await WithService(s => s.ApproveSolutionAsync(id, Approver));

        var edit = new ActivitySolutionUpdateRequestDto { Solution = JsonDocument.Parse("""{"r1":["c2"],"r2":["c2"]}""").RootElement.Clone() };
        var edited = await WithService(s => s.UpdateSolutionAsync(id, edit, Actor));

        edited!.Status.ShouldBe("pending");
        edited.Source.ShouldBe("AI");
        edited.EditedByName.ShouldBe("Eğitmen");
        edited.ApprovedByName.ShouldBeNull();
        edited.Solution!.Value.GetProperty("r1")[0].GetString().ShouldBe("c2");

        var approved = await WithService(s => s.ApproveSolutionAsync(id, Approver));
        approved!.Source.ShouldBe("Human");
        approved.Status.ShouldBe("approved");
    }

    [Fact]
    public async Task EditAndApproveInOneCall_ByTheEditor()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor));

        var result = await WithService(s => s.UpdateSolutionAsync(solved.DraftSolutionId!.Value, new ActivitySolutionUpdateRequestDto
        {
            Solution = JsonDocument.Parse("""{"l2":"r3","l3":"r2"}""").RootElement.Clone(),
            Approve = true,
        }, Approver));

        result!.Source.ShouldBe("Human");
        result.Status.ShouldBe("approved");
        result.EditedByName.ShouldBe("Onaylayan");
        result.ApprovedByName.ShouldBe("Onaylayan");
    }

    [Fact]
    public async Task Edit_InvalidSolution_400_AndNothingChanges()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor));
        var edit = new ActivitySolutionUpdateRequestDto { Solution = JsonDocument.Parse("""{"l1":"r1"}""").RootElement.Clone() };

        await Should.ThrowAsync<LabelingValidationException>(() => WithService(s => s.UpdateSolutionAsync(solved.DraftSolutionId!.Value, edit, Actor)));
        (await WithService(s => s.UpdateSolutionAsync(12345, edit, Actor))).ShouldBeNull();
        (await WithService(s => s.ApproveSolutionAsync(12345, Actor))).ShouldBeNull();
    }

    [Fact]
    public async Task ForcedLiveSolve_StartsANewPendingDraft_WithoutApproval()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(DragPlaceGrader.Type), Actor));
        await WithService(s => s.ApproveSolutionAsync(solved.DraftSolutionId!.Value, Approver));

        var forced = await WithService(s => s.SolveAsync(Solve(DragPlaceGrader.Type, force: true), Actor));

        forced.Origin.ShouldBe("live");
        forced.Status.ShouldBe("pending");
        forced.ApprovedByName.ShouldBeNull();
        forced.DraftSolutionId.ShouldNotBe(solved.DraftSolutionId); // review W5: onaylı satır korunur
    }

    [Fact]
    public async Task FindSolutions_ByCropHash_RestoresDrafts()
    {
        var ai = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor));
        _clock.Now = Now.AddMinutes(1);
        var other = await WithService(s => s.SolveAsync(Solve(SlotFillGrader.Type), Actor));

        var all = await WithService(s => s.FindSolutionsAsync(ai.CropSha256, null, null));
        all.Solutions.Select(x => x.InteractionType).ShouldBe(["slotFill", "matrixChoice"]);

        var matrix = await WithService(s => s.FindSolutionsAsync(ai.CropSha256, "matrixChoice", ai.PlanHash));
        matrix.Solutions.Single().DraftSolutionId.ShouldBe(ai.DraftSolutionId);
        matrix.Solutions.Single().Plan!.Value.GetProperty("type").GetString().ShouldBe("matrixChoice");

        await Should.ThrowAsync<LabelingValidationException>(() => WithService(s => s.FindSolutionsAsync("nope", null, null)));
        other.CropSha256.ShouldBe(ai.CropSha256);
    }

    // ================================================================== review düzeltmeleri

    // W6: öğretmen düzeltmesi olan sayfa onaysız yeniden okunmaz.
    [Fact]
    public async Task ReadAnswerKey_EditedPage_IsServedStale_ForceNeedsConfirmOverwrite()
    {
        var first = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        await WithService(s => s.UpdateEntryAsync(first.Entries[0].Id,
            new AnswerKeyEntryUpdateRequestDto { PrintedPage = 12, ActivityLabel = "Etkinlik 3", ItemLabel = "a", AnswerText = "9/10" }, Actor));
        await SavePromptAsync("answerKey", "Read the key, version two.");

        var stale = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        stale.Origin.ShouldBe("cached");
        stale.Stale.ShouldBeTrue();
        stale.HasEdits.ShouldBeTrue();
        stale.Entries[0].AnswerText.ShouldBe("9/10");
        _ai.KeyCalls.ShouldBe(1);

        await Should.ThrowAsync<LabelingAnswerKeyHasEditsException>(() => WithService(s => s.ReadAnswerKeyAsync(KeyRead(force: true), Actor)));
        _ai.KeyCalls.ShouldBe(1);

        var request = KeyRead(force: true);
        request.ConfirmOverwrite = true;
        var replaced = await WithService(s => s.ReadAnswerKeyAsync(request, Actor));
        replaced.Origin.ShouldBe("live");
        replaced.HasEdits.ShouldBeFalse();
        replaced.Entries[0].AnswerText.ShouldBe("3/4");
    }

    [Fact]
    public async Task DeleteEntry_IsSoft_RecordsWho_HidesFromListAndKey_AndCountsAsEdit()
    {
        var read = await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor));
        var deleted = read.Entries[0].Id;
        (await WithService(s => s.DeleteEntryAsync(deleted, Approver))).ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            var row = await ctx.LabelingAnswerKeyEntries.SingleAsync(e => e.Id == deleted);
            (row.DeletedByKeycloakId, row.DeletedAt).ShouldBe(("kc-trainer-2", (DateTime?)Now.UtcDateTime));
        }
        (await WithService(s => s.ListAnswerKeyAsync(_bookId, null, null, 0)))!.Entries.Select(e => e.Id).ShouldNotContain(deleted);
        (await Should.ThrowAsync<LabelingValidationException>(() =>
            WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, "key", keyIds: [deleted]), Actor)))).Message
            .ShouldBe("labeling.solution.keyEntryNotFound");
        (await WithService(s => s.UpdateEntryAsync(deleted, new AnswerKeyEntryUpdateRequestDto { AnswerText = "x" }, Actor))).ShouldBeNull();

        await Should.ThrowAsync<LabelingAnswerKeyHasEditsException>(() => WithService(s => s.ReadAnswerKeyAsync(KeyRead(force: true), Actor)));
    }

    // W5: onaylı / düzeltilmiş çözümün üzerine yazılmaz, yeni satır açılır.
    [Fact]
    public async Task ForcedSolve_AfterApproval_KeepsTheApprovedRow_AndRestoresNewestFirst()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor));
        await WithService(s => s.ApproveSolutionAsync(solved.DraftSolutionId!.Value, Approver));
        _clock.Now = Now.AddMinutes(2);

        var forced = await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, force: true), Actor));

        forced.DraftSolutionId.ShouldNotBe(solved.DraftSolutionId);
        forced.Status.ShouldBe("pending");
        await using (var ctx = _db.NewContext())
        {
            var old = await ctx.LabelingActivitySolutions.SingleAsync(x => x.Id == solved.DraftSolutionId);
            (old.Status, old.ApprovedByKeycloakId).ShouldBe((SolutionStatus.Approved, "kc-trainer-2"));
        }
        var restored = await WithService(s => s.FindSolutionsAsync(solved.CropSha256, "matrixChoice", null));
        restored.Solutions.Select(x => x.DraftSolutionId).ShouldBe([forced.DraftSolutionId, solved.DraftSolutionId]);
        // Kayıttan dönen en yenisi.
        (await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type), Actor))).DraftSolutionId.ShouldBe(forced.DraftSolutionId);

        // Onaylanmamış, düzeltilmemiş en yeni satırın üzerine yazılır (satır sayısı artmaz).
        await WithService(s => s.SolveAsync(Solve(MatrixChoiceGrader.Type, force: true), Actor));
        await using var check = _db.NewContext();
        (await check.LabelingActivitySolutions.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task ForcedSolve_AfterHumanEdit_KeepsTheEditedRow()
    {
        var solved = await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor));
        await WithService(s => s.UpdateSolutionAsync(solved.DraftSolutionId!.Value, new ActivitySolutionUpdateRequestDto
        {
            Solution = JsonDocument.Parse("""{"l2":"r3","l3":"r2"}""").RootElement.Clone(),
        }, Actor));

        var forced = await WithService(s => s.SolveAsync(Solve(MatchGrader.Type, force: true), Actor));

        forced.DraftSolutionId.ShouldNotBe(solved.DraftSolutionId);
        await using var ctx = _db.NewContext();
        (await ctx.LabelingActivitySolutions.SingleAsync(x => x.Id == solved.DraftSolutionId)).EditedByName.ShouldBe("Eğitmen");
    }

    [Fact]
    public async Task PlanHash_IgnoresKeyOrder()
    {
        var first = await WithService(s => s.SolveAsync(Solve(MatchGrader.Type), Actor));
        var reordered = Solve(MatchGrader.Type);
        var node = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ActivitySolutionDraftsTests.MatchPlan)!;
        var shuffled = new System.Text.Json.Nodes.JsonObject(node.Reverse().Select(p =>
            new KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>(p.Key, p.Value?.DeepClone())));
        reordered.Plan = JsonDocument.Parse(shuffled.ToJsonString()).RootElement.Clone();

        var second = await WithService(s => s.SolveAsync(reordered, Actor));

        second.Origin.ShouldBe("cached");
        second.PlanHash.ShouldBe(first.PlanHash);
    }

    [Fact]
    public async Task Solve_TooManyKeyEntries_HasItsOwnMessage()
    {
        var request = Solve(MatrixChoiceGrader.Type, "key", keyIds: Enumerable.Range(1, 61).ToList());
        (await Should.ThrowAsync<LabelingValidationException>(() => WithService(s => s.SolveAsync(request, Actor)))).Message
            .ShouldBe("labeling.solution.tooManyKeyEntries");
    }

    // D-1: tahmin metin uzunluğuyla büyür.
    [Fact]
    public void SolveEstimate_GrowsWithText()
    {
        var block = new Rectangle(0, 0, 100, 100);
        var page = new Size(1000, 1000);
        var small = ClaudeActivitySolutionAi.WorstCaseSolveInputTokens(block, page, 1568, [], 300);
        var large = ClaudeActivitySolutionAi.WorstCaseSolveInputTokens(block, page, 1568, [], 300_000);
        (large - small).ShouldBe(100_000 - 100);
    }

    // W11: fasiküllü kitapta etkinliğin dosya sayfası anahtarı aynı fasiküle daraltır.
    [Fact]
    public async Task ListAnswerKey_WithFilePage_PrefersTheActivitysFascicle()
    {
        int bookId;
        await using (var ctx = _db.NewContext())
        {
            var book = new Book { Name = "TT MatLab" };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync();
            bookId = book.Id;
            // Fasikül A: dosya 1-50 (bastırılmış = dosya), anahtarı 51-52. Fasikül B: dosya 101-150 (bastırılmış = dosya - 100),
            // anahtarı 151-152. Anahtar sayfaları ofset aralığı dışında.
            ctx.BookPageOffsets.AddRange(
                new BookPageOffset { BookId = bookId, FileFrom = 1, FileTo = 50, Offset = 0 },
                new BookPageOffset { BookId = bookId, FileFrom = 101, FileTo = 150, Offset = -100 });
            ctx.BookAnswerKeyRanges.AddRange(
                new BookAnswerKeyRange { BookId = bookId, FileFrom = 51, FileTo = 52 },
                new BookAnswerKeyRange { BookId = bookId, FileFrom = 151, FileTo = 152 });
            var pageA = new LabelingAnswerKeyPage { BookId = bookId, FilePage = 51, ImageSha256 = "a", ModelName = "m", PromptHash = "p" };
            var pageB = new LabelingAnswerKeyPage { BookId = bookId, FilePage = 151, ImageSha256 = "b", ModelName = "m", PromptHash = "p" };
            ctx.AddRange(pageA, pageB);
            await ctx.SaveChangesAsync();
            ctx.LabelingAnswerKeyEntries.AddRange(
                new LabelingAnswerKeyEntry { PageId = pageA.Id, BookId = bookId, FilePage = 51, PrintedPage = 12, ActivityLabel = "Etkinlik 3", AnswerText = "A" },
                new LabelingAnswerKeyEntry { PageId = pageB.Id, BookId = bookId, FilePage = 151, PrintedPage = 12, ActivityLabel = "Etkinlik 3", AnswerText = "B" });
            await ctx.SaveChangesAsync();
        }

        (await WithService(s => s.ListAnswerKeyAsync(bookId, 12, "Etkinlik 3", 0)))!.Total.ShouldBe(2);
        (await WithService(s => s.ListAnswerKeyAsync(bookId, null, "3", 0, filePage: 12)))!.Entries.Single().AnswerText.ShouldBe("A");
        (await WithService(s => s.ListAnswerKeyAsync(bookId, null, "3", 0, filePage: 112)))!.Entries.Single().AnswerText.ShouldBe("B");
    }

    [Fact]
    public void NarrowToFascicle_SameOffsetRangeWins_ThenNextKeyRange_ThenAll()
    {
        AnswerKeyEntryDto E(int id, int keyFilePage) => new(id, keyFilePage, 12, "Etkinlik 3", null, "x", false, null);
        BookPageOffset[] offsets = [new() { FileFrom = 1, FileTo = 60 }, new() { FileFrom = 61, FileTo = 120 }];
        BookAnswerKeyRange[] keys = [new() { FileFrom = 200, FileTo = 210 }, new() { FileFrom = 300, FileTo = 310 }];

        // Anahtar fasikülün içinde (aynı ofset aralığı).
        ActivitySolutionService.NarrowToFascicle([E(1, 55), E(2, 115)], 70, offsets, keys).Select(e => e.Id).ShouldBe([2]);
        // Anahtar sayfaları aralık dışında: etkinlikten sonraki ilk anahtar aralığı.
        ActivitySolutionService.NarrowToFascicle([E(1, 205), E(2, 305)], 70, offsets, keys).Select(e => e.Id).ShouldBe([1]);
        // Hiçbiri tutmazsa daraltma yok.
        ActivitySolutionService.NarrowToFascicle([E(1, 999)], 70, offsets, keys).Select(e => e.Id).ShouldBe([1]);
    }

    // ================================================================== kapsam genişlemesi: toplu yükleme + toplu arama

    private AnswerKeyReadRequestDto UploadRead(string image) =>
        new() { BookId = _bookId, FilePage = null, ImageBase64 = image, Model = "claude-test" };

    [Fact]
    public async Task ReadAnswerKey_WithoutFilePage_StoresByImageHash_SameImageIsCached()
    {
        var first = await WithService(s => s.ReadAnswerKeyAsync(UploadRead(_keyPage), Actor));
        first.Origin.ShouldBe("live");
        first.Entries.ShouldAllBe(e => e.FilePage == null);

        (await WithService(s => s.ReadAnswerKeyAsync(UploadRead(_keyPage), Actor))).Origin.ShouldBe("cached");

        var other = Convert.ToBase64String(TestImages.Png(1000, 1401));
        (await WithService(s => s.ReadAnswerKeyAsync(UploadRead(other), Actor))).Origin.ShouldBe("live");
        // Sayfası bilinen okuma ayrı satır.
        (await WithService(s => s.ReadAnswerKeyAsync(KeyRead(), Actor))).Origin.ShouldBe("live");
        _ai.KeyCalls.ShouldBe(3);

        await using var ctx = _db.NewContext();
        (await ctx.LabelingAnswerKeyPages.CountAsync(p => p.FilePage == null)).ShouldBe(2);
        (await ctx.LabelingAnswerKeyPages.CountAsync(p => p.FilePage == 295)).ShouldBe(1);
        // Sayfası bilinen ve aynı fasikülde olan okuma tercih edilir; sayfasızlar yalnız başka aday yoksa döner.
        (await WithService(s => s.ListAnswerKeyAsync(_bookId, 12, "Etkinlik 3", 0, filePage: 14)))!.Entries.ShouldAllBe(e => e.FilePage == 295);
        (await WithService(s => s.ListAnswerKeyAsync(_bookId, 12, "Etkinlik 3", 0)))!.Total.ShouldBe(6);
    }

    [Fact]
    public async Task ReadAnswerKey_WithoutFilePage_UnknownBook_404()
    {
        var request = UploadRead(_keyPage);
        request.BookId = 999;
        await Should.ThrowAsync<LabelingNotFoundException>(() => WithService(s => s.ReadAnswerKeyAsync(request, Actor)));
    }

    [Fact]
    public void AnswerKeyInstruction_CoversMcqTestGrids()
    {
        ActivityStructureSettings.DefaultAnswerKeyInstruction.ShouldContain("Multiple-choice test keys");
        ActivityStructureSettings.DefaultAnswerKeyInstruction.ShouldContain("itemLabel = the question number");
    }

    private async Task SeedLookupEntriesAsync()
    {
        _ai.KeyEntries =
        [
            new(14, "ETKİNLİK 24", null, "çözüm 24", false, null),
            new(14, "Etkinlik 24", "b", "çözüm 24b", false, null),
            new(15, "Etkinlik 24", null, "başka sayfa", false, null),
            new(null, "Test 01", "1", "B", false, null),
            new(null, "Test 01", "2", "C", false, null),
            new(null, "Test 02", "1", "D", false, null),
            new(14, null, "7", "A", false, null),
        ];
        await WithService(s => s.ReadAnswerKeyAsync(UploadRead(_keyPage), Actor));
    }

    [Fact]
    public async Task LookupAnswerKey_MatchesActivitiesByPageAndLabel_AndMcqBySectionAndNumber()
    {
        await SeedLookupEntriesAsync();

        // Dosya 16 → bastırılmış 14 (ofset -2).
        var response = await WithService(s => s.LookupAnswerKeyAsync(_bookId, new AnswerKeyLookupRequestDto
        {
            FilePage = 16,
            SectionLabel = "TEST 1",
            Items =
            [
                new() { Key = "a", Label = "Etkinlik 24" },
                new() { Key = "a-b", Label = "24.", ItemLabel = "b" },
                new() { Key = "q1", Label = "1" },
                new() { Key = "q2", Label = "2", Kind = "mcq" },
                new() { Key = "q9", Label = "9" },
                new() { Key = "none", Label = "" },
            ],
        }));

        var byKey = response!.Results.ToDictionary(r => r.Key, r => r.Entries.Select(e => e.AnswerText).ToList());
        response.Results.Select(r => r.Key).ShouldBe(["a", "a-b", "q1", "q2", "q9", "none"]);
        byKey["a"].ShouldBe(["çözüm 24", "çözüm 24b"]);
        byKey["a-b"].ShouldBe(["çözüm 24b"]);
        byKey["q1"].ShouldBe(["B"]);
        byKey["q2"].ShouldBe(["C"]);
        byKey["q9"].ShouldBeEmpty();
        byKey["none"].ShouldBeEmpty();
    }

    [Fact]
    public async Task LookupAnswerKey_McqWithoutSection_UsesThePrintedPage()
    {
        await SeedLookupEntriesAsync();

        var response = await WithService(s => s.LookupAnswerKeyAsync(_bookId, new AnswerKeyLookupRequestDto
        {
            FilePage = 16,
            Items = [new() { Key = "q7", Label = "7", Kind = "mcq" }, new() { Key = "q1", Label = "1", Kind = "mcq" }],
        }));

        response!.Results[0].Entries.Single().AnswerText.ShouldBe("A");
        response.Results[1].Entries.ShouldBeEmpty();
    }

    public static TheoryData<Action<AnswerKeyLookupRequestDto>> InvalidLookups => new()
    {
        r => r.Items = [],
        r => r.Items = Enumerable.Range(0, 61).Select(i => new AnswerKeyLookupItemDto { Key = $"k{i}", Label = "1" }).ToList(),
        r => r.Items = [new() { Key = "a", Label = "1" }, new() { Key = "a", Label = "2" }],
        r => r.Items = [new() { Key = "", Label = "1" }],
        r => r.Items = [new() { Key = "a", Label = new string('x', 65) }],
        r => r.Items = [new() { Key = "a", Label = "1", Kind = "essay" }],
        r => r.FilePage = 0,
    };

    [Theory]
    [MemberData(nameof(InvalidLookups))]
    public async Task LookupAnswerKey_InvalidRequest_400(Action<AnswerKeyLookupRequestDto> mutate)
    {
        var request = new AnswerKeyLookupRequestDto { FilePage = 16, Items = [new() { Key = "a", Label = "1" }] };
        mutate(request);

        await Should.ThrowAsync<LabelingValidationException>(() => WithService(s => s.LookupAnswerKeyAsync(_bookId, request)));
        (await WithService(s => s.LookupAnswerKeyAsync(999, new AnswerKeyLookupRequestDto
        {
            FilePage = 16, Items = [new() { Key = "a", Label = "1" }],
        }))).ShouldBeNull();
    }

    // ================================================================== sahte Claude

    private sealed class FakeAi : IActivitySolutionAi
    {
        public const int InputTokens = 1000;
        public const int OutputTokens = 200;
        public const decimal CostUsd = 0.006m;

        public sealed record SolveCall(string Type, ActivityPromptSettings Prompt, IReadOnlyList<AnswerKeyInput> Key, Rectangle Block);

        public bool IsConfigured { get; set; } = true;

        public int KeyCalls;

        public ActivityPromptSettings? LastKeyPrompt { get; private set; }

        public ConcurrentQueue<SolveCall> SolveCallsQueue { get; } = new();

        public List<SolveCall> SolveCalls => SolveCallsQueue.ToList();

        public bool FailKey { get; set; }

        public bool FailSolve { get; set; }

        public IReadOnlyList<AnswerKeyReadEntry> KeyEntries { get; set; } =
        [
            new(12, "Etkinlik 3", "a", "3/4", false, null),
            new(12, "Etkinlik 3", "b", "D", false, null),
            new(14, "Etkinlik 5", null, null, true, new Rectangle(100, 200, 300, 150)),
        ];

        public Func<string, string>? SolutionFor { get; set; }

        public Task<AnswerKeyReading> ReadAnswerKeyAsync(
            LabelingImage page, ActivityPromptSettings prompt, LabelingAiModelOption model, CancellationToken ct = default)
        {
            Interlocked.Increment(ref KeyCalls);
            LastKeyPrompt = prompt;
            if (FailKey)
                throw new LabelingAiException("simulated", usage: new LabelingAiUsage(InputTokens, OutputTokens));
            return Task.FromResult(new AnswerKeyReading(KeyEntries, InputTokens, OutputTokens));
        }

        public Task<ActivitySolving> SolveAsync(
            LabelingImage page, Rectangle block, string interactionType, string planJson, ActivityPromptSettings prompt,
            IReadOnlyList<AnswerKeyInput> key, LabelingAiModelOption model, CancellationToken ct = default)
        {
            SolveCallsQueue.Enqueue(new SolveCall(interactionType, prompt, key, block));
            if (FailSolve)
                throw new LabelingAiException("simulated", usage: new LabelingAiUsage(InputTokens, OutputTokens));
            var solution = SolutionFor?.Invoke(interactionType) ?? interactionType switch
            {
                MatrixChoiceGrader.Type => """{"r1":["c1"],"r2":["c2"]}""",
                SlotFillGrader.Type => """{"s1":{"value":"12"},"s2":{"accept":["Ankara"]},"s3":{"optionIds":["s3o2"]},"s4":{"accept":[">"]}}""",
                DragPlaceGrader.Type => """{"t1":["k1","k3"],"t2":["k2"]}""",
                MatchGrader.Type => """{"l2":"r2","l3":"r3"}""",
                _ => throw new ArgumentOutOfRangeException(nameof(interactionType)),
            };
            return Task.FromResult(new ActivitySolving(solution, InputTokens, OutputTokens));
        }
    }
}
