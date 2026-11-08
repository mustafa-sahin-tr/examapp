using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Support;

/// <summary>
/// Issue #1021: ağa çıkmayan kurulum yapay zekâsı. Varsayılan: her sayfa tek bir testin (1-01) sayfası, anahtar yok. Testler
/// <see cref="Pages"/> / <see cref="Keys"/> ile kitabı tarif eder; her çağrı sabit bir maliyet ekler.
/// </summary>
public sealed class FakeBookSetupAi : IBookSetupAi
{
    public static readonly LabelingAiModelOption Model = new() { Id = "fake", DisplayName = "Fake", InputUsdPerMTok = 1m, OutputUsdPerMTok = 1m };

    /// <summary>Dosya sayfası → tarama sonucu; tanımsız sayfa için <see cref="DefaultPage"/>.</summary>
    public Dictionary<int, SurveyedPage> Pages { get; } = new();

    public List<SurveyedTocRow> Toc { get; } = new();

    /// <summary>Anahtar sayfası → (tam sayfa okuması, döşemeli okuma).</summary>
    public Dictionary<int, (IReadOnlyList<AnswerKeyRow> First, IReadOnlyList<AnswerKeyRow> Second)> Keys { get; } = new();

    public Dictionary<int, int?> UnitTopics { get; } = new();

    public bool IsConfigured { get; set; } = true;

    /// <summary>Doluysa tarama bu hatayla düşer (maliyet yine eklenir).</summary>
    public string? SurveyFailure { get; set; }

    public List<IReadOnlyList<int>> SurveyBatches { get; } = new();
    public List<(int Page, bool Tiled)> KeyReads { get; } = new();

    public static SurveyedPage DefaultPage(int filePage) =>
        new(filePage, BookPageKind.Test, filePage, filePage == 1 ? 1 : null, filePage == 1 ? 1 : null, filePage == 1 ? "Test" : null);

    public static AnswerKeyRow Row(int unit, int testNo, string answers) =>
        new(unit, testNo, answers.Select((c, i) => (Number: i + 1, Answer: c)).Where(x => x.Answer != ' ')
            .ToDictionary(x => x.Number, x => x.Answer));

    public Task<SurveyResult> SurveyAsync(IReadOnlyList<BookPageImage> pages, BookSetupCost cost, CancellationToken ct)
    {
        SurveyBatches.Add(pages.Select(p => p.FilePage).ToList());
        cost.Add(Model, new LabelingAiUsage(1000, 100));
        if (SurveyFailure is { } failure)
            throw new BookImportFailure(failure);
        var surveyed = pages.Select(p => Pages.GetValueOrDefault(p.FilePage) ?? DefaultPage(p.FilePage)).ToList();
        return Task.FromResult(new SurveyResult(surveyed, SurveyBatches.Count == 1 ? Toc : []));
    }

    public Task<IReadOnlyList<AnswerKeyRow>> ReadAnswerKeyAsync(BookPageImage page, bool tiled, BookSetupCost cost, CancellationToken ct)
    {
        KeyReads.Add((page.FilePage, tiled));
        cost.Add(Model, new LabelingAiUsage(2000, 500));
        var reads = Keys.GetValueOrDefault(page.FilePage);
        return Task.FromResult(tiled ? reads.Second ?? [] : reads.First ?? []);
    }

    public Task<IReadOnlyDictionary<int, int?>> MapUnitsAsync(
        IReadOnlyList<PlannedUnit> units, IReadOnlyList<TopicCandidate> topics, BookSetupCost cost, CancellationToken ct)
    {
        cost.Add(Model, new LabelingAiUsage(100, 10));
        IReadOnlyDictionary<int, int?> map = units.ToDictionary(u => u.Unit, u => UnitTopics.GetValueOrDefault(u.Unit));
        return Task.FromResult(map);
    }
}
