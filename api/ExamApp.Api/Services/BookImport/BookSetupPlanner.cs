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
    /// <summary>#1134: çoktan seçmeli olmayan etkinlik sayfası (eşleştirme, boşluk doldurma, tabloda işaretleme …).</summary>
    Activity,
}

/// <summary>
/// Bir sayfanın tarama sonucu: tür, basılı sayfa no ve (test sayfasıysa) test başlığı. <paramref name="Series"/>: testi "Test 01"
/// yerine bölüm adı + numarayla adlandıran kitaplarda bölüm adı ("Kavramları Kavra 01" → "Kavramları Kavra"; #1163).
/// </summary>
public sealed record SurveyedPage(
    int FilePage,
    BookPageKind Kind,
    int? PrintedPage = null,
    int? Unit = null,
    int? TestNo = null,
    string? TestTitle = null,
    int? Minutes = null,
    int? MaxQuestionNumber = null,
    string? Series = null,
    int? ExamNo = null);

/// <summary>İçindekiler satırı: ünite no (varsa), başlık, basılı başlangıç sayfası.</summary>
public sealed record SurveyedTocRow(int? Unit, string Title, int? PrintedPage);

/// <summary>
/// Cevap anahtarındaki bir test satırı: soru no → harf (A–E; okunamayan "?"). <paramref name="KeyPage"/>: okunduğu dosya sayfası
/// (#1122: test numarasız anahtar, sayfadaki yerine göre önündeki bölüme bağlanır); bilinmiyorsa 0. <paramref name="Series"/>:
/// satırın altında durduğu bölüm adı (#1163; <see cref="SurveyedPage.Series"/>).
/// </summary>
/// <paramref name="ExamNo"/>: satır bir deneme sınavınınsa ("1. Sınav") numarası; test satırında null.
public sealed record AnswerKeyRow(int Unit, int TestNo, IReadOnlyDictionary<int, char> Answers, int KeyPage = 0, string? Series = null,
    int? ExamNo = null);

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
    IReadOnlyList<string> Flags,
    int? KeyPage = null);

/// <summary>
/// Anahtarda olup testi bulunamayan satır: etiket ("5-36" ya da bölüm adı + no), iki okumanın birleşimi ve okunduğu sayfa. Onay
/// ekranında "AI ile eşle" bunları anahtarsız testlere önerir.
/// </summary>
public sealed record OrphanKey(string Label, string AnswerKey, string? KeyConflicts, int? KeyPage);

/// <summary>Ünite: içindekiler başlığı ve testlerinin kapsadığı dosya sayfaları (kitap kaydındaki içindekiler satırı).</summary>
public sealed record PlannedUnit(int Unit, string? Title, int FileFrom, int FileTo);

public sealed record BookSetupPlan(
    int? Offset,
    IReadOnlyList<(int From, int To)> AnswerKeyRanges,
    IReadOnlyList<int> SelfAssessmentPages,
    IReadOnlyList<PlannedUnit> Units,
    IReadOnlyList<PlannedTest> Tests,
    IReadOnlyList<string> OrphanKeys,
    IReadOnlyList<OrphanKey>? OrphanRows = null)
{
    /// <summary>Etkinlik bölümleri (#1134) anahtar beklemez: yalnız testlere bakılır; hiç test yoksa false.</summary>
    public bool NoAnswerKey
    {
        get
        {
            var tests = Tests.Where(t => !t.Flags.Contains(BookSetupPlanner.FlagActivity)).ToList();
            return tests.Count > 0 && (AnswerKeyRanges.Count == 0 || tests.All(t => t.AnswerKey is null));
        }
    }
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
/// <item>Bölüm adlı kitap (#1163; ör. "Kavramları Kavra 01", "Öğrendiğini Kanıtla 02"): test, bölüm adı + numarayla tanınır ve
/// anahtardaki aynı adlı bölümün satırıyla eşlenir; testler içindekilerdeki konulara gruplanır (bkz. <c>SeriesPlan</c>). Anahtarda
/// bölüm adı yoksa (#1165) normal "ünite + test no" yolu; başlıkta ünite yoksa ünite numaranın başa dönmesinden çıkar.</item>
/// <item>Anahtar: iki okuma soru soru karşılaştırılır; farklıysa (biri boş da olsa) ilk okuma alınır, diğeri
/// <see cref="PlannedTest.KeyConflicts"/>'ta tutulur ("." uyumlu, harf diğer okuma, "-" diğer okumada yok).</item>
/// <item>Etkinlik bölümü (#1134): hiçbir testin sayfa aralığına girmeyen ardışık etkinlik sayfaları anahtarsız bir bölüm olur
/// (<see cref="FlagActivity"/>; bkz. <c>WithActivitySections</c>).</item>
/// </list>
/// </summary>
public static class BookSetupPlanner
{
    public const string FlagNoKey = "noKey";
    public const string FlagKeyMismatch = "keyMismatch";
    public const string FlagKeyConflicts = "keyConflicts";
    /// <summary>Anahtar ünitesi tutmadı, kitapta tek olan test numarasıyla eşlendi (testler kitap boyunca numaralı).</summary>
    public const string FlagKeyByNumber = "keyByNumber";

    /// <summary>#1134: etkinlik bölümü (çoktan seçmeli test değil): anahtar beklenmez, adı "1-E01 · Başlık".</summary>
    public const string FlagActivity = "activity";

    /// <summary>Deneme sınavı ("1. Sınav", "Ara Deneme Sınavı"): test numarası yok, adı "2-S01"; konusu üniteden alınmaz.</summary>
    public const string FlagExam = "exam";

    /// <summary>#1134: bir etkinlik bölümündeki en fazla sayfa (uzun etkinlik dizisi bu kadar sayfada bir bölünür).</summary>
    public const int MaxActivitySectionPages = 8;

    /// <summary>Bir testteki en fazla soru (anahtar dizgisinin üst sınırı); bölümlü kitapta bir ünite tek testtir (#1122).</summary>
    public const int MaxQuestionsPerTest = Data.BookImportJobTest.MaxKeyLength;

    public static BookSetupPlan Plan(
        IReadOnlyList<SurveyedPage> pages,
        IReadOnlyList<SurveyedTocRow> toc,
        IReadOnlyList<AnswerKeyRow> firstRead,
        IReadOnlyList<AnswerKeyRow> secondRead) =>
        WithActivitySections(PlanTests(pages, toc, firstRead, secondRead), pages, toc);

    /// <summary>
    /// #1134: hiçbir testin sayfa aralığına girmeyen ardışık etkinlik sayfaları etkinlik bölümü olur (testin aralığındaki etkinlik
    /// sayfası o testle aktarılır). Dizi, içindekiler satırının başladığı sayfada ve <see cref="MaxActivitySectionPages"/> sayfada
    /// bir bölünür. Ünite: bölümden önce başlayan son testinki (yoksa ilk testinki; hiç test yoksa içindekiler satırınınki, o da
    /// yoksa satırın sırası); numara ünite içinde sayfa sırasıyla; başlık bölümden önce başlayan son içindekiler satırı.
    /// </summary>
    private static BookSetupPlan WithActivitySections(BookSetupPlan plan, IReadOnlyList<SurveyedPage> pages,
        IReadOnlyList<SurveyedTocRow> toc)
    {
        var runs = Ranges(pages.Where(p => p.Kind == BookPageKind.Activity
                                           && !plan.Tests.Any(t => p.FilePage >= t.FileFrom && p.FilePage <= t.FileTo))
            .Select(p => p.FilePage));
        if (runs.Count == 0)
            return plan;

        var offset = plan.Offset ?? 0;
        var rows = toc.Where(r => r.PrintedPage is > 0).OrderBy(r => r.PrintedPage).ToList();
        var sections = new List<(int From, int To)>();
        foreach (var (from, to) in runs)
        {
            var start = from;
            for (var page = from + 1; page <= to; page++)
            {
                if (page - start < MaxActivitySectionPages && !rows.Any(r => r.PrintedPage - offset == page))
                    continue;
                sections.Add((start, page - 1));
                start = page;
            }

            sections.Add((start, to));
        }

        var tests = plan.Tests.OrderBy(t => t.FileFrom).ToList();
        var units = plan.Units.ToDictionary(u => u.Unit);
        var numbers = new Dictionary<int, int>();
        var added = new List<PlannedTest>();
        foreach (var (from, to) in sections)
        {
            var row = rows.LastOrDefault(r => r.PrintedPage - offset <= from);
            var unit = tests.LastOrDefault(t => t.FileFrom < from)?.Unit ?? tests.FirstOrDefault()?.Unit
                       ?? (row is null ? 1 : row.Unit is > 0 ? row.Unit.Value : rows.IndexOf(row) + 1);
            var testNo = numbers[unit] = numbers.GetValueOrDefault(unit) + 1;
            var title = Clean(row?.Title);
            added.Add(new PlannedTest(0, unit, testNo, title, null, from, to, null, null, null, [FlagActivity]));
            units[unit] = units.TryGetValue(unit, out var existing)
                ? existing with { FileFrom = Math.Min(existing.FileFrom, from), FileTo = Math.Max(existing.FileTo, to) }
                : new PlannedUnit(unit, title, from, to);
        }

        return plan with
        {
            Tests = tests.Concat(added).OrderBy(t => t.FileFrom).Select((t, i) => t with { Order = i + 1 }).ToList(),
            Units = units.Values.OrderBy(u => u.Unit).ToList(),
        };
    }

    private static BookSetupPlan PlanTests(
        IReadOnlyList<SurveyedPage> pages,
        IReadOnlyList<SurveyedTocRow> toc,
        IReadOnlyList<AnswerKeyRow> firstRead,
        IReadOnlyList<AnswerKeyRow> secondRead)
    {
        var ordered = pages.OrderBy(p => p.FilePage).ToList();
        // Ünitesi sayfada basılı olan sayfalar (FillUnits komşudan doldurmadan önce): anahtar ünitesi yalnız bunlar yoksa kazanır.
        var printedUnits = ordered.Where(p => p.Unit is > 0).Select(p => p.FilePage).ToHashSet();
        var keyRanges = Ranges(ordered.Where(p => p.Kind == BookPageKind.AnswerKey).Select(p => p.FilePage));
        var selfAssessment = ordered.Where(p => p.Kind == BookPageKind.SelfAssessment).Select(p => p.FilePage).ToList();

        // Bölüm adıyla eşleme yalnız anahtar da bölüm adlıysa: sayfada ad olup anahtarı "Ünite + Test n" olan kitapta (Define)
        // ad yalnız başlığa eklenir.
        var series = firstRead.Concat(secondRead).Any(r => Fold(r.Series).Length > 0) ? SeriesIds(ordered) : [];
        ordered = series.Count > 0
            ? ordered.Select(p => p.Series is not null && series.TryGetValue(Fold(p.Series), out var id) && p.TestNo is > 0
                ? p with { Unit = id }
                : p with { Unit = null, TestNo = null }).ToList()
            : InferUnits(FillUnits(ordered));
        var drafts = CollectTests(ordered, printedUnits);
        var sectioned = drafts.Count == 0;
        Dictionary<(int Unit, int TestNo), AnswerKeyRow> first, second;
        var unmatched = new List<string>();
        if (series.Count > 0)
        {
            first = ByTest(SeriesRows(firstRead, series));
            second = ByTest(SeriesRows(secondRead, series));
        }
        else if (sectioned)
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

        // Testler kitap boyunca numaralı (Test 1–58), anahtar ünite ünite ("5. Ünite → Test 36"): ünite tutmaz, numara tektir.
        var byNumber = series.Count == 0 && !sectioned ? MatchByTestNo(drafts.Where(d => !d.Exam).ToList(), first, second) : [];
        var firstExams = ExamKeys(firstRead);
        var secondExams = ExamKeys(secondRead);

        var tests = new List<PlannedTest>();
        foreach (var draft in drafts)
        {
            AnswerKeyRow? a, b;
            if (draft.Exam)
            {
                firstExams.TryGetValue(draft.TestNo, out a);
                secondExams.TryGetValue(draft.TestNo, out b);
            }
            else
            {
                first.TryGetValue((draft.Unit, draft.TestNo), out a);
                second.TryGetValue((draft.Unit, draft.TestNo), out b);
            }

            var (key, conflicts) = MergeKey(a, b);
            var expected = key?.Length ?? draft.MaxQuestion;

            var flags = new List<string>();
            if (key is null && keyRanges.Count > 0)
                flags.Add(FlagNoKey);
            if (key is not null && draft.MaxQuestion is { } seen && seen != key.Length)
                flags.Add(FlagKeyMismatch);
            if (conflicts is not null && conflicts.Any(c => c != '.'))
                flags.Add(FlagKeyConflicts);
            if (!draft.Exam && byNumber.Contains((draft.Unit, draft.TestNo)))
                flags.Add(FlagKeyByNumber);
            if (draft.Exam)
                flags.Add(FlagExam);

            tests.Add(new PlannedTest(tests.Count + 1, draft.Exam ? ExamUnit(drafts, draft) : draft.Unit, draft.TestNo, draft.Title, draft.Minutes, draft.From, draft.To,
                expected, key, conflicts, flags, KeyPageOf(a, b)));
        }

        var planned = tests.Select(t => (t.Unit, t.TestNo)).ToHashSet();
        var labels = series.Count == 0 ? null : SeriesLabels(ordered, firstRead.Concat(secondRead), series);
        var orphanKeys = first.Keys.Concat(second.Keys).Distinct()
            .Where(k => !planned.Contains(k))
            .OrderBy(k => k.Unit).ThenBy(k => k.TestNo)
            .ToList();
        string Label((int Unit, int TestNo) k) => labels is null ? $"{k.Unit}-{k.TestNo:D2}" : $"{labels[k.Unit]} {k.TestNo:D2}";
        var plannedExams = drafts.Where(d => d.Exam).Select(d => d.TestNo).ToHashSet();
        var orphanExams = firstExams.Keys.Concat(secondExams.Keys).Distinct().Where(n => !plannedExams.Contains(n)).Order().ToList();
        var orphans = orphanKeys.Select(Label).Concat(orphanExams.Select(ExamLabel)).Concat(unmatched.Distinct()).ToList();
        var orphanRows = new List<OrphanKey>();
        foreach (var k in orphanKeys)
        {
            first.TryGetValue(k, out var a);
            second.TryGetValue(k, out var b);
            if (MergeKey(a, b) is ({ } key, var conflicts))
                orphanRows.Add(new OrphanKey(Label(k), key, conflicts, KeyPageOf(a, b)));
        }

        foreach (var n in orphanExams)
        {
            firstExams.TryGetValue(n, out var a);
            secondExams.TryGetValue(n, out var b);
            if (MergeKey(a, b) is ({ } key, var conflicts))
                orphanRows.Add(new OrphanKey(ExamLabel(n), key, conflicts, KeyPageOf(a, b)));
        }

        if (labels is not null)
            return SeriesPlan(ordered, toc, keyRanges, selfAssessment, tests, labels, orphans) with { OrphanRows = orphanRows };

        // Deneme birden çok üniteyi kapsar: ünite aralığına girmez (konusu üniteden alınmaz).
        var units = tests.Where(t => !t.Flags.Contains(FlagExam)).GroupBy(t => t.Unit)
            .OrderBy(g => g.Key)
            .Select(g => new PlannedUnit(g.Key, sectioned ? g.First().Title : UnitTitle(g.Key, toc), g.Min(t => t.FileFrom),
                g.Max(t => t.FileTo)))
            .ToList();

        return new BookSetupPlan(Offset(ordered), keyRanges, selfAssessment, units, tests, orphans, orphanRows);
    }

    /// <summary>
    /// Anahtarı bulunamayan testi, ünitesi farklı ama aynı numaralı sahipsiz anahtar satırına bağlar; yalnız o numara hem testlerde
    /// hem sahipsiz satırlarda tekse (her ünitede "Test 1" olan kitapta bağlamaz). İki okuma ayrı ayrı taşınır; bağlanan testler döner.
    /// </summary>
    private static HashSet<(int Unit, int TestNo)> MatchByTestNo(List<TestDraft> drafts,
        Dictionary<(int Unit, int TestNo), AnswerKeyRow> first, Dictionary<(int Unit, int TestNo), AnswerKeyRow> second)
    {
        var tests = drafts.Select(d => (d.Unit, d.TestNo)).ToHashSet();
        var uniqueTests = drafts.GroupBy(d => d.TestNo).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        var orphans = first.Keys.Concat(second.Keys).Distinct().Where(k => !tests.Contains(k))
            .GroupBy(k => k.TestNo).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var matched = new HashSet<(int, int)>();
        foreach (var draft in drafts)
        {
            var test = (draft.Unit, draft.TestNo);
            if (first.ContainsKey(test) || second.ContainsKey(test) || !uniqueTests.Contains(draft.TestNo)
                || !orphans.TryGetValue(draft.TestNo, out var orphan))
                continue;
            if (!draft.UnitPrinted)
            {
                // Sayfada ünite basılı değil (sol sayfa, ünite ilk sayfasından okunmadı): anahtarın ünitesi doğrudur ("2. Ünite → Test 8").
                draft.Unit = orphan.Unit;
                matched.Add(orphan);
                continue;
            }

            Move(first, orphan, test);
            Move(second, orphan, test);
            matched.Add(test);
        }

        return matched;

        static void Move(Dictionary<(int Unit, int TestNo), AnswerKeyRow> keys, (int Unit, int TestNo) from, (int Unit, int TestNo) to)
        {
            if (!keys.Remove(from, out var row))
                return;
            keys[to] = row with { Unit = to.Unit };
        }
    }

    /// <summary>Issue #1176: anahtarın okunduğu dosya sayfası (ilk okuma önce); bilinmiyorsa null.</summary>
    private static int? KeyPageOf(AnswerKeyRow? first, AnswerKeyRow? second) =>
        first is { KeyPage: > 0 } ? first.KeyPage : second is { KeyPage: > 0 } ? second.KeyPage : null;

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
        public int Unit { get; set; } = unit;
        /// <summary>Deneme sınavı: <see cref="TestNo"/> deneme numarası, anahtarı <see cref="AnswerKeyRow.ExamNo"/> satırı.</summary>
        public bool Exam { get; init; }
        /// <summary>Sayfalarından birinde ünite basılı (komşudan doldurulmadı).</summary>
        public bool UnitPrinted { get; set; }
        public int TestNo { get; } = testNo;
        public int From { get; } = from;
        public int To { get; set; } = from;
        public string? Title { get; set; }
        public int? Minutes { get; set; }
        public int? MaxQuestion { get; set; }
    }

    /// <summary>
    /// Test: başlıklı sayfayla başlar, başlıksız ardışık test sayfaları ona eklenir, test olmayan sayfa kapatır. Testin numarasını
    /// taşıyan etkinlik sayfası (bulmacalı ama şıklı soru, "Test 3"ün ikinci sayfası) testi kapatmaz, ona eklenir. Deneme başlıklı
    /// sayfa ("1. Sınav") deneme testi açar.
    /// </summary>
    private static List<TestDraft> CollectTests(List<SurveyedPage> pages, IReadOnlySet<int> printedUnits)
    {
        var tests = new List<TestDraft>();
        var seen = new HashSet<(int, int)>();
        var seenExams = new HashSet<int>();
        TestDraft? current = null;
        foreach (var page in pages)
        {
            if (page.ExamNo is { } examNo and > 0 && page.Kind is BookPageKind.Test or BookPageKind.Activity)
            {
                if (current is not { Exam: true } || current.TestNo != examNo)
                {
                    if (!seenExams.Add(examNo))
                    {
                        current = null;
                        continue;
                    }

                    current = new TestDraft(0, examNo, page.FilePage) { Exam = true };
                    tests.Add(current);
                }

                Extend(current, page);
                continue;
            }

            var continues = page.Kind == BookPageKind.Activity && current is { Exam: false } && page.TestNo == current.TestNo;
            if (page.Kind != BookPageKind.Test && !continues)
            {
                current = null;
                continue;
            }

            if (page.TestNo is { } testNo and > 0 && !continues)
            {
                var unit = page.Unit is > 0 ? page.Unit.Value : 1;
                if (current is null || current.Exam || current.Unit != unit || current.TestNo != testNo)
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
            Extend(current, page);
        }

        return tests;

        void Extend(TestDraft draft, SurveyedPage page)
        {
            draft.To = page.FilePage;
            draft.Title ??= Clean(page.TestTitle);
            draft.Minutes ??= page.Minutes is > 0 and <= 600 ? page.Minutes : null;
            if (page.MaxQuestionNumber is > 0 and <= MaxQuestionsPerTest)
                draft.MaxQuestion = Math.Max(draft.MaxQuestion ?? 0, page.MaxQuestionNumber.Value);
            if (printedUnits.Contains(page.FilePage))
                draft.UnitPrinted = true;
        }
    }

    /// <summary>
    /// Sol sayfada yalnız "Test 11" başlığı, ünite sağ sayfanın altında ("Ünite 2"): ünitesiz sayfa ünite 1 sayılınca test ikiye
    /// bölünüyor, üçüncü sayfası kayboluyordu (5. sınıf Sosyal Bilgiler soru bankası). Aynı test numaralı ardışık sayfalar (test ya
    /// da etkinlik) bir dizidir: dizide tek ünite okunduysa ünitesiz sayfaları onu alır; hiç okunmadıysa önceki dizinin ünitesi
    /// (numara geri gitmediyse; geri giderse yeni ünite olabilir, dokunulmaz). Başlıkların yarısından azında ünite varsa
    /// dokunulmaz: <see cref="InferUnits"/> üniteyi numaranın başa dönmesinden çıkarır.
    /// </summary>
    private static List<SurveyedPage> FillUnits(List<SurveyedPage> pages)
    {
        var headers = pages.Where(p => p.Kind == BookPageKind.Test && p.TestNo is > 0).ToList();
        if (headers.Count == 0 || headers.Count(p => p.Unit is > 0) * 2 < headers.Count)
            return pages;

        static bool InRun(SurveyedPage p) => p.Kind is BookPageKind.Test or BookPageKind.Activity && p.TestNo is > 0 && p.ExamNo is null;
        var result = pages.ToList();
        int? lastUnit = null;
        var lastNo = 0;
        for (var i = 0; i < result.Count;)
        {
            if (!InRun(result[i]))
            {
                i++;
                continue;
            }

            var testNo = result[i].TestNo!.Value;
            var j = i;
            while (j + 1 < result.Count && InRun(result[j + 1]) && result[j + 1].TestNo == testNo)
                j++;
            var units = result.Skip(i).Take(j - i + 1).Select(p => p.Unit).OfType<int>().Where(u => u > 0).Distinct().ToList();
            int? unit = units.Count == 1 ? units[0] : units.Count == 0 && testNo >= lastNo ? lastUnit : null;
            if (unit is { } u)
            {
                for (var k = i; k <= j; k++)
                    if (result[k].Unit is not > 0)
                        result[k] = result[k] with { Unit = u };
            }

            lastUnit = units.Count > 0 ? units[^1] : unit;
            lastNo = testNo;
            i = j + 1;
        }

        return result;
    }

    /// <summary>Deneme anahtar satırları (<see cref="AnswerKeyRow.ExamNo"/>) deneme numarasına göre; tekrar okunan satır birleşir.</summary>
    private static Dictionary<int, AnswerKeyRow> ExamKeys(IReadOnlyList<AnswerKeyRow> rows)
    {
        var map = new Dictionary<int, AnswerKeyRow>();
        foreach (var row in rows)
        {
            if (row.ExamNo is not { } n || n < 1)
                continue;
            map[n] = map.TryGetValue(n, out var existing) ? existing with { Answers = MergeAnswers(existing.Answers, row.Answers) } : row;
        }

        return map;
    }

    /// <summary>Denemenin ünitesi (yalnız ad ve sıra için; konusu üniteden alınmaz): önündeki testinki, yoksa sonrakininki, yoksa 1.</summary>
    private static int ExamUnit(List<TestDraft> drafts, TestDraft exam) =>
        drafts.LastOrDefault(d => !d.Exam && d.From < exam.From)?.Unit ?? drafts.FirstOrDefault(d => !d.Exam)?.Unit ?? 1;

    /// <summary>Sahipsiz deneme anahtarının etiketi ("S01").</summary>
    internal static string ExamLabel(int examNo) => $"S{examNo:D2}";

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

    /// <summary>
    /// #1165: başlıklarda ünite no'su yoksa (çoğunda) ünite, test numarasının geri düşmesinden çıkarılır ("Kavrama Testi - 14" →
    /// "Kavrama Testi - 1": yeni ünite); aynı numaranın tekrarı (testin ikinci sayfası) aynı testtir. Bölüm adı başlığa eklenir
    /// ("Kavrama Testi: Güneş").
    /// </summary>
    private static List<SurveyedPage> InferUnits(List<SurveyedPage> pages)
    {
        var headers = pages.Where(p => p.Kind == BookPageKind.Test && p.TestNo is > 0).ToList();
        var inferUnits = headers.Count(p => p.Unit is > 0) * 2 < headers.Count;
        var unit = 1;
        var last = 0;
        return pages.Select(p =>
        {
            if (p.Kind != BookPageKind.Test || p.TestNo is not > 0)
                return p;
            if (inferUnits)
            {
                if (p.TestNo < last)
                    unit++;
                last = p.TestNo.Value;
            }

            var series = Clean(p.Series);
            var title = series is null ? p.TestTitle : Clean(p.TestTitle) is { } topic ? $"{series}: {topic}" : series;
            return p with { Unit = inferUnits ? unit : p.Unit, TestTitle = title };
        }).ToList();
    }

    /// <summary>
    /// #1163: test sayfalarındaki bölüm adlarına (katlanmış: büyük/küçük harf, Türkçe harf ve boşluk farkı yok) kitaptaki ilk
    /// görülme sırasıyla geçici ünite no'su verir. Hiçbir test sayfasında bölüm adı yoksa boştur (kitap bölüm adlı değil).
    /// </summary>
    private static Dictionary<string, int> SeriesIds(List<SurveyedPage> pages)
    {
        var ids = new Dictionary<string, int>();
        foreach (var page in pages)
        {
            if (page.Kind == BookPageKind.Test && page.TestNo is > 0 && Fold(page.Series) is { Length: > 0 } key)
                ids.TryAdd(key, ids.Count + 1);
        }

        return ids;
    }

    /// <summary>
    /// #1163: anahtar satırını bölüm adının geçici ünite no'suna taşır. Test sayfası olmayan bölümün satırı (ör. tek soruluk
    /// "Doğru Yorumla") yeni no alır ve sahipsiz kalır; bölüm adı okunmamış satır alınmaz.
    /// </summary>
    private static List<AnswerKeyRow> SeriesRows(IReadOnlyList<AnswerKeyRow> rows, Dictionary<string, int> ids)
    {
        var mapped = new List<AnswerKeyRow>();
        foreach (var row in rows)
        {
            if (Fold(row.Series) is not { Length: > 0 } key)
                continue;
            if (!ids.TryGetValue(key, out var id))
                ids[key] = id = ids.Count + 1;
            mapped.Add(row with { Unit = id });
        }

        return mapped;
    }

    /// <summary>Geçici ünite no → görünen bölüm adı (sayfadaki ilk yazılış; yoksa anahtardaki).</summary>
    private static Dictionary<int, string> SeriesLabels(List<SurveyedPage> pages, IEnumerable<AnswerKeyRow> rows,
        Dictionary<string, int> ids)
    {
        var labels = new Dictionary<int, string>();
        foreach (var name in pages.Select(p => p.Series).Concat(rows.Select(r => r.Series)))
        {
            if (Fold(name) is { Length: > 0 } key && ids.TryGetValue(key, out var id))
                labels.TryAdd(id, Clean(name)!);
        }

        return labels;
    }

    /// <summary>
    /// #1163: bölüm adlı kitapta testler konulara gruplanır: ünite, testin ilk sayfasından önce başlayan son içindekiler satırıdır
    /// (içindekiler yoksa test başlığındaki konu). Ünite içinde test no sayfa sırasıdır; başlık "Kavramları Kavra 01" olur.
    /// Anahtar varken hiçbir testi anahtarda bulunmayan bölüm (açık uçlu "Şimdi Uygula" gibi) test sayılmaz.
    /// </summary>
    private static BookSetupPlan SeriesPlan(List<SurveyedPage> pages, IReadOnlyList<SurveyedTocRow> toc,
        IReadOnlyList<(int From, int To)> keyRanges, IReadOnlyList<int> selfAssessment, List<PlannedTest> tests,
        Dictionary<int, string> labels, List<string> orphans)
    {
        if (keyRanges.Count > 0)
        {
            var keyed = tests.Where(t => t.AnswerKey is not null).Select(t => t.Unit).ToHashSet();
            tests = tests.Where(t => keyed.Contains(t.Unit)).ToList();
        }

        var offset = Offset(pages) ?? 0;
        var rows = toc.Where(r => r.PrintedPage is > 0 && !keyRanges.Any(k => r.PrintedPage - offset >= k.From && r.PrintedPage - offset <= k.To))
            .OrderBy(r => r.PrintedPage)
            .ToList();
        var unitOf = new Dictionary<string, (int Unit, string? Title)>();
        var counts = new Dictionary<int, int>();
        var planned = new List<PlannedTest>();
        var units = new List<PlannedUnit>();
        foreach (var test in tests)
        {
            var topic = rows.Count > 0 ? Clean(rows.LastOrDefault(r => r.PrintedPage <= test.FileFrom + offset)?.Title) : test.Title;
            if (!unitOf.TryGetValue(topic ?? "", out var unit))
                unitOf[topic ?? ""] = unit = (unitOf.Count + 1, topic);
            var testNo = counts[unit.Unit] = counts.GetValueOrDefault(unit.Unit) + 1;
            planned.Add(test with
            {
                Order = planned.Count + 1, Unit = unit.Unit, TestNo = testNo, Title = Clean($"{labels[test.Unit]} {test.TestNo:D2}"),
            });
        }

        foreach (var group in planned.GroupBy(t => t.Unit))
        {
            var title = unitOf.Values.First(u => u.Unit == group.Key).Title;
            units.Add(new PlannedUnit(group.Key, title, group.Min(t => t.FileFrom), group.Max(t => t.FileTo)));
        }

        return new BookSetupPlan(Offset(pages), keyRanges, selfAssessment, units, planned, orphans);
    }

    /// <summary>Bölüm adını karşılaştırma için katlar: yalnız harfler, küçük, Türkçe harfler ASCII ("Kavramları Kavra" → "kavramlarikavra").</summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var folded = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            var ascii = c switch { 'ı' or 'i' or 'î' or 'İ' => 'i', 'ş' => 's', 'ğ' => 'g', 'ü' or 'û' => 'u', 'ö' => 'o', 'ç' => 'c', 'â' => 'a', _ => c };
            if (ascii is >= 'a' and <= 'z')
                folded.Append(ascii);
        }

        return folded.ToString();
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
