using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Sayfa taramasının sayfa türü (#1021). Claude dizgi olarak döner; tanınmayan değer <see cref="Other"/>.</summary>
public enum BookPageKind
{
    Other,
    Cover,
    Toc,
    Test,
    AnswerKey,
    SelfAssessment,
}

/// <summary>Bir sayfanın tarama sonucu: tür, basılı sayfa no ve (test sayfasıysa) test başlığı.</summary>
public sealed record SurveyedPage(
    int FilePage,
    BookPageKind Kind,
    int? PrintedPage = null,
    int? Unit = null,
    int? TestNo = null,
    string? TestTitle = null,
    int? Minutes = null,
    int? MaxQuestionNumber = null);

/// <summary>İçindekiler satırı: ünite no (varsa), başlık, basılı başlangıç sayfası.</summary>
public sealed record SurveyedTocRow(int? Unit, string Title, int? PrintedPage);

/// <summary>
/// Cevap anahtarındaki bir test satırı: soru no → harf (A–E; okunamayan "?"). <paramref name="KeyPage"/>: okunduğu dosya sayfası
/// (#1122: test numarasız anahtar, sayfadaki yerine göre önündeki bölüme bağlanır); bilinmiyorsa 0.
/// </summary>
public sealed record AnswerKeyRow(int Unit, int TestNo, IReadOnlyDictionary<int, char> Answers, int KeyPage = 0);

/// <summary>Planlanan bir test (onay ekranındaki tablo satırı).</summary>
public sealed record PlannedTest(
    int Order,
    int Unit,
    int TestNo,
    string? Title,
    int? Minutes,
    int FileFrom,
    int FileTo,
    int? ExpectedQuestions,
    string? AnswerKey,
    string? KeyConflicts,
    IReadOnlyList<string> Flags);

/// <summary>Ünite: içindekiler başlığı ve testlerinin kapsadığı dosya sayfaları (kitap kaydındaki içindekiler satırı).</summary>
public sealed record PlannedUnit(int Unit, string? Title, int FileFrom, int FileTo);

public sealed record BookSetupPlan(
    int? Offset,
    IReadOnlyList<(int From, int To)> AnswerKeyRanges,
    IReadOnlyList<int> SelfAssessmentPages,
    IReadOnlyList<PlannedUnit> Units,
    IReadOnlyList<PlannedTest> Tests,
    IReadOnlyList<string> OrphanKeys)
{
    public bool NoAnswerKey => AnswerKeyRanges.Count == 0 || Tests.All(t => t.AnswerKey is null);
}

/// <summary>
/// Issue #1021: sayfa taraması ve iki bağımsız anahtar okumasından kitap kurulumunu çıkarır. Saf fonksiyon (Claude yok);
/// kurulum kuralları burada test edilir.
/// <list type="bullet">
/// <item>Test: başlığı (ünite + test no) olan test sayfasıyla başlar; başlıksız ardışık test sayfaları ona eklenir; test
/// olmayan sayfa testi kapatır. Aynı başlık yeniden görülürse (ör. bölüm sonu tekrarı) yeni test açılmaz.</item>
/// <item>Ofset: basılı = dosya + ofset; basılı sayfa okunan sayfalarda en sık fark (#577 kitap kaydıyla aynı yön).</item>
/// <item>Bölümlü kitap (#1122; ör. MEB LGS çalışma kitabı): hiçbir sayfada test başlığı yoksa her ünite / sınav bölümü tek test
/// olur. Bölüm kapak, içindekiler ya da anahtar sayfasında ve soru numarası başa dönünce kapanır; başlık, bölümün ilk sayfasından
/// önce başlayan son içindekiler satırıdır. Test numarası olmayan anahtar sayfası, önceki anahtardan bu yana biten bölümlere
/// bağlanır: tek bölüm varsa sayfanın tüm satırları onun, birden çoksa satırın ünite no'su o bölümlerin sırasıdır.</item>
/// <item>Anahtar: iki okuma soru soru karşılaştırılır; farklıysa (biri boş da olsa) ilk okuma alınır, diğeri
/// <see cref="PlannedTest.KeyConflicts"/>'ta tutulur ("." uyumlu, harf diğer okuma, "-" diğer okumada yok).</item>
/// </list>
/// </summary>
public static class BookSetupPlanner
{
    public const string FlagNoKey = "noKey";
    public const string FlagKeyMismatch = "keyMismatch";
    public const string FlagKeyConflicts = "keyConflicts";

    /// <summary>Bir testteki en fazla soru (anahtar dizgisinin üst sınırı); bölümlü kitapta bir ünite tek testtir (#1122).</summary>
    public const int MaxQuestionsPerTest = Data.BookImportJobTest.MaxKeyLength;

    public static BookSetupPlan Plan(
        IReadOnlyList<SurveyedPage> pages,
        IReadOnlyList<SurveyedTocRow> toc,
        IReadOnlyList<AnswerKeyRow> firstRead,
        IReadOnlyList<AnswerKeyRow> secondRead)
    {
        var ordered = pages.OrderBy(p => p.FilePage).ToList();
        var keyRanges = Ranges(ordered.Where(p => p.Kind == BookPageKind.AnswerKey).Select(p => p.FilePage));
        var selfAssessment = ordered.Where(p => p.Kind == BookPageKind.SelfAssessment).Select(p => p.FilePage).ToList();

        var drafts = CollectTests(ordered);
        var sectioned = drafts.Count == 0;
        Dictionary<(int Unit, int TestNo), AnswerKeyRow> first, second;
        var unmatched = new List<string>();
        if (sectioned)
        {
            drafts = CollectSections(ordered, toc, keyRanges);
            first = SectionKeys(drafts, firstRead, keyRanges, unmatched);
            second = SectionKeys(drafts, secondRead, keyRanges, unmatched);
        }
        else
        {
            first = ByTest(firstRead);
            second = ByTest(secondRead);
        }

        var tests = new List<PlannedTest>();
        foreach (var draft in drafts)
        {
            first.TryGetValue((draft.Unit, draft.TestNo), out var a);
            second.TryGetValue((draft.Unit, draft.TestNo), out var b);
            var (key, conflicts) = MergeKey(a, b);
            var expected = key?.Length ?? draft.MaxQuestion;

            var flags = new List<string>();
            if (key is null && keyRanges.Count > 0)
                flags.Add(FlagNoKey);
            if (key is not null && draft.MaxQuestion is { } seen && seen != key.Length)
                flags.Add(FlagKeyMismatch);
            if (conflicts is not null && conflicts.Any(c => c != '.'))
                flags.Add(FlagKeyConflicts);

            tests.Add(new PlannedTest(tests.Count + 1, draft.Unit, draft.TestNo, draft.Title, draft.Minutes, draft.From, draft.To,
                expected, key, conflicts, flags));
        }

        var planned = tests.Select(t => (t.Unit, t.TestNo)).ToHashSet();
        var orphans = first.Keys.Concat(second.Keys).Distinct()
            .Where(k => !planned.Contains(k))
            .OrderBy(k => k.Unit).ThenBy(k => k.TestNo)
            .Select(k => $"{k.Unit}-{k.TestNo:D2}")
            .Concat(unmatched.Distinct())
            .ToList();

        var units = tests.GroupBy(t => t.Unit)
            .OrderBy(g => g.Key)
            .Select(g => new PlannedUnit(g.Key, sectioned ? g.First().Title : UnitTitle(g.Key, toc), g.Min(t => t.FileFrom),
                g.Max(t => t.FileTo)))
            .ToList();

        return new BookSetupPlan(Offset(ordered), keyRanges, selfAssessment, units, tests, orphans);
    }

    /// <summary>İki okumayı soru soru birleştirir. İkisi de yoksa (null, null).</summary>
    internal static (string? Key, string? Conflicts) MergeKey(AnswerKeyRow? first, AnswerKeyRow? second)
    {
        if (first is null && second is null)
            return (null, null);

        var count = Math.Min(MaxQuestionsPerTest, Math.Max(MaxNumber(first), MaxNumber(second)));
        if (count == 0)
            return (null, null);

        var key = new StringBuilder(count);
        var conflicts = new StringBuilder(count);
        for (var n = 1; n <= count; n++)
        {
            char? a = first is not null && first.Answers.TryGetValue(n, out var x) ? x : null;
            char? b = second is not null && second.Answers.TryGetValue(n, out var y) ? y : null;
            // Satır yalnız bir okumada varsa karşılaştırma yapılamaz (uyumlu sayılır). İşaret, seçilmeyen okumanın değeridir.
            key.Append(a ?? b ?? '?');
            conflicts.Append(first is null || second is null || a == b ? '.' : a is null ? '-' : b ?? '-');
        }

        return (key.ToString(), conflicts.ToString());
    }

    private static int MaxNumber(AnswerKeyRow? row) => row is null || row.Answers.Count == 0 ? 0 : row.Answers.Keys.Max();

    private static Dictionary<(int Unit, int TestNo), AnswerKeyRow> ByTest(IReadOnlyList<AnswerKeyRow> rows)
    {
        // Döşemeler arasında aynı satır iki kez okunabilir: cevapları birleştirilir, ilk okunan kazanır.
        var map = new Dictionary<(int, int), AnswerKeyRow>();
        foreach (var row in rows)
        {
            if (row.TestNo < 1 || row.Unit < 1)
                continue;
            var k = (row.Unit, row.TestNo);
            if (map.TryGetValue(k, out var existing))
            {
                map[k] = existing with { Answers = MergeAnswers(existing.Answers, row.Answers) };
            }
            else
            {
                map[k] = row;
            }
        }

        return map;
    }

    private sealed class TestDraft(int unit, int testNo, int from)
    {
        public int Unit { get; } = unit;
        public int TestNo { get; } = testNo;
        public int From { get; } = from;
        public int To { get; set; } = from;
        public string? Title { get; set; }
        public int? Minutes { get; set; }
        public int? MaxQuestion { get; set; }
    }

    private static List<TestDraft> CollectTests(List<SurveyedPage> pages)
    {
        var tests = new List<TestDraft>();
        var seen = new HashSet<(int, int)>();
        TestDraft? current = null;
        foreach (var page in pages)
        {
            if (page.Kind != BookPageKind.Test)
            {
                current = null;
                continue;
            }

            if (page.TestNo is { } testNo and > 0)
            {
                var unit = page.Unit is > 0 ? page.Unit.Value : 1;
                if (current is null || current.Unit != unit || current.TestNo != testNo)
                {
                    if (!seen.Add((unit, testNo)))
                    {
                        current = null;
                        continue;
                    }

                    current = new TestDraft(unit, testNo, page.FilePage);
                    tests.Add(current);
                }
            }

            if (current is null)
                continue;
            current.To = page.FilePage;
            current.Title ??= Clean(page.TestTitle);
            current.Minutes ??= page.Minutes is > 0 and <= 600 ? page.Minutes : null;
            if (page.MaxQuestionNumber is > 0 and <= MaxQuestionsPerTest)
                current.MaxQuestion = Math.Max(current.MaxQuestion ?? 0, page.MaxQuestionNumber.Value);
        }

        return tests;
    }

    /// <summary>
    /// #1122: test başlığı olmayan kitapta bölümler. Kapak / içindekiler / anahtar sayfası bölümü kapatır; arada kalan "diğer" sayfa
    /// (görsel ağırlıklı soru sayfası yanlış sınıflanabilir) kapatmaz. Soru numarası yarıdan aşağı düşerse yeni bölüm başlar.
    /// </summary>
    private static List<TestDraft> CollectSections(List<SurveyedPage> pages, IReadOnlyList<SurveyedTocRow> toc,
        IReadOnlyList<(int From, int To)> keyRanges)
    {
        var sections = new List<TestDraft>();
        TestDraft? current = null;
        foreach (var page in pages)
        {
            if (page.Kind is BookPageKind.Cover or BookPageKind.Toc or BookPageKind.AnswerKey or BookPageKind.SelfAssessment)
            {
                current = null;
                continue;
            }

            if (page.Kind != BookPageKind.Test)
                continue;
            var max = page.MaxQuestionNumber is > 0 and <= MaxQuestionsPerTest ? page.MaxQuestionNumber : null;
            if (current is { MaxQuestion: >= 5 and var seen } && max is { } n && n < seen / 2)
                current = null;
            if (current is null)
            {
                current = new TestDraft(sections.Count + 1, 1, page.FilePage);
                sections.Add(current);
            }

            current.To = page.FilePage;
            if (max is { } m)
                current.MaxQuestion = Math.Max(current.MaxQuestion ?? 0, m);
        }

        var offset = Offset(pages) ?? 0;
        var rows = toc.Where(r => r.PrintedPage is > 0 && !keyRanges.Any(k => r.PrintedPage - offset >= k.From && r.PrintedPage - offset <= k.To))
            .OrderBy(r => r.PrintedPage)
            .ToList();
        foreach (var section in sections)
        {
            var row = rows.LastOrDefault(r => r.PrintedPage <= section.From + offset);
            section.Title = row is null ? null : Clean(row.Unit is { } unit ? $"{unit}. Ünite: {row.Title}" : row.Title);
        }

        return sections;
    }

    /// <summary>
    /// #1122: test numarasız anahtar satırlarını bölümlere bağlar (anahtar = önceki anahtar aralığından bu yana biten bölümler).
    /// Bağlanamayan satır <paramref name="unmatched"/>'e "s{sayfa}-{ünite}" olarak eklenir.
    /// </summary>
    private static Dictionary<(int Unit, int TestNo), AnswerKeyRow> SectionKeys(IReadOnlyList<TestDraft> sections,
        IReadOnlyList<AnswerKeyRow> rows, IReadOnlyList<(int From, int To)> keyRanges, List<string> unmatched)
    {
        var map = new Dictionary<(int, int), AnswerKeyRow>();
        var previousEnd = 0;
        foreach (var (from, to) in keyRanges)
        {
            var pending = sections.Where(s => s.From > previousEnd && s.To < from).ToList();
            previousEnd = to;
            var here = rows.Where(r => r.KeyPage >= from && r.KeyPage <= to).ToList();
            foreach (var row in here)
            {
                var target = pending.Count == 1 ? pending[0]
                    : row.Unit >= 1 && row.Unit <= pending.Count ? pending[row.Unit - 1]
                    : null;
                if (target is null)
                {
                    unmatched.Add($"s{row.KeyPage}-{row.Unit}");
                    continue;
                }

                var k = (target.Unit, target.TestNo);
                map[k] = map.TryGetValue(k, out var existing)
                    ? existing with { Answers = MergeAnswers(existing.Answers, row.Answers) }
                    : row with { Unit = target.Unit, TestNo = target.TestNo };
            }
        }

        return map;
    }

    private static Dictionary<int, char> MergeAnswers(IReadOnlyDictionary<int, char> first, IReadOnlyDictionary<int, char> more)
    {
        var merged = new Dictionary<int, char>(first);
        foreach (var (n, answer) in more)
            merged.TryAdd(n, answer);
        return merged;
    }

    private static int? Offset(List<SurveyedPage> pages)
    {
        var diffs = pages.Where(p => p.PrintedPage is > 0).Select(p => p.PrintedPage!.Value - p.FilePage).ToList();
        if (diffs.Count == 0)
            return null;
        return diffs.GroupBy(d => d).OrderByDescending(g => g.Count()).ThenBy(g => Math.Abs(g.Key)).First().Key;
    }

    private static string? UnitTitle(int unit, IReadOnlyList<SurveyedTocRow> toc) =>
        toc.Where(r => r.Unit == unit).Select(r => Clean(r.Title)).FirstOrDefault(t => t is not null);

    private static List<(int From, int To)> Ranges(IEnumerable<int> filePages)
    {
        var ranges = new List<(int From, int To)>();
        foreach (var page in filePages.Distinct().Order())
        {
            if (ranges.Count > 0 && ranges[^1].To == page - 1)
                ranges[^1] = (ranges[^1].From, page);
            else
                ranges.Add((page, page));
        }

        return ranges;
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= Data.BookImportJobTest.MaxTitleLength ? trimmed : trimmed[..Data.BookImportJobTest.MaxTitleLength];
    }
}
