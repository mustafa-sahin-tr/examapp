using ExamApp.Api.Services.BookImport;
using static ExamApp.Api.Tests.Support.FakeBookSetupAi;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1021: sayfa taraması + iki anahtar okumasından kitap kurulumu (test listesi, ofset, anahtar uyuşmazlıkları).</summary>
public class BookSetupPlannerTests
{
    private static SurveyedPage Test(int file, int? unit = null, int? testNo = null, string? title = null, int? minutes = null,
        int? maxQuestion = null, int? printed = null) =>
        new(file, BookPageKind.Test, printed, unit, testNo, title, minutes, maxQuestion);

    private static SurveyedPage Page(int file, BookPageKind kind, int? printed = null) => new(file, kind, printed);

    [Fact]
    public void Tests_start_at_headers_take_continuation_pages_and_end_at_other_pages()
    {
        var pages = new[]
        {
            Page(1, BookPageKind.Cover),
            Test(2, 1, 1, "Milyonlar - I", 16, maxQuestion: 2),
            Test(3, maxQuestion: 4),
            Test(4, 1, 2, "Milyonlar - II", maxQuestion: 3),
            Page(5, BookPageKind.Other),
            Test(6, maxQuestion: 2), // başlıksız ve önünde test yok: alınmaz
            Test(7, 2, 1, "Kesirler", maxQuestion: 5),
            Test(8, 1, 1, "Tekrar", maxQuestion: 9), // aynı başlık yeniden: yeni test açılmaz
        };

        var plan = BookSetupPlanner.Plan(pages, [], [], []);

        plan.Tests.Select(t => (t.Order, t.Unit, t.TestNo, t.FileFrom, t.FileTo, t.ExpectedQuestions)).ShouldBe(
        [
            (1, 1, 1, 2, 3, (int?)4),
            (2, 1, 2, 4, 4, 3),
            (3, 2, 1, 7, 7, 5),
        ]);
        plan.Tests[0].Title.ShouldBe("Milyonlar - I");
        plan.Tests[0].Minutes.ShouldBe(16);
        plan.Tests.ShouldAllBe(t => t.AnswerKey == null && t.Flags.Count == 0);
        plan.NoAnswerKey.ShouldBeTrue();
        plan.Units.ShouldBe([new PlannedUnit(1, null, 2, 4), new PlannedUnit(2, null, 7, 7)]);
    }

    [Fact]
    public void Offset_is_the_most_common_printed_minus_file_difference()
    {
        var pages = new[]
        {
            Page(1, BookPageKind.Cover),
            Test(2, 1, 1, printed: 1),
            Test(3, printed: 2),
            Test(4, printed: 3),
            Page(5, BookPageKind.Other, printed: 40), // yanlış okunmuş sayı çoğunluğu bozmaz
        };

        BookSetupPlanner.Plan(pages, [], [], []).Offset.ShouldBe(-1);
        BookSetupPlanner.Plan([Test(1, 1, 1)], [], [], []).Offset.ShouldBeNull();
    }

    [Fact]
    public void Answer_key_and_self_assessment_pages_become_ranges_and_unit_titles_come_from_the_toc()
    {
        var pages = new[]
        {
            Test(1, 1, 1), Test(2, 2, 1),
            Page(3, BookPageKind.SelfAssessment),
            Page(4, BookPageKind.AnswerKey), Page(5, BookPageKind.AnswerKey), Page(7, BookPageKind.AnswerKey),
        };
        var toc = new[] { new SurveyedTocRow(1, " Doğal Sayılar ", 7), new SurveyedTocRow(null, "Cevap Anahtarı", 237) };

        var plan = BookSetupPlanner.Plan(pages, toc, [], []);

        plan.AnswerKeyRanges.ShouldBe([(4, 5), (7, 7)]);
        plan.SelfAssessmentPages.ShouldBe([3]);
        plan.Units.Select(u => u.Title).ShouldBe(["Doğal Sayılar", null]);
    }

    [Fact]
    public void Two_reads_are_compared_question_by_question()
    {
        var pages = new[] { Test(1, 1, 1, maxQuestion: 5), Test(2, 1, 2, maxQuestion: 3), Test(3, 1, 3, maxQuestion: 2), Page(4, BookPageKind.AnswerKey) };
        AnswerKeyRow[] first = [Row(1, 1, "ABCDA"), Row(1, 2, "AB "), Row(1, 9, "C")];
        AnswerKeyRow[] second = [Row(1, 1, "ABDDA"), Row(1, 2, "ABC")];

        var plan = BookSetupPlanner.Plan(pages, [], first, second);

        var agreeExceptThird = plan.Tests[0];
        agreeExceptThird.AnswerKey.ShouldBe("ABCDA");
        agreeExceptThird.KeyConflicts.ShouldBe("..D..");
        agreeExceptThird.Flags.ShouldBe([BookSetupPlanner.FlagKeyConflicts]);

        // İlk okumada boş hücre: ikinci okumadaki harf alınır, ilk okuma yok ("-") olarak işaretlenir.
        var missingInFirst = plan.Tests[1];
        missingInFirst.AnswerKey.ShouldBe("ABC");
        missingInFirst.KeyConflicts.ShouldBe("..-");

        var noKey = plan.Tests[2];
        noKey.AnswerKey.ShouldBeNull();
        noKey.ExpectedQuestions.ShouldBe(2);
        noKey.Flags.ShouldBe([BookSetupPlanner.FlagNoKey]);

        plan.OrphanKeys.ShouldBe(["1-09"]);
        plan.NoAnswerKey.ShouldBeFalse();
    }

    [Fact]
    public void Each_test_keeps_the_page_its_key_was_read_from()
    {
        var pages = new[] { Test(1, 1, 1), Test(2, 1, 2), Test(3, 1, 3), Page(4, BookPageKind.AnswerKey), Page(5, BookPageKind.AnswerKey) };
        AnswerKeyRow[] first = [Row(1, 1, "AB") with { KeyPage = 4 }, Row(1, 2, "C") with { KeyPage = 0 }];
        AnswerKeyRow[] second = [Row(1, 1, "AB") with { KeyPage = 5 }, Row(1, 2, "C") with { KeyPage = 5 }];

        var plan = BookSetupPlanner.Plan(pages, [], first, second);

        // İlk okuma önce; onda sayfa yoksa ikinci okumanınki; anahtarı olmayan testte yok.
        plan.Tests.Select(t => t.KeyPage).ShouldBe([4, 5, null]);
    }

    [Fact]
    public void Key_length_drives_the_expected_count_and_a_different_page_count_is_flagged()
    {
        var pages = new[] { Test(1, 1, 1, maxQuestion: 4), Page(2, BookPageKind.AnswerKey) };

        var test = BookSetupPlanner.Plan(pages, [], [Row(1, 1, "ABCDA")], [Row(1, 1, "ABCDA")]).Tests.Single();

        test.ExpectedQuestions.ShouldBe(5);
        test.KeyConflicts.ShouldBe(".....");
        test.Flags.ShouldBe([BookSetupPlanner.FlagKeyMismatch]);
    }

    [Fact]
    public void A_row_read_in_both_tiles_is_merged_and_a_row_from_one_read_only_is_not_a_conflict()
    {
        var pages = new[] { Test(1, 1, 1, maxQuestion: 4), Test(2, 1, 2, maxQuestion: 2), Page(3, BookPageKind.AnswerKey) };
        // Döşemeli okuma: üst döşeme 1–2, alt döşeme 3–4 (ve çakışan 2).
        AnswerKeyRow[] tiles = [Row(1, 1, "AB"), Row(1, 1, " BCA")];

        var plan = BookSetupPlanner.Plan(pages, [], [Row(1, 1, "ABDA"), Row(1, 2, "BB")], tiles);

        plan.Tests[0].AnswerKey.ShouldBe("ABDA");
        plan.Tests[0].KeyConflicts.ShouldBe("..C.");
        plan.Tests[1].AnswerKey.ShouldBe("BB");
        plan.Tests[1].KeyConflicts.ShouldBe("..");
        plan.Tests[1].Flags.ShouldBeEmpty();
    }

    /// <summary>
    /// #1122: MEB LGS çalışma kitabı düzeni — test başlığı yok; ünite kapağı + kesintisiz numaralı sorular, ünitelerin anahtarı
    /// sonda tek sayfada (ünite ünite), ardından her biri kendi etiketsiz anahtarıyla biten çıkmış sınav bölümleri.
    /// </summary>
    [Fact]
    public void A_book_without_test_headers_becomes_one_test_per_section_with_keys_from_the_following_key_page()
    {
        var pages = new[]
        {
            Page(1, BookPageKind.Cover),
            Page(2, BookPageKind.Toc, 2),
            Page(3, BookPageKind.Cover, 3), // 1. Ünite kapağı
            Test(4, maxQuestion: 4, printed: 4),
            Page(5, BookPageKind.Other, 5), // görsel ağırlıklı soru sayfası yanlış sınıflandı: bölümü kapatmaz
            Test(6, maxQuestion: 150, printed: 6),
            Test(7, maxQuestion: 3, printed: 7), // kapak okunamadı ama numara başa döndü: 2. Ünite
            Test(8, maxQuestion: 6, printed: 8),
            Page(9, BookPageKind.AnswerKey, 9),
            Page(10, BookPageKind.Cover, 10), // 2018-2019 sınavı
            Test(11, maxQuestion: 3, printed: 11),
            Page(12, BookPageKind.AnswerKey, 12),
        };
        var toc = new List<SurveyedTocRow>
        {
            new(1, "Çarpanlar ve Katlar", 3),
            new(2, "Kareköklü İfadeler", 7),
            new(null, "Cevap Anahtarı", 9),
            new(null, "2018-2019 Merkezî Sınav Soruları", 10),
            new(null, "Cevap Anahtarı", 12),
        };
        var unit1 = new string('A', 150);
        AnswerKeyRow Key(int unit, string answers, int page) => Row(unit, 1, answers) with { KeyPage = page };
        var first = new[] { Key(1, unit1, 9), Key(2, "BCDABC", 9), Key(1, "DAB", 12) };
        var second = new[] { Key(1, unit1, 9), Key(2, "BCDABD", 9), Key(1, "DAB", 12) };

        var plan = BookSetupPlanner.Plan(pages, toc, first, second);

        plan.Tests.Select(t => (t.Unit, t.TestNo, t.Title, t.FileFrom, t.FileTo, t.ExpectedQuestions, t.AnswerKey)).ShouldBe(
        [
            (1, 1, "1. Ünite: Çarpanlar ve Katlar", 4, 6, (int?)150, unit1),
            (2, 1, "2. Ünite: Kareköklü İfadeler", 7, 8, 6, "BCDABC"),
            (3, 1, "2018-2019 Merkezî Sınav Soruları", 11, 11, 3, "DAB"),
        ]);
        plan.Tests[1].KeyConflicts.ShouldBe(".....D");
        plan.Units.Select(u => (u.Unit, u.Title)).ShouldBe(
        [
            (1, "1. Ünite: Çarpanlar ve Katlar"),
            (2, "2. Ünite: Kareköklü İfadeler"),
            (3, "2018-2019 Merkezî Sınav Soruları"),
        ]);
        plan.OrphanKeys.ShouldBeEmpty();
    }

    [Fact]
    public void A_sectioned_key_row_that_fits_no_section_is_reported()
    {
        var pages = new[] { Test(1, maxQuestion: 2), Page(2, BookPageKind.AnswerKey) };
        var rows = new[] { Row(1, 1, "AB") with { KeyPage = 2 }, Row(1, 1, "C") with { KeyPage = 7 } };
        var twoSections = new[] { Test(1, maxQuestion: 2), Page(2, BookPageKind.Cover), Test(3, maxQuestion: 2), Page(4, BookPageKind.AnswerKey) };
        var thirdUnit = new[] { Row(3, 1, "AB") with { KeyPage = 4 } };

        BookSetupPlanner.Plan(pages, [], rows, []).Tests.Single().AnswerKey.ShouldBe("AB");
        BookSetupPlanner.Plan(twoSections, [], thirdUnit, []).OrphanKeys.ShouldBe(["s4-3"]);
    }

    private static SurveyedPage Series(int file, string series, int testNo, string topic, int maxQuestion, int printed) =>
        new(file, BookPageKind.Test, printed, null, testNo, topic, null, maxQuestion, series);

    [Fact]
    public void A_book_naming_tests_by_section_label_matches_keys_by_label_and_groups_tests_by_toc_topic()
    {
        // Issue #1163 (HiperZeka MAX fasikülleri): "Kavramları Kavra 01", "Öğrendiğini Kanıtla 01" ...; anahtar bölüm adına göre.
        var pages = new[]
        {
            Page(1, BookPageKind.Toc, 1),
            Page(2, BookPageKind.Other, 2), // Şimdi Uygula: açık uçlu
            Series(3, "Kavramları Kavra", 1, "Dikdörtgenin Çevre Uzunluğu", 6, 3),
            Series(4, "KAVRAMLARI KAVRA", 1, "Dikdörtgenin Çevre Uzunluğu", 11, 4), // aynı test, farklı yazılış
            Series(5, "Öğrendiğini Kanıtla", 1, "Dikdörtgenin Çevre Uzunluğu", 8, 5),
            Series(6, "Öğrendiğini Kanıtla", 2, "Dikdörtgenin Çevre Uzunluğu", 8, 6),
            Series(7, "Kavramları Kavra", 2, "Dikdörtgenin Alanı", 12, 7),
            Series(8, "Şimdi Uygula", 30, "Dikdörtgenin Alanı", 2, 8), // anahtarda harf yok: test değil
            Series(9, "Becerini Sergile", 1, "Geometrik Nicelikler", 4, 9),
            Page(10, BookPageKind.AnswerKey, 10),
        };
        var toc = new List<SurveyedTocRow>
        {
            new(null, "Geometrik Nicelikler", 1),
            new(null, "Dikdörtgenin Çevre Uzunluğu", 2),
            new(null, "Dikdörtgenin Alanı", 7),
            new(null, "Cevap Anahtarı", 10),
        };
        AnswerKeyRow Key(string series, int testNo, string answers) => Row(1, testNo, answers) with { KeyPage = 10, Series = series };
        var first = new[]
        {
            Key("Kavramları Kavra", 1, "DCADBBBCDCC"), Key("Kavramları Kavra", 2, "DBDDBDDDBABA"),
            Key("Öğrendiğini Kanıtla", 1, "CABDACCB"), Key("Öğrendiğini Kanıtla", 2, "BDACABDC"),
            Key("Becerini Sergile", 1, "ABCB"), Key("Doğru Yorumla", 1, "B"),
        };
        var second = first.Select(r => r.Series == "Becerini Sergile" ? r with { Series = "BECERİNİ SERGİLE" } : r).ToArray();

        var plan = BookSetupPlanner.Plan(pages, toc, first, second);

        plan.Tests.Select(t => (t.Order, t.Unit, t.TestNo, t.Title, t.FileFrom, t.FileTo, t.AnswerKey)).ShouldBe(
        [
            (1, 1, 1, "Kavramları Kavra 01", 3, 4, "DCADBBBCDCC"),
            (2, 1, 2, "Öğrendiğini Kanıtla 01", 5, 5, "CABDACCB"),
            (3, 1, 3, "Öğrendiğini Kanıtla 02", 6, 6, "BDACABDC"),
            (4, 2, 1, "Kavramları Kavra 02", 7, 7, "DBDDBDDDBABA"),
            (5, 2, 2, "Becerini Sergile 01", 9, 9, "ABCB"),
        ]);
        plan.Tests.ShouldAllBe(t => t.Flags.Count == 0);
        plan.Units.ShouldBe(
        [
            new PlannedUnit(1, "Dikdörtgenin Çevre Uzunluğu", 3, 6),
            new PlannedUnit(2, "Dikdörtgenin Alanı", 7, 9),
        ]);
        plan.OrphanKeys.ShouldBe(["Doğru Yorumla 01"]);
    }

    [Fact]
    public void A_section_label_book_without_toc_groups_tests_by_the_header_topic()
    {
        var pages = new[]
        {
            Series(1, "Kavramları Kavra", 1, "Çevre", 2, 1),
            Series(2, "Kavramları Kavra", 2, "Alan", 2, 2),
            Series(3, "Öğrendiğini Kanıtla", 1, "Alan", 2, 3),
        };

        AnswerKeyRow Key(string series, int testNo) => Row(1, testNo, "AB") with { Series = series };
        var key = new[] { Key("Kavramları Kavra", 1), Key("Kavramları Kavra", 2), Key("Öğrendiğini Kanıtla", 1) };

        var plan = BookSetupPlanner.Plan(pages, [], key, key);

        plan.Tests.Select(t => (t.Unit, t.TestNo, t.Title)).ShouldBe(
            [(1, 1, "Kavramları Kavra 01"), (2, 1, "Kavramları Kavra 02"), (2, 2, "Öğrendiğini Kanıtla 01")]);
        plan.Units.Select(u => u.Title).ShouldBe(["Çevre", "Alan"]);
    }

    [Fact]
    public void Section_labels_with_a_unit_test_key_use_unit_and_test_number_and_infer_units_from_restarts()
    {
        // Issue #1165 (Define soru bankası): başlık "Pusula Kavrama Testi - 1", numara ünite içinde türden bağımsız sıralı;
        // anahtar "1. ÜNİTE: Test 1, Test 2 ..." (bölüm adı yok).
        var pages = new[]
        {
            Page(1, BookPageKind.Cover),
            Series(2, "Pusula Kavrama Testi", 1, "Güneş", 4, 2),
            Series(3, "Pusula Kavrama Testi", 1, "Güneş", 8, 3), // testin ikinci sayfası
            Series(4, "Anahtar Geçiş Testi", 2, "Güneş", 6, 4),
            Page(5, BookPageKind.Cover, 5), // 2. Ünite kapağı
            Series(6, "Pusula Kavrama Testi", 1, "Kuvvet", 8, 6),
            Series(7, "Define Beceri Temelli Test", 2, "Kuvvet", 5, 7),
            Page(8, BookPageKind.AnswerKey, 8),
        };
        var key = new[] { Row(1, 1, "BDABCCBB"), Row(1, 2, "ABCDDB"), Row(2, 1, "DCACCCDA"), Row(2, 2, "ADCCD") };

        var plan = BookSetupPlanner.Plan(pages, [], key, key);

        plan.Tests.Select(t => (t.Unit, t.TestNo, t.Title, t.FileFrom, t.FileTo, t.AnswerKey)).ShouldBe(
        [
            (1, 1, "Pusula Kavrama Testi: Güneş", 2, 3, "BDABCCBB"),
            (1, 2, "Anahtar Geçiş Testi: Güneş", 4, 4, "ABCDDB"),
            (2, 1, "Pusula Kavrama Testi: Kuvvet", 6, 6, "DCACCCDA"),
            (2, 2, "Define Beceri Temelli Test: Kuvvet", 7, 7, "ADCCD"),
        ]);
        plan.OrphanKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Tests_numbered_through_the_book_take_unit_keys_by_unique_test_number()
    {
        // Sayfalar: Test 1–3 kitap boyunca (ünite yazmıyor → hepsi ünite 1); anahtar ünite ünite: "2. Ünite → Test 3".
        var pages = new[]
        {
            Test(2, testNo: 1, maxQuestion: 2),
            Test(3, testNo: 2, maxQuestion: 2),
            Test(4, testNo: 3, maxQuestion: 3),
            Page(5, BookPageKind.AnswerKey),
        };
        AnswerKeyRow Row(int unit, int testNo, string key) =>
            new(unit, testNo, key.Select((c, i) => (i + 1, c)).ToDictionary(x => x.Item1, x => x.c), 5);
        var read = new[] { Row(1, 1, "AB"), Row(1, 2, "CD"), Row(2, 3, "ABC") };

        var plan = BookSetupPlanner.Plan(pages, [], read, read);

        // Sayfada ünite basılı değil: test anahtarın ünitesini alır (içindekiler aralığı ve konu doğru üniteden).
        plan.Tests.Select(t => (t.Unit, t.TestNo, t.AnswerKey)).ShouldBe([(1, 1, "AB"), (1, 2, "CD"), (2, 3, "ABC")]);
        plan.Tests[2].Flags.ShouldBe([BookSetupPlanner.FlagKeyByNumber]);
        plan.Tests[0].Flags.ShouldBeEmpty();
        plan.OrphanKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Repeated_test_numbers_are_not_matched_across_units()
    {
        // Her ünitede Test 1: ünitesi tutmayan anahtar satırı başka üniteye bağlanmaz.
        var pages = new[] { Test(2, 1, 1, maxQuestion: 2), Test(3, 2, 1, maxQuestion: 2), Page(4, BookPageKind.AnswerKey) };
        var read = new[] { new AnswerKeyRow(1, 1, new Dictionary<int, char> { [1] = 'A', [2] = 'B' }, 4),
            new AnswerKeyRow(3, 1, new Dictionary<int, char> { [1] = 'C', [2] = 'D' }, 4) };

        var plan = BookSetupPlanner.Plan(pages, [], read, read);

        plan.Tests[0].AnswerKey.ShouldBe("AB");
        plan.Tests[1].AnswerKey.ShouldBeNull();
        plan.OrphanKeys.ShouldBe(["3-01"]);
        plan.OrphanRows!.Select(r => (r.Label, r.AnswerKey)).ShouldBe([("3-01", "CD")]);
    }

    [Fact]
    public void Setup_log_describes_pages_in_order_and_key_rows()
    {
        var pages = new[]
        {
            Page(12, BookPageKind.Other),
            new SurveyedPage(10, BookPageKind.Test, 10, 1, 1, Series: "Kavrama"),
            Test(11),
        };
        var keys = new[] { new AnswerKeyRow(5, 36, new Dictionary<int, char> { [1] = 'A', [2] = 'B' }, 223, "Prova") };

        BookSetupStage.DescribePages(pages).ShouldBe("10:test u1 t1 s=\"Kavrama\" 11:test 12:other");
        BookSetupStage.DescribeKeys(keys).ShouldBe("p223 u5 t36 s=\"Prova\" n=2");
    }

    [Fact]
    public void Activity_pages_outside_tests_become_keyless_activity_sections_issue1134()
    {
        var pages = new[]
        {
            Page(1, BookPageKind.Cover),
            Test(2, 1, 1, "Doğal Sayılar", maxQuestion: 4, printed: 12),
            Page(3, BookPageKind.Activity, printed: 13),
            Page(4, BookPageKind.Activity, printed: 14),
            Test(5, 2, 1, "Kesirler", maxQuestion: 3, printed: 15),
            Page(6, BookPageKind.Activity, printed: 16), // içindekiler satırı burada başlıyor: ayrı bölüm
            Page(7, BookPageKind.Activity, printed: 17),
            Page(8, BookPageKind.AnswerKey),
        };
        var toc = new[] { new SurveyedTocRow(1, "Doğal Sayılar", 12), new SurveyedTocRow(2, "Kesirler", 15), new SurveyedTocRow(2, "Kesir Etkinlikleri", 17) };
        var key = new[] { Row(1, 1, "ABCD"), Row(2, 1, "ABC") };

        var plan = BookSetupPlanner.Plan(pages, toc, key, key);

        plan.Tests.Select(t => (t.Order, t.Unit, t.TestNo, t.FileFrom, t.FileTo, Activity: t.Flags.Contains(BookSetupPlanner.FlagActivity)))
            .ShouldBe(
            [
                (1, 1, 1, 2, 2, false),
                (2, 1, 1, 3, 4, true),
                (3, 2, 1, 5, 5, false),
                (4, 2, 1, 6, 6, true),
                (5, 2, 2, 7, 7, true),
            ]);
        plan.Tests[1].Title.ShouldBe("Doğal Sayılar");
        plan.Tests[4].Title.ShouldBe("Kesir Etkinlikleri");
        plan.Tests.Where(t => t.Flags.Contains(BookSetupPlanner.FlagActivity))
            .ShouldAllBe(t => t.AnswerKey == null && t.ExpectedQuestions == null && t.Flags.Count == 1);
        // Etkinlik bölümü anahtar beklemez: testlerin anahtarı var, kitap "anahtarsız" sayılmaz.
        plan.NoAnswerKey.ShouldBeFalse();
        plan.Units.Select(u => (u.Unit, u.FileFrom, u.FileTo)).ShouldBe([(1, 2, 4), (2, 5, 7)]);
    }

    [Fact]
    public void A_book_with_only_activity_pages_is_planned_in_sections_of_limited_length_issue1134()
    {
        var pages = Enumerable.Range(1, BookSetupPlanner.MaxActivitySectionPages + 2)
            .Select(file => Page(file, BookPageKind.Activity)).ToArray();

        var plan = BookSetupPlanner.Plan(pages, [], [], []);

        plan.Tests.Select(t => (t.Unit, t.TestNo, t.FileFrom, t.FileTo)).ShouldBe(
        [
            (1, 1, 1, BookSetupPlanner.MaxActivitySectionPages),
            (1, 2, BookSetupPlanner.MaxActivitySectionPages + 1, BookSetupPlanner.MaxActivitySectionPages + 2),
        ]);
        plan.NoAnswerKey.ShouldBeFalse();
        BookImportService.BookTestName(new ExamApp.Api.Data.BookImportJobTest
        {
            Unit = 1, TestNo = 2, FlagsJson = "[\"activity\"]",
        }).ShouldBe("1-E02 · Etkinlikler");
    }

    [Fact]
    public void An_activity_page_inside_a_section_of_a_headerless_book_stays_in_that_section_issue1134()
    {
        var pages = new[]
        {
            Page(1, BookPageKind.Cover),
            Test(2, maxQuestion: 5),
            Page(3, BookPageKind.Activity),
            Test(4, maxQuestion: 12),
        };

        var plan = BookSetupPlanner.Plan(pages, [], [], []);

        plan.Tests.Select(t => (t.FileFrom, t.FileTo, t.Flags.Count)).ShouldBe([(2, 4, 0)]);
    }
}
