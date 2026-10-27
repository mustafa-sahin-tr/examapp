using System.Collections.Concurrent;
using System.Drawing;
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
/// Issue #579: etkinlik yapısı servisi — tipe göre talimat, önbellek (kırpma + tip + model + o tipin prompt'u), force, paralel
/// sınır, kota/bütçe 429 (baştan), blok başına hata yalıtımı, doğrulama sonucu ve tip başına prompt sürümleri.
/// </summary>
public class ActivityStructureServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly LabelingActor Actor = new("kc-trainer-1", "Eğitmen");

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly InMemoryFixedWindowCounterStore _counters;
    private readonly FakeExtractor _extractor = new();

    // Düz renkli sayfa: kırpma hash'i yalnız boyuta bağlı. Testler blokları farklı boyutlarla ayırır.
    private readonly string _page = Convert.ToBase64String(TestImages.Png(800, 1000));

    public ActivityStructureServiceTests() => _counters = new InMemoryFixedWindowCounterStore(_clock);

    public void Dispose() => _db.Dispose();

    private static LabelingAiModelOption Model(string id = "claude-test") => new()
    {
        Id = id, DisplayName = "Test", InputUsdPerMTok = 3m, OutputUsdPerMTok = 15m, IsDefault = true,
    };

    private ActivityStructureService NewService(AppDbContext ctx, int liveCallsPerHour = 60, int maxParallel = 4,
        decimal dailyBudgetUsdPerUser = 5m, decimal dailyBudgetUsdTotal = 25m, int requestsPerMinute = 30,
        TimeSpan? liveCallTimeout = null) => new(
        ctx,
        _extractor,
        InteractionGraderRegistry.Default,
        new ImageHelper(),
        _counters,
        Options.Create(new LabelingAiOptions
        {
            Models = [Model(), Model("claude-other")],
            LiveCallsPerHour = liveCallsPerHour,
            MaxParallelCrops = maxParallel,
            DailyBudgetUsdPerUser = dailyBudgetUsdPerUser,
            DailyBudgetUsdTotal = dailyBudgetUsdTotal,
            ActivityStructureRequestsPerMinute = requestsPerMinute,
        }),
        _clock,
        NullLogger<ActivityStructureService>.Instance)
    {
        LiveCallTimeout = liveCallTimeout ?? TimeSpan.FromMinutes(3),
    };

    private static ActivityStructureBlockDto Block(string key, string type, int width = 100, int height = 50, int x = 10, int y = 10) =>
        new() { Key = key, InteractionType = type, X = x, Y = y, Width = width, Height = height };

    private ActivityStructureRequestDto Request(bool force = false, string model = "claude-test", params ActivityStructureBlockDto[] blocks) =>
        new() { ImageBase64 = _page, Model = model, Force = force, Blocks = blocks.ToList() };

    private async Task<ActivityStructureResponseDto> ExtractAsync(ActivityStructureRequestDto request, int liveCallsPerHour = 60,
        int maxParallel = 4)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx, liveCallsPerHour, maxParallel).ExtractAsync(request, Actor);
    }

    private async Task<long> CallsCounted() => (await _counters.PeekAsync("labeling-ai:" + Actor.KeycloakId)).Count;

    private async Task<long> SpentMicroUsd() => (await _counters.PeekAsync("labeling-ai-usd:" + Actor.KeycloakId)).Count;

    // ---------- Akış ----------

    [Fact]
    public async Task Extract_MixedTypes_EachBlockUsesItsOwnTypeInstruction_ResultsInRequestOrder()
    {
        var response = await ExtractAsync(Request(false, "claude-test",
            Block("b-slot", SlotFillGrader.Type, 120),
            Block("b-matrix", MatrixChoiceGrader.Type, 100),
            Block("b-match", "MATCH", 140),
            Block("b-drag", DragPlaceGrader.Type, 160)));

        response.Results.Select(r => (r.Key, r.InteractionType, r.Status, r.Origin))
            .ShouldBe([
                ("b-slot", "slotFill", "ok", "live"), ("b-matrix", "matrixChoice", "ok", "live"),
                ("b-match", "match", "ok", "live"), ("b-drag", "dragPlace", "ok", "live"),
            ]);
        _extractor.Calls.Count.ShouldBe(4);
        _extractor.Calls.ShouldAllBe(c => c.Instruction == ActivityStructureSettings.DefaultInstruction(c.Type) && c.Version == 0);
        _extractor.Calls.Select(c => c.Type).OrderBy(t => t).ShouldBe(["dragPlace", "match", "matrixChoice", "slotFill"]);
        response.Results.ShouldAllBe(r => r.Valid && r.ValidationErrors.Count == 0 && r.Plan != null && r.PromptVersion == 0);
        response.Results[0].Instruction.ShouldBe("printed slotFill");
        response.Results[0].Plan!.Value.GetProperty("type").GetString().ShouldBe("slotFill");
        (await CallsCounted()).ShouldBe(4);
        response.Results[0].InputTokens.ShouldBe(FakeExtractor.InputTokens);
        response.Results[0].EstimatedCostUsd.ShouldBe(FakeExtractor.CostUsd);
    }

    [Fact]
    public async Task Extract_SecondTime_SameCropAndPrompt_IsServedFromCacheForFree()
    {
        var request = Request(false, "claude-test", Block("a", MatrixChoiceGrader.Type), Block("b", SlotFillGrader.Type, 120));
        await ExtractAsync(request);
        var spent = await SpentMicroUsd();

        var second = await ExtractAsync(request);

        second.Results.ShouldAllBe(r => r.Origin == "cached" && r.Status == "ok" && r.Valid);
        _extractor.Calls.Count.ShouldBe(2);
        (await CallsCounted()).ShouldBe(2);
        (await SpentMicroUsd()).ShouldBe(spent);
        // Kayıttan dönen sonuç: ilk çağrının token/maliyeti, aynı plan.
        second.Results[0].InputTokens.ShouldBe(FakeExtractor.InputTokens);
        second.Results[0].Plan!.Value.GetProperty("type").GetString().ShouldBe("matrixChoice");
    }

    [Fact]
    public async Task Extract_CacheIsPerModel_AndSameCropAtAnotherPositionHitsTheCache()
    {
        await ExtractAsync(Request(false, "claude-test", Block("a", MatrixChoiceGrader.Type)));

        // Aynı boyut, başka konum: düz sayfada kırpma baytları aynı → aynı SHA-256 → kayıttan.
        var moved = await ExtractAsync(Request(false, "claude-test", Block("a", MatrixChoiceGrader.Type, x: 300, y: 400)));
        var otherModel = await ExtractAsync(Request(false, "claude-other", Block("a", MatrixChoiceGrader.Type)));

        moved.Results.Single().Origin.ShouldBe("cached");
        otherModel.Results.Single().Origin.ShouldBe("live");
        _extractor.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Extract_SameCropAndTypeTwiceInOneRequest_MakesOneCall()
    {
        var response = await ExtractAsync(Request(false, "claude-test",
            Block("a", SlotFillGrader.Type), Block("b", SlotFillGrader.Type, x: 400), Block("c", MatchGrader.Type)));

        _extractor.Calls.Count.ShouldBe(2);
        (await CallsCounted()).ShouldBe(2);
        response.Results.Select(r => r.Key).ShouldBe(["a", "b", "c"]);
        response.Results.ShouldAllBe(r => r.Origin == "live");
    }

    [Fact]
    public async Task Extract_PromptChangeForOneType_InvalidatesOnlyThatTypesCache()
    {
        var request = Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type), Block("s", SlotFillGrader.Type, 120));
        await ExtractAsync(request);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SavePromptAsync(MatrixChoiceGrader.Type, new() { Instruction = "New matrix {width}" }, Actor)).Version.ShouldBe(1);

        var second = await ExtractAsync(request);

        second.Results.Select(r => (r.Key, r.Origin, r.PromptVersion)).ShouldBe([("m", "live", 1), ("s", "cached", 0)]);
        _extractor.Calls.Count.ShouldBe(3);
        _extractor.Calls.Last().ShouldBe(new FakeExtractor.Call(MatrixChoiceGrader.Type, "New matrix {width}", 1, new Rectangle(10, 10, 100, 50)));

        // Kayıt üzerine yazıldı: üçüncü istek ikisini de kayıttan verir.
        (await ExtractAsync(request)).Results.ShouldAllBe(r => r.Origin == "cached");
        _extractor.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Extract_TypeChangedForSameBlock_IsReExtractedWithTheNewTypesInstruction()
    {
        await ExtractAsync(Request(false, "claude-test", Block("x", MatrixChoiceGrader.Type)));

        var changed = await ExtractAsync(Request(false, "claude-test", Block("x", SlotFillGrader.Type)));

        changed.Results.Single().Origin.ShouldBe("live");
        changed.Results.Single().InteractionType.ShouldBe("slotFill");
        _extractor.Calls.Last().Type.ShouldBe(SlotFillGrader.Type);
        _extractor.Calls.Last().Instruction.ShouldBe(ActivityStructureSettings.DefaultSlotFillInstruction);

        // Eski tipin kaydı durur: geri dönünce ücretsiz.
        (await ExtractAsync(Request(false, "claude-test", Block("x", MatrixChoiceGrader.Type)))).Results.Single().Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task Extract_Force_BypassesCacheAndOverwrites()
    {
        var request = Request(false, "claude-test", Block("a", MatchGrader.Type));
        await ExtractAsync(request);
        _extractor.Instruction = "second run";

        var forced = await ExtractAsync(Request(true, "claude-test", Block("a", MatchGrader.Type)));

        forced.Results.Single().Origin.ShouldBe("live");
        forced.Results.Single().Instruction.ShouldBe("second run");
        _extractor.Calls.Count.ShouldBe(2);
        (await ExtractAsync(request)).Results.Single().Instruction.ShouldBe("second run");
        await using var ctx = _db.NewContext();
        (await ctx.LabelingActivityResults.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Extract_LiveCallsRunInParallel_UpToMaxParallelCrops()
    {
        _extractor.Delay = TimeSpan.FromMilliseconds(150);

        var response = await ExtractAsync(Request(false, "claude-test",
            Enumerable.Range(0, 6).Select(i => Block($"b{i}", SlotFillGrader.Type, 100 + i * 10)).ToArray()), maxParallel: 2);

        response.Results.Count.ShouldBe(6);
        _extractor.Calls.Count.ShouldBe(6);
        _extractor.MaxConcurrent.ShouldBe(2);
    }

    [Fact]
    public async Task Extract_ValidationResultIsPassedThrough_InvalidDraftStillReturned()
    {
        _extractor.PlanFor = type => type == MatrixChoiceGrader.Type
            ? """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"c1","label":"D"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":5,"height":5}}]}"""
            : null;

        var response = await ExtractAsync(Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type), Block("s", SlotFillGrader.Type, 120)));

        var matrix = response.Results[0];
        matrix.Status.ShouldBe("ok");
        matrix.Valid.ShouldBeFalse();
        matrix.ValidationErrors.ShouldBe(["labeling.activity.invalid.tooFewColumns"]);
        matrix.Plan.ShouldNotBeNull();
        response.Results[1].Valid.ShouldBeTrue();

        // Kayıttan dönen taslak da aynı şekilde işaretlenir.
        var cached = await ExtractAsync(Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type)));
        cached.Results.Single().Valid.ShouldBeFalse();
    }

    // ---------- Kota / bütçe / yapılandırma ----------

    [Fact]
    public async Task Extract_NotEnoughHourlyCallsForTheLiveBlocks_Is429UpFront_AndNothingIsCalled()
    {
        await ExtractAsync(Request(false, "claude-test", Block("a", MatchGrader.Type)), liveCallsPerHour: 3);

        // 1 kayıtlı + 2 yeni = 2 canlı → 3/3, izinli.
        var ok = await ExtractAsync(Request(false, "claude-test",
            Block("a", MatchGrader.Type), Block("b", MatchGrader.Type, 120), Block("c", MatchGrader.Type, 140)), liveCallsPerHour: 3);
        ok.Results.Select(r => r.Origin).ShouldBe(["cached", "live", "live"]);
        _extractor.Calls.Count.ShouldBe(3);

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() => ExtractAsync(Request(false, "claude-test",
            Block("a", MatchGrader.Type), Block("d", MatchGrader.Type, 160)), liveCallsPerHour: 3));

        ex.Budget.ShouldBeFalse();
        ex.RetryAfter.ShouldNotBeNull();
        _extractor.Calls.Count.ShouldBe(3);

        // Tamamı kayıtlı istek kotaya takılmaz.
        (await ExtractAsync(Request(false, "claude-test", Block("b", MatchGrader.Type, 120)), liveCallsPerHour: 3))
            .Results.Single().Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task Extract_RequestNeedingMoreCallsThanTheLimit_IsRejectedWhole()
    {
        await Should.ThrowAsync<LabelingRateLimitException>(() => ExtractAsync(Request(false, "claude-test",
            Block("a", MatchGrader.Type), Block("b", MatchGrader.Type, 120), Block("c", MatchGrader.Type, 140)), liveCallsPerHour: 2));

        _extractor.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Extract_UserBudgetSpent_Is429Budget_AndNothingIsCalled()
    {
        await _counters.TryAcquireAsync("labeling-ai-usd:" + Actor.KeycloakId, 5_000_000, int.MaxValue, TimeSpan.FromHours(24));

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() =>
            ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type))));

        ex.Budget.ShouldBeTrue();
        _extractor.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Extract_TotalBudgetSpent_Is429Budget()
    {
        await _counters.TryAcquireAsync("labeling-ai-usd:all", 25_000_000, int.MaxValue, TimeSpan.FromHours(24));

        (await Should.ThrowAsync<LabelingRateLimitException>(() =>
            ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type))))).Budget.ShouldBeTrue();
    }

    [Fact]
    public async Task Extract_LiveCost_IsChargedToUserAndTotalBudgets()
    {
        await ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type), Block("b", MatchGrader.Type, 120)));

        // 1000 girdi × $3 + 200 çıktı × $15 / 1M = $0.006 → 6000 mikro-USD, iki blok.
        (await SpentMicroUsd()).ShouldBe(12_000);
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(12_000);
    }

    [Fact]
    public async Task Extract_NotConfigured_NeedsLiveCall_Throws503Exception_ButCachedOnlyRequestWorks()
    {
        await ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type)));
        _extractor.IsConfigured = false;

        (await ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type)))).Results.Single().Origin.ShouldBe("cached");
        await Should.ThrowAsync<LabelingAiUnavailableException>(() =>
            ExtractAsync(Request(false, "claude-test", Block("b", SlotFillGrader.Type, 150))));
    }

    [Fact]
    public async Task Extract_OneBlockFails_OtherBlocksReturn_PaidFailureIsCharged_AndFailureIsNotCached()
    {
        _extractor.FailTypes.Add(MatrixChoiceGrader.Type);

        var response = await ExtractAsync(Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type), Block("s", SlotFillGrader.Type, 120)));

        var failed = response.Results[0];
        failed.Status.ShouldBe("error");
        failed.Origin.ShouldBeNull();
        failed.Plan.ShouldBeNull();
        failed.Valid.ShouldBeFalse();
        failed.Error.ShouldBe(ActivityStructureService.AiFailedKey);
        failed.InputTokens.ShouldBe(FakeExtractor.InputTokens);
        response.Results[1].Status.ShouldBe("ok");
        // Başarısız ama ödenmiş çağrı da bütçeye sayılır: 2 × 6000.
        (await SpentMicroUsd()).ShouldBe(12_000);

        _extractor.FailTypes.Clear();
        var retry = await ExtractAsync(Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type), Block("s", SlotFillGrader.Type, 120)));
        retry.Results.Select(r => r.Origin).ShouldBe(["live", "cached"]);
    }

    [Fact]
    public async Task Extract_UnpaidFailure_ChargesNothing()
    {
        _extractor.FailTypes.Add(MatchGrader.Type);
        _extractor.FailWithoutUsage = true;

        var response = await ExtractAsync(Request(false, "claude-test", Block("a", MatchGrader.Type)));

        response.Results.Single().Status.ShouldBe("error");
        response.Results.Single().InputTokens.ShouldBeNull();
        (await SpentMicroUsd()).ShouldBe(0);
    }

    // ---------- Review düzeltmeleri: iptal, rezervasyon, kayıt hatası, istek sınırı ----------

    [Fact]
    public async Task Extract_ClientCancelsDuringCalls_PaidResultsAreStillCachedAndCharged()
    {
        _extractor.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        await using var ctx = _db.NewContext();
        var running = NewService(ctx).ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type)), Actor, cts.Token);

        await _extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        _extractor.Hold.SetResult();
        var response = await running;

        response.Results.Single().Status.ShouldBe("ok");
        (await SpentMicroUsd()).ShouldBe(6_000);
        (await CallsCounted()).ShouldBe(1);
        _extractor.Hold = null;
        (await ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type)))).Results.Single().Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task Extract_ServerTimeout_BlocksThatNeverStartedGetTheirHourlyQuotaBack()
    {
        _extractor.Hold = new TaskCompletionSource();   // hiç bitmez: zaman aşımı keser
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, maxParallel: 1, liveCallTimeout: TimeSpan.FromMilliseconds(200)).ExtractAsync(
            Request(false, "claude-test", Block("a", MatchGrader.Type), Block("b", MatchGrader.Type, 120), Block("c", MatchGrader.Type, 140)),
            Actor);

        response.Results.ShouldAllBe(r => r.Status == "error" && r.InputTokens == null);
        _extractor.Calls.Count.ShouldBe(1);
        (await CallsCounted()).ShouldBe(1);       // başlayan tek çağrı sayılır, kuyruktaki iki çağrı iade
        (await SpentMicroUsd()).ShouldBe(0);      // rezervasyon tamamen iade (kullanım bilinmiyor)
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(0);
        (await ctx.LabelingActivityResults.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Extract_WorstCaseCostIsReservedWhileCallsRun_ThenSettledToActual()
    {
        _extractor.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ctx = _db.NewContext();
        var running = NewService(ctx).ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type)), Actor);

        await _extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Ayrılan: (kırpma görseli + 3000 talimat) × $3 + 8000 × $15 / 1M ≈ $0.129 — gerçek maliyetten (0.006) çok büyük.
        var reserved = await SpentMicroUsd();
        reserved.ShouldBeGreaterThan(120_000);
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(reserved);

        _extractor.Hold.SetResult();
        await running;

        (await SpentMicroUsd()).ShouldBe(6_000);
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(6_000);
    }

    [Fact]
    public async Task Extract_ReservationWouldExceedUserBudget_Is429Budget_RolledBack_NoCallsAndNoQuotaUsed()
    {
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() => NewService(ctx, dailyBudgetUsdPerUser: 0.05m)
            .ExtractAsync(Request(false, "claude-test", Block("a", SlotFillGrader.Type)), Actor));

        ex.Budget.ShouldBeTrue();
        _extractor.Calls.ShouldBeEmpty();
        (await SpentMicroUsd()).ShouldBe(0);
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(0);
        (await CallsCounted()).ShouldBe(0);
    }

    [Fact]
    public async Task Extract_ReservationWouldExceedTotalBudget_RollsBackBothCounters()
    {
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<LabelingRateLimitException>(() => NewService(ctx, dailyBudgetUsdTotal: 0.2m).ExtractAsync(
            Request(false, "claude-test", Block("a", SlotFillGrader.Type), Block("b", SlotFillGrader.Type, 120)), Actor))).Budget.ShouldBeTrue();

        (await SpentMicroUsd()).ShouldBe(0);
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(0);
        _extractor.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Extract_HourlyLimitRejected_ReleasesTheBudgetReservation()
    {
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingRateLimitException>(() => NewService(ctx, liveCallsPerHour: 1).ExtractAsync(
            Request(false, "claude-test", Block("a", SlotFillGrader.Type), Block("b", SlotFillGrader.Type, 120)), Actor));

        (await SpentMicroUsd()).ShouldBe(0);
        (await CallsCounted()).ShouldBe(0);     // reddedilen istek kotayı yemez
    }

    [Fact]
    public async Task Extract_CachingOneBlockFails_ResponseStillReturned_OtherBlockCached()
    {
        var failing = new FailActivityResultSave(MatrixChoiceGrader.Type);
        await using (var ctx = _db.NewContext(failing))
        {
            var response = await NewService(ctx).ExtractAsync(
                Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type), Block("s", SlotFillGrader.Type, 120)), Actor);

            response.Results.Select(r => (r.Status, r.Origin, r.Valid)).ShouldBe([("ok", "live", true), ("ok", "live", true)]);
            response.Results[0].Plan!.Value.GetProperty("type").GetString().ShouldBe("matrixChoice");
            failing.Failures.ShouldBe(1);
        }

        var again = await ExtractAsync(Request(false, "claude-test", Block("m", MatrixChoiceGrader.Type), Block("s", SlotFillGrader.Type, 120)));
        again.Results.Select(r => r.Origin).ShouldBe(["live", "cached"]);
    }

    [Fact]
    public async Task Extract_PerUserRequestLimit_AppliesToCachedRequestsToo()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, requestsPerMinute: 2);
        var request = Request(false, "claude-test", Block("a", SlotFillGrader.Type));
        await service.ExtractAsync(request, Actor);
        (await service.ExtractAsync(request, Actor)).Results.Single().Origin.ShouldBe("cached");

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() => service.ExtractAsync(request, Actor));

        ex.Budget.ShouldBeFalse();
        ex.RetryAfter.ShouldNotBeNull();
        // Başka kullanıcının sınırı ayrı.
        (await service.ExtractAsync(request, new LabelingActor("kc-other", "Diğer"))).Results.Single().Origin.ShouldBe("cached");
    }

    [Fact]
    public void WorstCaseInputTokens_UsesTheDownscaledCropPlusPromptAllowance()
    {
        // 100x50 blok + 16 px pay = 132x82 → 10824 px / 750 = 15 token.
        ClaudeActivityStructureExtractor.WorstCaseInputTokens(new Rectangle(200, 300, 100, 50), new Size(1000, 1400), 1568)
            .ShouldBe(15 + ClaudeActivityStructureExtractor.PromptTokenAllowance);
        // 3168x1032 kırpma → uzun kenar 1568'e: 1568 x 511 → 1069 token (ceil).
        ClaudeActivityStructureExtractor.WorstCaseInputTokens(new Rectangle(16, 16, 3136, 1000), new Size(4000, 2000), 1568)
            .ShouldBe((long)Math.Ceiling(1568d * Math.Ceiling(1032 * (1568d / 3168)) / 750) + ClaudeActivityStructureExtractor.PromptTokenAllowance);
    }

    /// <summary>Belirli tipin <c>LabelingActivityResult</c> kaydında SaveChanges'i patlatır (DB hatası benzetimi).</summary>
    private sealed class FailActivityResultSave(string type) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<LabelingActivityResult>().Any(e => e.Entity.InteractionType == type))
            {
                Failures++;
                throw new DbUpdateException("simulated database failure");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // ---------- Girdi doğrulama ----------

    public static TheoryData<string, ActivityStructureBlockDto[]> InvalidBlocks => new()
    {
        { "labeling.activity.invalidBlocks", [] },
        { "labeling.activity.invalidBlocks", Enumerable.Range(0, 21).Select(i => Block($"k{i}", MatchGrader.Type)).ToArray() },
        { "labeling.activity.invalidBlockKey", [Block("", MatchGrader.Type)] },
        { "labeling.activity.invalidBlockKey", [Block(new string('k', 65), MatchGrader.Type)] },
        { "labeling.activity.duplicateBlockKey", [Block("a", MatchGrader.Type), Block("a", SlotFillGrader.Type)] },
        { "labeling.activity.unknownType", [Block("a", "mcq")] },
        { "labeling.activity.unknownType", [Block("a", "dragDropLabeling")] },
        { "labeling.invalidRegion", [Block("a", MatchGrader.Type, width: 0)] },
        { "labeling.invalidRegion", [Block("a", MatchGrader.Type, x: -5)] },
        { "labeling.invalidRegion", [Block("a", MatchGrader.Type, x: 900)] },   // sayfa 800 px: tamamen dışarıda
    };

    [Theory]
    [MemberData(nameof(InvalidBlocks))]
    public async Task Extract_InvalidBlocks_Throw400Keys(string expected, ActivityStructureBlockDto[] blocks)
    {
        var ex = await Should.ThrowAsync<LabelingValidationException>(() => ExtractAsync(Request(false, "claude-test", blocks)));

        ex.Message.ShouldBe(expected);
        _extractor.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Extract_NullBlocks_UnknownModelAndBadImage_Throw400()
    {
        (await Should.ThrowAsync<LabelingValidationException>(() => ExtractAsync(new ActivityStructureRequestDto
            { ImageBase64 = _page, Model = "claude-test", Blocks = null }))).Message.ShouldBe("labeling.activity.invalidBlocks");
        (await Should.ThrowAsync<LabelingValidationException>(() =>
            ExtractAsync(Request(false, "gpt-x", Block("a", MatchGrader.Type))))).Message.ShouldBe("labeling.unknownModel");
        (await Should.ThrowAsync<LabelingValidationException>(() => ExtractAsync(new ActivityStructureRequestDto
            { ImageBase64 = "not-base64!", Model = "claude-test", Blocks = [Block("a", MatchGrader.Type)] }))).Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public async Task Extract_BlockPartlyOutsideThePage_IsClampedToThePage()
    {
        await ExtractAsync(Request(false, "claude-test", Block("a", MatchGrader.Type, width: 200, height: 100, x: 700, y: 950)));

        _extractor.Calls.Single().Block.ShouldBe(new Rectangle(700, 950, 100, 50));
    }

    // ---------- Tip başına prompt sürümleri ----------

    [Fact]
    public async Task GetPrompts_NoVersions_ReturnsCodeDefaultsAsVersionZeroForEveryType()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).GetPromptsAsync();

        state.Types.Select(t => t.InteractionType).ShouldBe(["matrixChoice", "slotFill", "dragPlace", "match"]);
        state.Types.ShouldAllBe(t => t.Active.Version == 0 && t.Defaults.Version == 0 && t.History.Count == 0
                                     && t.Active.Instruction == ActivityStructureSettings.DefaultInstruction(t.InteractionType)
                                     && t.Active.CreatedAt == null && t.Active.CreatedByName == null);
    }

    [Fact]
    public async Task SavePrompt_VersionsArePerType_AndSameContentCreatesNoVersion()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        // Varsayılanla aynı içerik (boşluklar kırpılır) → sürüm yok, v0 döner.
        (await service.SavePromptAsync("matrixChoice", new() { Instruction = "  " + ActivityStructureSettings.DefaultMatrixChoiceInstruction + "\n" }, Actor))
            .Version.ShouldBe(0);
        var v1 = await service.SavePromptAsync("MatrixChoice", new() { Instruction = "Matrix v1" }, Actor);
        var again = await service.SavePromptAsync("matrixChoice", new() { Instruction = "Matrix v1 " }, Actor);
        var slot = await service.SavePromptAsync("slotFill", new() { Instruction = "Slot v1" }, Actor);
        var v2 = await service.SavePromptAsync("matrixChoice", new() { Instruction = "Matrix v2" }, Actor);

        v1.ShouldBe(new ActivityPromptVersionDto(1, "Matrix v1", "Eğitmen", Now.UtcDateTime));
        again.Version.ShouldBe(1);
        slot.Version.ShouldBe(1);
        v2.Version.ShouldBe(2);
        (await ctx.LabelingActivityPromptVersions.CountAsync()).ShouldBe(3);

        var state = await service.GetPromptsAsync();
        var matrix = state.Types.Single(t => t.InteractionType == "matrixChoice");
        matrix.Active.Instruction.ShouldBe("Matrix v2");
        matrix.History.Select(h => h.Version).ShouldBe([2, 1]);
        matrix.Defaults.Instruction.ShouldBe(ActivityStructureSettings.DefaultMatrixChoiceInstruction);
        state.Types.Single(t => t.InteractionType == "slotFill").Active.Version.ShouldBe(1);
        state.Types.Single(t => t.InteractionType == "match").Active.Version.ShouldBe(0);

        // Varsayılana dönmek yeni sürümdür (etkin içerik farklı).
        (await service.SavePromptAsync("matrixChoice", new() { Instruction = ActivityStructureSettings.DefaultMatrixChoiceInstruction }, Actor))
            .Version.ShouldBe(3);
    }

    [Theory]
    [InlineData("mcq", "x", "labeling.activity.unknownType")]
    [InlineData("matrixChoice", "   ", "labeling.promptRequired")]
    [InlineData("matrixChoice", null, "labeling.promptRequired")]
    public async Task SavePrompt_InvalidInput_Throws400Keys(string type, string? instruction, string expected)
    {
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SavePromptAsync(type, new() { Instruction = instruction }, Actor))).Message.ShouldBe(expected);
    }

    [Fact]
    public async Task SavePrompt_TooLong_Throws400()
    {
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SavePromptAsync("match",
            new() { Instruction = new string('x', ActivityStructureSettings.MaxInstructionLength + 1) }, Actor)))
            .Message.ShouldBe("labeling.promptTooLong");
        (await NewService(ctx).SavePromptAsync("match",
            new() { Instruction = new string('x', ActivityStructureSettings.MaxInstructionLength) }, Actor)).Version.ShouldBe(1);
    }

    /// <summary>
    /// Ağa çıkmayan çıkarıcı: her tip için geçerli bir plan döner (ya da <see cref="PlanFor"/>), çağrıları ve eşzamanlılığı kaydeder.
    /// </summary>
    private sealed class FakeExtractor : IActivityStructureExtractor
    {
        public const int InputTokens = 1000;
        public const int OutputTokens = 200;
        public const decimal CostUsd = 0.006m;

        public sealed record Call(string Type, string Instruction, int Version, Rectangle Block);

        private int _current;
        private int _max;

        public bool IsConfigured { get; set; } = true;

        public ConcurrentQueue<Call> Calls { get; } = new();

        public HashSet<string> FailTypes { get; } = new();

        public bool FailWithoutUsage { get; set; }

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public string? Instruction { get; set; }

        public Func<string, string?>? PlanFor { get; set; }

        /// <summary>Verilirse her çağrı <see cref="Entered"/>'ı işaretler ve bu görev tamamlanana kadar bekler (iptali dinleyerek).</summary>
        public TaskCompletionSource? Hold { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrent => _max;

        public async Task<ActivityExtraction> ExtractAsync(
            LabelingImage image, Rectangle block, ActivityPromptSettings prompt, LabelingAiModelOption model, CancellationToken ct = default)
        {
            Calls.Enqueue(new Call(prompt.InteractionType, prompt.Instruction, prompt.Version, block));
            var now = Interlocked.Increment(ref _current);
            int seen;
            while ((seen = _max) < now && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }
            try
            {
                if (Hold is not null)
                {
                    Entered.TrySetResult();
                    await Hold.Task.WaitAsync(ct);
                }
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, ct);
                if (FailTypes.Contains(prompt.InteractionType))
                    throw new LabelingAiException("simulated", usage: FailWithoutUsage ? null : new LabelingAiUsage(InputTokens, OutputTokens));
                var plan = PlanFor?.Invoke(prompt.InteractionType) ?? ValidPlan(prompt.InteractionType);
                return new ActivityExtraction(plan, Instruction ?? $"printed {prompt.InteractionType}", InputTokens, OutputTokens);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        private static string ValidPlan(string type) => type switch
        {
            MatrixChoiceGrader.Type =>
                """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"c1","label":"D"},{"id":"c2","label":"Y"}],"rows":[{"id":"r1","label":"x","box":{"x":0,"y":0,"width":10,"height":10}}]}""",
            SlotFillGrader.Type =>
                """{"version":1,"type":"slotFill","slots":[{"id":"s1","box":{"x":0,"y":0,"width":10,"height":10},"input":{"kind":"text"}}]}""",
            DragPlaceGrader.Type =>
                """{"version":1,"type":"dragPlace","mode":"place","reuse":false,"targets":[{"id":"t1","capacity":1}],"cards":[{"id":"k1","label":"a"}]}""",
            MatchGrader.Type =>
                """{"version":1,"type":"match","left":[{"id":"l1","label":"a"}],"right":[{"id":"r1","label":"b"}],"prelinked":[]}""",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
