using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Worksheets;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #402: soru bankası OKUMA uçları kaynak kapsamlı (gerçek pipeline + Postgres).
/// P1 tüm bankayı döken <c>GET api/worksheet/questions</c> kaldırıldı; P2/P3 test/soru okuma <c>WorksheetAccess.CanView</c>
/// kuralıyla (görünmeyen → 404); P4 paragraf listesi yalnız çağıranın; P5 başkasının paragrafına id ile bağlanma
/// olmayan paragrafla aynı 400 (varlık kehaneti yok).
/// Öğrenci bu uçların hiçbirine erişemez (doğru cevap alanları öğrenciye bu yoldan gitmez).
/// </summary>
public class QuestionReadAuthorizationEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int OwnerId = 8201;
    private const int OtherTeacherId = 8202;
    private const int AdminId = 8203;
    private const int StudentId = 8204;

    private sealed record Seed(int PrivateWorksheet, int PublicWorksheet, int SchoolOnlyWorksheet,
        int PrivateQuestion, int PublicQuestion, int CorrectAnswerId, int OwnerPassage, int OtherPassage);

    private async Task<Seed> SeedAsync(bool sameSchool = false)
    {
        var (schoolA, schoolB) = await WithDbAsync(async db =>
        {
            var a = new School { Name = "A Okulu" };
            var b = new School { Name = "B Okulu" };
            db.Schools.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        await SeedApprovedTeacherAsync(OwnerId, schoolA);
        await SeedApprovedTeacherAsync(OtherTeacherId, sameSchool ? schoolA : schoolB);

        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "7" };
            db.Grades.Add(grade);
            await db.SaveChangesAsync();

            db.SetCurrentUser(OwnerId);
            var privateWs = new Worksheet { Name = "Private", Description = "", GradeId = grade.Id };
            var publicWs = new Worksheet { Name = "Public", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.PublicView };
            var schoolWs = new Worksheet { Name = "Okul", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.SchoolOnly };
            var ownerPassage = new Passage { Title = "Sahibin paragrafı", Text = "p" };
            var privateQ = new Question { Text = "Gizli soru" };
            var publicQ = new Question { Text = "Paylaşılan soru" };
            db.AddRange(privateWs, publicWs, schoolWs, ownerPassage, privateQ, publicQ);
            await db.SaveChangesAsync();

            var correct = new Answer { QuestionId = privateQ.Id, Text = "Doğru" };
            db.Answers.Add(correct);
            await db.SaveChangesAsync();
            privateQ.CorrectAnswerId = correct.Id;
            privateQ.PassageId = ownerPassage.Id;
            db.TestQuestions.AddRange(
                new WorksheetQuestion { TestId = privateWs.Id, QuestionId = privateQ.Id, Order = 1 },
                new WorksheetQuestion { TestId = publicWs.Id, QuestionId = publicQ.Id, Order = 1 },
                new WorksheetQuestion { TestId = schoolWs.Id, QuestionId = publicQ.Id, Order = 1 });
            await db.SaveChangesAsync();

            db.SetCurrentUser(OtherTeacherId);
            var otherPassage = new Passage { Title = "Diğerinin paragrafı", Text = "o" };
            db.Passage.Add(otherPassage);
            await db.SaveChangesAsync();

            return new Seed(privateWs.Id, publicWs.Id, schoolWs.Id, privateQ.Id, publicQ.Id, correct.Id,
                ownerPassage.Id, otherPassage.Id);
        });
    }

    private Task<HttpClient> OwnerAsync() => ClientAsAsync(OwnerId, "Teacher", "kc-qr-owner", "Teacher");
    private Task<HttpClient> OtherAsync() => ClientAsAsync(OtherTeacherId, "Teacher", "kc-qr-other", "Teacher");
    private Task<HttpClient> AdminAsync() => ClientAsAsync(AdminId, "Admin", "kc-qr-admin", "Admin");
    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentId, "Student", "kc-qr-student", "Student");

    // ---------------- P1 ----------------

    [Fact]
    public async Task P1_whole_bank_endpoint_is_gone_even_for_admin()
    {
        await SeedAsync();
        var admin = await AdminAsync();

        var response = await admin.GetAsync("/api/worksheet/questions");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------- P2: bytest ----------------

    [Fact]
    public async Task P2_owner_and_admin_read_a_private_worksheet_with_answers()
    {
        var seed = await SeedAsync();

        foreach (var client in new[] { await OwnerAsync(), await AdminAsync() })
        {
            var response = await client.GetAsync($"/api/questions/bytest/{seed.PrivateWorksheet}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            var q = list.EnumerateArray().ShouldHaveSingleItem();
            q.GetProperty("correctAnswerId").GetInt32().ShouldBe(seed.CorrectAnswerId);
        }
    }

    [Fact]
    public async Task P2_other_teacher_cannot_read_a_private_worksheet_or_probe_ids()
    {
        var seed = await SeedAsync();
        var other = await OtherAsync();

        var response = await other.GetAsync($"/api/questions/bytest/{seed.PrivateWorksheet}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("Gizli soru");

        // Var olmayan test ile aynı yanıt: id taramasıyla varlık sızmaz.
        (await other.GetAsync("/api/questions/bytest/987654")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task P2_other_teacher_reads_public_worksheets_but_school_only_only_from_the_same_school()
    {
        var seed = await SeedAsync(sameSchool: false);
        var other = await OtherAsync();

        (await other.GetAsync($"/api/questions/bytest/{seed.PublicWorksheet}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync($"/api/questions/bytest/{seed.SchoolOnlyWorksheet}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task P2_school_only_worksheet_is_readable_by_a_same_school_teacher()
    {
        var seed = await SeedAsync(sameSchool: true);
        var other = await OtherAsync();

        (await other.GetAsync($"/api/questions/bytest/{seed.SchoolOnlyWorksheet}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync($"/api/questions/bytest/{seed.PrivateWorksheet}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------- P3: question by id (+ image) ----------------

    [Fact]
    public async Task P3_question_by_id_is_scoped_to_owner_admin_and_visible_worksheets()
    {
        var seed = await SeedAsync();

        (await (await OwnerAsync()).GetAsync($"/api/questions/{seed.PrivateQuestion}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await AdminAsync()).GetAsync($"/api/questions/{seed.PrivateQuestion}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var other = await OtherAsync();
        var denied = await other.GetAsync($"/api/questions/{seed.PrivateQuestion}");
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await denied.Content.ReadAsStringAsync()).ShouldNotContain("correctAnswerId");
        // Görselde de aynı kapsam (MinIO'ya hiç gidilmez).
        (await other.GetAsync($"/api/questions/{seed.PrivateQuestion}/image")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Public bir testte yer alan soru başka öğretmene görünür.
        (await other.GetAsync($"/api/questions/{seed.PublicQuestion}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Students_get_403_on_every_question_read_endpoint()
    {
        var seed = await SeedAsync();
        var student = await StudentAsync();

        (await student.GetAsync($"/api/questions/bytest/{seed.PublicWorksheet}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.GetAsync($"/api/questions/{seed.PublicQuestion}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.GetAsync("/api/questions/passages")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------- P4: passages ----------------

    [Fact]
    public async Task P4_passage_list_contains_only_the_callers_passages_admin_sees_all()
    {
        var seed = await SeedAsync();

        static async Task<int[]> Ids(HttpClient client)
        {
            var response = await client.GetAsync("/api/questions/passages");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            return list.EnumerateArray().Select(p => p.GetProperty("id").GetInt32()).ToArray();
        }

        (await Ids(await OwnerAsync())).ShouldBe(new[] { seed.OwnerPassage });
        (await Ids(await OtherAsync())).ShouldBe(new[] { seed.OtherPassage });
        (await Ids(await AdminAsync())).ShouldBe(new[] { seed.OtherPassage, seed.OwnerPassage }, ignoreOrder: true);
    }

    // ---------------- P5: link existing passage ----------------

    private static object NewQuestion(int testId, int passageId) => new
    {
        id = 0,
        testId,
        text = "Yeni soru",
        categoryName = "x",
        point = 5,
        answers = Array.Empty<object>(),
        passage = new { id = passageId },
    };

    [Fact]
    public async Task P5_linking_another_teachers_passage_looks_like_a_missing_passage_and_nothing_is_saved()
    {
        var seed = await SeedAsync();
        var otherWorksheet = await WithDbAsync(async db =>
        {
            db.SetCurrentUser(OtherTeacherId);
            var ws = new Worksheet { Name = "Diğerinin testi", Description = "", GradeId = db.Grades.First().Id };
            db.Worksheets.Add(ws);
            await db.SaveChangesAsync();
            return ws.Id;
        });
        var other = await OtherAsync();

        var response = await other.PostAsJsonAsync("/api/questions", NewQuestion(otherWorksheet, seed.OwnerPassage));
        var missing = await other.PostAsJsonAsync("/api/questions", NewQuestion(otherWorksheet, 987654));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldBe(await missing.Content.ReadAsStringAsync());
        (await WithDbAsync(db => Task.FromResult(db.TestQuestions.Count(tq => tq.TestId == otherWorksheet)))).ShouldBe(0);
    }

    [Fact]
    public async Task P5_linking_own_passage_works_and_unknown_passage_is_400()
    {
        var seed = await SeedAsync();
        var owner = await OwnerAsync();

        var ok = await owner.PostAsJsonAsync("/api/questions", NewQuestion(seed.PrivateWorksheet, seed.OwnerPassage));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => Task.FromResult(
            db.Questions.Count(q => q.PassageId == seed.OwnerPassage)))).ShouldBe(2);

        (await owner.PostAsJsonAsync("/api/questions", NewQuestion(seed.PrivateWorksheet, 987654)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task P5_admin_may_link_any_passage()
    {
        var seed = await SeedAsync();
        var admin = await AdminAsync();

        (await admin.PostAsJsonAsync("/api/questions", NewQuestion(seed.PrivateWorksheet, seed.OtherPassage)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
