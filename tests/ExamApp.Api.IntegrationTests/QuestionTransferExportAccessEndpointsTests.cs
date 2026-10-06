using System.Net;
using System.Net.Http.Json;
using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #365 (S1): soru aktarımı tamamen Admin-only (gerçek MVC + yetkilendirme pipeline'ı). Onaylı öğretmen her uçta
/// 403, anonim 401; Admin iş başlatır, listeler ve içeriği indirir. Hangfire dashboard oturum ucu (taşındı, aynı URL)
/// yalnız Admin/SuperAdmin.
/// </summary>
public class QuestionTransferExportAccessEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int TeacherUserId = 36501;
    private const int AdminUserId = 36502;

    public static TheoryData<string> GetPaths() => new()
    {
        "/api/question-transfer/exports/sources",
        "/api/question-transfer/exports/s365/bundles",
        "/api/question-transfer/exports/s365/bundles/1/download",
        "/api/question-transfer/exports/s365/bundles/1/map",
        "/api/question-transfer/exports/s365/index",
        "/api/question-transfer/exports/s365/package",
        "/api/question-transfer/jobs",
        "/api/question-transfer/jobs/3650a000-0000-0000-0000-000000000365",
        "/api/question-transfer/jobs/3650a000-0000-0000-0000-000000000365/download",
    };

    private string DefaultBucket =>
        Factory.Services.GetRequiredService<IConfiguration>().GetSection("MinioConfig")["BucketName"] ?? "exam-questions";

    private FakeMinIoService Storage => (FakeMinIoService)Factory.Services.GetRequiredService<IMinIoService>();

    private async Task<HttpClient> ApprovedTeacherAsync(string sub)
    {
        await SeedApprovedTeacherAsync(TeacherUserId);
        return await ClientAsAsync(TeacherUserId, "Teacher", sub, "Teacher");
    }

    private static MultipartFormDataContent ZipForm()
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]), "File", "b.zip");
        return form;
    }

    [Theory]
    [MemberData(nameof(GetPaths))]
    public async Task Approved_teacher_is_forbidden_on_every_get_endpoint(string path)
    {
        var teacher = await ApprovedTeacherAsync("kc-qt365-teacher");

        (await teacher.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approved_teacher_is_forbidden_on_export_import_and_preview()
    {
        var teacher = await ApprovedTeacherAsync("kc-qt365-teacher-post");

        (await teacher.PostAsJsonAsync("/api/question-transfer/exports", new StartQuestionExportDto()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.PostAsync("/api/question-transfer/imports", ZipForm())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.PostAsync("/api/question-transfer/imports/preview", ZipForm())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(GetPaths))]
    public async Task Anonymous_caller_is_rejected_on_every_get_endpoint(string path)
    {
        (await Anonymous().GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_starts_an_export_lists_jobs_and_sources_and_downloads_bundle_zip_and_index()
    {
        const string sourceKey = "s365-admin";
        var bundleUrl = $"/img/{DefaultBucket}/question-transfer/exports/{sourceKey}/bundle-0001.zip";
        await WithDbAsync(async db =>
        {
            db.QuestionTransferExportBundles.Add(new QuestionTransferExportBundle
            {
                SourceKey = sourceKey, BundleNo = 1, QuestionCount = 3, FileUrl = bundleUrl,
            });
            await db.SaveChangesAsync();
        });
        Storage.Put(bundleUrl, [0x50, 0x4B, 0x05, 0x06]);
        Storage.Put($"/img/{DefaultBucket}/question-transfer/exports/{sourceKey}/index.json",
            Encoding.UTF8.GetBytes("""{"bundles":[1]}"""));

        var admin = await ClientAsAsync(AdminUserId, "Admin", "kc-qt365-admin", "Admin");

        (await admin.PostAsJsonAsync("/api/question-transfer/exports", new StartQuestionExportDto()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync("/api/question-transfer/jobs")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync("/api/question-transfer/exports/sources")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/question-transfer/exports/{sourceKey}/bundles")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var bundle = await admin.GetAsync($"/api/question-transfer/exports/{sourceKey}/bundles/1/download");
        bundle.StatusCode.ShouldBe(HttpStatusCode.OK);
        bundle.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        (await bundle.Content.ReadAsByteArrayAsync()).ShouldBe(new byte[] { 0x50, 0x4B, 0x05, 0x06 });

        var index = await admin.GetAsync($"/api/question-transfer/exports/{sourceKey}/index");
        index.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await index.Content.ReadAsStringAsync()).ShouldBe("""{"bundles":[1]}""");
    }

    [Fact]
    public async Task Hangfire_session_is_admin_or_superadmin_only()
    {
        var teacher = await ApprovedTeacherAsync("kc-qt365-hangfire");
        (await teacher.PostAsync("/api/question-transfer/hangfire/login", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.PostAsync("/api/question-transfer/hangfire/logout", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(36503, "Student", "kc-qt365-hangfire-student", "Student");
        (await student.PostAsync("/api/question-transfer/hangfire/login", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await ClientAsAsync(AdminUserId, "Admin", "kc-qt365-hangfire-admin", "Admin");
        (await admin.PostAsync("/api/question-transfer/hangfire/login", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync("/api/question-transfer/hangfire/logout", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var superAdmin = await ClientAsAsync(36504, "SuperAdmin", "kc-qt365-hangfire-super", "SuperAdmin");
        (await superAdmin.PostAsync("/api/question-transfer/hangfire/login", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
