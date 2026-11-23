using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Hangfire;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1022: soru aktarımı aşaması — sayfa bazlı numaralama, anahtardan cevap, inceleme işaretleri, bir sayfanın / testin
/// hatasının diğerlerini durdurmaması, kırpma tekrarlarının sayılması, kaldığı testten devam ve maliyet.
/// </summary>
public class BookQuestionImportTests : IDisposable
{
    private const int OwnerId = 41;
    private const int PageWidth = 2363;
    private const int PageHeight = 3300;
    private const string Model = BookImportJobRunner.DefaultDetectModel;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly ILabelingService _labeling = Substitute.For<ILabelingService>();
    private readonly ILabelingBankService _bank = Substitute.For<ILabelingBankService>();
    private readonly IActivityStructureService _activity = Substitute.For<IActivityStructureService>();
    /// <summary>#1134: yapı çıkarımına giden bloklar (sayfa görseli, tip) ve tipi → sonuç (yoksa geçerli taslak).</summary>
    private readonly List<ActivityStructureBlockDto> _extracted = new();
    private readonly Dictionary<string, Func<ActivityStructureBlockDto, ActivityStructureResultDto>> _extraction = new();
    private readonly ServiceProvider _services;

    /// <summary>Dosya sayfası → tespit edilen sorular (her biri şık sayısıyla); listede olmayan sayfa tespitte hata verir.</summary>
    private readonly Dictionary<int, List<PredictionDto>> _detections = new();

    private readonly List<LabelingImportPageRequest> _saved = new();
    private readonly HashSet<string> _duplicateItems = new();
    /// <summary>#1025: bir kez kaydı patlayan sayfalar (çalışma ortasında çökme).</summary>
    private readonly HashSet<int> _crashOnSave = new();
    /// <summary>#1025: kaydedilen taslaklar (sayfa, soru no) → id; <c>ResumeDrafts</c> isteği bunları yeniden kullanır.</summary>
    private readonly Dictionary<(int Page, string Label), int> _drafts = new();
    /// <summary>#1025: tespit bu sayfaya gelince çalıştırılır (ör. işi iptal et).</summary>
    private readonly Dictionary<int, Func<Task>> _onDetect = new();
    /// <summary>#1320: kayıttan hemen sonra iptal edilen istek (düzeltme penceresi kapandı, bağlantı koptu).</summary>
    private CancellationTokenSource? _cancelAfterSave;
    private int _bookId;
    private int _gradeId;

    public BookQuestionImportTests()
    {
        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(ci.Arg<string>()))));
        _labeling.GetAiModels().Returns([new LabelingAiModelDto(Model, "Haiku", 1, 5, true)]);
        _labeling.DetectForImportAsync(Arg.Any<string>(), Model, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var url = Encoding.UTF8.GetString(Convert.FromBase64String(ci.ArgAt<string>(0).Split(',')[1]));
            var page = int.Parse(url[(url.LastIndexOf('/') + 1)..]);
            if (_onDetect.Remove(page, out var hook))
                hook().GetAwaiter().GetResult();
            if (!_detections.TryGetValue(page, out var predictions))
                throw new LabelingAiException("Claude response could not be used.");
            var response = new LabelingAiDetectResponseDto("live", new string('a', 64), new LabelingResultDto("Ai", Model,
                new PredictionListDto { Predictions = predictions }, null, null, 10, 10, 0.01m, 0, null, DateTime.UtcNow,
                DateTime.UtcNow, null));
            return Task.FromResult(new LabelingImportDetection(response, 0.01m, new LabelingAiUsage(1000, 100)));
        });
        _bank.SaveImportedPageAsync(Arg.Any<LabelingImportPageRequest>(), OwnerId, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var request = ci.Arg<LabelingImportPageRequest>();
            _saved.Add(request);
            if (_crashOnSave.Remove(request.FilePage))
                throw new InvalidOperationException("connection reset");
            var duplicates = request.Questions.Select((q, i) => (q, i)).Where(x => _duplicateItems.Contains(x.q.ItemLabel!))
                .Select(x => x.i).ToList();
            var resumed = request.Questions.Select((q, i) => (q, i))
                .Where(x => request.ResumeDrafts && !duplicates.Contains(x.i) && _drafts.ContainsKey((request.FilePage, x.q.ItemLabel!)))
                .ToDictionary(x => x.i, x => _drafts[(request.FilePage, x.q.ItemLabel!)]);
            // Bankaya kayıt gibi: kaydedilen her soru taslak bir satır (şıklar, anahtardaki doğru şık, kitap kaynağı).
            using var ctx = _db.NewContext();
            ctx.SetCurrentUser(OwnerId);
            var created = request.Questions.Select((q, i) => (q, i)).Where(x => !duplicates.Contains(x.i) && !resumed.ContainsKey(x.i)).Select(x =>
            {
                var q = x.q;
                // #1134: etkinlik taslağı (şıksız, tipi ve planıyla).
                var activity = request.Activities?.GetValueOrDefault(x.i);
                var question = new Question
                {
                    Text = q.ItemLabel!, IsDraft = true, InteractionType = activity?.InteractionType ?? "mcq",
                    InteractionPlan = activity?.PlanJson,
                };
                question.Answers = Enumerable.Range(0, q.Options.Count)
                    .Select(o => new Answer { Text = ((char)('A' + o)).ToString(), Order = o }).ToList();
                ctx.Questions.Add(question);
                ctx.QuestionSources.Add(new QuestionSource
                {
                    Question = question, BookId = request.BookId, FilePage = request.FilePage, ItemLabel = q.ItemLabel,
                    CropSha256 = Guid.NewGuid().ToString("N"), BoxX = q.X, BoxY = q.Y, BoxWidth = q.Width, BoxHeight = q.Height,
                });
                return (question, q.CorrectOptionIndex);
            }).ToList();
            ctx.SaveChanges();
            foreach (var (question, correct) in created.Where(c => c.CorrectOptionIndex >= 0))
                question.CorrectAnswerId = question.Answers.ElementAt(correct).Id;
            ctx.SaveChanges();
            foreach (var (question, _) in created)
                _drafts[(request.FilePage, question.Text!)] = question.Id;
            var ids = new List<int>();
            var next = 0;
            for (var i = 0; i < request.Questions.Count; i++)
            {
                if (duplicates.Contains(i))
                    continue;
                ids.Add(resumed.TryGetValue(i, out var id) ? id : created[next++].question.Id);
            }
            _cancelAfterSave?.Cancel();
            return Task.FromResult(new LabelingImportPageResult(created.Count, duplicates, ids, resumed.Count));
        });
        _activity.ExtractForImportAsync(Arg.Any<string>(), Model, Arg.Any<IReadOnlyList<ActivityStructureBlockDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var blocks = ci.ArgAt<IReadOnlyList<ActivityStructureBlockDto>>(2);
                _extracted.AddRange(blocks);
                var results = blocks.Select(b => _extraction.TryGetValue(b.InteractionType!, out var make) ? make(b) : Plan(b)).ToList();
                return Task.FromResult(new ActivityImportExtraction(results, 0.02m * blocks.Count, new LabelingAiUsage(2000, 300), false));
            });
        _services = new ServiceCollection().AddScoped(_ => _bank).BuildServiceProvider();
    }

    private static ActivityStructureResultDto Plan(ActivityStructureBlockDto block, bool valid = true, string status = "ok") =>
        new(block.Key!, block.InteractionType!, status, status == "ok" ? "live" : null,
            status == "ok" ? System.Text.Json.JsonDocument.Parse($"{{\"type\":\"{block.InteractionType}\"}}").RootElement : null,
            null, valid, [], 0, 2000, 300, 0.02m, status == "ok" ? null : "labeling.activity.aiFailed");

    private static PredictionDto Activity(int x, int y, string? interactionType, string? component = "B") => new()
    {
        X = x, Y = y, Width = 900, Height = 400, Kind = LabelingBlocks.Activity, Component = component, InteractionType = interactionType,
    };

    [Fact]
    public async Task Activity_blocks_are_extracted_and_saved_as_answerless_drafts_in_reading_order_issue1134()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        _detections[1] =
        [
            Question(100, 100, 4),
            Activity(100, 800, "slotFill"),
            Question(100, 1500, 4),
            Activity(1300, 100, "match", "C"),
        ];

        await RunAsync(id);

        _extracted.Select(b => (b.InteractionType, b.X, b.Y)).ShouldBe([("slotFill", 100d, 800d), ("match", 1300d, 100d)]);
        var request = _saved.Single();
        request.Questions.Select(q => q.ItemLabel).ShouldBe(["1", "E1", "2", "E2"]);
        request.Activities!.Keys.Order().ShouldBe([1, 3]);
        request.Activities[1].InteractionType.ShouldBe("slotFill");
        request.Activities[1].PlanJson.ShouldBe("{\"type\":\"slotFill\"}");
        request.Questions[1].Options.ShouldBeEmpty();
        request.Questions[1].ActivityLabel.ShouldBe(request.Questions[0].ActivityLabel);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        test.QuestionsFound.ShouldBe(4);
        test.QuestionsSaved.ShouldBe(4);
        await using var check = _db.NewContext();
        // Tespit 0,01 + iki etkinlik çıkarımı 0,04; çıkarım da testin maliyet satırlarında.
        (await check.BookImportJobs.SingleAsync()).AiCostUsd.ShouldBe(0.05m);
        (await check.BookImportCosts.Where(c => c.TestId == test.Id).SumAsync(c => c.CostUsd)).ShouldBe(0.05m);

        // Etkinlikler cevapsız: onayı engellemez, önizlemede tipiyle görünür.
        var service = Service(check);
        var questions = await service.GetReviewQuestionsAsync(Owner, id, test.Id, default);
        questions!.Select(q => (q.Number, q.InteractionType)).ShouldBe([("1", null), ("E1", "slotFill"), ("2", null), ("E2", "match")]);
        (await service.GetAsync(Owner, id, default))!.Tests.Single().Answerless.ShouldBe(0);
        (await service.ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);
    }

    [Fact]
    public async Task Unsupported_and_invalid_activity_blocks_are_flagged_on_their_page_not_saved_issue1134()
    {
        var id = await SeedAsync([new(1, 1, 2, "A", [1, 1])]);
        _detections[1] = [Question(100, 100, 4), Activity(100, 800, null, null), Activity(100, 1500, "openText", "H")];
        _detections[2] = [Activity(100, 100, "slotFill"), Activity(100, 800, "match", "C"), Activity(100, 1500, "matrixChoice", "A")];
        _extraction["slotFill"] = b => Plan(b, valid: false);
        _extraction["match"] = b => Plan(b, status: "error");

        await RunAsync(id);

        // Tipi belirlenemeyen / çıkarımı desteklenmeyen blok Claude'a gitmez.
        _extracted.Select(b => b.InteractionType).ShouldBe(["slotFill", "match", "matrixChoice"]);
        _saved.Select(r => (r.FilePage, Labels: string.Join(",", r.Questions.Select(q => q.ItemLabel))))
            .ShouldBe([(1, "1"), (2, "E1")]);
        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        Flags(test).ShouldBe([F("activityUnsupported", [1]), F("activityInvalid", [2])]);
        test.QuestionsFound.ShouldBe(6);
        test.QuestionsSaved.ShouldBe(2);
    }

    [Fact]
    public async Task An_activity_section_becomes_a_draft_activity_sheet_and_page_fix_leaves_activity_drafts_issue1134()
    {
        var id = await SeedAsync([new(1, 1, 1, null, [1])]);
        await using (var seed = _db.NewContext())
        {
            var row = await seed.BookImportJobTests.AsTracking().SingleAsync();
            row.FlagsJson = "[\"activity\"]";
            row.Title = null;
            await seed.SaveChangesAsync();
        }
        // Sayfadaki basılı en büyük no (5) etkinlik numarasıdır: etkinlik bölümünde "eksik soru" uyarısı çıkmaz.
        await using (var seed = _db.NewContext())
            await seed.BookImportPages.ExecuteUpdateAsync(p => p.SetProperty(x => x.MaxQuestionNumber, 5));
        _detections[1] = [Question(100, 100, 4), Activity(100, 800, "slotFill")];

        await RunAsync(id);

        await using var check = _db.NewContext();
        var test = await check.BookImportJobTests.SingleAsync();
        Flags(test).ShouldBe([F("noAnswer", [1], [1])]);
        var sheet = await check.Worksheets.WithDrafts().SingleAsync(w => w.Id == test.WorksheetId);
        sheet.Kind.ShouldBe(WorksheetKind.Activity);
        sheet.Name.ShouldEndWith("1-E01 · Etkinlikler");

        // İşaretlemede düzeltme yalnız çoktan seçmeli taslağı değiştirir; etkinlik taslağı ve sırası kalır.
        (await Fix(check).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0, 2)], default)).Error.ShouldBe(BookImportPageFixError.None);
        await using var after = _db.NewContext();
        var links = await after.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key])
            .Where(wq => wq.TestId == test.WorksheetId && !wq.IsDeleted).OrderBy(wq => wq.Order)
            .Select(wq => wq.Question.InteractionType).ToListAsync();
        links.ShouldBe(["mcq", "slotFill"]);
    }

    /// <summary>
    /// Issue #1293: tablolu soru etkinlik sanıldı (çoktan seçmeli 3, anahtar 4). Etkinlik okuma sırasında 2 numarasını tutar,
    /// sonraki sorular basılı numaraları ve anahtar cevaplarıyla kalır.
    /// </summary>
    [Fact]
    public async Task An_activity_in_a_test_short_of_its_key_takes_a_question_number_issue1293()
    {
        var id = await SeedAsync([new(1, 1, 1, "ABCD", [4])]);
        _detections[1] =
        [
            Question(100, 100, 4), Activity(100, 800, "matrixChoice", "A"), Question(100, 1500, 4), Question(100, 2200, 4),
        ];

        await RunAsync(id);

        var request = _saved.Single();
        request.Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 0), ("2", -1), ("3", 2), ("4", 3)]);
        request.Activities!.Keys.ShouldBe([1]);
        var test = await SingleTestAsync();
        Flags(test).ShouldBe([F("countMismatch", [1], null, 3, 4)]);
    }

    /// <summary>
    /// Issue #1293: tuvalde etkinlik soruya çevrilince (etkinlik kutusu listede yok) etkinlik taslağı silinir, yerine çizilen soru
    /// etkinliğin numarasını ve anahtardaki cevabını alır; tuvalde kalan etkinlik yerinde kalır ve numarasını korur.
    /// </summary>
    [Fact]
    public async Task Page_fix_replaces_an_activity_turned_into_a_question_and_keeps_one_left_on_the_canvas_issue1293()
    {
        var id = await SeedAsync([new(1, 1, 1, "ABCD", [4])]);
        _detections[1] =
        [
            Question(100, 100, 4), Activity(100, 800, "matrixChoice", "A"), Question(100, 1500, 4), Question(100, 2200, 4),
        ];
        await RunAsync(id);
        var test = await SingleTestAsync();
        var activityBox = new LayoutBox(100, 800, 900, 400);

        // Etkinlik tuvalde kaldı: taslağı ve numarası (2) yerinde, sorular 1, 3, 4.
        await using (var ctx = _db.NewContext())
        {
            var kept = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(2), Box(3)], default, [activityBox]);
            kept.Page!.Removed.ShouldBe(3);
        }
        _saved.Last().Questions.Select(q => q.ItemLabel).ShouldBe(["1", "3", "4"]);
        (await LiveAsync(test.WorksheetId)).ShouldBe([("1", "mcq"), ("2", "matrixChoice"), ("3", "mcq"), ("4", "mcq")]);

        // Etkinlik soruya çevrildi: taslağı silinir, onun yerindeki soru 2 numarası ve B cevabıyla girer.
        await using (var ctx = _db.NewContext())
        {
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1), Box(2), Box(3)], default, []);
            result.Error.ShouldBe(BookImportPageFixError.None);
            result.Page!.Removed.ShouldBe(4);
        }
        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 0), ("2", 1), ("3", 2), ("4", 3)]);
        (await LiveAsync(test.WorksheetId)).ShouldBe([("1", "mcq"), ("2", "mcq"), ("3", "mcq"), ("4", "mcq")]);
        test = await SingleTestAsync();
        Flags(test).ShouldBeEmpty();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        test.QuestionsSaved.ShouldBe(4);
    }

    /// <summary>Issue #1293: elle doğrulanan sonuçta yalnız tuvalde kalan etkinlikler; yeni etkinlik kutusu tipsiz eklenir.</summary>
    [Fact]
    public void Manual_result_keeps_only_the_activities_left_on_the_canvas_issue1293()
    {
        PredictionDto[] others =
        [
            Activity(100, 800, "matrixChoice", "A"),
            Activity(100, 1500, "slotFill"),
            new() { X = 100, Y = 2200, Width = 900, Height = 300, Kind = LabelingBlocks.InfoBox },
        ];

        var result = BookImportPageFixService.KeepActivities(others, [new LayoutBox(110, 1510, 880, 380), new LayoutBox(1300, 100, 600, 400)]);

        result.Select(p => (p.Kind, p.Y, p.InteractionType)).ShouldBe(
        [
            (LabelingBlocks.Activity, 1500, "slotFill"), (LabelingBlocks.InfoBox, 2200, null), (LabelingBlocks.Activity, 100, null),
        ]);
        BookImportPageFixService.KeepActivities(others, []).Select(p => p.Kind).ShouldBe([LabelingBlocks.InfoBox]);
    }

    private async Task<List<(string? Text, string? Type)>> LiveAsync(int? worksheetId)
    {
        await using var check = _db.NewContext();
        return (await check.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key])
                .Where(wq => wq.TestId == worksheetId && !wq.IsDeleted && !wq.Question.IsDeleted).OrderBy(wq => wq.Order)
                .Select(wq => new { wq.Question.Text, wq.Question.InteractionType }).ToListAsync())
            .Select(x => ((string?)x.Text, (string?)x.InteractionType)).ToList();
    }

    [Fact]
    public async Task Quick_add_ignores_activity_blocks_issue1134()
    {
        var id = await SeedQuickAddAsync("A");
        _detections[1] = [Question(100, 100, 4), Activity(100, 800, "slotFill")];

        await RunAsync(id);

        _extracted.ShouldBeEmpty();
        _saved.Single().Questions.Count.ShouldBe(1);
        (await SingleTestAsync()).ReviewFlagsJson.ShouldBeNull();
    }

    [Fact]
    public async Task Tests_are_imported_in_order_with_page_anchored_numbers_and_answers_from_the_key()
    {
        var id = await SeedAsync(
            [new(1, 1, 2, "BCAD", [2, 4]), new(2, 3, 3, "DA", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        Detect(3, 4, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        job.Stage.ShouldBe(BookImportStage.Review);
        job.StageTotal.ShouldBe(2);
        job.StageProcessed.ShouldBe(2);
        job.AiCostUsd.ShouldBe(0.03m);

        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.ShouldAllBe(t => t.Status == BookImportTestStatus.Done && t.ReviewFlagsJson == null);
        tests[0].QuestionsFound.ShouldBe(4);
        tests[0].QuestionsSaved.ShouldBe(4);

        _saved.Select(s => s.FilePage).ShouldBe([1, 2, 3]);
        _saved.ShouldAllBe(s => s.BookId == _bookId && s.GradeId == _gradeId && s.Sharing == QuestionBankSharing.Private);
        var first = _saved.Where(s => s.FilePage <= 2).SelectMany(s => s.Questions).ToList();
        first.Select(q => q.ItemLabel).ShouldBe(["1", "2", "3", "4"]);
        first.Select(q => q.CorrectOptionIndex).ShouldBe([1, 2, 0, 3]);
        first.ShouldAllBe(q => q.ActivityLabel == "1-01 · Test 1" && q.Options.Count == 4);
        _saved[2].Questions.Select(q => q.CorrectOptionIndex).ShouldBe([3, 0]);
    }

    [Fact]
    public async Task A_printed_answer_mark_inside_the_question_box_is_its_answer_and_a_different_key_is_flagged_issue1235()
    {
        var id = await SeedAsync([new(1, 1, 1, "BC", [2]), new(2, 2, 2, null, [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await using (var ctx = _db.NewContext())
        {
            // Soru kutuları (100, 100) ve (100, 800), 900×400: 1. sorunun işareti D (anahtar B), 2. sorununki C (anahtarla aynı);
            // 2. testin anahtarı yok, yalnız 1. sorusunda işaret var; kutu dışındaki işaret sayılmaz.
            var pages = await ctx.BookImportPages.AsTracking().ToDictionaryAsync(p => p.FilePage);
            pages[1].MarksJson = """[{"letter":"D","x":560,"y":300,"w":30,"h":30},{"letter":"C","x":120,"y":1000,"w":30,"h":30}]""";
            pages[2].MarksJson = """[{"letter":"A","x":120,"y":300,"w":30,"h":30},{"letter":"B","x":1500,"y":300,"w":30,"h":30}]""";
            await ctx.SaveChangesAsync();
        }

        await RunAsync(id);

        _saved[0].Questions.Select(q => q.CorrectOptionIndex).ShouldBe([3, 2]);
        _saved[1].Questions.Select(q => q.CorrectOptionIndex).ShouldBe([0, -1]);
        await using var check = _db.NewContext();
        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        var first = BookImportReviewFlag.Parse(tests[0].ReviewFlagsJson).ShouldHaveSingleItem();
        (first.Code, first.Numbers!.Single()).ShouldBe((BookImportReviewFlag.MarkMismatch, 1));
        var second = BookImportReviewFlag.Parse(tests[1].ReviewFlagsJson).ShouldHaveSingleItem();
        (second.Code, second.Numbers!.Single()).ShouldBe((BookImportReviewFlag.NoAnswer, 2));
    }

    [Fact]
    public async Task A_missed_question_is_flagged_on_its_page_and_does_not_shift_the_next_page()
    {
        var id = await SeedAsync([new(1, 1, 2, "BCAD", [2, 4])]);
        Detect(1, 4);
        Detect(2, 4, 4);

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        test.QuestionsFound.ShouldBe(3);
        Flags(test).ShouldBe([
            F(BookImportReviewFlag.MissingNumbers, [1], [2]),
            F(BookImportReviewFlag.CountMismatch, [1, 2], null, 3, 4),
        ], ignoreOrder: true);
        _saved[1].Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("3", 0), ("4", 3)]);
    }

    [Fact]
    public async Task Option_count_and_missing_answers_are_flagged_and_one_option_questions_are_not_saved()
    {
        var id = await SeedAsync([new(1, 1, 1, null, [3])]);
        Detect(1, 4, 3, 1);

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        Flags(test).ShouldBe([
            F(BookImportReviewFlag.NoAnswer, [1], [1, 2]),
            F(BookImportReviewFlag.OptionCount, [1], [2, 3]),
            F(BookImportReviewFlag.NotSaved, [1], [3]),
        ], ignoreOrder: true);
        _saved.ShouldHaveSingleItem().Questions.Select(q => q.CorrectOptionIndex).ShouldBe([-1, -1]);
        test.QuestionsSaved.ShouldBe(2);
    }

    [Fact]
    public async Task A_failing_page_and_a_failing_test_do_not_stop_the_other_tests()
    {
        var id = await SeedAsync(
            [new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [1]), new(3, 4, 4, "D", [1])]);
        Detect(2, 4); // sayfa 1 ve 3 tespitte hata verir
        Detect(4, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.Select(t => t.Status).ShouldBe([BookImportTestStatus.NeedsReview, BookImportTestStatus.Failed, BookImportTestStatus.Done]);
        Flags(tests[0]).ShouldContain(F(BookImportReviewFlag.DetectionFailed, [1]));
        Flags(tests[0]).ShouldContain(F(BookImportReviewFlag.MissingNumbers, [1], [1]));
        _saved.First().Questions.ShouldHaveSingleItem().ItemLabel.ShouldBe("2"); // sayfa 2 hâlâ 2'den başlar
        tests[1].Error.ShouldNotBeNull();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
    }

    [Fact]
    public async Task Crop_duplicates_are_counted_not_failed()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        Detect(1, 4, 4);
        _duplicateItems.Add("2");

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        test.QuestionsSaved.ShouldBe(1);
        test.DuplicatesSkipped.ShouldBe(1);
    }

    [Fact]
    public async Task Finished_tests_are_skipped_when_the_job_resumes()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1])]);
        await using (var ctx = _db.NewContext())
        {
            var done = await ctx.BookImportJobTests.AsTracking().SingleAsync(t => t.Order == 1);
            done.Status = BookImportTestStatus.Done;
            await ctx.SaveChangesAsync();
        }
        Detect(2, 4);

        await RunAsync(id);

        _saved.ShouldHaveSingleItem().FilePage.ShouldBe(2);
        await _labeling.Received(1).DetectForImportAsync(Arg.Any<string>(), Model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_paid_call_is_recorded_per_test_and_charged_to_the_import_budget_and_only_admin_sees_the_breakdown()
    {
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        var store = new InMemoryFixedWindowCounterStore();

        await RunAsync(id, store);

        var tests = await TestsAsync();
        await using (var check = _db.NewContext())
        {
            var costs = await check.BookImportCosts.ToListAsync();
            costs.Count.ShouldBe(3);
            costs.ShouldAllBe(c => c.Stage == BookImportStage.Questions && c.ModelId == Model && c.InputTokens == 1000 && c.OutputTokens == 100);
            costs.Count(c => c.TestId == tests[0].Id).ShouldBe(2);
            (await check.BookImportJobs.SingleAsync()).AiCostUsd.ShouldBe(0.03m);
        }
        (await store.PeekAsync(LabelingAiQuota.ImportBudgetKey)).Count.ShouldBe(30_000);
        (await store.PeekAsync(LabelingAiQuota.TotalBudgetKey)).Count.ShouldBe(30_000);

        await using var ctx = _db.NewContext();
        var admin = (await Service(ctx).GetAsync(Admin, id, CancellationToken.None))!;
        admin.AiCostUsd.ShouldBe(0.03m);
        var row = admin.Costs.ShouldHaveSingleItem();
        (row.Stage, row.ModelId, row.Calls, row.InputTokens, row.CostUsd).ShouldBe(("Questions", Model, 3, 3000L, 0.03m));
        admin.Tests.Select(t => t.CostUsd).ShouldBe([0.02m, 0.01m]);

        var owner = (await Service(ctx).GetAsync(Owner, id, CancellationToken.None))!;
        owner.AiCostUsd.ShouldBeNull();
        owner.Costs.ShouldBeEmpty();
        owner.Tests.ShouldAllBe(t => t.CostUsd == null);
    }

    [Fact]
    public async Task A_claude_rate_limit_pauses_the_job_instead_of_failing_the_page_and_it_resumes()
    {
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        // SDK'nın kendi yeniden denemelerinden sonra da 429: 3. sayfanın tespiti.
        _onDetect[3] = () => throw new LabelingAiException("Claude request failed.",
            new Anthropic.Exceptions.AnthropicRateLimitException(new HttpRequestException("429"))
            {
                StatusCode = System.Net.HttpStatusCode.TooManyRequests, ResponseBody = "",
            });
        var jobs = Substitute.For<IBackgroundJobClient>();

        await RunAsync(id, new InMemoryFixedWindowCounterStore(), jobs: jobs);

        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.AwaitingBudget);
            job.ResumeAt.ShouldNotBeNull();
        }
        jobs.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.ScheduledState>());
        // Test başarısız sayılmaz, sayfa "tespit başarısız" işaretlenmez: devamda baştan alınır.
        (await TestsAsync()).Select(t => t.Status).ShouldBe([BookImportTestStatus.Done, BookImportTestStatus.Running]);

        await RunAsync(id, new InMemoryFixedWindowCounterStore());

        await using var after = _db.NewContext();
        (await after.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        _saved.Select(s => s.FilePage).ShouldBe([1, 2, 3]);
        (await TestsAsync()).ShouldAllBe(t => t.Status == BookImportTestStatus.Done && t.ReviewFlagsJson == null);
    }

    [Fact]
    public async Task A_full_import_budget_pauses_the_job_and_the_unfinished_test_is_redone_when_it_resumes()
    {
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        var jobs = Substitute.For<IBackgroundJobClient>();

        // İki çağrıdan (0.02 $) sonra aktarım bütçesi (0.015 $) dolu: ikinci testten önce iş durur.
        await RunAsync(id, new InMemoryFixedWindowCounterStore(), new LabelingAiOptions { DailyBudgetUsdImport = 0.015m }, jobs);

        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.AwaitingBudget);
            job.ResumeAt.ShouldNotBeNull();
            job.AiCostUsd.ShouldBe(0.02m);
            (await check.BookImportCosts.CountAsync()).ShouldBe(2);
        }
        jobs.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.ScheduledState>());
        (await TestsAsync()).Select(t => t.Status).ShouldBe([BookImportTestStatus.Done, BookImportTestStatus.Running]);

        // Pencere yenilendi (yeni sayaç): iş kaldığı testten devam eder.
        await RunAsync(id, new InMemoryFixedWindowCounterStore());

        await using var after = _db.NewContext();
        var resumed = await after.BookImportJobs.SingleAsync();
        resumed.Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        resumed.ResumeAt.ShouldBeNull();
        resumed.AiCostUsd.ShouldBe(0.03m);
        _saved.Select(s => s.FilePage).ShouldBe([1, 2, 3]);
        (await TestsAsync()).ShouldAllBe(t => t.Status == BookImportTestStatus.Done);
    }

    [Fact]
    public async Task Exceeding_the_job_cost_ceiling_waits_until_an_admin_continues_with_a_raised_ceiling()
    {
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BookImport:MaxJobCostUsd"] = "0.015",
        }).Build();
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        var runnerJobs = Substitute.For<IBackgroundJobClient>();

        await RunAsync(id, jobs: runnerJobs);

        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.AwaitingCostApproval);
            job.AiCostUsd.ShouldBe(0.02m);
        }
        runnerJobs.ReceivedCalls().ShouldBeEmpty();

        var jobs = Substitute.For<IBackgroundJobClient>();
        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx, jobs).ContinueAsync(Owner, id, CancellationToken.None)).ShouldBe(ContinueBookImportResult.NotFound);
            (await Service(ctx, jobs).ContinueAsync(Admin, id, CancellationToken.None)).ShouldBe(ContinueBookImportResult.Continued);
            (await Service(ctx, jobs).ContinueAsync(Admin, id, CancellationToken.None)).ShouldBe(ContinueBookImportResult.InvalidState);
        }
        jobs.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.EnqueuedState>());
        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.Queued);
            job.CostCeilingUsd.ShouldBe(0.035m);
        }

        await RunAsync(id);

        await using var after = _db.NewContext();
        (await after.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
    }

    [Fact]
    public async Task An_info_box_above_a_question_is_saved_as_its_passage_and_shared_with_the_next_question()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        _detections[1] =
        [
            new PredictionDto { Kind = LabelingBlocks.InfoBox, X = 100, Y = 100, Width = 900, Height = 300, SharedQuestions = 2 },
            Question(100, 500, 4),
            Question(100, 1200, 4),
        ];

        await RunAsync(id);

        var request = _saved.Single();
        request.Questions.Count.ShouldBe(2);
        var passage = request.Passages.ShouldNotBeNull().ShouldHaveSingleItem();
        passage.Box.Y.ShouldBe(100);
        passage.Box.Height.ShouldBe(300);
        passage.QuestionIndexes.ShouldBe([0, 1]);
    }

    [Fact]
    public async Task Only_multiple_choice_blocks_are_imported_and_a_stem_split_from_its_options_is_merged()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        _detections[1] =
        [
            new PredictionDto { X = 100, Y = 100, Width = 900, Height = 300 }, // kök, şıksız
            Question(110, 410, 4),                                              // hemen altında şıklar
            new PredictionDto { Kind = LabelingBlocks.InfoBox, X = 100, Y = 900, Width = 900, Height = 200 },
            Question(1300, 100, 4),
        ];

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.QuestionsFound.ShouldBe(2);
        test.Status.ShouldBe(BookImportTestStatus.Done);
        var merged = _saved.Single().Questions[0];
        merged.Y.ShouldBe(100);
    }

    [Fact]
    public async Task Missing_detection_model_fails_the_job_with_a_readable_error()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1])]);
        _labeling.GetAiModels().Returns([]);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Failed);
        job.Error.ShouldBe("Claude is not configured (Anthropic:ApiKey is empty).");
    }

    [Fact]
    public async Task Each_test_gets_a_hidden_draft_worksheet_with_its_questions_in_order()
    {
        var id = await SeedAsync([new(1, 1, 2, "ABC", [2, 3]), new(2, 3, 3, "D", [1])]);
        Detect(1, 4, 4);
        Detect(2, 4);
        Detect(3, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.ShouldAllBe(t => t.WorksheetId != null);
        var first = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
            .SingleAsync(w => w.Id == tests[0].WorksheetId);
        first.IsDraft.ShouldBeTrue();
        first.CreateUserId.ShouldBe(OwnerId);
        first.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        first.Name.ShouldBe("Puan BTS 5 Matematik – 1-01 · Test 1");
        first.WorksheetQuestions.OrderBy(wq => wq.Order).Select(wq => wq.Question.Text).ShouldBe(["1", "2", "3"]);
        (await check.Worksheets.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Test_worksheets_take_the_sharing_picked_at_confirmation()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [2])], WorksheetTeacherSharing.BookAudience);
        Detect(1, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.WithDrafts().SingleAsync();
        (worksheet.TeacherSharing, worksheet.BookTestId).ShouldBe((WorksheetTeacherSharing.BookAudience, (await SingleTestAsync()).BookTestId));
    }

    [Fact]
    public async Task Answerless_test_is_blocked_until_its_answers_are_picked_then_approval_activates_it()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            var detail = await service.GetAsync(Owner, id, default);
            detail!.Tests.Single().Answerless.ShouldBe(1);
            (await service.ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Answerless);

            var questions = await service.GetReviewQuestionsAsync(Owner, id, test.Id, default);
            questions!.Select(q => (q.Number, q.FilePage, q.OptionCount, q.AnswerIndex)).ShouldBe([("1", 1, 4, 0), ("2", 1, 4, null)]);
            (await service.SetAnswerAsync(Owner, id, test.Id, questions[1].Id, 4, default)).ShouldBe(BookImportReviewResult.InvalidOption);
            (await service.SetAnswerAsync(Owner, id, test.Id, questions[1].Id, 2, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        // #1269: cevap seçilince "cevapsız" işareti kalkar; anahtardan fazla soru işareti kaldıkça test onaylanmaz.
        await using (var ctx = _db.NewContext())
        {
            var flagged = await ctx.BookImportJobTests.SingleAsync();
            BookImportReviewFlag.Parse(flagged.ReviewFlagsJson).Select(f => f.Code).ShouldBe([BookImportReviewFlag.CountMismatch]);
            flagged.Status.ShouldBe(BookImportTestStatus.NeedsReview);
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Flagged);
            // Fazla soru çıkınca sayı anahtarla tutar: işaret kalmaz, test hazır ve onaylanır.
            var questions = await service.GetReviewQuestionsAsync(Owner, id, test.Id, default);
            (await service.RemoveQuestionAsync(Owner, id, test.Id, questions![1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using (var ctx = _db.NewContext())
        {
            var cleared = await ctx.BookImportJobTests.SingleAsync();
            BookImportReviewFlag.Parse(cleared.ReviewFlagsJson).ShouldBeEmpty();
            cleared.Status.ShouldBe(BookImportTestStatus.Done);
            cleared.FixedAt.ShouldNotBeNull();
            (await Service(ctx).ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(1);
        var worksheet = await check.Worksheets.SingleAsync();
        worksheet.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        (await check.BookImportJobTests.SingleAsync()).Status.ShouldBe(BookImportTestStatus.Approved);
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Completed);
        job.Stage.ShouldBe(BookImportStage.Approval);
    }

    // #1303: anahtar fazla okunmuş (3 cevap, 2 soru), sorular ve cevapları doğru: öğretmen sayıyı doğrular, test onaylanır.
    [Fact]
    public async Task Confirming_the_question_count_clears_the_count_mismatch_and_the_test_can_be_approved()
    {
        var id = await SeedAsync([new(1, 1, 1, "ABC", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            var flagged = await ctx.BookImportJobTests.SingleAsync();
            BookImportReviewFlag.Parse(flagged.ReviewFlagsJson).Select(f => (f.Code, f.Found, f.Expected))
                .ShouldBe([(BookImportReviewFlag.CountMismatch, (int?)2, (int?)3)]);
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Flagged);
            (await service.AcceptQuestionCountAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using (var ctx = _db.NewContext())
        {
            var cleared = await ctx.BookImportJobTests.SingleAsync();
            BookImportReviewFlag.Parse(cleared.ReviewFlagsJson).ShouldBeEmpty();
            (cleared.Status, cleared.ExpectedQuestions, cleared.AnswerKey).ShouldBe((BookImportTestStatus.Done, (int?)2, "ABC"));
            cleared.FixedAt.ShouldNotBeNull();
            var service = Service(ctx);
            // İşaret kalmadı: ikinci doğrulama geçersiz.
            (await service.AcceptQuestionCountAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.InvalidState);
        }

        // Sonraki sayfa düzeltmesi doğrulanan sayıyla karşılaştırır: işaret geri gelmez, onay açılır.
        await using (var ctx = _db.NewContext())
            (await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0, 0), Box(1, 1)], default)).Error.ShouldBe(BookImportPageFixError.None);
        await using (var ctx = _db.NewContext())
        {
            BookImportReviewFlag.Parse((await ctx.BookImportJobTests.SingleAsync()).ReviewFlagsJson).ShouldBeEmpty();
            (await Service(ctx).ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }
    }

    [Fact]
    public async Task Approve_all_skips_blocked_tests_and_says_why()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "C", [2]), new(3, 3, 3, "D", [1])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        // Sayfa 3 tespit edilemez → test Failed.
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            var result = await Service(ctx).ApproveAllAsync(Owner, id, default);
            result!.Approved.ShouldBe([tests[0].Id]);
            result.Skipped.ShouldBe([new(tests[1].Id, "answerless"), new(tests[2].Id, "failed")]);
        }

        await using var check = _db.NewContext();
        (await check.Worksheets.Select(w => w.Id).ToListAsync()).ShouldBe([tests[0].WorksheetId!.Value]);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
    }

    [Fact]
    public async Task Approve_all_skips_flagged_tests_which_are_approved_only_after_a_labeling_fix_clears_their_flags()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "CD", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();
        await using (var ctx = _db.NewContext())
            await ctx.BookImportJobTests.Where(t => t.Id == tests[1].Id)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, BookImportTestStatus.NeedsReview));

        await using (var ctx = _db.NewContext())
        {
            var result = await Service(ctx).ApproveAllAsync(Owner, id, default);
            result!.Skipped.ShouldBe([new(tests[1].Id, "flagged")]);
            result.Approved.ShouldBe([tests[0].Id]);
        }

        await using (var ctx = _db.NewContext())
        {
            (await ctx.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
            // #1269: işaretli test tek tek de onaylanmaz; işaretlemede düzeltilip işaretleri kalkınca onaylanır (#1270: işaretli).
            (await Service(ctx).ApproveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Flagged);
            (await Fix(ctx).ReplacePageAsync(Owner, id, tests[1].Id, 2, [Box(0), Box(1)], default)).Error.ShouldBe(BookImportPageFixError.None);
        }

        await using (var ctx = _db.NewContext())
        {
            var fixedTest = await ctx.BookImportJobTests.SingleAsync(t => t.Id == tests[1].Id);
            fixedTest.Status.ShouldBe(BookImportTestStatus.Done);
            fixedTest.FixedAt.ShouldNotBeNull();
            (await Service(ctx).GetAsync(Owner, id, default))!.Tests.Select(t => t.FixedAt is not null).ShouldBe([false, true]);
            (await Service(ctx).ApproveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Completed);
    }

    [Fact]
    public async Task Deferred_job_leaves_the_list_for_later_until_its_last_test_is_reviewed()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "CD", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();
        var stranger = BookImportOwner.Create(OwnerId + 1, "QuestionTrainer");

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.SetDeferredAsync(stranger, id, true, default)).ShouldBe(BookImportReviewResult.NotFound);
            (await service.SetDeferredAsync(Owner, id, true, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ListAsync(Owner, 50, default)).ShouldBeEmpty();
            var counts = await service.CountsAsync(Owner, default);
            counts.Total.ShouldBe(0);
            counts.Deferred.ShouldBe(1);
            var later = (await service.ListDeferredAsync(Owner, default)).ShouldHaveSingleItem();
            later.Id.ShouldBe(id);
            later.Tests.Select(t => t.Id).ShouldBe([tests[1].Id]);
            (await service.ListDeferredAsync(stranger, default)).ShouldBeEmpty();

            // Geri alınınca listeye döner; yeniden bırakılır.
            (await service.SetDeferredAsync(Owner, id, false, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.ListAsync(Owner, 50, default)).ShouldHaveSingleItem().DeferredAt.ShouldBeNull();
            (await service.SetDeferredAsync(Owner, id, true, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.ApproveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ListDeferredAsync(Owner, default)).ShouldBeEmpty();
            (await service.ListAsync(Owner, 50, default)).ShouldHaveSingleItem().Status.ShouldBe(nameof(BookImportJobStatus.Completed));
            (await service.CountsAsync(Owner, default)).Deferred.ShouldBe(0);
            (await service.SetDeferredAsync(Owner, id, true, default)).ShouldBe(BookImportReviewResult.InvalidState);
        }
    }

    [Fact]
    public async Task Removing_a_test_deletes_only_its_draft_worksheet_and_questions()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "CD", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.RemoveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.InvalidState);
            (await service.RemoveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using var check = _db.NewContext();
        (await check.Questions.WithDrafts().CountAsync()).ShouldBe(2);
        (await check.Questions.IgnoreQueryFilters().CountAsync(q => q.IsDeleted && q.IsDraft)).ShouldBe(2);
        (await check.Worksheets.WithDrafts().Select(w => w.Id).ToListAsync()).ShouldBe([tests[0].WorksheetId!.Value]);
        (await check.BookImportJobTests.OrderBy(t => t.Order).Select(t => t.Status).ToListAsync())
            .ShouldBe([BookImportTestStatus.Approved, BookImportTestStatus.Removed]);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Completed);
    }

    // #1169: yanlış bulunan soru inceleme ekranından testten çıkarılır; son soru çıkarılamaz.
    [Fact]
    public async Task Removing_a_question_drops_it_from_the_draft_test_but_not_the_last_one()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [2]), new(2, 2, 2, "CD", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();
        int kept;

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            var questions = await service.GetReviewQuestionsAsync(Owner, id, tests[0].Id, default);
            kept = questions![0].Id;
            var other = await service.GetReviewQuestionsAsync(Owner, id, tests[1].Id, default);
            (await service.RemoveQuestionAsync(Owner, id, tests[0].Id, other![0].Id, default)).ShouldBe(BookImportReviewResult.NotFound);
            (await service.RemoveQuestionAsync(BookImportOwner.Create(OwnerId + 1, "QuestionTrainer"), id, tests[0].Id, questions[1].Id, default))
                .ShouldBe(BookImportReviewResult.NotFound);

            (await service.RemoveQuestionAsync(Owner, id, tests[0].Id, questions[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.GetReviewQuestionsAsync(Owner, id, tests[0].Id, default))!.Select(q => q.Id).ShouldBe([questions[0].Id]);
            (await service.RemoveQuestionAsync(Owner, id, tests[0].Id, questions[0].Id, default)).ShouldBe(BookImportReviewResult.LastQuestion);
            // Cevapsız soru çıkınca test onaylanabilir.
            (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.RemoveQuestionAsync(Owner, id, tests[0].Id, questions[0].Id, default)).ShouldBe(BookImportReviewResult.InvalidState);
        }

        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.Include(w => w.WorksheetQuestions).SingleAsync();
        worksheet.WorksheetQuestions.Select(wq => wq.QuestionId).ShouldBe([kept]);
        (await check.Questions.IgnoreQueryFilters().CountAsync(q => q.IsDeleted)).ShouldBe(1);
    }

    [Fact]
    public async Task Reimporting_a_test_replaces_its_drafts_and_leaves_approved_tests()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "CD", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.ReimportTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(RetryBookImportResult.InvalidState);
            (await service.ReimportTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(RetryBookImportResult.Queued);
        }
        // Gerçek banka silinmiş taslağı devam saymaz (!Question.IsDeleted); sahte bankanın taslak listesi de öyle olsun.
        _drafts.Clear();
        await RunAsync(id);

        await using var check = _db.NewContext();
        // Eski iki taslak silindi, yerine yenileri yazıldı (devam eski taslakları yeniden kullanmadı).
        (await check.Questions.IgnoreQueryFilters().CountAsync(q => q.IsDeleted && q.IsDraft)).ShouldBe(2);
        var reimported = await check.BookImportJobTests.SingleAsync(t => t.Id == tests[1].Id);
        (reimported.Status, reimported.QuestionsSaved).ShouldBe((BookImportTestStatus.Done, 2));
        (await check.Questions.WithDrafts().CountAsync()).ShouldBe(4);
        reimported.WorksheetId.ShouldNotBe(tests[1].WorksheetId);
        (await check.BookImportJobTests.SingleAsync(t => t.Id == tests[0].Id)).Status.ShouldBe(BookImportTestStatus.Approved);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
    }

    [Fact]
    public async Task Another_user_cannot_review_the_job()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();
        var stranger = BookImportOwner.Create(OwnerId + 1, "QuestionTrainer");

        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        (await service.GetReviewQuestionsAsync(stranger, id, test.Id, default)).ShouldBeNull();
        (await service.ApproveTestAsync(stranger, id, test.Id, default)).ShouldBe(BookImportReviewResult.NotFound);
        (await service.RemoveTestAsync(stranger, id, test.Id, default)).ShouldBe(BookImportReviewResult.NotFound);
        (await service.ApproveAllAsync(stranger, id, default)).ShouldBeNull();
        (await service.ApproveTestAsync(BookImportOwner.Create(OwnerId + 2, "Admin"), id, test.Id, default))
            .ShouldBe(BookImportReviewResult.Ok);
    }

    [Fact]
    public void Draft_questions_are_hidden_unless_explicitly_included()
    {
        using var ctx = _db.NewContext();
        ctx.Questions.AddRange(new Question { Text = "aktif" }, new Question { Text = "taslak", IsDraft = true });
        ctx.SaveChanges();

        using var check = _db.NewContext();
        check.Questions.Select(q => q.Text).ToList().ShouldBe(["aktif"]);
        check.Questions.WithDrafts().Select(q => q.Text).OrderBy(t => t).ToList().ShouldBe(["aktif", "taslak"]);
        // Taslağı açmak soft-delete filtresini kapatmaz.
        var deleted = check.Questions.WithDrafts().AsTracking().First(q => q.Text == "aktif");
        deleted.IsDeleted = true;
        check.SaveChanges();
        check.Questions.WithDrafts().Select(q => q.Text).ToList().ShouldBe(["taslak"]);
    }

    // ---------- yardımcılar ----------

    [Fact]
    public async Task Fixing_a_page_in_labeling_replaces_its_drafts_in_place_with_answers_from_the_key()
    {
        var id = await SeedAsync([new(1, 1, 2, "ABCD", [2, 4])]);
        Detect(1, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();
        Flags(test).ShouldBe([F("missingNumbers", [1], [2]), F("countMismatch", [1, 2], null, 3, 4)]);
        // Uyarılar ekranda sayfaya göre gruplansın diye sayfa → numara eşlemesi; sayı uyuşmazlığı testin bütünü.
        var parsed = BookImportReviewFlag.Parse((await SingleTestAsync()).ReviewFlagsJson);
        parsed[0].ByPage!.Select(p => $"{p.Key}:{string.Join(',', p.Value)}").ShouldBe(["1:2"]);
        parsed[1].ByPage.ShouldBeNull();

        await using (var ctx = _db.NewContext())
        {
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1)], default);
            result.Error.ShouldBe(BookImportPageFixError.None);
            result.Page!.Saved.ShouldBe(2);
            result.Page.Removed.ShouldBe(1);
            result.Page.Answerless.ShouldBe(0);
        }

        _saved.Last().ReplacingQuestionIds!.Count.ShouldBe(1);
        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 0), ("2", 1)]);
        test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        Flags(test).ShouldBeEmpty();
        test.QuestionsSaved.ShouldBe(4);
        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
            .SingleAsync(w => w.Id == test.WorksheetId);
        worksheet.WorksheetQuestions.OrderBy(wq => wq.Order).Select(wq => (wq.Order, wq.Question.Text))
            .ShouldBe([(1, "1"), (2, "2"), (3, "3"), (4, "4")]);
        (await check.Questions.IgnoreQueryFilters().CountAsync(q => q.IsDeleted && q.IsDraft && q.Text == "1")).ShouldBe(1);
    }

    /// <summary>
    /// Issue #1320: kayıttan sonra iptal edilen düzeltme isteği yeni taslakları yine bağlar (yetim taslak kalmaz); önceki bir
    /// kesik kayıttan kalmış, hiçbir teste bağlı olmayan taslak bir sonraki düzeltmede kopya sayılmaz, silinir.
    /// </summary>
    [Fact]
    public async Task A_page_fix_cancelled_after_saving_still_links_and_a_leftover_unlinked_draft_is_replaced_not_counted_issue1320()
    {
        var id = await SeedAsync([new(1, 1, 1, "ABCD", [4])]);
        Detect(1, 4, 4, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        using (var cts = new CancellationTokenSource())
        {
            _cancelAfterSave = cts;
            await using var ctx = _db.NewContext();
            // OperationCanceledException xUnit'te başarısızlık sayılmaz; açıkça yakalanır.
            BookImportPageFixResult? cancelled = null;
            try
            {
                cancelled = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1), Box(2), Box(3)], cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                throw new ShouldAssertException("the page fix was cut off after its drafts were saved", ex);
            }
            cancelled.Error.ShouldBe(BookImportPageFixError.None);
            _cancelAfterSave = null;
        }
        test = await SingleTestAsync();
        test.QuestionsSaved.ShouldBe(4);

        // Eski sürümün bıraktığı yetim taslak: sayfanın kaynağı var, hiçbir teste bağlı değil.
        int orphanId;
        await using (var seed = _db.NewContext())
        {
            seed.SetCurrentUser(OwnerId);
            var orphan = new Question { Text = "orphan", IsDraft = true, InteractionType = "mcq" };
            seed.Questions.Add(orphan);
            seed.QuestionSources.Add(new QuestionSource
            {
                Question = orphan, BookId = _bookId, FilePage = 1, ItemLabel = "1", CropSha256 = Guid.NewGuid().ToString("N"),
            });
            await seed.SaveChangesAsync();
            orphanId = orphan.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1), Box(2), Box(3)], default);
            result.Error.ShouldBe(BookImportPageFixError.None);
            result.Page!.Removed.ShouldBe(4);
        }
        _saved.Last().ReplacingQuestionIds!.ShouldContain(orphanId);
        await using var check = _db.NewContext();
        (await check.Questions.IgnoreQueryFilters().SingleAsync(q => q.Id == orphanId)).IsDeleted.ShouldBeTrue();
        (await SingleTestAsync()).QuestionsSaved.ShouldBe(4);
        (await check.Questions.WithDrafts().CountAsync(q => q.IsDraft && q.CreateUserId == OwnerId
            && !check.TestQuestions.Any(wq => wq.QuestionId == q.Id && !wq.IsDeleted))).ShouldBe(0);
    }

    /// <summary>Issue #1171: ortadaki kutu silinince kalanlar numaralarını ve anahtardaki cevaplarını korur; sıra okuma sırası.</summary>
    [Fact]
    public async Task Fixing_a_page_keeps_the_numbers_of_the_remaining_boxes_when_one_is_deleted()
    {
        var id = await SeedAsync([new(1, 1, 1, "ABCD", [4])]);
        Detect(1, 4, 4, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(3), Box(0), Box(2)], default);
            result.Error.ShouldBe(BookImportPageFixError.None);
            result.Page!.Removed.ShouldBe(4);
        }

        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 0), ("3", 2), ("4", 3)]);
        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
            .SingleAsync(w => w.Id == test.WorksheetId);
        worksheet.WorksheetQuestions.Where(wq => !wq.IsDeleted).OrderBy(wq => wq.Order).Select(wq => wq.Question.Text)
            .ShouldBe(["1", "3", "4"]);
    }

    /// <summary>
    /// Düzeltmede tuvalde sorulara bağlanan pasajlar kayda gider (kaydedilen sıraya çevrilmiş; geçersiz / ikinci kez bağlanan sıra
    /// atılır) ve tuval yeniden açılınca taslağın pasajı geri gelir.
    /// </summary>
    [Fact]
    public async Task Passages_drawn_in_a_page_fix_are_saved_with_their_questions_and_come_back_to_the_canvas()
    {
        var id = await SeedAsync([new(1, 1, 1, "ABCD", [4])]);
        Detect(1, 4, 4, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            (await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(3), Box(0), Box(2)], default, passages:
            [
                new LabelingBankPassageDto { X = 100, Y = 20, Width = 900, Height = 60, QuestionIndexes = [1, 2, 9] },
                new LabelingBankPassageDto { X = 100, Y = 2000, Width = 900, Height = 60, QuestionIndexes = [1, 0] },
                new LabelingBankPassageDto { X = 100, Y = 2100, Width = 900, Height = 60, QuestionIndexes = [] },
            ])).Error.ShouldBe(BookImportPageFixError.None);
        }

        // Kaydedilen sıra okuma sırası: Box(0) "1", Box(2) "3", Box(3) "4".
        var request = _saved.Last();
        request.Questions.Select(q => q.ItemLabel).ShouldBe(["1", "3", "4"]);
        request.Passages.ShouldNotBeNull().Select(p => (p.Box.Y, string.Join(',', p.QuestionIndexes))).ShouldBe([(20, "0,1"), (2000, "2")]);

        await using (var seed = _db.NewContext())
        {
            var passage = new Passage { X = 100, Y = 20.4, Width = 900, Height = 60, IsCanvasQuestion = true };
            seed.Passage.Add(passage);
            await seed.SaveChangesAsync();
            await seed.Questions.IgnoreQueryFilters().Where(q => !q.IsDeleted && q.IsDraft && (q.Text == "1" || q.Text == "3"))
                .ExecuteUpdateAsync(q => q.SetProperty(x => x.PassageId, passage.Id));
        }
        await using var check = _db.NewContext();
        var drafts = (await Fix(check).GetPageDraftsAsync(Owner, id, test.Id, 1, default)).Drafts;
        drafts.Select(d => d.Number).ShouldBe([1, 3, 4]);
        drafts[0].Passage.ShouldNotBeNull().Y.ShouldBe(20);
        drafts[1].Passage.ShouldNotBeNull().Id.ShouldBe(drafts[0].Passage!.Id);
        drafts[2].Passage.ShouldBeNull();
    }

    /// <summary>Issue #1173: düzeltme tuvali sayfanın taslaklarını numara, kutu ve doğru şıkla alır.</summary>
    [Fact]
    public async Task Page_drafts_list_numbers_boxes_and_correct_options_for_the_canvas()
    {
        var id = await SeedAsync([new(1, 1, 1, "B", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using var ctx = _db.NewContext();
        var (error, drafts) = await Fix(ctx).GetPageDraftsAsync(Owner, id, test.Id, 1, default);

        error.ShouldBe(BookImportPageFixError.None);
        drafts.Select(d => (d.Number, d.CorrectOptionIndex)).ShouldBe([(1, 1), (2, -1)]);
        // Aktarımın şıklarında Order boş olabilir (demo verisi): sıra incelemedeki gibi Id'den; hep A olmamalı.
        await using (var clear = _db.NewContext())
            await clear.Answers.ExecuteUpdateAsync(a => a.SetProperty(x => x.Order, (int?)null));
        (await Fix(ctx).GetPageDraftsAsync(Owner, id, test.Id, 1, default)).Drafts.Select(d => d.CorrectOptionIndex).ShouldBe([1, -1]);
        var saved = _saved.Single().Questions;
        drafts.Select(d => (d.X, d.Y, d.Width, d.Height)).ShouldBe(saved.Select(q => ((int?)q.X, (int?)q.Y, (int?)q.Width, (int?)q.Height)));
        (await Fix(ctx).GetPageDraftsAsync(BookImportOwner.Create(OwnerId + 1, "QuestionTrainer"), id, test.Id, 1, default)).Error
            .ShouldBe(BookImportPageFixError.NotFound);
        (await Fix(ctx).GetPageDraftsAsync(Owner, id, test.Id, 2, default)).Error.ShouldBe(BookImportPageFixError.InvalidState);
    }

    [Fact]
    public void Page_fix_numbering_matches_old_boxes_then_fills_the_next_free_number()
    {
        static LayoutBox Row(int row, int x = 100) => new(x, 100 + row * 700, 900, 400);
        var reference = new[] { new BookImportPageFixService.NumberedBox(Row(0), 5), new(Row(1), 6), new(Row(0, 1300), 7), new(Row(1, 1300), 8) };

        // 6 silindi, 7 biraz büyütüldü: eşleşmeler korunur.
        BookImportPageFixService.NumberBoxes([new(1300, 80, 950, 450), Row(0), Row(1, 1300)], [null, null, null], reference, 5, 2363)
            .ShouldBe([(1, 5), (0, 7), (2, 8)]);
        // Eski kutuya uymayan yeni kutu öncekinden sonraki boş numarayı alır; etiket önce gelir.
        BookImportPageFixService.NumberBoxes([Row(0), Row(2), Row(0, 1300)], [null, null, "9"], reference, 5, 2363)
            .ShouldBe([(0, 5), (1, 6), (2, 9)]);
        // Eşleşme yoksa okuma sırasıyla baştan.
        BookImportPageFixService.NumberBoxes([Row(0, 1300), Row(0)], [null, null], [], 3, 2363).ShouldBe([(1, 3), (0, 4)]);
    }

    [Fact]
    public void Boxless_old_drafts_take_their_boxes_from_the_import_order()
    {
        LayoutBox[] ordered = [new(0, 0, 10, 10), new(0, 20, 10, 10), new(0, 40, 10, 10)];

        BookImportPageFixService.ImportNumbering(ordered, new HashSet<int> { 4, 6 }, 3).Select(b => (b.Box.Y, b.Number))
            .ShouldBe([(0, 4), (40, 6)]);
        // Sayfanın ilk sorusu testten çıkarılmış: önceki sayfanın sonundan devam.
        BookImportPageFixService.ImportNumbering(ordered, new HashSet<int> { 5, 6 }, 3).Select(b => (b.Box.Y, b.Number))
            .ShouldBe([(20, 5), (40, 6)]);
        BookImportPageFixService.ImportNumbering(ordered, new HashSet<int> { 1, 9 }, 0).ShouldBeEmpty();
    }

    /// <summary>Issue #1133: düzeltilen kutular eğitim verisi için elle doğrulanmış sonuç olur; Claude'un diğer blokları kalır.</summary>
    [Fact]
    public async Task Fixing_a_page_records_the_boxes_as_the_manual_labeling_result()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();
        await using (var seed = _db.NewContext())
        {
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("http://minio/pages/1")));
            var page = new LabelingPage { ImageSha256 = sha, Width = PageWidth, Height = PageHeight, CreatedAt = DateTime.UtcNow };
            page.Results.Add(new LabelingResult
            {
                Source = LabelingSource.Ai, ModelName = Model, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                BoxesJson = System.Text.Json.JsonSerializer.Serialize(new PredictionListDto
                {
                    Predictions = [Question(100, 100, 4), new PredictionDto { X = 100, Y = 1500, Width = 900, Height = 900, Kind = LabelingBlocks.Activity }],
                }),
            });
            seed.LabelingPages.Add(page);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1)], default)).Error.ShouldBe(BookImportPageFixError.None);

        await _labeling.Received(1).SaveManualAsync(Arg.Is<LabelingSaveManualRequestDto>(r =>
            r.BaseSource == "Ai"
            && r.ImageBase64 == "data:image/webp;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("http://minio/pages/1"))
            && r.Predictions.Predictions.Select(p => p.Kind).SequenceEqual(new[] { LabelingBlocks.McqQuestion, LabelingBlocks.McqQuestion, LabelingBlocks.Activity })
            && r.Predictions.Predictions[1].Y == 800 && r.Predictions.Predictions[1].Subpredictions.Count == 4),
            Arg.Any<LabelingActor>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_marked_answer_wins_over_the_key_and_an_unanswered_question_stays_flagged()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            var questions = new List<LabelingBankQuestionDto> { Box(0, correct: 2), Box(1), Box(2, label: "7") };
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, questions, default);
            result.Page!.Answerless.ShouldBe(2);
        }

        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 2), ("2", -1), ("7", -1)]);
        test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        Flags(test).ShouldBe([F("noAnswer", [1], [2, 7]), F("countMismatch", [1], null, 3, 1)]);
    }

    [Fact]
    public async Task Fixing_a_failed_test_creates_its_draft_worksheet()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        // Sayfa 1 tespit edilemez → test Failed, taslak test yok.
        await RunAsync(id);
        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Failed);
        test.WorksheetId.ShouldBeNull();

        await using (var ctx = _db.NewContext())
            (await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1)], default)).Error.ShouldBe(BookImportPageFixError.None);

        test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        test.Error.ShouldBeNull();
        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).SingleAsync(w => w.Id == test.WorksheetId);
        worksheet.IsDraft.ShouldBeTrue();
        worksheet.CreateUserId.ShouldBe(OwnerId);
        worksheet.WorksheetQuestions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_page_cannot_be_fixed_outside_review_outside_the_test_or_by_another_user()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1])]);
        Detect(1, 4);
        Detect(2, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using var ctx = _db.NewContext();
        var fix = Fix(ctx);
        (await fix.ReplacePageAsync(Owner, id, tests[0].Id, 2, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.InvalidState);
        (await fix.ReplacePageAsync(BookImportOwner.Create(OwnerId + 1, "QuestionTrainer"), id, tests[0].Id, 1, [Box(0)], default))
            .Error.ShouldBe(BookImportPageFixError.NotFound);
        (await Service(ctx).ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        (await fix.ReplacePageAsync(Owner, id, tests[0].Id, 1, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.InvalidState);
        _saved.Count.ShouldBe(2);
    }

    // ---------- Issue #1267: sorular alınırken bitmiş testi incele / düzelt ----------

    private async Task BackToImportingAsync(Guid id, params int[] pendingTests)
    {
        await using var ctx = _db.NewContext();
        await ctx.BookImportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(j => j.Status, BookImportJobStatus.Running).SetProperty(j => j.Stage, BookImportStage.Questions));
        await ctx.BookImportJobTests.Where(t => pendingTests.Contains(t.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, BookImportTestStatus.Pending));
    }

    [Fact]
    public async Task A_finished_test_is_reviewed_and_fixed_while_the_rest_are_still_importing()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1])]);
        Detect(1, 4);
        Detect(2, 4);
        await RunAsync(id);
        var tests = await TestsAsync();
        await BackToImportingAsync(id, tests[1].Id);

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await Fix(ctx).ReplacePageAsync(Owner, id, tests[0].Id, 1, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.None);
            (await Fix(ctx).ReplacePageAsync(Owner, id, tests[1].Id, 2, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.InvalidState);
            (await service.RemoveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.InvalidState);
            var all = await service.ApproveAllAsync(Owner, id, default);
            all!.Approved.ShouldBe([tests[0].Id]);
            all.Skipped.ShouldBeEmpty();
            (await service.ApproveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.InvalidState);
        }

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Running);
        job.Stage.ShouldBe(BookImportStage.Questions);
        (await check.BookImportJobTests.OrderBy(t => t.Order).Select(t => t.Status).ToListAsync())
            .ShouldBe([BookImportTestStatus.Approved, BookImportTestStatus.Pending]);
    }

    [Fact]
    public async Task Job_completes_when_every_test_was_approved_before_the_import_finished()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1])]);
        Detect(1, 4);
        Detect(2, 4);
        await RunAsync(id);
        var tests = await TestsAsync();
        await BackToImportingAsync(id);
        await using (var ctx = _db.NewContext())
            (await Service(ctx).ApproveAllAsync(Owner, id, default))!.Approved.Count.ShouldBe(2);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Completed);
        job.Stage.ShouldBe(BookImportStage.Approval);
        job.FinishedAt.ShouldNotBeNull();
    }

    // ---------- Issue #1025: devam, yeniden deneme, iptal ----------

    [Fact]
    public async Task An_interrupted_test_is_retried_reusing_its_saved_drafts_without_duplicates_and_in_order()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 3, "ABCD", [2, 4])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        Detect(3, 4, 4);
        _crashOnSave.Add(3); // test 2'nin ikinci sayfası kaydedilirken bağlantı koptu

        await RunAsync(id);

        var tests = await TestsAsync();
        tests[1].Status.ShouldBe(BookImportTestStatus.Failed);
        tests[1].Error.ShouldBe("connection reset");
        tests[1].WorksheetId.ShouldBeNull();
        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.RetryTestsAsync(Owner, id, tests[0].Id, default)).ShouldBe(RetryBookImportResult.InvalidState);
            (await service.RetryTestsAsync(Owner, id, tests[1].Id, default)).ShouldBe(RetryBookImportResult.Queued);
        }
        (await TestsAsync())[1].Status.ShouldBe(BookImportTestStatus.Pending);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        var after = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        after[0].Status.ShouldBe(BookImportTestStatus.Approved); // onaylanan test yeniden alınmaz
        after[1].Status.ShouldBe(BookImportTestStatus.Done);
        after[1].QuestionsSaved.ShouldBe(4);
        after[1].DuplicatesSkipped.ShouldBe(0);
        _saved.Select(s => s.FilePage).ShouldBe([1, 2, 3, 2, 3]);
        _saved.Skip(3).ShouldAllBe(s => s.ResumeDrafts);
        var worksheet = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
            .SingleAsync(w => w.Id == after[1].WorksheetId);
        worksheet.WorksheetQuestions.OrderBy(wq => wq.Order).Select(wq => wq.Question.Text).ShouldBe(["1", "2", "3", "4"]);
        (await check.Questions.WithDrafts().CountAsync()).ShouldBe(6); // test 1: 2, test 2: 4 — sayfa 2 iki kez kaydedilmedi
    }

    [Fact]
    public async Task A_restarted_run_continues_from_the_unfinished_test_and_relinks_its_existing_draft_test()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "AB", [2])]);
        Detect(1, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        // Yeniden başlatma: test 2 çalışırken API düştü (satır Running, taslak testi var), Hangfire işi yeniden çalıştırır.
        await using (var ctx = _db.NewContext())
        {
            var test = await ctx.BookImportJobTests.AsTracking().SingleAsync(t => t.Id == tests[1].Id);
            test.Status = BookImportTestStatus.Running;
            var job = await ctx.BookImportJobs.AsTracking().SingleAsync();
            job.Status = BookImportJobStatus.Running;
            job.Stage = BookImportStage.Questions;
            await ctx.SaveChangesAsync();
        }
        _saved.Clear();

        await RunAsync(id);

        _saved.ShouldHaveSingleItem().FilePage.ShouldBe(2);
        await using var check = _db.NewContext();
        var after = await check.BookImportJobTests.SingleAsync(t => t.Id == tests[1].Id);
        after.Status.ShouldBe(BookImportTestStatus.Done);
        after.WorksheetId.ShouldBe(tests[1].WorksheetId);
        after.QuestionsSaved.ShouldBe(2);
        (await check.Worksheets.WithDrafts().CountAsync()).ShouldBe(2);
        (await check.TestQuestions.CountAsync(wq => wq.TestId == after.WorksheetId)).ShouldBe(2);
        (await check.Questions.WithDrafts().CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Cancelling_a_running_job_stops_before_the_next_page_and_keeps_its_drafts_for_review()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 3, "AB", [1, 2]), new(3, 4, 4, "C", [1])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        Detect(4, 4);
        _onDetect[2] = async () =>
        {
            await using var ctx = _db.NewContext();
            (await Service(ctx).CancelAsync(Owner, id, false, default)).ShouldBe(CancelBookImportResult.Cancelled);
        };

        await RunAsync(id);

        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.Cancelled);
            job.FinishedAt.ShouldNotBeNull();
        }
        var tests = await TestsAsync();
        tests.Select(t => t.Status).ShouldBe([BookImportTestStatus.Done, BookImportTestStatus.Pending, BookImportTestStatus.Pending]);
        _saved.Select(s => s.FilePage).ShouldBe([1]);
        await _labeling.Received(2).DetectForImportAsync(Arg.Any<string>(), Model, Arg.Any<CancellationToken>()); // sayfa 3 yok

        // Kalan taslaklar incelenebilir; iptal edilmiş iş yeniden denenemez.
        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        (await service.RetryAsync(Owner, id, default)).ShouldBe(RetryBookImportResult.InvalidState);
        (await service.CancelAsync(Owner, id, false, default)).ShouldBe(CancelBookImportResult.InvalidState);
    }

    [Fact]
    public async Task Cancelling_with_delete_drafts_removes_unapproved_draft_tests_and_unlinked_drafts_only()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1]), new(3, 3, 4, "CD", [1, 2])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        Detect(4, 4);
        _crashOnSave.Add(4); // test 3: sayfa 3 kaydedildi, teste bağlanmadan düştü
        await RunAsync(id);
        var tests = await TestsAsync();
        await using (var ctx = _db.NewContext())
            (await Service(ctx).ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        await using (var ctx = _db.NewContext())
        {
            // İncelemedeki iş iptal edilmez (yapılacak iş yok; testler tek tek silinir); hatalı iş edilir.
            (await Service(ctx).CancelAsync(Owner, id, true, default)).ShouldBe(CancelBookImportResult.InvalidState);
            var job = await ctx.BookImportJobs.AsTracking().SingleAsync();
            job.Status = BookImportJobStatus.Failed;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await Service(ctx).CancelAsync(BookImportOwner.Create(99), id, true, default)).ShouldBe(CancelBookImportResult.NotFound);
        await using (var ctx = _db.NewContext())
            (await Service(ctx).CancelAsync(Owner, id, true, default)).ShouldBe(CancelBookImportResult.Cancelled);

        await using var check = _db.NewContext();
        (await check.Questions.WithDrafts().Select(q => q.Text).ToListAsync()).ShouldBe(["1"]); // yalnız onaylanan testin sorusu
        (await check.Worksheets.WithDrafts().Select(w => w.Id).ToListAsync()).ShouldBe([tests[0].WorksheetId!.Value]);
        (await check.BookImportJobTests.OrderBy(t => t.Order).Select(t => t.Status).ToListAsync())
            .ShouldBe([BookImportTestStatus.Approved, BookImportTestStatus.Removed, BookImportTestStatus.Failed]);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Cancelled);
    }

    [Fact]
    public async Task A_failed_or_stalled_job_can_be_retried_and_a_progressing_one_cannot()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1])]);
        var jobs = Substitute.For<IBackgroundJobClient>();
        async Task SetAsync(BookImportJobStatus status, DateTime? updated)
        {
            await using var ctx = _db.NewContext();
            await ctx.BookImportJobs.ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status).SetProperty(j => j.UpdateTime, updated).SetProperty(j => j.Error, "boom"));
        }
        async Task<(RetryBookImportResult Result, bool Stalled)> RetryAsync()
        {
            await using var ctx = _db.NewContext();
            var service = Service(ctx, jobs);
            var stalled = (await service.GetAsync(Owner, id, default))!.Stalled;
            return (await service.RetryAsync(Owner, id, default), stalled);
        }

        await SetAsync(BookImportJobStatus.Running, DateTime.UtcNow.AddMinutes(-2));
        (await RetryAsync()).ShouldBe((RetryBookImportResult.InvalidState, false));
        await SetAsync(BookImportJobStatus.ReadyForReview, DateTime.UtcNow.AddHours(-2));
        (await RetryAsync()).ShouldBe((RetryBookImportResult.InvalidState, false));
        await SetAsync(BookImportJobStatus.Running, DateTime.UtcNow.AddMinutes(-BookImportService.DefaultStallMinutes - 1));
        (await RetryAsync()).ShouldBe((RetryBookImportResult.Queued, true));
        await SetAsync(BookImportJobStatus.Failed, DateTime.UtcNow);
        (await RetryAsync()).ShouldBe((RetryBookImportResult.Queued, false));

        jobs.Received(2).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.EnqueuedState>());
        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Queued);
        job.Error.ShouldBeNull();
        job.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Retry_all_requeues_every_failed_test_only_after_the_questions_stage()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1]), new(3, 3, 3, "C", [1])]);
        Detect(2, 4); // sayfa 1 ve 3 tespitte hata verir
        await RunAsync(id);
        var jobs = Substitute.For<IBackgroundJobClient>();

        await using (var ctx = _db.NewContext())
            (await Service(ctx, jobs).RetryTestsAsync(Owner, id, null, default)).ShouldBe(RetryBookImportResult.Queued);

        (await TestsAsync()).Select(t => t.Status)
            .ShouldBe([BookImportTestStatus.Pending, BookImportTestStatus.Done, BookImportTestStatus.Pending]);
        await using (var ctx = _db.NewContext())
        {
            var job = await ctx.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.Queued);
            job.Stage.ShouldBe(BookImportStage.Questions);
            // İş kuyruktayken (henüz bitmedi) tekrar istenemez; başkasının işi görünmez.
            (await Service(ctx, jobs).RetryTestsAsync(Owner, id, null, default)).ShouldBe(RetryBookImportResult.InvalidState);
            (await Service(ctx, jobs).RetryTestsAsync(BookImportOwner.Create(99), id, null, default)).ShouldBe(RetryBookImportResult.NotFound);
        }
        jobs.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.EnqueuedState>());

        Detect(1, 4);
        Detect(3, 4);
        await RunAsync(id);
        (await TestsAsync()).ShouldAllBe(t => t.Status == BookImportTestStatus.Done);
    }

    [Fact]
    public async Task Quick_add_photos_are_imported_without_a_book_and_a_matching_key_answers_them_in_order()
    {
        var id = await SeedQuickAddAsync("CA", null);
        Detect(1, 4, 4);
        Detect(2, 4, 4, 4);
        var store = new InMemoryFixedWindowCounterStore();

        await RunAsync(id, store);

        var tests = await TestsAsync();
        tests.ShouldAllBe(t => t.QuestionsSaved == t.QuestionsFound && t.WorksheetId != null);
        tests[0].Status.ShouldBe(BookImportTestStatus.Done);
        _saved.ShouldAllBe(s => s.BookId == null && s.GradeId == _gradeId && s.Sharing == QuestionBankSharing.Private);
        _saved[0].Questions.Select(q => q.CorrectOptionIndex).ShouldBe([2, 0]);
        _saved[0].Questions.ShouldAllBe(q => q.ActivityLabel == "Fotoğraf 1");
        _saved[1].Questions.Select(q => q.ItemLabel).ShouldBe(["1", "2", "3"]);
        Flags(tests[1]).ShouldBe(["noAnswer p2 n1,2,3 /"]);
        // Ayrı bütçe sayacı: kitap aktarımının sayacı tüketilmez.
        (await store.PeekAsync(LabelingAiQuota.QuickAddBudgetKey)).Count.ShouldBe(20_000);
        (await store.PeekAsync(LabelingAiQuota.ImportBudgetKey)).Count.ShouldBe(0);
        (await store.PeekAsync(LabelingAiQuota.TotalBudgetKey)).Count.ShouldBe(20_000);

        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        (await check.Worksheets.WithDrafts().SingleAsync(w => w.Id == tests[0].WorksheetId)).Name.ShouldBe("Hızlı ekleme – Fotoğraf 1");
    }

    [Fact]
    public async Task Quick_add_key_that_does_not_match_the_detected_count_is_not_applied_and_flagged()
    {
        var id = await SeedQuickAddAsync("ABC");
        Detect(1, 4, 4);

        await RunAsync(id);

        _saved.Single().Questions.ShouldAllBe(q => q.CorrectOptionIndex == -1);
        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        Flags(test).ShouldBe(["noAnswer p1 n1,2 /", "countMismatch p1 n 2/3"]);
    }

    [Fact]
    public async Task Quick_adds_and_book_imports_are_listed_only_by_their_own_endpoints()
    {
        var quickAdd = await SeedQuickAddAsync([null]);
        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        var quickOwner = Owner with { Kind = BookImportJobKind.QuickAdd };

        (await service.ListAsync(quickOwner, 50, CancellationToken.None)).ShouldHaveSingleItem().Kind.ShouldBe("QuickAdd");
        (await service.ListAsync(Owner, 50, CancellationToken.None)).ShouldBeEmpty();
        (await service.GetAsync(Owner, quickAdd, CancellationToken.None)).ShouldBeNull();
        (await service.GetAsync(Admin, quickAdd, CancellationToken.None)).ShouldBeNull();
        (await service.GetAsync(Admin with { Kind = BookImportJobKind.QuickAdd }, quickAdd, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public void Page_flags_keep_the_page_map_without_the_fixed_page()
    {
        var flags = BookImportPageFixService.ReplacePageFlags(
            [
                new(BookImportReviewFlag.OptionCount, [1, 2], [2, 6],
                    ByPage: new Dictionary<int, IReadOnlyList<int>> { [1] = [2], [2] = [6] }),
            ],
            filePage: 1, pageNumbers: new HashSet<int> { 1, 2, 3 }, answerless: [3], optionCount: [1], found: 6, keyLength: 6, from: 1, to: 2);

        flags.Select(f => $"{f.Code} {string.Join(' ', f.ByPage!.OrderBy(p => p.Key).Select(p => $"{p.Key}:{string.Join(',', p.Value)}"))}")
            .ShouldBe(["optionCount 1:1 2:6", "noAnswer 1:3"]);
    }

    [Fact]
    public void Page_flags_drop_the_fixed_page_and_keep_the_others()
    {
        var flags = BookImportPageFixService.ReplacePageFlags(
            [
                new(BookImportReviewFlag.MissingNumbers, [1, 2], [2, 5]),
                new(BookImportReviewFlag.DetectionFailed, [1]),
                new(BookImportReviewFlag.OptionCount, [2], [6]),
                new(BookImportReviewFlag.CountMismatch, [1, 2], Found: 4, Expected: 6),
            ],
            filePage: 1, pageNumbers: new HashSet<int> { 1, 2, 3 }, answerless: [3], optionCount: [], found: 6, keyLength: 6, from: 1, to: 2);

        flags.Select(f => F(f.Code, f.Pages, f.Numbers, f.Found, f.Expected))
            .ShouldBe([F("missingNumbers", [2], [5]), F("optionCount", [2], [6]), F("noAnswer", [1], [3])]);
    }

    private static BookImportOwner QuickOwner => Owner with { Kind = BookImportJobKind.QuickAdd };

    /// <summary>
    /// Issue #1144: karışık sorular — Sayfalar'dan sonra kurulum olmadan sorulara geçer, görsel başına bağımsız test; hepsini
    /// onaylarken sorular isteğe bağlı olarak yeni bir teste sırasıyla toplanır, iç taslak testler silinir.
    /// </summary>
    [Fact]
    public async Task Loose_images_skip_setup_and_approve_all_can_collect_the_questions_into_a_new_test()
    {
        var id = await SeedQuickAddAsync("AB", "C");
        await using (var ctx = _db.NewContext())
        {
            await ctx.BookImportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(j => j.Kind, BookImportJobKind.Book)
                .SetProperty(j => j.Source, BookImportSource.LooseImages)
                .SetProperty(j => j.BookName, "Karışık")
                .SetProperty(j => j.FileUrl, "/img/exam-questions/book-import-uploads/u1/")
                .SetProperty(j => j.Stage, BookImportStage.Pages));
            await ctx.BookImportJobTests.ExecuteUpdateAsync(u => u.SetProperty(t => t.Title, t => "Görsel " + t.Order));
        }
        Detect(1, 4, 4);
        Detect(2, 4);

        await RunAsync(id);

        await using (var first = _db.NewContext())
        {
            var j = await first.BookImportJobs.SingleAsync();
            (j.Status, j.Stage, j.Error).ShouldBe((BookImportJobStatus.ReadyForReview, BookImportStage.Review, null));
        }
        var tests = await TestsAsync();
        tests.Select(t => (t.Status, t.Error)).ShouldBe([(BookImportTestStatus.Done, null), (BookImportTestStatus.Done, null)]);
        _saved.ShouldAllBe(s => s.BookId == null);
        _saved[0].Questions.ShouldAllBe(q => q.ActivityLabel == "Görsel 1");
        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            (job.Status, job.Stage).ShouldBe((BookImportJobStatus.ReadyForReview, BookImportStage.Review));
            (await check.Worksheets.WithDrafts().SingleAsync(w => w.Id == tests[0].WorksheetId)).Name.ShouldBe("Karışık – Görsel 1");
        }

        ApproveAllBookImportResultDto result;
        await using (var ctx = _db.NewContext())
            result = (await Service(ctx).ApproveAllAsync(Owner, id, default, "  Karışık deneme  "))!;

        result.Approved.Count.ShouldBe(2);
        await using var after = _db.NewContext();
        var collected = await after.Worksheets.Include(w => w.WorksheetQuestions).SingleAsync();
        collected.Id.ShouldBe(result.CollectedTestId!.Value);
        (collected.Name, collected.IsDraft, collected.GradeId).ShouldBe(("Karışık deneme", false, _gradeId));
        var questionIds = await after.Questions.OrderBy(q => q.Id).Select(q => q.Id).ToListAsync();
        collected.WorksheetQuestions.OrderBy(wq => wq.Order).Select(wq => wq.QuestionId).ShouldBe(questionIds);
        (await after.TestQuestions.IgnoreQueryFilters().Where(wq => wq.TestId == tests[0].WorksheetId).AllAsync(wq => wq.IsDeleted)).ShouldBeTrue();
        (await after.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Completed);
    }

    [Fact]
    public async Task Collecting_into_a_test_is_ignored_for_quick_add()
    {
        var id = await SeedQuickAddAsync("AB");
        Detect(1, 4, 4);
        await RunAsync(id);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).ApproveAllAsync(QuickOwner, id, default, "Yeni test"))!.CollectedTestId.ShouldBeNull();

        await using var check = _db.NewContext();
        (await check.Worksheets.CountAsync()).ShouldBe(0);
    }


    /// <summary>Issue #1054: onayda sorular etkinleşir, iç taslak test bağlantılarıyla silinir; test yayımlanmaz.</summary>
    [Fact]
    public async Task Approving_a_quick_add_photo_activates_questions_and_deletes_the_internal_test()
    {
        var id = await SeedQuickAddAsync("AB");
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var before = _db.NewContext())
            (await before.Questions.CountAsync()).ShouldBe(0);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).ApproveTestAsync(QuickOwner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(2);
        (await check.Worksheets.WithDrafts().AnyAsync(w => w.Id == test.WorksheetId)).ShouldBeFalse();
        (await check.Worksheets.CountAsync()).ShouldBe(0);
        (await check.TestQuestions.IgnoreQueryFilters().Where(wq => wq.TestId == test.WorksheetId).AllAsync(wq => wq.IsDeleted)).ShouldBeTrue();
        (await SingleTestAsync()).Status.ShouldBe(BookImportTestStatus.Approved);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Completed);
    }

    [Fact]
    public async Task Quick_add_with_answerless_questions_is_not_approved_until_answers_are_set()
    {
        var id = await SeedQuickAddAsync("AB", null);
        Detect(1, 4, 4);
        Detect(2, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx).ApproveTestAsync(QuickOwner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Answerless);
            var all = await Service(ctx).ApproveAllAsync(QuickOwner, id, default);
            all!.Approved.ShouldBe([tests[0].Id]);
            all.Skipped.ShouldBe([new BookImportSkippedTestDto(tests[1].Id, BookImportService.SkipAnswerless)]);
        }
        await using (var leak = _db.NewContext())
            (await leak.Questions.CountAsync()).ShouldBe(2);

        await using (var ctx = _db.NewContext())
        {
            var question = (await Service(ctx).GetReviewQuestionsAsync(QuickOwner, id, tests[1].Id, default))!.Single();
            (question.Number, question.FilePage, question.AnswerIndex).ShouldBe(("1", 2, (int?)null));
            (await Service(ctx).SetAnswerAsync(QuickOwner, id, tests[1].Id, question.Id, 3, default)).ShouldBe(BookImportReviewResult.Ok);
            (await Service(ctx).ApproveTestAsync(QuickOwner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Removing_a_quick_add_photo_deletes_only_its_drafts()
    {
        var id = await SeedQuickAddAsync("A", "B");
        Detect(1, 4);
        Detect(2, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx).RemoveTestAsync(QuickOwner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await Service(ctx).ApproveTestAsync(QuickOwner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using var check = _db.NewContext();
        (await check.Questions.Select(q => q.Text).ToListAsync()).ShouldBe(["1"]);
        (await check.Questions.WithDrafts().CountAsync()).ShouldBe(1);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Completed);
    }

    [Fact]
    public async Task Quick_add_jobs_are_not_reachable_through_book_import_review_or_fix()
    {
        var id = await SeedQuickAddAsync("A");
        Detect(1, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using var ctx = _db.NewContext();
        (await Service(ctx).ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.NotFound);
        (await Service(ctx).ApproveTestAsync(Admin, id, test.Id, default)).ShouldBe(BookImportReviewResult.NotFound);
        (await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.NotFound);
        (await Service(ctx).ApproveTestAsync(QuickOwner with { Subject = EntitlementSubject.Create(OwnerId + 1, "Teacher") }, id, test.Id, default))
            .ShouldBe(BookImportReviewResult.NotFound);
    }

    [Fact]
    public async Task Fixing_a_quick_add_photo_replaces_drafts_and_applies_the_key_only_when_the_count_matches()
    {
        var id = await SeedQuickAddAsync("BC");
        Detect(1, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();
        Flags(test).ShouldContain(f => f.StartsWith("countMismatch"));

        await using (var ctx = _db.NewContext())
            (await Fix(ctx).ReplacePageAsync(QuickOwner, id, test.Id, 1, [Box(0), Box(1)], default)).Error.ShouldBe(BookImportPageFixError.None);

        _saved.Last().BookId.ShouldBeNull();
        _saved.Last().ReplacingQuestionIds!.Count.ShouldBe(1);
        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 1), ("2", 2)]);
        test = await SingleTestAsync();
        (test.Status, test.QuestionsSaved).ShouldBe((BookImportTestStatus.Done, 2));

        await using (var ctx = _db.NewContext())
            (await Fix(ctx).ReplacePageAsync(QuickOwner, id, test.Id, 1, [Box(0), Box(1), Box(2)], default)).Error.ShouldBe(BookImportPageFixError.None);

        _saved.Last().Questions.ShouldAllBe(q => q.CorrectOptionIndex == -1);
    }

    [Fact]
    public void Quick_add_labeling_fix_endpoint_needs_the_manual_labeling_plan_issue1087()
    {
        var action = typeof(QuickAddController).GetMethod(nameof(QuickAddController.ReplacePage))!;
        action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).ShouldBeEmpty();
        action.GetCustomAttributes(typeof(ExamApp.Api.Services.Entitlements.RequiresEntitlementAttribute), false)
            .Cast<ExamApp.Api.Services.Entitlements.RequiresEntitlementAttribute>().Single().FeatureKey
            .ShouldBe(ExamApp.Api.Services.Entitlements.EntitlementFeatures.ManualLabeling);
    }

    private BookImportPageFixService Fix(AppDbContext ctx) =>
        new(ctx, _minio, _bank, _labeling, NullLogger<BookImportPageFixService>.Instance);

    private static LabelingBankQuestionDto Box(int row, int correct = -1, string? label = null) => new()
    {
        X = 100, Y = 100 + row * 700, Width = 900, Height = 400, CorrectOptionIndex = correct, ItemLabel = label,
        Options = Enumerable.Range(0, 4)
            .Select(i => new LabelingBankBoxDto { X = 120 + i % 2 * 440, Y = 300 + row * 700 + i / 2 * 90, Width = 400, Height = 60 })
            .ToList(),
    };

    private sealed record TestSeed(int Order, int FileFrom, int FileTo, string? Key, int[] PageMaxNumbers);

    private static PredictionDto Question(int x, int y, int options) => new()
    {
        X = x, Y = y, Width = 900, Height = 400, Kind = LabelingBlocks.McqQuestion,
        Subpredictions = Enumerable.Range(0, options)
            .Select(i => new PredictionDto { X = x + 20 + i % 2 * 440, Y = y + 200 + i / 2 * 90, Width = 400, Height = 60 })
            .ToList(),
    };

    /// <summary>Sayfada yukarıdan aşağı, verilen şık sayılarıyla sorular (sol sütun).</summary>
    private void Detect(int filePage, params int[] optionCounts) =>
        _detections[filePage] = optionCounts.Select((n, i) => Question(100, 100 + i * 700, n)).ToList();

    private async Task<Guid> SeedAsync(IReadOnlyList<TestSeed> tests,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        var book = new Book { Name = "Puan BTS 5 Matematik" };
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();
        _gradeId = grade.Id;
        _bookId = book.Id;

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = book.Name,
            BookId = book.Id,
            GradeId = grade.Id,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Questions,
            FileUrl = "http://minio/source.pdf",
            TeacherSharing = sharing,
        };
        ctx.SetCurrentUser(OwnerId);
        ctx.BookImportJobs.Add(job);
        foreach (var t in tests)
        {
            ctx.BookImportJobTests.Add(new BookImportJobTest
            {
                JobId = job.Id, Order = t.Order, Unit = 1, TestNo = t.Order, Title = $"Test {t.Order}",
                FileFrom = t.FileFrom, FileTo = t.FileTo, AnswerKey = t.Key, KeyAnswers = t.Key?.Length ?? 0,
            });
            for (var page = t.FileFrom; page <= t.FileTo; page++)
            {
                ctx.BookImportPages.Add(new BookImportPage
                {
                    JobId = job.Id, FilePage = page, ImageUrl = $"http://minio/pages/{page}", Width = PageWidth, Height = PageHeight,
                    MaxQuestionNumber = t.PageMaxNumbers[page - t.FileFrom],
                });
            }
        }
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>Issue #1052: hızlı ekleme işi — kitapsız, fotoğraf başına bir test; anahtarlar fotoğraf sırasıyla.</summary>
    private async Task<Guid> SeedQuickAddAsync(params string?[] keys)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        _gradeId = grade.Id;

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(), Kind = BookImportJobKind.QuickAdd, BookName = QuickAddService.JobName, GradeId = grade.Id,
            Status = BookImportJobStatus.Queued, Stage = BookImportStage.Questions, PageCount = keys.Length,
        };
        ctx.SetCurrentUser(OwnerId);
        ctx.BookImportJobs.Add(job);
        for (var photo = 1; photo <= keys.Length; photo++)
        {
            ctx.BookImportJobTests.Add(new BookImportJobTest
            {
                JobId = job.Id, Order = photo, TestNo = photo, Title = $"Fotoğraf {photo}", FileFrom = photo, FileTo = photo,
                AnswerKey = keys[photo - 1],
            });
            ctx.BookImportPages.Add(new BookImportPage
            {
                JobId = job.Id, FilePage = photo, ImageUrl = $"http://minio/photos/{photo}", Width = PageWidth, Height = PageHeight,
            });
        }
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private async Task RunAsync(Guid id, IFixedWindowCounterStore? budgetStore = null, LabelingAiOptions? aiOptions = null,
        IBackgroundJobClient? jobs = null)
    {
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);
        await using var ctx = _db.NewContext();
        await new BookImportJobRunner(ctx, guard, _minio, Substitute.For<IPdfRasterizer>(), new FakeBookSetupAi(), _labeling,
            _services.GetRequiredService<IServiceScopeFactory>(), _config, budgetStore: budgetStore,
            aiOptions: aiOptions is null ? null : Options.Create(aiOptions), jobs: jobs, activityStructure: _activity).RunLockedAsync(id);
    }

    private static readonly BookImportOwner Owner = BookImportOwner.Create(OwnerId, "QuestionTrainer");
    private static readonly BookImportOwner Admin = BookImportOwner.Create(1, "Admin");

    private IConfiguration _config = new ConfigurationBuilder().Build();

    private BookImportService Service(AppDbContext ctx, IBackgroundJobClient? jobs = null) => new(ctx, _minio,
        jobs ?? Substitute.For<IBackgroundJobClient>(), Substitute.For<IEntitlementService>(), new BookRegistryService(ctx, TimeProvider.System),
        _config);

    private async Task<List<BookImportJobTest>> TestsAsync()
    {
        await using var check = _db.NewContext();
        return await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
    }

    private async Task<BookImportJobTest> SingleTestAsync()
    {
        await using var check = _db.NewContext();
        return await check.BookImportJobTests.SingleAsync();
    }

    private static List<string> Flags(BookImportJobTest test) =>
        BookImportReviewFlag.Parse(test.ReviewFlagsJson).Select(f => F(f.Code, f.Pages, f.Numbers, f.Found, f.Expected)).ToList();

    private static string F(string code, IEnumerable<int> pages, IEnumerable<int>? numbers = null, int? found = null, int? expected = null) =>
        $"{code} p{string.Join(",", pages)} n{string.Join(",", numbers ?? [])} {found}/{expected}";

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }
}
