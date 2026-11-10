using ExamApp.Api.Services.BookImport;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Testin soru numaralaması: sayfaya dayalı (kurulumda okunan basılı en büyük no) ama bulunan toplam anahtarın uzunluğuna
/// eşitse sıralı — kurulumda yanlış okunan bir sayfa no'su sonraki soruları kaydırmasın.
/// </summary>
public class BookQuestionNumberingTests
{
    [Fact]
    public void PrintedMax_AnchorsPages_WhenTheTotalDoesNotMatchTheKey()
    {
        // 1. sayfada 6 sorudan 5'i bulundu: 2. sayfa yine 7'den başlar.
        var numbering = BookQuestionImportStage.NumberPages([(6, 5), (12, 6)], keyLength: 12);

        numbering.ShouldBe([(1, (int?)6), (7, (int?)12)]);
    }

    [Fact]
    public void WrongPrintedMax_IsIgnored_WhenTheTotalMatchesTheKey()
    {
        // Kurulum 1. sayfanın en büyük no'sunu 12 okudu, sayfada 6 soru var; toplam 12 = anahtar → sıralı.
        var numbering = BookQuestionImportStage.NumberPages([(12, 6), (18, 6)], keyLength: 12);

        numbering.ShouldBe([(1, (int?)null), (7, (int?)null)]);
    }

    [Fact]
    public void FailedPage_ConsumesItsPrintedNumbers_AndKeepsAnchoring()
    {
        var numbering = BookQuestionImportStage.NumberPages([(6, null), (12, 6)], keyLength: 6);

        numbering.ShouldBe([(1, (int?)6), (7, (int?)12)]);
    }

    [Fact]
    public void NoKey_KeepsAnchoring()
    {
        var numbering = BookQuestionImportStage.NumberPages([(6, 4), (null, 3)], keyLength: null);

        numbering.ShouldBe([(1, (int?)6), (7, (int?)null)]);
    }

    [Fact]
    public void PrintedMaxBelowTheStart_IsIgnored()
    {
        var numbering = BookQuestionImportStage.NumberPages([(6, 8), (5, 4)], keyLength: 20);

        numbering.ShouldBe([(1, (int?)6), (9, (int?)null)]);
    }

    [Fact]
    public void ExtraBoxOnAPage_DoesNotShiftTheFollowingPages()
    {
        // 1. sayfada 2 soru var, kural satırı ayrı kutu sanıldı (3 kutu): 2. ve 3. sayfa yine basılı no'dan başlar.
        var numbering = BookQuestionImportStage.NumberPages([(2, 3), (4, 2), (6, 2)], keyLength: 6);

        numbering.ShouldBe([(1, (int?)2), (3, (int?)4), (5, (int?)6)]);
    }
}
