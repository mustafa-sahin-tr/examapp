using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Bir PDF sayfasının görseli (#1020'de üretilen WebP).</summary>
public sealed record BookPageImage(int FilePage, byte[] Image);

/// <summary>
/// Kurulumda harcanan tahmini Claude maliyeti; başarısız (ama yanıt almış) çağrılar da eklenir. İşte çalışırken (#1024) her çağrı
/// öncesi <see cref="EnsureAsync"/> bütçeyi denetler ve her çağrı işin maliyet satırlarına (<see cref="BookImportBudget"/>) yazılır.
/// </summary>
public sealed class BookSetupCost
{
    internal BookImportBudget? Budget { get; init; }

    public decimal Usd { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }

    public void Add(LabelingAiModelOption model, LabelingAiUsage usage)
    {
        InputTokens += usage.Input;
        OutputTokens += usage.Output;
        Usd += LabelingAiQuota.Cost(model, usage.Input, usage.Output);
        Budget?.Record(model, usage);
    }

    /// <summary>Canlı çağrıdan önce: bütçe ya da işin tavanı doluysa <see cref="BookImportPause"/>.</summary>
    public Task EnsureAsync(CancellationToken ct) => Budget?.EnsureAsync(ct) ?? Task.CompletedTask;
}

public sealed record SurveyResult(IReadOnlyList<SurveyedPage> Pages, IReadOnlyList<SurveyedTocRow> Toc);

/// <summary>Ünite eşlemesi için konu adayı.</summary>
public sealed record TopicCandidate(int Id, string Name);

/// <summary>
/// Issue #1021: kitap kurulumunun Claude çağrıları. Kurulum kuralları <see cref="BookSetupPlanner"/>'da (saf); bu arayüz
/// yalnız görseli okur, testler sahtesini verir.
/// </summary>
public interface IBookSetupAi
{
    bool IsConfigured { get; }

    /// <summary>Sayfa toplu taraması (ucuz model): tür, basılı sayfa, test başlığı, içindekiler satırları.</summary>
    Task<SurveyResult> SurveyAsync(IReadOnlyList<BookPageImage> pages, BookSetupCost cost, CancellationToken ct);

    /// <summary>
    /// Cevap anahtarı sayfasını okur. <paramref name="tiled"/>: sayfa iki örtüşen yarıya bölünür (ikinci, bağımsız okuma — ayrıntı
    /// daha büyük görünür). <paramref name="strong"/>: iki ucuz okuma çelişince hakem okuması güçlü modelle (#1122).
    /// </summary>
    Task<IReadOnlyList<AnswerKeyRow>> ReadAnswerKeyAsync(BookPageImage page, bool tiled, bool strong, BookSetupCost cost, CancellationToken ct);

    /// <summary>Ünite başlıklarını dersin konularıyla eşler (ucuz model, görselsiz); eşleşmeyen ünite null.</summary>
    Task<IReadOnlyDictionary<int, int?>> MapUnitsAsync(
        IReadOnlyList<PlannedUnit> units, IReadOnlyList<TopicCandidate> topics, BookSetupCost cost, CancellationToken ct);
}

/// <summary>
/// <see cref="IBookSetupAi"/>'nin Claude uygulaması. Modeller <c>BookImport:SurveyModel</c> (varsayılan Haiku),
/// <c>BookImport:AnswerKeyModel</c> (anahtarın iki okuması, varsayılan Haiku) ve <c>BookImport:AnswerKeyTieBreakModel</c> (okumalar
/// çelişince hakem, varsayılan Sonnet; #1122 öncesi iki okuma da Sonnet'ti); hepsi <c>Anthropic:Models</c> listesinde olmalı (fiyat oradan).
/// Sistem mesajı, şemalar ya da görsel boyutları değişince <see cref="SchemaRevision"/> artırılır (kurulum özetine yazılır).
/// </summary>
public sealed class ClaudeBookSetupAi : IBookSetupAi, IDisposable
{
    /// <summary>1: ilk sürüm (#1021). 2: test başlığı olmayan bölümlü kitaplar ve test numarasız anahtarlar (#1122).</summary>
    public const int SchemaRevision = 2;

    public const string DefaultSurveyModel = "claude-haiku-5-5";
    public const string DefaultAnswerKeyModel = "claude-haiku-5-5";
    public const string DefaultAnswerKeyTieBreakModel = "claude-sonnet-5-5";

    // Tarama görseli küçük tutulur: başlık, sayfa no ve soru numaraları okunur; ayrıntı gerekmez.
    private const int SurveyImageEdge = 1000;
    private const int KeyImageEdge = 1568;
    // Döşemeler sayfanın üst ve alt %55'i: ortadaki satır iki döşemede de görünür (planlayıcı birleştirir).
    private const double TileShare = 0.55;
    private const int SurveyOutputTokensPerPage = 400;
    private const int KeyMaxOutputTokens = 16000;
    private const int MapMaxOutputTokens = 2000;

    private const string SurveySystemPrompt =
        "You catalogue the pages of scanned Turkish school question banks (soru bankası) so that the book can be imported. " +
        "Report only what is printed on each page; never guess values that are not visible.";

    private const string SurveyInstruction =
        "Each image above is one page; the text before it gives its file page number. For every page return: " +
        "kind (cover, toc, test, answerKey, selfAssessment or other); printedPage, the page number printed on the page " +
        "(null if none); for test pages the test header if this page shows one: unit (the section number such as " +
        "\"1. Bölüm\" or \"1. Ünite\"), testNo (\"Test 01\" → 1), title (the topic name next to the test number) and " +
        "minutes (\"16 dk.\" → 16); maxQuestionNumber, the highest question number printed on the page. A test page " +
        "without its own header still has kind test. Some books (e.g. LGS workbooks) have no test headers at all: the question " +
        "pages of a unit or of an exam section are kind test with unit and testNo null, and the title page of a unit or " +
        "section (\"1. Ünite\", an exam year cover) is kind cover. answerKey is the answer key table (\"Cevap Anahtarı\"); " +
        "selfAssessment is a scoring or self-evaluation page; toc is the table of contents (\"İçindekiler\"). For toc " +
        "pages also list the rows in tocRows: unit number, title and printed start page.";

    private const string KeySystemPrompt =
        "You transcribe answer key tables (Cevap Anahtarı) of Turkish school question banks exactly as printed. " +
        "Every answer is one letter A, B, C, D or E. Never infer or complete an answer that is not printed; " +
        "use \"?\" for a cell you cannot read.";

    private const string KeyInstruction =
        "Transcribe every row of the answer key table(s). A row belongs to one test (\"Test 01\" → testNo 1) inside a " +
        "section heading such as \"1. Bölüm\" or \"1. Ünite\" (unit 1); use unit 1 when the key has no sections. " +
        "Cells look like \"3. B\": number 3, answer B. Skip empty cells. Rows cut off at the image edge are skipped. " +
        "Keys without test numbers: a block per unit (\"1. Ünite\" beside it) is one row with that unit and testNo 1; " +
        "a single numbered list is one row with unit 1 and testNo 1.";

    private const string MapSystemPrompt =
        "You match the units of a Turkish school question bank to curriculum topics. Answer only with ids from the list.";

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeBookSetupAi> _logger;
    private readonly string _surveyModelId;
    private readonly string _keyModelId;
    private readonly string _tieBreakModelId;

    public ClaudeBookSetupAi(IOptions<LabelingAiOptions> options, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeBookSetupAi>();
        _chat = LabelingClaude.CreateClient(_options, loggerFactory);
        _surveyModelId = configuration["BookImport:SurveyModel"] is { Length: > 0 } survey ? survey : DefaultSurveyModel;
        _keyModelId = configuration["BookImport:AnswerKeyModel"] is { Length: > 0 } key ? key : DefaultAnswerKeyModel;
        _tieBreakModelId = configuration["BookImport:AnswerKeyTieBreakModel"] is { Length: > 0 } tie ? tie : DefaultAnswerKeyTieBreakModel;
    }

    public bool IsConfigured => _chat is not null;

    public async Task<SurveyResult> SurveyAsync(IReadOnlyList<BookPageImage> pages, BookSetupCost cost, CancellationToken ct)
    {
        var content = new List<AIContent>();
        foreach (var page in pages)
        {
            content.Add(new TextContent($"File page {page.FilePage}:"));
            content.Add(new DataContent(Encode(page.Image, SurveyImageEdge, null), "image/jpeg"));
        }

        content.Add(new TextContent(SurveyInstruction));
        var model = Model(_surveyModelId);
        var batch = await AskAsync<SurveyBatch>(model, SurveyOutputTokensPerPage * pages.Count + 1000, SurveySystemPrompt, content, cost, ct);

        var asked = pages.Select(p => p.FilePage).ToHashSet();
        var surveyed = (batch.Pages ?? [])
            .Where(p => p is not null && asked.Contains(p.FilePage))
            .GroupBy(p => p.FilePage)
            .Select(g => g.First())
            .Select(p => new SurveyedPage(p.FilePage, ParseKind(p.Kind), Positive(p.PrintedPage), Positive(p.Unit), Positive(p.TestNo),
                p.Title, Positive(p.Minutes), Positive(p.MaxQuestionNumber)))
            .ToList();
        var toc = (batch.TocRows ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Title))
            .Select(r => new SurveyedTocRow(Positive(r.Unit), r.Title.Trim(), Positive(r.PrintedPage)))
            .ToList();
        return new SurveyResult(surveyed, toc);
    }

    public async Task<IReadOnlyList<AnswerKeyRow>> ReadAnswerKeyAsync(BookPageImage page, bool tiled, bool strong, BookSetupCost cost,
        CancellationToken ct)
    {
        var model = Model(strong ? _tieBreakModelId : _keyModelId);
        var regions = tiled ? new[] { (Top: 0d, Share: TileShare), (Top: 1 - TileShare, Share: TileShare) } : [(0d, 1d)];
        var rows = new List<AnswerKeyRow>();
        foreach (var (top, share) in regions)
        {
            var content = new List<AIContent>
            {
                new DataContent(Encode(page.Image, KeyImageEdge, (top, share)), "image/jpeg"),
                new TextContent(KeyInstruction),
            };
            var table = await AskAsync<KeyTable>(model, KeyMaxOutputTokens, KeySystemPrompt, content, cost, ct);
            rows.AddRange((table.Rows ?? []).Where(r => r is not null).Select(ToRow));
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<int, int?>> MapUnitsAsync(
        IReadOnlyList<PlannedUnit> units, IReadOnlyList<TopicCandidate> topics, BookSetupCost cost, CancellationToken ct)
    {
        var result = units.ToDictionary(u => u.Unit, _ => (int?)null);
        if (units.Count == 0 || topics.Count == 0 || units.All(u => u.Title is null))
            return result;

        var instruction =
            "Topics (id: name):\n" + string.Join("\n", topics.Select(t => $"{t.Id}: {t.Name}")) +
            "\n\nUnits (number: title):\n" + string.Join("\n", units.Where(u => u.Title is not null).Select(u => $"{u.Unit}: {u.Title}")) +
            "\n\nFor every unit give the id of the topic it mostly covers, or null when no topic fits.";
        var model = Model(_surveyModelId);
        var (mapping, _) = await Call(() => LabelingClaude.AskTextAsync<UnitTopics>(
            _chat!, _options, _logger, model, MapMaxOutputTokens, MapSystemPrompt, instruction, ct), model, cost, ct);

        var known = topics.Select(t => t.Id).ToHashSet();
        foreach (var match in mapping.Units ?? [])
        {
            if (match is not null && result.ContainsKey(match.Unit) && match.TopicId is { } id && known.Contains(id))
                result[match.Unit] = id;
        }

        return result;
    }

    public void Dispose() => _chat?.Dispose();

    private async Task<T> AskAsync<T>(LabelingAiModelOption model, int maxOutputTokens, string system, List<AIContent> content,
        BookSetupCost cost, CancellationToken ct)
    {
        var (result, _) = await Call(() => LabelingClaude.AskContentAsync<T>(
            _chat!, _options, _logger, model, maxOutputTokens, system, content, ct), model, cost, ct);
        return result;
    }

    private async Task<(T Result, LabelingAiUsage Usage)> Call<T>(
        Func<Task<(T, LabelingAiUsage)>> call, LabelingAiModelOption model, BookSetupCost cost, CancellationToken ct)
    {
        if (_chat is null)
            throw new BookImportFailure("Claude is not configured (Anthropic:ApiKey is empty).");
        await cost.EnsureAsync(ct);
        try
        {
            var (result, usage) = await call();
            cost.Add(model, usage);
            return (result, usage);
        }
        catch (LabelingAiException ex)
        {
            if (ex.Usage is { } usage)
                cost.Add(model, usage);
            throw new BookImportFailure($"Book setup failed: {ex.Message}");
        }
    }

    private LabelingAiModelOption Model(string id) =>
        _options.Models.FirstOrDefault(m => m.Id == id)
        ?? throw new BookImportFailure($"Model {id} is not listed in Anthropic:Models.");

    /// <summary>WebP sayfa → JPEG; isteğe bağlı dikey dilim (üstten oran, yükseklik oranı). Uzun kenar <paramref name="maxEdge"/>.</summary>
    internal static byte[] Encode(byte[] image, int maxEdge, (double Top, double Share)? slice)
    {
        using var bitmap = SkiaImaging.DecodeOriented(image, out _);
        var top = slice is { } s ? (int)Math.Round(bitmap.Height * s.Top) : 0;
        var height = slice is { } t ? Math.Min(bitmap.Height - top, (int)Math.Round(bitmap.Height * t.Share)) : bitmap.Height;
        var rect = SKRectI.Create(0, top, bitmap.Width, height);
        var scale = Math.Max(rect.Width, rect.Height) > maxEdge ? (double)Math.Max(rect.Width, rect.Height) / maxEdge : 1d;
        using var resized = SkiaImaging.CropResize(bitmap, rect,
            Math.Max(1, (int)Math.Round(rect.Width / scale)), Math.Max(1, (int)Math.Round(rect.Height / scale)));
        return SkiaImaging.Encode(resized, SKEncodedImageFormat.Jpeg, 85);
    }

    internal static BookPageKind ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "cover" => BookPageKind.Cover,
        "toc" => BookPageKind.Toc,
        "test" => BookPageKind.Test,
        "answerkey" => BookPageKind.AnswerKey,
        "selfassessment" => BookPageKind.SelfAssessment,
        _ => BookPageKind.Other,
    };

    private static AnswerKeyRow ToRow(KeyTableRow row)
    {
        var answers = new Dictionary<int, char>();
        foreach (var cell in row.Answers ?? [])
        {
            if (cell is null || cell.Number is < 1 or > BookSetupPlanner.MaxQuestionsPerTest)
                continue;
            var letter = cell.Answer?.Trim().ToUpperInvariant() is { Length: 1 } s && s[0] is >= 'A' and <= 'E' ? s[0] : '?';
            answers.TryAdd(cell.Number, letter);
        }

        return new AnswerKeyRow(row.Unit is > 0 ? row.Unit : 1, row.TestNo, answers);
    }

    private static int? Positive(int? value) => value is > 0 ? value : null;

    internal sealed record SurveyBatch(
        [property: Description("One entry per page image, in the given order.")] List<SurveyPage> Pages,
        [property: Description("Table of contents rows from toc pages; empty when no toc page was given.")] List<TocRow> TocRows);

    internal sealed record SurveyPage(
        [property: Description("The file page number written before the image.")] int FilePage,
        [property: Description("One of: cover, toc, test, answerKey, selfAssessment, other.")] string Kind,
        [property: Description("Page number printed on the page; null when none is printed.")] int? PrintedPage,
        [property: Description("Unit/section number from the test header on this page; null when the page has no test header.")] int? Unit,
        [property: Description("Test number from the test header on this page; null when the page has no test header.")] int? TestNo,
        [property: Description("Test title from the header; null when none.")] string? Title,
        [property: Description("Suggested minutes from the header; null when none.")] int? Minutes,
        [property: Description("Highest question number printed on this page; null when the page has no questions.")] int? MaxQuestionNumber);

    internal sealed record TocRow(
        [property: Description("Unit/section number; null when the row has none.")] int? Unit,
        string Title,
        [property: Description("Printed start page; null when none.")] int? PrintedPage);

    internal sealed record KeyTable(List<KeyTableRow> Rows);

    internal sealed record KeyTableRow(int Unit, int TestNo, List<KeyCell> Answers);

    internal sealed record KeyCell(int Number, [property: Description("A, B, C, D, E or ? when unreadable.")] string Answer);

    internal sealed record UnitTopics(List<UnitTopic> Units);

    internal sealed record UnitTopic(int Unit, [property: Description("Topic id from the list, or null.")] int? TopicId);
}
