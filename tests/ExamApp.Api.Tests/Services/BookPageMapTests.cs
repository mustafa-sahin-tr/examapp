using ExamApp.Api.Data;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #577: bastırılmış ↔ dosya sayfası dönüşümü. TT MatLab benzeri fasiküllü kitap: her fasikül kendi ofsetiyle ve
/// bastırılmış numaralar fasiküller arasında tekrar eder (fasikül 1 ve 2 ikisi de 1'den başlar), fasikül 3 +109 ile 210'dan.
/// </summary>
public class BookPageMapTests
{
    // Kapak + iç kapak (dosya 1-2) aralıkta değil.
    private static readonly BookPageOffset[] MatLab =
    [
        new() { FileFrom = 3, FileTo = 50, Offset = -2 },     // fasikül 1: bastırılmış 1-48
        new() { FileFrom = 51, FileTo = 100, Offset = -50 },  // fasikül 2: bastırılmış 1-50
        new() { FileFrom = 101, FileTo = 150, Offset = 109 }, // fasikül 3: bastırılmış 210-259
    ];

    private static BookPageMap Map() => new(
        MatLab,
        [new BookAnswerKeyRange { FileFrom = 151, FileTo = 160 }],
        [
            new BookTocEntry { FileFrom = 3, FileTo = 20, GradeId = 5, SubjectId = 1, TopicId = 10 },
            new BookTocEntry { FileFrom = 21, FileTo = 50, GradeId = 5, SubjectId = 1, TopicId = 11, SubTopicId = 110 },
        ]);

    [Theory]
    [InlineData(3, 1)]
    [InlineData(50, 48)]
    [InlineData(51, 1)]
    [InlineData(60, 10)]
    [InlineData(100, 50)]
    [InlineData(101, 210)]
    [InlineData(150, 259)]
    public void ToPrinted_UsesTheFasciclesOffset(int file, int printed) => Map().ToPrinted(file).ShouldBe(printed);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(151)]
    public void ToPrinted_OutsideEveryRange_IsNull(int file) => Map().ToPrinted(file).ShouldBeNull();

    [Fact]
    public void ToFilePages_RepeatedPrintedNumber_ReturnsEveryFascicle()
    {
        Map().ToFilePages(10).ShouldBe([12, 60]);
        Map().ToFilePages(48).ShouldBe([50, 98]);
        Map().ToFilePages(49).ShouldBe([99]);   // yalnız fasikül 2
        Map().ToFilePages(220).ShouldBe([111]); // fasikül 3
        Map().ToFilePages(100).ShouldBeEmpty(); // hiçbir fasikülde yok
    }

    [Fact]
    public void ToFilePages_Near_RanksTheClosestFirst()
    {
        Map().ToFilePages(10, near: 70).ShouldBe([60, 12]);
        Map().ToFilePages(10, near: 5).ShouldBe([12, 60]);
        Map().ToFilePages(10, near: 36).ShouldBe([12, 60]); // eşit uzaklık → küçük önce
    }

    [Fact]
    public void RoundTrip_EveryFilePage_IsAmongTheFilePagesOfItsPrintedNumber()
    {
        var map = Map();
        for (var file = 3; file <= 150; file++)
            map.ToFilePages(map.ToPrinted(file)!.Value).ShouldContain(file);
    }

    [Fact]
    public void AnswerKey_And_Toc_Lookup()
    {
        var map = Map();
        map.IsAnswerKey(151).ShouldBeTrue();
        map.IsAnswerKey(160).ShouldBeTrue();
        map.IsAnswerKey(150).ShouldBeFalse();
        map.TocFor(3)!.TopicId.ShouldBe(10);
        map.TocFor(20)!.TopicId.ShouldBe(10);
        map.TocFor(21)!.SubTopicId.ShouldBe(110);
        map.TocFor(51).ShouldBeNull();
    }

    [Fact]
    public void Validate_AcceptsAdjacentRanges()
    {
        BookPageMap.Validate(
            [(3, 50, -2), (51, 100, -50), (101, 150, 109)],
            [(151, 160)],
            [(3, 20), (21, 50)]).ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 9)]
    [InlineData(1, BookPageMap.MaxFilePage + 1)]
    public void Validate_InvalidRange(int from, int to)
    {
        BookPageMap.Validate([(from, to, 0)], [], []).ShouldBe("labeling.books.invalidRange");
        BookPageMap.Validate([], [(from, to)], []).ShouldBe("labeling.books.invalidRange");
        BookPageMap.Validate([], [], [(from, to)]).ShouldBe("labeling.books.invalidRange");
    }

    [Fact]
    public void Validate_OffsetThatPrintsBelowOne_IsRejected()
    {
        BookPageMap.Validate([(3, 50, -3)], [], []).ShouldBe("labeling.books.invalidOffset");
        BookPageMap.Validate([(3, 50, -2)], [], []).ShouldBeNull();
    }

    [Fact]
    public void Validate_Overlaps_AreRejectedPerSet()
    {
        BookPageMap.Validate([(3, 50, 0), (50, 60, 0)], [], []).ShouldBe("labeling.books.offsetOverlap");
        BookPageMap.Validate([], [(10, 20), (5, 10)], []).ShouldBe("labeling.books.answerKeyOverlap");
        BookPageMap.Validate([], [], [(10, 30), (15, 20)]).ShouldBe("labeling.books.tocOverlap"); // iç içe de çakışma
        // Kümeler arası çakışma serbest (cevap anahtarı içindekiler aralığında olabilir).
        BookPageMap.Validate([(1, 100, 0)], [(90, 100)], [(1, 100)]).ShouldBeNull();
    }
}
