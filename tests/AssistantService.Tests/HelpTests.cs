using AssistantService.Help;
using AssistantService.Tools;
using Shouldly;
using static AssistantService.Tests.ToolHarness;

namespace AssistantService.Tests;

/// <summary>Issue #506: the user guide index behind search_help.</summary>
public class HelpTests
{
    private static HelpArticle Article(string slug, string title, string roles = "Student", string keywords = "", string body = "Metin.") =>
        HelpIndex.Parse(slug, $"---\nslug: {slug}\ntitle: {title}\nroles: {roles}\nkeywords: {keywords}\n---\n{body}\n");

    [Fact]
    public void The_embedded_guide_loads_and_every_article_is_for_a_role()
    {
        Guide.Count.ShouldBeGreaterThanOrEqualTo(10);
        Guide.Articles.ShouldAllBe(a => a.Roles.Count > 0 && a.Body.Length > 0);
    }

    [Theory]
    [InlineData("velimi nasıl eklerim?", "veli-ekleme")]
    [InlineData("VELİMİ NASIL EKLERİM", "veli-ekleme")]
    [InlineData("velim neleri görebiliyor", "veli-ne-gorur")]
    [InlineData("şifremi unuttum", "sifre-degistir")]
    [InlineData("ödevlerim nerede", "odevlerim")]
    [InlineData("yanlış yaptığım sorular", "yanlislarim")]
    [InlineData("günlük serim bozuldu mu", "gunun-sorulari")]
    [InlineData("sınıfımı değiştirmek istiyorum", "sinif-okul")]
    public void A_student_question_finds_the_right_article_first(string query, string slug) =>
        Guide.Search(query, ["Student"]).First().Article.Slug.ShouldBe(slug);

    [Fact]
    public void Only_the_callers_role_articles_come_back()
    {
        var index = new HelpIndex([
            Article("veli-ekleme", "Velimi nasıl eklerim?", keywords: "veli"),
            Article("cocuk-hesabi", "Çocuğuma hesap açma", roles: "Parent", keywords: "veli, cocuk"),
        ]);

        index.Search("veli", ["Student"]).Select(h => h.Article.Slug).ShouldBe(["veli-ekleme"]);
        index.Search("veli", ["Parent"]).Select(h => h.Article.Slug).ShouldBe(["cocuk-hesabi"]);
        index.Search("veli", ["Teacher"]).ShouldBeEmpty();
    }

    [Fact]
    public void Question_words_alone_and_body_only_matches_return_nothing()
    {
        var index = new HelpIndex([Article("sifre", "Şifre değiştirme", body: "Ayarlar sayfasını aç.")]);

        index.Search("nasıl ne nerede?", ["Student"]).ShouldBeEmpty();
        index.Search("ayarlar", ["Student"]).ShouldBeEmpty();
        index.Search("şifre ayarlar", ["Student"]).Single().Article.Slug.ShouldBe("sifre");
    }

    [Fact]
    public void At_most_three_articles_come_back()
    {
        var index = new HelpIndex(Enumerable.Range(1, 6).Select(i => Article($"a-{i}", $"Rozet {i}")));
        index.Search("rozet", ["Student"]).Count.ShouldBe(HelpIndex.MaxResults);
    }

    [Theory]
    [InlineData("slug: a\ntitle: T\nroles: Student\n---\nMetin", "front matter")]
    [InlineData("---\ntitle: T\nroles: Student\n---\nMetin", "'slug' is required")]
    [InlineData("---\nslug: Veli_Ekleme\ntitle: T\nroles: Student\n---\nMetin", "kebab-case")]
    [InlineData("---\nslug: a\ntitle: T\nroles: Ogrenci\n---\nMetin", "not a realm role")]
    [InlineData("---\nslug: a\ntitle: T\nroles: Student\n---\n", "body is empty")]
    public void A_malformed_article_is_rejected(string text, string reason) =>
        Should.Throw<InvalidOperationException>(() => HelpIndex.Parse("x.md", text)).Message.ShouldContain(reason);

    [Fact]
    public void A_slug_used_twice_is_rejected() =>
        Should.Throw<InvalidOperationException>(() => new HelpIndex([Article("a", "Bir"), Article("a", "İki")]));

    [Fact]
    public async Task Search_help_returns_title_slug_and_text()
    {
        var result = await new SearchHelpTool(Guide).ExecuteAsync(Student(), Args(new { query = "velimi nasıl eklerim?" }));

        result.Ok.ShouldBeTrue();
        var first = result.Data!["results"]!.AsArray()[0]!;
        first["slug"]!.GetValue<string>().ShouldBe("veli-ekleme");
        first["text"]!.GetValue<string>().ShouldContain("Daveti gönder");
    }

    [Fact]
    public async Task Search_help_rejects_an_empty_query()
    {
        var result = await new SearchHelpTool(Guide).ExecuteAsync(Student(), Args(new { query = "  " }));
        result.ErrorCode.ShouldBe("InvalidArguments");
    }
}
