using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #365 (S3): <c>POST /api/study-items/book-pages/lookup</c>. Bucket'lar özel olduğu için editör kitap sayfasını
/// tarayıcıdan yoklayamaz; sunucu nesne varlığını kontrol eder, var olan sayfa için saklama yolu + gateway host'u için
/// geçerli imzalı önizleme URL'si döner. Yalnız onaylı öğretmen (Admin dahil değil); kullanıcı başına rate limit; kitap adı
/// sıkı doğrulanır.
/// </summary>
public class StudyBookPageLookupEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Endpoint = "/api/study-items/book-pages/lookup";
    private const string GatewayMinioHost = "localhost:9000"; // IntegrationApiFactory: MinioConfig__Endpoint
    private const string TestSk = "x"; // MinioConfig__PresignSecretKey

    private FakeMinIoService Storage => (FakeMinIoService)Factory.Services.GetRequiredService<IMinIoService>();

    private static object Body(params (string Book, int Page)[] pages) =>
        new { pages = pages.Select(p => new { book = p.Book, pageNumber = p.Page }).ToArray() };

    private async Task<HttpClient> ApprovedTeacherAsync(int userId, string sub)
    {
        await SeedApprovedTeacherAsync(userId);
        return await ClientAsAsync(userId, "Teacher", sub, "Teacher");
    }

    [Fact]
    public async Task Approved_teacher_gets_existence_stored_path_and_a_signed_preview()
    {
        const string book = "Fen Bilimleri Kitabı 36551";
        Storage.Put($"/img/study-pages/books/{book}/page_2.webp", [1, 2, 3]);
        var teacher = await ApprovedTeacherAsync(36551, "kc-s3-lookup-teacher");

        var res = await teacher.PostAsJsonAsync(Endpoint, Body((book, 2), (book, 3)));

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = JsonNode.Parse(await res.Content.ReadAsStringAsync())!.AsArray();
        items.Count.ShouldBe(2);

        var found = items[0]!;
        found["book"]!.GetValue<string>().ShouldBe(book);
        found["pageNumber"]!.GetValue<int>().ShouldBe(2);
        found["exists"]!.GetValue<bool>().ShouldBeTrue();
        found["minioUrl"]!.GetValue<string>().ShouldBe($"/img/study-pages/books/{book}/page_2.webp"); // imzasız saklama yolu
        var preview = found["previewUrl"]!.GetValue<string>();
        preview.ShouldStartWith("/img/study-pages/books/Fen%20Bilimleri%20Kitab%C4%B1%2036551/page_2.webp?");
        var verdict = SigV4PresignVerifier.VerifyImgUrl(preview, GatewayMinioHost, TestSk, DateTime.UtcNow);
        verdict.Valid.ShouldBeTrue(verdict.Reason);

        var missing = items[1]!;
        missing["exists"]!.GetValue<bool>().ShouldBeFalse();
        missing["minioUrl"].ShouldBeNull();
        missing["previewUrl"].ShouldBeNull();
    }

    [Fact]
    public async Task Admin_is_forbidden_least_privilege()
    {
        var admin = await ClientAsAsync(36552, "Admin", "kc-s3-lookup-admin", "Admin");

        (await admin.PostAsJsonAsync(Endpoint, Body(("Mat 36552", 1)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lookup_is_rate_limited_per_user()
    {
        // IntegrationApiFactory: RateLimiting__StudyBookPageLookup__PermitLimit=10 / 3600 sn.
        var teacher = await ApprovedTeacherAsync(36557, "kc-s3-lookup-ratelimit");
        for (var i = 0; i < 10; i++)
            (await teacher.PostAsJsonAsync(Endpoint, Body(("Mat", 1)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var limited = await teacher.PostAsJsonAsync(Endpoint, Body(("Mat", 1)));

        limited.StatusCode.ShouldBe((HttpStatusCode)429);
        JsonNode.Parse(await limited.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().ShouldBe("RateLimited");

        // Başka öğretmenin kovası ayrı.
        var other = await ApprovedTeacherAsync(36558, "kc-s3-lookup-ratelimit-2");
        (await other.PostAsJsonAsync(Endpoint, Body(("Mat", 1)))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Student_is_forbidden()
    {
        var student = await ClientAsAsync(36553, "Student", "kc-s3-lookup-student", "Student");

        (await student.PostAsJsonAsync(Endpoint, Body(("Mat", 1)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unapproved_teacher_is_forbidden()
    {
        await WithDbAsync(async db =>
        {
            db.Teachers.Add(new Teacher { UserId = 36554, AccountApprovedAt = null });
            await db.SaveChangesAsync();
        });
        var teacher = await ClientAsAsync(36554, "Teacher", "kc-s3-lookup-unapproved", "Teacher");

        (await teacher.PostAsJsonAsync(Endpoint, Body(("Mat", 1)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        (await Anonymous().PostAsJsonAsync(Endpoint, Body(("Mat", 1)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(36560, "../../exam-questions/question-transfer/exports/default")]
    [InlineData(36561, "a/b")]
    [InlineData(36562, "a&b")]
    [InlineData(36563, "a%2Fb")]
    [InlineData(36564, " lead")]
    public async Task Invalid_book_names_get_400(int userId, string book)
    {
        var teacher = await ApprovedTeacherAsync(userId, $"kc-s3-lookup-bad-{userId}");

        var res = await teacher.PostAsJsonAsync(Endpoint, Body(("Mat", 1), (book, 1)));

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_or_oversized_page_lists_and_bad_page_numbers_get_400()
    {
        var teacher = await ApprovedTeacherAsync(36556, "kc-s3-lookup-limits");

        (await teacher.PostAsJsonAsync(Endpoint, new { pages = Array.Empty<object>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.PostAsJsonAsync(Endpoint, Body(Enumerable.Range(1, 201).Select(n => ("Mat", n)).ToArray())))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.PostAsJsonAsync(Endpoint, Body(("Mat", 0)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
