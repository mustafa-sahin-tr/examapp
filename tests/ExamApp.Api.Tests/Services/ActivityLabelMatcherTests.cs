using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #580: cevap anahtarı etiketi ↔ etkinlik etiketi eşleme kuralı (<see cref="ActivityLabelMatcher"/>).</summary>
public class ActivityLabelMatcherTests
{
    [Theory]
    [InlineData("Etkinlik 9", "ETKİNLİK 9")]
    [InlineData("Etkinlik 9", "9.")]
    [InlineData("9. Etkinlik", "Etkinlik 9")]
    [InlineData("ETKINLIK 9", "etkinlik 9")]          // ASCII I (dotless) ≈ İ
    [InlineData("Alıştırma 2", "ALIŞTIRMA 2")]
    [InlineData("Alıştırma 2", "alistirma 2")]        // Türkçe harfler ASCII'ye katlanır
    [InlineData("Etkinlik 3.2", "3.2")]
    [InlineData("Etkinlik 07", "Etkinlik 7")]         // baştaki sıfır
    [InlineData("Etkinlik A", "etkinlik a")]          // numarasız: metin eşitliği
    [InlineData("Etkinlik 9a", "Etkinlik 9")]         // tek harfli son ek gürültü
    [InlineData("Etkinlik No: 9", "ETKİNLİK 9")]       // numara belirteci
    [InlineData("9 numaralı etkinlik", "Etkinlik 9")]
    [InlineData("Test 01", "TEST 1")]                  // test başlıkları (MCQ anahtarı)
    [InlineData("Test-1", "Test 01")]
    [InlineData("Destekleme Testi 2", "DESTEKLEME TESTİ 2")]
    public void Matches(string a, string b)
    {
        ActivityLabelMatcher.Matches(a, b).ShouldBeTrue();
        ActivityLabelMatcher.Matches(b, a).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Etkinlik 9", "Etkinlik 10")]
    [InlineData("Etkinlik 9", "Alıştırma 9")]        // tür kelimesi farklı
    [InlineData("Test 1", "Test 2")]
    [InlineData("Etkinlik 3.2", "Etkinlik 3")]
    [InlineData("Etkinlik 9", "Etkinlik")]           // biri numaralı, diğeri değil
    [InlineData("Etkinlik A", "Etkinlik B")]
    [InlineData("", "9")]
    [InlineData(null, "9")]
    [InlineData("...", "9")]
    [InlineData("Etkinlik 99999999999", "Etkinlik 99999999999")] // int'e sığmaz → eşleşmez
    public void DoesNotMatch(string? a, string? b)
    {
        ActivityLabelMatcher.Matches(a, b).ShouldBeFalse();
    }

    [Fact]
    public void Parse_SplitsKindWordsAndNumbers()
    {
        var parsed = ActivityLabelMatcher.Parse("  9. ETKİNLİK  ")!;
        parsed.Kind.ShouldBe("etkinlik");
        parsed.Number.ShouldBe([9]);
        ActivityLabelMatcher.Parse("1. Ünite Değerlendirme 4")!.Kind.ShouldBe("unite degerlendirme");
    }
}
