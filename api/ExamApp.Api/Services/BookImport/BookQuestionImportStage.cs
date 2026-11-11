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
/// <remarks>
/// <c>ByPage</c>: sayfa → o sayfadaki numaralar (numarasız işarette boş liste); ekranda uyarılar sayfaya göre gruplanır. Eski
/// kayıtlarda yok (null): ekran <c>Pages</c> / <c>Numbers</c> ile yazar. Sayı uyuşmazlığında yok (testin bütünü).
/// </remarks>
public sealed record BookImportReviewFlag(
    string Code, IReadOnlyList<int> Pages, IReadOnlyList<int>? Numbers = null, int? Found = null, int? Expected = null,
    IReadOnlyDictionary<int, IReadOnlyList<int>>? ByPage = null)
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
    AppDbContext db, IMinIoService minio, ILabelingService labeling, IServiceScopeFactory scopes, BookImportBudget budget,
    string detectModel, ILogger logger)
{
    internal const int ExpectedOptions = 4;

    public async Task RunAsync(BookImportJob job, CancellationToken ct = default)
    {
        // #1052: hızlı eklemenin kitabı yok; kitap aktarımında kurulum onayı kitabı bağlar.
        var bookId = job.BookId;
        if (bookId is null && job.Kind == BookImportJobKind.Book)
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
            await StopIfCancelledAsync(job, ct);
            test.Status = BookImportTestStatus.Running;
            test.Error = null;
            await db.SaveChangesAsync(ct);
            budget.Stage = BookImportStage.Questions;
            budget.TestId = test.Id;
            try
            {
                await ImportTestAsync(job, bookId, test, pages, ct);
            }
            catch (BookImportCancelled)
            {
                // #1025: iptal edildi; yarıda kalan test bekleyen olarak kalır, kaydedilmiş taslaklar yerinde.
                test.Status = BookImportTestStatus.Pending;
                await budget.FlushAsync();
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or BookImportPause))
            {
                logger.LogError(ex, "Book import job {JobId}: test {Order} failed", job.Id, test.Order);
                test.Status = BookImportTestStatus.Failed;
                test.Error = Truncate(ex.Message);
            }
            job.StageProcessed++;
            await budget.FlushAsync();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Yeniden çalıştırmada atlanan testler; incelemede onaylanan / silinen testler de (#1025 hatalı testi yeniden dene).</summary>
    internal static bool IsFinished(BookImportJobTest test) =>
        test.Status is BookImportTestStatus.Done or BookImportTestStatus.NeedsReview or BookImportTestStatus.Failed
            or BookImportTestStatus.Approved or BookImportTestStatus.Removed;

    /// <summary>Issue #1025: iş iptal edildiyse (<see cref="BookImportService.CancelAsync"/>) çalışma test / sayfa arasında durur.</summary>
    internal static async Task StopIfCancelledAsync(AppDbContext db, Guid jobId, CancellationToken ct)
    {
        if (await db.BookImportJobs.AnyAsync(j => j.Id == jobId && j.Status == BookImportJobStatus.Cancelled, ct))
            throw new BookImportCancelled();
    }

    private Task StopIfCancelledAsync(BookImportJob job, CancellationToken ct) => StopIfCancelledAsync(db, job.Id, ct);

    private async Task ImportTestAsync(
        BookImportJob job, int? bookId, BookImportJobTest test, IReadOnlyDictionary<int, BookImportPage> pages, CancellationToken ct)
    {
        if (test.FileFrom is not { } from || test.FileTo is not { } to || to < from)
            throw new BookImportFailure("The test has no pages.");

        var key = test.AnswerKey;
        var label = SourceLabel(job, test);
        var flags = new FlagSet();
        var questionIds = new List<int>();
        int saved = 0, duplicates = 0, failedPages = 0, pageCount = 0;

        // Önce testin bütün sayfaları tespit edilir: numaralama testin toplamına bakar (NumberPages).
        var detected = new List<DetectedPage>();
        for (var filePage = from; filePage <= to; filePage++)
        {
            pageCount++;
            if (!pages.TryGetValue(filePage, out var page))
            {
                detected.Add(new DetectedPage(filePage, null, null, null, []));
                continue;
            }
            await StopIfCancelledAsync(job, ct);
            try
            {
                var imageBase64 = await LoadAsync(page);
                var detection = await DetectAsync(imageBase64, ct);
                var questions = BookQuestionLayout.MergeSplit(
                    BookQuestionLayout.Order(ToLayout(detection.Response.Result.Predictions), page.Width), page.Width, page.Height);
                var passages = BookQuestionLayout.AttachPassages(questions, ToStimuli(detection.Response.Result.Predictions), page.Width);
                detected.Add(new DetectedPage(filePage, page, imageBase64, questions, passages));
            }
            catch (Exception ex) when (ex is LabelingAiException or LabelingImportDetectException or LabelingValidationException
                                           or LabelingAiUnavailableException or BookImportFailure)
            {
                logger.LogWarning("Book import job {JobId}: detection failed on page {Page}: {Reason}", job.Id, filePage, ex.Message);
                detected.Add(new DetectedPage(filePage, page, null, null, []));
            }
        }

        var found = detected.Sum(d => d.Questions?.Count ?? 0);
        // #1052: hızlı eklemede anahtar yalnız bulunan sayı tutarsa uygulanır (sırayla eşlenir); tutmazsa sorular cevapsız kalır
        // ve sayı uyuşmazlığı işaretlenir, cevaplar incelemede girilir.
        var answers = job.Kind == BookImportJobKind.QuickAdd && key is not null && found != key.Length ? null : key;
        var numbering = NumberPages(
            detected.Select(d => (d.Page is null ? null : d.Page.MaxQuestionNumber, d.Questions?.Count)).ToList(), key?.Length);

        for (var p = 0; p < detected.Count; p++)
        {
            var (filePage, page, imageBase64, questions, passages) = detected[p];
            var (start, pageMax) = numbering[p];
            if (page is null || questions is null || imageBase64 is null)
            {
                flags.Add(BookImportReviewFlag.DetectionFailed, filePage);
                if (pageMax is { } m)
                    flags.Add(BookImportReviewFlag.MissingNumbers, filePage, Enumerable.Range(start, m - start + 1));
                failedPages++;
                continue;
            }
            var (width, height) = (page.Width, page.Height);

            if (pageMax is { } expectedMax)
            {
                var expected = expectedMax - start + 1;
                if (questions.Count < expected)
                    flags.Add(BookImportReviewFlag.MissingNumbers, filePage, Enumerable.Range(start + questions.Count, expected - questions.Count));
                else if (questions.Count > expected)
                    flags.Add(BookImportReviewFlag.ExtraQuestions, filePage, Enumerable.Range(expectedMax + 1, questions.Count - expected));
            }

            var toSave = new List<LabelingBankQuestionDto>();
            var savedIndexes = new Dictionary<int, int>();
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

                var answer = AnswerIndex(answers, number);
                if (answer >= prepared.Options.Count)
                    answer = -1;
                if (answer < 0)
                    flags.Add(BookImportReviewFlag.NoAnswer, filePage, [number]);
                savedIndexes[i] = toSave.Count;
                toSave.Add(ToBankQuestion(prepared, answer, label, number));
            }

            if (toSave.Count == 0)
                continue;
            try
            {
                // Ayrı scope (ayrı DbContext): toplu kaydın geri alınan varlıkları işin izleyicisinde kalmasın.
                await using var scope = scopes.CreateAsyncScope();
                var bank = scope.ServiceProvider.GetRequiredService<ILabelingBankService>();
                // #1025: yarıda kalmış çalıştırmanın taslakları tekrar sayılmaz, yeniden kullanılır.
                var result = await bank.SaveImportedPageAsync(new LabelingImportPageRequest(
                    imageBase64, bookId, filePage, job.GradeId, QuestionBankSharing.Private, toSave,
                    ResumeDrafts: true, ResumeWorksheetId: test.WorksheetId, Passages: ToImportPassages(passages, savedIndexes)),
                    job.CreateUserId!.Value, ct);
                saved += result.Saved + result.Resumed;
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
    /// süre anahtardan, öğretmen paylaşımı Private). Yeniden çalıştırmada var olan taslak test yeniden kullanılır ve sıra bu
    /// çalıştırmanınkine göre yeniden yazılır (#1025); test bağlantısı taslak testle aynı kayıtta yazılır.
    /// </summary>
    private async Task CreateDraftTestAsync(BookImportJob job, BookImportJobTest test, List<int> questionIds, CancellationToken ct)
    {
        var worksheet = test.WorksheetId is { } existingId
            ? await db.Worksheets.WithDrafts().AsTracking().Include(w => w.WorksheetQuestions)
                .FirstOrDefaultAsync(w => w.Id == existingId && w.IsDraft, ct)
            : null;
        if (worksheet is null)
        {
            worksheet = NewDraftTest(job, test);
            // Sahip denetim damgasıyla yazılır (iş isteksiz çalışır): taslağın sahibi işin sahibi.
            db.SetCurrentUser(job.CreateUserId!.Value);
            db.Worksheets.Add(worksheet);
        }

        var links = worksheet.WorksheetQuestions.Where(q => !q.IsDeleted).GroupBy(q => q.QuestionId)
            .ToDictionary(g => g.Key, g => g.First());
        var order = 0;
        foreach (var id in questionIds.Distinct())
        {
            if (links.Remove(id, out var link))
                link.Order = ++order;
            else
                worksheet.WorksheetQuestions.Add(new WorksheetQuestion { QuestionId = id, Order = ++order });
        }
        // Bu çalıştırmada görülmeyen eski bağlantılar sona.
        foreach (var link in links.Values.OrderBy(l => l.Order))
            link.Order = ++order;
        test.Worksheet = worksheet;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Testin taslak testi (yeni; #1023 işaretlemede düzeltme de kullanır).</summary>
    internal static Worksheet NewDraftTest(BookImportJob job, BookImportJobTest test)
    {
        var name = $"{job.BookName} – {BookImportService.BookTestName(job, test)}";
        return new Worksheet
        {
            Name = name.Length <= DraftTestNameLength ? name : name[..DraftTestNameLength],
            Description = job.Kind == BookImportJobKind.QuickAdd
                ? $"{job.BookName}. Fotoğraftan eklenen sorular."
                : $"{job.BookName}. Cevaplar kitabın cevap anahtarından.",
            GradeId = job.GradeId,
            SubjectId = job.SubjectId,
            MaxDurationSeconds = (test.Minutes ?? 0) * 60,
            BookTestId = test.BookTestId,
            TeacherSharing = WorksheetTeacherSharing.Private,
            IsDraft = true,
        };
    }

    /// <summary>Soruların etkinlik etiketi: testin adı (kaynak etiketi sınırına kırpılır).</summary>
    internal static string SourceLabel(BookImportJob job, BookImportJobTest test)
    {
        var label = BookImportService.BookTestName(job, test);
        return label.Length <= QuestionSource.MaxLabelLength ? label : label[..QuestionSource.MaxLabelLength];
    }

    private const int DraftTestNameLength = 200;

    /// <summary>Testin bir sayfasının tespiti; <c>Questions</c> null → sayfa görseli yok ya da tespit başarısız.</summary>
    private sealed record DetectedPage(int FilePage, BookImportPage? Page, string? ImageBase64, List<LayoutQuestion>? Questions,
        List<LayoutPassage> Passages);

    /// <summary>
    /// Sayfaların ilk soru no'su ve beklenen en büyük no'su. Numaralama sayfaya dayanır: kurulumda okunan basılı en büyük no
    /// biliniyorsa sayfa önceki sayfanın basılı en büyük no'sundan sonra başlar ve orada biter; tespiti başarısız sayfa numaralarını yine tüketir.
    /// Her sayfa tespit edildi ve bulunan toplam anahtarın uzunluğuna eşitse basılı numaralar yok sayılır, sorular sırayla
    /// numaralanır: kurulumda yanlış okunan bir sayfa no'su sonraki soruları kaydırıp cevapsız bırakmasın.
    /// </summary>
    /// <param name="pages">Sayfa başına basılı en büyük no (bilinmiyorsa null) ve bulunan soru sayısı (tespit başarısızsa null).</param>
    internal static IReadOnlyList<(int Start, int? Max)> NumberPages(
        IReadOnlyList<(int? PrintedMax, int? Found)> pages, int? keyLength)
    {
        var sequential = keyLength is { } n && pages.All(p => p.Found is not null) && pages.Sum(p => p.Found!.Value) == n;
        var result = new List<(int Start, int? Max)>(pages.Count);
        var last = 0;
        int? previousMax = null;
        foreach (var (printedMax, foundOnPage) in pages)
        {
            // Önceki sayfanın basılı no'su ile bu sayfanınki tutarlıysa sayfa oradan başlar: önceki sayfadaki fazla kutu
            // (ör. sorunun ayrı kutu sanılan kural satırı) sonraki sayfaların numaralarını ve cevaplarını kaydırmasın.
            var start = previousMax is { } pm && printedMax is { } pmax && pmax > pm ? pm + 1 : last + 1;
            var max = !sequential && printedMax is { } m && m >= start ? m : (int?)null;
            result.Add((start, max));
            last = foundOnPage is { } count ? Math.Max(max ?? 0, start + count - 1) : max ?? last;
            previousMax = max;
        }
        return result;
    }

    /// <summary>
    /// Issue #1024: önce bütçe ve tavan (doluysa <see cref="BookImportPause"/>; önbellekteki sayfa da bekler, devamda ücretsizdir),
    /// sonra tespit; ödenen tutar testin maliyetine yazılır.
    /// </summary>
    private async Task<LabelingImportDetection> DetectAsync(string imageBase64, CancellationToken ct)
    {
        await budget.EnsureAsync(ct);
        try
        {
            var detection = await labeling.DetectForImportAsync(imageBase64, detectModel, ct);
            budget.Record(detectModel, detection.Usage, detection.CostUsd);
            return detection;
        }
        catch (LabelingImportDetectException failed)
        {
            // Kullanılamayan yanıtın token'ı da ödendi.
            budget.Record(detectModel, failed.Usage, failed.CostUsd);
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

    /// <summary>Issue #1062: sorudan ayrı çerçevedeki uyaranlar; soruyla örtüşmeyenler pasaj olur.</summary>
    internal static IEnumerable<LayoutStimulus> ToStimuli(PredictionListDto predictions) =>
        (predictions.Predictions ?? [])
        .Where(p => p.Kind == LabelingBlocks.InfoBox)
        .Select(p => new LayoutStimulus(Box(p), p.SharedQuestions ?? 1));

    /// <summary>Pasajların soru sıraları kaydedilecek listeye çevrilir; sorusu kaydedilmeyen pasaj düşer.</summary>
    private static List<LabelingImportPassage> ToImportPassages(List<LayoutPassage> passages, Dictionary<int, int> savedIndexes) =>
        passages.Select(p => new LabelingImportPassage(
                new LabelingBankBoxDto { X = p.Box.X, Y = p.Box.Y, Width = p.Box.Width, Height = p.Box.Height },
                p.QuestionIndexes.Where(savedIndexes.ContainsKey).Select(i => savedIndexes[i]).ToList()))
            .Where(p => p.QuestionIndexes.Count > 0)
            .ToList();

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
        // Kitap sayfaları WebP, hızlı ekleme fotoğrafları JPEG (#1052).
        var mime = page.ImageUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/webp";
        return $"data:{mime};base64," + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Truncate(string message) =>
        message.Length <= BookImportJob.MaxMessageLength ? message : message[..BookImportJob.MaxMessageLength];

    /// <summary>Aynı kodlu işaretleri birleştirir: sayfa ve numara listeleri sıralı ve tekil.</summary>
    private sealed class FlagSet
    {
        private readonly Dictionary<string, (SortedSet<int> Pages, SortedSet<int> Numbers, int? Found, int? Expected)> _flags = new();
        private readonly Dictionary<string, SortedDictionary<int, SortedSet<int>>> _byPage = new();
        private readonly List<string> _order = [];

        public bool IsEmpty => _flags.Count == 0;

        public void Add(string code, int page, IEnumerable<int>? numbers = null)
        {
            var flag = Get(code);
            flag.Pages.Add(page);
            var byPage = _byPage.TryGetValue(code, out var existing) ? existing : _byPage[code] = new();
            var onPage = byPage.TryGetValue(page, out var set) ? set : byPage[page] = [];
            if (numbers is not null)
            {
                var list = numbers.ToList();
                flag.Numbers.UnionWith(list);
                onPage.UnionWith(list);
            }
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
            var byPage = _byPage.TryGetValue(code, out var pages)
                ? pages.ToDictionary(p => p.Key, p => (IReadOnlyList<int>)p.Value.ToList())
                : null;
            return new BookImportReviewFlag(code, f.Pages.ToList(), f.Numbers.Count == 0 ? null : f.Numbers.ToList(), f.Found, f.Expected,
                byPage);
        }).ToList();
    }
}
