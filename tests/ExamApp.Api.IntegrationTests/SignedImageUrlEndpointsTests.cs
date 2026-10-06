using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #365 (S2): gerçek MVC pipeline'ında (Program.cs JSON ayarları) öğrencinin kullandığı ana uçlar görselleri
/// kısa ömürlü imzalı <c>/img/...?X-Amz-...</c> göreli URL olarak döner; imza gateway'in MinIO host'u
/// (<c>MinioConfig:Endpoint</c> = localhost:9000) için geçerlidir. DB'de saklanan <c>question-transfer/</c> değeri bir
/// görsel alanında bulunsa bile imzalanmaz. İstemciden gelen allowlist dışı görsel adresleri 400 alır.
/// </summary>
public class SignedImageUrlEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // IntegrationApiFactory: MinioConfig__Endpoint=localhost:9000, AccessKey/SecretKey=x, BucketName=test.
    private const string GatewayMinioHost = "localhost:9000";
    private const string TestSk = "x";
    private const string QuestionImage = "/img/test/questions/abc/question.jpg";
    private const string QuestionImageV2 = "/img/test/questions/abc/question-v2.jpg"; // canvas varyantı
    private const string AnswerImage = "/img/test/questions/abc/1.jpg";
    private const string PassageImage = "/img/test/passages/p.jpg";
    private const string CoverImage = "/img/worksheets/77-background.png";
    private const string TransferObject = "/img/test/question-transfer/exports/default/index.json";

    private sealed record Seeded(int WorksheetId, int QuestionId, int CorrectAnswerId);

    private async Task<Seeded> SeedAsync(int userId)
    {
        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "6" };
            var subject = new Subject { Name = "Fen" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();

            db.Students.Add(new Student { UserId = userId, StudentNumber = $"S{userId}", GradeId = grade.Id });
            var ws = new Worksheet
            {
                Name = "Imzali", Description = "", GradeId = grade.Id, SubjectId = subject.Id, MaxDurationSeconds = 600,
                StudentVisibility = WorksheetStudentVisibility.Normal, ImageUrl = CoverImage,
            };
            var passage = new Passage { Title = "P", Text = "metin", ImageUrl = PassageImage };
            db.AddRange(ws, passage);
            await db.SaveChangesAsync();

            var q = new Question
            {
                Text = "Soru", SubjectId = subject.Id, Point = 10, DifficultyLevel = 1, ImageUrl = QuestionImage, PassageId = passage.Id,
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();

            var correct = new Answer { QuestionId = q.Id, Text = "A", Tag = "A", Order = 0, ImageUrl = AnswerImage };
            // Görsel alanına bir şekilde soru bankası paketi yazılmış olsa bile imzalanmamalı.
            var poisoned = new Answer { QuestionId = q.Id, Text = "B", Tag = "B", Order = 1, ImageUrl = TransferObject };
            db.Answers.AddRange(correct, poisoned);
            await db.SaveChangesAsync();
            q.CorrectAnswerId = correct.Id;
            db.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = 1 });
            await db.SaveChangesAsync();
            return new Seeded(ws.Id, q.Id, correct.Id);
        });
    }

    /// <summary>JSON'daki tüm <c>*imageUrl</c> değerlerini toplar.</summary>
    private static List<string> ImageUrls(JsonNode? node)
    {
        var found = new List<string>();
        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var (name, value) in o)
                    {
                        if ((name.EndsWith("imageUrl", StringComparison.OrdinalIgnoreCase) || name.EndsWith("imageUrlV2", StringComparison.OrdinalIgnoreCase)) &&
                            value is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                            found.Add(s);
                        Walk(value);
                    }
                    break;
                case JsonArray a:
                    foreach (var item in a) Walk(item);
                    break;
            }
        }
        Walk(node);
        return found;
    }

    /// <summary>Beklenen her saklı değer imzalı ve gateway host'u için geçerli; question-transfer imzasız.</summary>
    private static void ShouldBeSignedFor(List<string> urls, params string[] storedValues)
    {
        foreach (var stored in storedValues)
        {
            var match = urls.FirstOrDefault(u => u.StartsWith(stored + "?", StringComparison.Ordinal));
            match.ShouldNotBeNull($"{stored} signed URL not found in: {string.Join(" | ", urls)}");
            var result = SigV4PresignVerifier.VerifyImgUrl(match, GatewayMinioHost, TestSk, DateTime.UtcNow);
            result.Valid.ShouldBeTrue($"{stored}: {result.Reason}");
            match.ShouldContain("X-Amz-Expires=14400");
        }

        urls.Where(u => u.Contains("question-transfer", StringComparison.OrdinalIgnoreCase))
            .ShouldAllBe(u => !u.Contains("X-Amz-"));
        urls.Where(u => u.StartsWith("/img/", StringComparison.Ordinal) && !u.Contains("question-transfer"))
            .ShouldAllBe(u => u.Contains("X-Amz-Signature="));
    }

    [Fact]
    public async Task Worksheet_detail_returns_signed_cover_and_sample_question_images()
    {
        const int userId = 36501;
        var seeded = await SeedAsync(userId);
        var student = await ClientAsAsync(userId, "Student", "kc-s2-detail", "Student");

        var res = await student.GetAsync($"/api/worksheet/{seeded.WorksheetId}/detail");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        ShouldBeSignedFor(ImageUrls(JsonNode.Parse(await res.Content.ReadAsStringAsync())), CoverImage, QuestionImage);
    }

    [Fact]
    public async Task Test_instance_and_canvas_return_signed_question_answer_and_passage_images()
    {
        const int userId = 36502;
        var seeded = await SeedAsync(userId);
        var student = await ClientAsAsync(userId, "Student", "kc-s2-test", "Student");

        var start = await student.PostAsync($"/api/worksheet/start-test/{seeded.WorksheetId}", null);
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        var instanceId = (await start.Content.ReadFromJsonAsync<TestStartResultDto>(Json))!.InstanceId;

        var instance = await student.GetAsync($"/api/worksheet/test-instance/{instanceId}");
        instance.StatusCode.ShouldBe(HttpStatusCode.OK);
        var instanceUrls = ImageUrls(JsonNode.Parse(await instance.Content.ReadAsStringAsync()));
        ShouldBeSignedFor(instanceUrls, QuestionImage, QuestionImageV2, AnswerImage, PassageImage);
        instanceUrls.ShouldContain(TransferObject); // aynen, imzasız

        var canvas = await student.GetAsync($"/api/worksheet/test-canvas-instance/{instanceId}");
        canvas.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeSignedFor(ImageUrls(JsonNode.Parse(await canvas.Content.ReadAsStringAsync())), QuestionImage, QuestionImageV2, AnswerImage);
    }

    [Fact]
    public async Task Practice_next_question_returns_signed_images()
    {
        const int userId = 36503;
        await SeedAsync(userId);
        var student = await ClientAsAsync(userId, "Student", "kc-s2-practice", "Student");

        (await student.GetAsync("/api/practice/daily")).EnsureSuccessStatusCode();
        var start = await (await student.PostAsync("/api/practice/daily/start", null)).Content.ReadFromJsonAsync<DailyStartResultDto>(Json);

        var next = await student.GetAsync($"/api/practice/sessions/{start!.SessionId}/next");
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeSignedFor(ImageUrls(JsonNode.Parse(await next.Content.ReadAsStringAsync())), QuestionImage, QuestionImageV2, AnswerImage, PassageImage);
    }

    [Fact]
    public async Task Study_item_returns_signed_images_with_encoded_turkish_keys()
    {
        const int userId = 36504;
        const string page = "/img/study-pages/books/Fen Bilimleri Kitabı/page_1.webp";
        var itemId = await WithDbAsync(async db =>
        {
            var item = new StudyItem
            {
                Title = "Sayfa", Description = "", IsPublished = true, ContentType = StudyItemContentType.Image,
                CreatedByUserId = 1, CreatedByName = "T", CreatedByRole = "Teacher",
            };
            item.Images.Add(new StudyItemImage { ImageUrl = page, SortOrder = 1, FileName = "page_1.webp" });
            db.StudyItems.Add(item);
            await db.SaveChangesAsync();
            return item.Id;
        });
        var student = await ClientAsAsync(userId, "Student", "kc-s2-study", "Student");

        var res = await student.GetAsync($"/api/study-items/{itemId}");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var urls = ImageUrls(JsonNode.Parse(await res.Content.ReadAsStringAsync()));

        urls.Count.ShouldBe(2); // coverImageUrl + images[0].imageUrl
        foreach (var url in urls)
        {
            url.ShouldStartWith("/img/study-pages/books/Fen%20Bilimleri%20Kitab%C4%B1/page_1.webp?");
            SigV4PresignVerifier.VerifyImgUrl(url, GatewayMinioHost, TestSk, DateTime.UtcNow).Valid.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("https://evil.example.com/a.webp")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/img/test/question-transfer/exports/default/bundle-0001.zip")]
    [InlineData("/img/study-pages/books/../../test/question-transfer/exports/default/index.json")]
    public async Task Attach_image_by_subtopics_rejects_urls_outside_the_allowlist_with_400(string url)
    {
        const int teacherUserId = 36505;
        await SeedApprovedTeacherAsync(teacherUserId);
        var teacher = await ClientAsAsync(teacherUserId, "Teacher", "kc-s2-teacher", "Teacher");

        var res = await teacher.PostAsJsonAsync("/api/study-items/attach-image-by-subtopics",
            new AttachStudyItemImageBySubTopicsRequestDto { ImageUrl = url, SubTopicIds = [1] });

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await WithDbAsync(async db => (await db.StudyItemImages.CountAsync()).ShouldBe(0));
    }

    [Theory]
    [InlineData("/img/test/question-transfer/exports/default/index.json")]
    [InlineData("https://evil.example.com/p.jpg")]
    [InlineData("/img/worksheets/1-background.png")]
    // #365 D1: paragraf görseli yalnız passages/ altından — aynı bucket'taki soru/şık görseli de reddedilir.
    [InlineData("/img/test/questions/1/question.jpg")]
    [InlineData("/img/test/answers/1/a.jpg")]
    [InlineData("/img/test/passages/A&B.jpg")]
    public async Task Question_save_rejects_a_passage_image_url_outside_the_allowlist_with_400(string url)
    {
        const int teacherUserId = 36506;
        await SeedApprovedTeacherAsync(teacherUserId);
        var teacher = await ClientAsAsync(teacherUserId, "Teacher", "kc-s2-author", "Teacher");

        var res = await teacher.PostAsJsonAsync("/api/questions", new
        {
            id = 0, text = "Yeni soru", categoryName = "x", point = 5,
            passage = new { id = 0, title = "P", text = "t", imageUrl = url },
            answers = Array.Empty<object>(),
        });

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await WithDbAsync(async db =>
        {
            (await db.Questions.CountAsync()).ShouldBe(0);
            (await db.Passage.CountAsync()).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Question_save_stores_a_signed_passage_url_without_its_signature()
    {
        const int teacherUserId = 36508;
        await SeedApprovedTeacherAsync(teacherUserId);
        var teacher = await ClientAsAsync(teacherUserId, "Teacher", "kc-s2-author3", "Teacher");

        var res = await teacher.PostAsJsonAsync("/api/questions", new
        {
            id = 0, text = "Yeni soru", categoryName = "x", point = 5,
            passage = new
            {
                id = 0, title = "P", text = "t",
                imageUrl = "/img/test/passages/7/p%20%C4%B1.jpg?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=abc",
            },
            answers = Array.Empty<object>(),
        });

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        await WithDbAsync(async db => (await db.Passage.SingleAsync()).ImageUrl.ShouldBe("/img/test/passages/7/p ı.jpg"));
    }

    [Fact]
    public async Task Question_save_linking_an_existing_passage_ignores_the_echoed_image_url()
    {
        // Mevcut paragrafa bağlanırken istemcinin geri gönderdiği (imzalı/eski) adres kullanılmaz → doğrulanmaz da.
        const int teacherUserId = 36507;
        await SeedApprovedTeacherAsync(teacherUserId);
        var passageId = await WithDbAsync(async db =>
        {
            var p = new Passage { Title = "P", Text = "t", ImageUrl = PassageImage };
            db.Passage.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
        var teacher = await ClientAsAsync(teacherUserId, "Teacher", "kc-s2-author2", "Teacher");

        var res = await teacher.PostAsJsonAsync("/api/questions", new
        {
            id = 0, text = "Yeni soru", categoryName = "x", point = 5,
            passage = new { id = passageId, imageUrl = "https://legacy.example.com/p.jpg" },
            answers = Array.Empty<object>(),
        });

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        await WithDbAsync(async db =>
        {
            (await db.Passage.SingleAsync()).ImageUrl.ShouldBe(PassageImage);
            (await db.Questions.SingleAsync()).PassageId.ShouldBe(passageId);
        });
    }
}
