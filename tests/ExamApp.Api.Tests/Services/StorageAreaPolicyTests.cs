using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #365 (S2): istemciden gelen görsel adresinin saklanmadan önce normalizasyonu. Yalnız göreli
/// <c>/img/{bucket}/{key}</c> ve alanın allowlist'i kabul edilir; imzalı URL'nin query'si atılır (imza asla saklanmaz),
/// yol bir kez decode edilir (imzalı URL'den gelen <c>%20</c> ham anahtara döner). Bu, istemcinin saklanan alana
/// <c>question-transfer/...</c> yazıp imzalatmasını ("imza kâhini") engeller.
/// </summary>
public class StorageAreaPolicyTests
{
    private readonly StorageAreaPolicy _policy = new("exam-questions");

    [Theory]
    [InlineData("/img/study-pages/books/Fen/page_1.webp", "/img/study-pages/books/Fen/page_1.webp")]
    [InlineData("  /img/study-pages/pages/3/a.png  ", "/img/study-pages/pages/3/a.png")]
    // Tarayıcıya verilen imzalı URL geri gelirse: query atılır, yol decode edilir.
    [InlineData("/img/study-pages/books/Matematik%20Kitab%C4%B1/page_1.webp?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=abc",
        "/img/study-pages/books/Matematik Kitabı/page_1.webp")]
    [InlineData("/img/study-pages/books/Fen/page_1.webp#frag", "/img/study-pages/books/Fen/page_1.webp")]
    public void Valid_study_page_urls_are_normalized(string input, string expected)
    {
        _policy.TryNormalizeClientUrl(input, StorageArea.StudyPage, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_valid_and_normalizes_to_null(string? input)
    {
        _policy.TryNormalizeClientUrl(input, StorageArea.StudyPage, out var normalized).ShouldBeTrue();
        normalized.ShouldBeNull();
        _policy.TryNormalizeRequiredClientUrl(input, StorageArea.StudyPage, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://evil.example.com/img/study-pages/books/x/page_1.webp")] // dış/tam URL
    [InlineData("http://localhost:5678/img/study-pages/books/x/page_1.webp")]
    [InlineData("//evil.example.com/img/study-pages/books/x.webp")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("/img/study-pages/books/../../exam-questions/question-transfer/index.json")] // ../
    [InlineData("/img/study-pages/books/%2e%2e/%2e%2e/x.webp")]                          // encode edilmiş ../
    [InlineData("/img/study-pages/books%2F..%2F..%2Fx.webp")]
    [InlineData("/img/study-pages/books\\x.webp")]
    [InlineData("/img/study-pages/books//x.webp")]
    [InlineData("/img/study-pages/books/x%00.webp")]                                      // kontrol karakteri
    [InlineData("/img/study-pages/books/x%3Fy.webp")]                                     // decode sonrası '?'
    [InlineData("/img/exam-questions/questions/1/a.jpg")]                                  // yabancı bucket (alan dışı)
    [InlineData("/img/exam-questions/question-transfer/exports/default/index.json")]
    [InlineData("/img/study-pages/question-transfer/x.webp")]
    [InlineData("/img/study-pages/other/x.webp")]                                          // prefix dışı
    [InlineData("/img/study-pages/books/")]                                                // boş anahtar
    [InlineData("/img/study-pages")]
    [InlineData("img/study-pages/books/x.webp")]                                           // göreli ama /img/ değil
    [InlineData("/api/study-items/1")]
    [InlineData("/img/study-pages/books/%E0%A4%A.webp")]                                   // bozuk percent-encoding
    [InlineData("/img/study-pages/books/A%26B/page_1.webp")]                               // & → gateway'den imzalı geçemez
    [InlineData("/img/study-pages/books/100%2541/page_1.webp")]                            // decode sonrası %41
    public void Anything_outside_the_area_allowlist_is_rejected(string input)
    {
        _policy.TryNormalizeClientUrl(input, StorageArea.StudyPage, out var normalized).ShouldBeFalse();
        normalized.ShouldBeNull();
    }

    [Fact]
    public void Overlong_input_is_rejected()
    {
        var input = "/img/study-pages/books/" + new string('a', StorageAreaPolicy.MaxUrlLength) + ".webp";
        _policy.TryNormalizeClientUrl(input, StorageArea.StudyPage, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/img/exam-questions/passages/p.jpg", true)]
    [InlineData("/img/exam-questions/questions/1/q.jpg", true)]
    [InlineData("/img/exam-questions/answers/1/a.jpg", true)]
    [InlineData("/img/exam-questions/question-transfer/exports/default/bundle-0001.zip", false)]
    [InlineData("/img/other-bucket/passages/p.jpg", false)]
    public void Question_image_area_uses_the_configured_default_bucket(string input, bool ok)
    {
        _policy.TryNormalizeClientUrl(input, StorageArea.QuestionImage, out _).ShouldBe(ok);
        new StorageAreaPolicy("custom-bucket")
            .TryNormalizeClientUrl(input.Replace("exam-questions", "custom-bucket"), StorageArea.QuestionImage, out _)
            .ShouldBe(ok && !input.Contains("other-bucket"));
    }

    [Fact]
    public void Question_transfer_prefix_is_denied_case_insensitively_for_every_area()
    {
        foreach (var area in Enum.GetValues<StorageArea>())
        {
            _policy.IsAllowed("worksheets", "question-transfer/x", [area]).ShouldBeFalse();
            _policy.IsAllowed("exam-questions", "QUESTION-TRANSFER/x", [area]).ShouldBeFalse();
        }
    }

    // #365 D1: paragraf görseli için alan allowlist'i passages/ ile daraltılır.
    [Theory]
    [InlineData("/img/exam-questions/passages/1/p.jpg?X-Amz-Signature=x", true, "/img/exam-questions/passages/1/p.jpg")]
    [InlineData("/img/exam-questions/passages/p.jpg", true, "/img/exam-questions/passages/p.jpg")]
    [InlineData("", true, null)]
    [InlineData("/img/exam-questions/questions/1/question.jpg", false, null)]
    [InlineData("/img/exam-questions/answers/1/a.jpg", false, null)]
    [InlineData("/img/exam-questions/passages/", false, null)]
    [InlineData("/img/exam-questions/passages/../question-transfer/x.zip", false, null)]
    [InlineData("/img/exam-questions/passages/A&B.jpg", false, null)]
    public void Prefix_restricted_normalization_accepts_only_keys_under_the_prefix(string input, bool ok, string? expected)
    {
        _policy.TryNormalizeClientUrl(input, StorageArea.QuestionImage, StorageAreaPolicy.PassageImagePrefix, out var normalized)
            .ShouldBe(ok);
        normalized.ShouldBe(expected);
    }
}
