using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1022: testin inceleme işareti. <c>Pages</c> dosya sayfaları; <c>Numbers</c> ilgili soru numaraları (varsa);
/// <c>Found</c> / <c>Expected</c> sayı işaretlerinde.
/// </summary>
public sealed record BookImportReviewFlag(
    string Code, IReadOnlyList<int> Pages, IReadOnlyList<int>? Numbers = null, int? Found = null, int? Expected = null)
{
    /// <summary>Testte bulunan soru sayısı anahtardakinden farklı.</summary>
    public const string CountMismatch = "countMismatch";

    /// <summary>Sayfada basılı en büyük soru no'ya göre bulunamayan numaralar.</summary>
    public const string MissingNumbers = "missingNumbers";

    /// <summary>Sayfada beklenenden fazla soru kutusu.</summary>
    public const string ExtraQuestions = "extraQuestions";

    /// <summary>Şık sayısı 4 değil.</summary>
    public const string OptionCount = "optionCount";

    /// <summary>Cevapsız kaydedildi (anahtar yok, anahtarda yok ya da okunamadı).</summary>
    public const string NoAnswer = "noAnswer";

    /// <summary>İkiden az şık: soru kaydedilemedi.</summary>
    public const string NotSaved = "notSaved";

    /// <summary>Sayfada tespit başarısız.</summary>
    public const string DetectionFailed = "detectionFailed";

    /// <summary>Sayfanın soruları kaydedilemedi.</summary>
    public const string SaveFailed = "saveFailed";

    internal static string? Serialize(IReadOnlyCollection<BookImportReviewFlag> flags) =>
        flags.Count == 0 ? null : JsonSerializer.Serialize(flags, BookSetupSummary.Json);

    public static IReadOnlyList<BookImportReviewFlag> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<BookImportReviewFlag>>(json, BookSetupSummary.Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Issue #1022: soru aktarımı — testler sırayla: sayfa başına Claude tespiti (<see cref="ILabelingService.DetectForImportAsync"/>,
/// işaretleme ekranının "Claude ile tespit"iyle aynı servis ve önbellek), okuma sırası, bölünmüş kökün birleştirilmesi,
/// şık hizalama (<see cref="BookQuestionLayout"/>), anahtardan cevap ve taslak kayıt
/// (<see cref="ILabelingBankService.SaveImportedPageAsync"/>, sayfa başına ayrı scope). Bir sayfanın ya da testin hatası diğerlerini durdurmaz; bitmiş
/// testler yeniden çalışmada atlanır, yarıda kalan test baştan alınır (kaydedilmiş sorular kırpma tekrarı olarak sayılır).
/// </summary>
internal sealed class BookQuestionImportStage(
    AppDbContext db, IMinIoService minio, ILabelingService labeling, IServiceScopeFactory scopes, string detectModel, ILogger logger)
{
    private const int ExpectedOptions = 4;

    public async Task RunAsync(BookImportJob job, CancellationToken ct = default)
    {
        if (job.BookId is not { } bookId)
            throw new BookImportFailure("The book record is missing (setup was not confirmed).");
        if (labeling.GetAiModels().All(m => m.Id != detectModel))
            throw new BookImportFailure(labeling.GetAiModels().Count == 0
                ? "Claude is not configured (Anthropic:ApiKey is empty)."
                : $"The detection model {detectModel} is not in the allowed labeling models.");
        if (job.CreateUserId is not > 0)
            throw new BookImportFailure("The job has no owner.");

        var tests = await db.BookImportJobTests.AsTracking().Where(t => t.JobId == job.Id).OrderBy(t => t.Order).ToListAsync(ct);
        var pages = await db.BookImportPages.AsNoTracking().Where(p => p.JobId == job.Id)
            .ToDictionaryAsync(p => p.FilePage, ct);
        job.StageTotal = tests.Count;
        job.StageProcessed = tests.Count(IsFinished);
        await db.SaveChangesAsync(ct);

        foreach (var test in tests.Where(t => !IsFinished(t)))
        {
            test.Status = BookImportTestStatus.Running;
            test.Error = null;
            await db.SaveChangesAsync(ct);
            try
            {
                await ImportTestAsync(job, bookId, test, pages, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Book import job {JobId}: test {Order} failed", job.Id, test.Order);
                test.Status = BookImportTestStatus.Failed;
                test.Error = Truncate(ex.Message);
            }
            job.StageProcessed++;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static bool IsFinished(BookImportJobTest test) =>
        test.Status is BookImportTestStatus.Done or BookImportTestStatus.NeedsReview or BookImportTestStatus.Failed;

    private async Task ImportTestAsync(
        BookImportJob job, int bookId, BookImportJobTest test, IReadOnlyDictionary<int, BookImportPage> pages, CancellationToken ct)
    {
        if (test.FileFrom is not { } from || test.FileTo is not { } to || to < from)
            throw new BookImportFailure("The test has no pages.");

        var key = test.AnswerKey;
        var label = BookImportService.BookTestName(test);
        label = label.Length <= QuestionSource.MaxLabelLength ? label : label[..QuestionSource.MaxLabelLength];
        var flags = new FlagSet();
        var questionIds = new List<int>();
        int found = 0, saved = 0, duplicates = 0, failedPages = 0, pageCount = 0;
        var last = 0;

        for (var filePage = from; filePage <= to; filePage++)
        {
            pageCount++;
            if (!pages.TryGetValue(filePage, out var page))
            {
                flags.Add(BookImportReviewFlag.DetectionFailed, filePage);
                failedPages++;
                continue;
            }

            // Numaralama sayfaya dayanır: basılı en büyük no biliniyorsa sayfa önceki sayfanın sonundan başlar ve orada biter.
            var start = last + 1;
            var pageMax = page.MaxQuestionNumber is { } max && max >= start ? max : (int?)null;

            string imageBase64;
            List<LayoutQuestion> questions;
            int width, height;
            try
            {
                imageBase64 = await LoadAsync(page);
                var detection = await DetectAsync(job, imageBase64, ct);
                width = page.Width;
                height = page.Height;
                questions = BookQuestionLayout.MergeSplit(
                    BookQuestionLayout.Order(ToLayout(detection.Response.Result.Predictions), width), width, height);
            }
            catch (Exception ex) when (ex is LabelingAiException or LabelingImportDetectException or LabelingValidationException
                                           or LabelingAiUnavailableException or BookImportFailure)
            {
                logger.LogWarning("Book import job {JobId}: detection failed on page {Page}: {Reason}", job.Id, filePage, ex.Message);
                flags.Add(BookImportReviewFlag.DetectionFailed, filePage);
                if (pageMax is { } m)
                {
                    flags.Add(BookImportReviewFlag.MissingNumbers, filePage, Enumerable.Range(start, m - start + 1));
                    last = m;
                }
                failedPages++;
                continue;
            }

            found += questions.Count;
            if (pageMax is { } expectedMax)
            {
                var expected = expectedMax - start + 1;
                if (questions.Count < expected)
                    flags.Add(BookImportReviewFlag.MissingNumbers, filePage, Enumerable.Range(start + questions.Count, expected - questions.Count));
                else if (questions.Count > expected)
                    flags.Add(BookImportReviewFlag.ExtraQuestions, filePage, Enumerable.Range(expectedMax + 1, questions.Count - expected));
            }

            var toSave = new List<LabelingBankQuestionDto>();
            for (var i = 0; i < questions.Count; i++)
            {
                var number = start + i;
                var prepared = BookQuestionLayout.Prepare(questions[i], width, height);
                if (prepared.Options.Count != ExpectedOptions)
                    flags.Add(BookImportReviewFlag.OptionCount, filePage, [number]);
                if (prepared.Options.Count is < LabelingBankService.MinOptions or > LabelingBankService.MaxOptions)
                {
                    flags.Add(BookImportReviewFlag.NotSaved, filePage, [number]);
                    continue;
                }

                var answer = AnswerIndex(key, number);
                if (answer >= prepared.Options.Count)
                    answer = -1;
                if (answer < 0)
                    flags.Add(BookImportReviewFlag.NoAnswer, filePage, [number]);
                toSave.Add(ToBankQuestion(prepared, answer, label, number));
            }
            last = Math.Max(pageMax ?? 0, start + questions.Count - 1);

            if (toSave.Count == 0)
                continue;
            try
            {
                // Ayrı scope (ayrı DbContext): toplu kaydın geri alınan varlıkları işin izleyicisinde kalmasın.
                await using var scope = scopes.CreateAsyncScope();
                var bank = scope.ServiceProvider.GetRequiredService<ILabelingBankService>();
                var result = await bank.SaveImportedPageAsync(new LabelingImportPageRequest(
                    imageBase64, bookId, filePage, job.GradeId, QuestionBankSharing.Private, toSave), job.CreateUserId!.Value, ct);
                saved += result.Saved;
                duplicates += result.DuplicateIndexes.Count;
                questionIds.AddRange(result.QuestionIds);
            }
            catch (Exception ex) when (ex is LabelingValidationException or LabelingBankSaveException)
            {
                logger.LogWarning("Book import job {JobId}: saving page {Page} failed: {Reason}", job.Id, filePage, ex.Message);
                flags.Add(BookImportReviewFlag.SaveFailed, filePage, toSave.Select(q => int.Parse(q.ItemLabel!)));
                failedPages++;
            }
        }

        if (key is not null && found != key.Length)
            flags.AddCount(BookImportReviewFlag.CountMismatch, from, to, found, key.Length);

        if (questionIds.Count > 0)
            await CreateDraftTestAsync(job, test, questionIds, ct);

        test.QuestionsFound = found;
        test.QuestionsSaved = saved;
        test.DuplicatesSkipped = duplicates;
        test.ReviewFlagsJson = BookImportReviewFlag.Serialize(flags.ToList());
        if (failedPages == pageCount)
        {
            test.Status = BookImportTestStatus.Failed;
            test.Error = "Detection or saving failed on every page of the test.";
        }
        else
        {
            test.Status = flags.IsEmpty ? BookImportTestStatus.Done : BookImportTestStatus.NeedsReview;
        }
        logger.LogInformation("Book import job {JobId}: test {Order} {Status}: {Found} found, {Saved} saved, {Duplicates} duplicates",
            job.Id, test.Order, test.Status, found, saved, duplicates);
    }

    /// <summary>
    /// Issue #1023: testin kaydedilen soruları taslak bir teste sırayla bağlanır (el ile aktarımdaki gibi: kitap adı – test adı,
    /// süre anahtardan, öğretmen paylaşımı Private). Yeniden çalıştırmada var olan taslak test yeniden kullanılır.
    /// </summary>
    private async Task CreateDraftTestAsync(BookImportJob job, BookImportJobTest test, List<int> questionIds, CancellationToken ct)
    {
        var worksheet = test.WorksheetId is { } existingId
            ? await db.Worksheets.WithDrafts().AsTracking().Include(w => w.WorksheetQuestions)
                .FirstOrDefaultAsync(w => w.Id == existingId && w.IsDraft, ct)
            : null;
        if (worksheet is null)
        {
            var name = $"{job.BookName} – {BookImportService.BookTestName(test)}";
            worksheet = new Worksheet
            {
                Name = name.Length <= DraftTestNameLength ? name : name[..DraftTestNameLength],
                Description = $"{job.BookName}. Cevaplar kitabın cevap anahtarından.",
                GradeId = job.GradeId,
                SubjectId = job.SubjectId,
                MaxDurationSeconds = (test.Minutes ?? 0) * 60,
                BookTestId = test.BookTestId,
                TeacherSharing = WorksheetTeacherSharing.Private,
                IsDraft = true,
            };
            // Sahip denetim damgasıyla yazılır (iş isteksiz çalışır): taslağın sahibi işin sahibi.
            db.SetCurrentUser(job.CreateUserId!.Value);
            db.Worksheets.Add(worksheet);
        }

        var linked = worksheet.WorksheetQuestions.Select(q => q.QuestionId).ToHashSet();
        var order = worksheet.WorksheetQuestions.Count == 0 ? 0 : worksheet.WorksheetQuestions.Max(q => q.Order);
        foreach (var id in questionIds.Where(linked.Add))
            worksheet.WorksheetQuestions.Add(new WorksheetQuestion { QuestionId = id, Order = ++order });
        await db.SaveChangesAsync(ct);
        test.WorksheetId = worksheet.Id;
    }

    private const int DraftTestNameLength = 200;

    private async Task<LabelingImportDetection> DetectAsync(BookImportJob job, string imageBase64, CancellationToken ct)
    {
        try
        {
            var detection = await labeling.DetectForImportAsync(imageBase64, detectModel, ct);
            job.AiCostUsd += detection.CostUsd;
            return detection;
        }
        catch (LabelingImportDetectException failed)
        {
            // Kullanılamayan yanıtın token'ı da ödendi.
            job.AiCostUsd += failed.CostUsd;
            throw;
        }
    }

    /// <summary>Anahtardaki harfin şık sırası (A → 0); anahtar yok / anahtarda yok / okunamadı → -1.</summary>
    internal static int AnswerIndex(string? key, int number)
    {
        if (key is null || number < 1 || number > key.Length)
            return -1;
        var letter = key[number - 1];
        return letter is >= 'A' and <= 'J' ? letter - 'A' : -1;
    }

    /// <summary>Yalnız çoktan seçmeli soru blokları (<c>kind</c> boş = YOLO / eski kayıt, mcq sayılır).</summary>
    internal static IEnumerable<LayoutQuestion> ToLayout(PredictionListDto predictions) =>
        (predictions.Predictions ?? [])
        .Where(p => (p.Kind ?? LabelingBlocks.McqQuestion) == LabelingBlocks.McqQuestion)
        .Select(p => new LayoutQuestion(Box(p), (p.Subpredictions ?? []).Select(Box).ToList()));

    private static LayoutBox Box(PredictionDto p) => new(p.X, p.Y, p.Width, p.Height);

    private static LabelingBankQuestionDto ToBankQuestion(LayoutQuestion q, int answer, string label, int number) => new()
    {
        X = q.Box.X,
        Y = q.Box.Y,
        Width = q.Box.Width,
        Height = q.Box.Height,
        Options = q.Options.Select(o => new LabelingBankBoxDto { X = o.X, Y = o.Y, Width = o.Width, Height = o.Height }).ToList(),
        CorrectOptionIndex = answer,
        ActivityLabel = label,
        ItemLabel = number.ToString(),
    };

    private async Task<string> LoadAsync(BookImportPage page)
    {
        await using var stream = await minio.GetFileStreamAsync(page.ImageUrl)
                                 ?? throw new BookImportFailure($"Page {page.FilePage} image could not be read from storage.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return "data:image/webp;base64," + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Truncate(string message) =>
        message.Length <= BookImportJob.MaxMessageLength ? message : message[..BookImportJob.MaxMessageLength];

    /// <summary>Aynı kodlu işaretleri birleştirir: sayfa ve numara listeleri sıralı ve tekil.</summary>
    private sealed class FlagSet
    {
        private readonly Dictionary<string, (SortedSet<int> Pages, SortedSet<int> Numbers, int? Found, int? Expected)> _flags = new();
        private readonly List<string> _order = [];

        public bool IsEmpty => _flags.Count == 0;

        public void Add(string code, int page, IEnumerable<int>? numbers = null)
        {
            var flag = Get(code);
            flag.Pages.Add(page);
            if (numbers is not null)
                flag.Numbers.UnionWith(numbers);
        }

        public void AddCount(string code, int from, int to, int found, int expected)
        {
            var flag = Get(code);
            flag.Pages.UnionWith(Enumerable.Range(from, to - from + 1));
            _flags[code] = flag with { Found = found, Expected = expected };
        }

        private (SortedSet<int> Pages, SortedSet<int> Numbers, int? Found, int? Expected) Get(string code)
        {
            if (_flags.TryGetValue(code, out var flag))
                return flag;
            flag = (new SortedSet<int>(), new SortedSet<int>(), null, null);
            _flags[code] = flag;
            _order.Add(code);
            return flag;
        }

        public List<BookImportReviewFlag> ToList() => _order.Select(code =>
        {
            var f = _flags[code];
            return new BookImportReviewFlag(code, f.Pages.ToList(), f.Numbers.Count == 0 ? null : f.Numbers.ToList(), f.Found, f.Expected);
        }).ToList();
    }
}
