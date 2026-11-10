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
}
