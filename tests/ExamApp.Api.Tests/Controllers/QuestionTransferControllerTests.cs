using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Tests.Controllers;

public class QuestionTransferControllerTests
{
    private readonly IQuestionTransferService _service = Substitute.For<IQuestionTransferService>();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private readonly IUserProfileProvider _profiles = Substitute.For<IUserProfileProvider>();

    public QuestionTransferControllerTests()
    {
        _profiles.GetAsync("kc-teacher", Arg.Any<CancellationToken>()).Returns(new UserProfileDto { Id = 77, KeycloakId = "kc-teacher" });
    }

    /// <summary>issue #289 / #365: varsayılan çağıran exam kullanıcı id'si 77 olan Admin (controller Admin-only).</summary>
    private QuestionTransferController NewController(string sub = "kc-teacher", params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, sub) };
        claims.AddRange((roles.Length == 0 ? ["Admin"] : roles).Select(r => new Claim(ClaimTypes.Role, r)));
        return new(_service, _minio, profiles: _profiles)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
            }
        };
    }

    private static IFormFile ZipFile(string? sourceKeyInManifest)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (sourceKeyInManifest != null)
            {
                var entry = zip.CreateEntry("manifest.json");
                using var s = entry.Open();
                JsonSerializer.Serialize(s, new { sourceKey = sourceKeyInManifest, questions = Array.Empty<object>() });
            }
        }
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "File", "bundle.zip");
    }

    private static IFormFile EmptyFile() => new FormFile(new MemoryStream(), 0, 0, "File", "empty.zip");

    [Fact]
    public async Task StartExport_returns_the_job_from_the_service()
    {
        var job = new QuestionTransferJobDto { Id = Guid.NewGuid(), Kind = "export", Status = "Queued" };
        _service.StartExportAsync(Arg.Any<StartQuestionExportDto>(), Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>()).Returns(job);

        var result = await NewController().StartExport(new StartQuestionExportDto { QuestionIds = { 1, 2 } }, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(job);
    }

    [Fact]
    public async Task StartExport_passes_the_admin_caller_as_job_owner()
    {
        _service.StartExportAsync(Arg.Any<StartQuestionExportDto>(), Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>())
            .Returns(new QuestionTransferJobDto());

        await NewController().StartExport(new StartQuestionExportDto(), default);

        await _service.Received(1).StartExportAsync(Arg.Any<StartQuestionExportDto>(), new QuestionTransferOwner(77, true), Arg.Any<CancellationToken>());
    }

    /// <summary>issue #365: defense-in-depth — attribute'a rağmen Admin olmayan çağıran iş başlatamaz/yükleyemez.</summary>
    [Fact]
    public async Task Non_admin_caller_is_forbidden_and_admin_with_unresolved_profile_owns_job_as_zero()
    {
        _service.StartExportAsync(Arg.Any<StartQuestionExportDto>(), Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>())
            .Returns(new QuestionTransferJobDto());

        (await NewController("kc-teacher", "Teacher").StartExport(new StartQuestionExportDto(), default)).Result.ShouldBeOfType<ForbidResult>();
        (await NewController("kc-teacher", "Teacher").StartImport(new StartQuestionImportFormDto { File = ZipFile("x") }, default))
            .Result.ShouldBeOfType<ForbidResult>();
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!);

        (await NewController("kc-unknown", "Admin").StartExport(new StartQuestionExportDto(), default)).Result.ShouldBeOfType<OkObjectResult>();
        await _service.Received(1).StartExportAsync(Arg.Any<StartQuestionExportDto>(), new QuestionTransferOwner(0, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_rejects_a_missing_file()
    {
        var result = await NewController().StartImport(new StartQuestionImportFormDto { File = EmptyFile() }, default);
        result.Result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task StartImport_infers_the_source_key_from_the_zip_manifest_then_uploads_and_queues()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns("http://minio/obj.zip");
        var job = new QuestionTransferJobDto { Id = Guid.NewGuid(), Kind = "import" };
        _service.StartImportAsync("from-manifest", "http://minio/obj.zip", Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>()).Returns(job);

        var result = await NewController().StartImport(
            new StartQuestionImportFormDto { File = ZipFile("from-manifest") }, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(job);
        await _service.Received(1).StartImportAsync("from-manifest", "http://minio/obj.zip", Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_falls_back_to_default_when_no_source_key_can_be_determined()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns("u");
        _service.StartImportAsync("default", "u", Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>())
            .Returns(new QuestionTransferJobDto());

        await NewController().StartImport(new StartQuestionImportFormDto { File = ZipFile(sourceKeyInManifest: null) }, default);

        await _service.Received(1).StartImportAsync("default", "u", Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_prefers_an_explicit_source_key_over_the_manifest()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns("u");
        _service.StartImportAsync("explicit", "u", Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>()).Returns(new QuestionTransferJobDto());

        await NewController().StartImport(
            new StartQuestionImportFormDto { File = ZipFile("from-manifest"), SourceKey = "  explicit  " }, default);

        await _service.Received(1).StartImportAsync("explicit", "u", Arg.Any<QuestionTransferOwner>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJob_returns_NotFound_when_the_service_has_no_such_job()
    {
        _service.GetJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((QuestionTransferJobDto?)null);
        (await NewController().GetJob(Guid.NewGuid(), default)).ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DownloadBundle_streams_a_zip_when_present_and_404s_otherwise()
    {
        _service.GetExportBundleStreamAsync("src", 1, Arg.Any<CancellationToken>()).Returns(new MemoryStream([1, 2, 3]));
        (await NewController().DownloadBundle("src", 1, default)).ShouldBeOfType<FileStreamResult>()
            .ContentType.ShouldBe("application/zip");

        _service.GetExportBundleStreamAsync("src", 2, Arg.Any<CancellationToken>()).Returns((Stream?)null);
        (await NewController().DownloadBundle("src", 2, default)).ShouldBeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("http://x/index.json", "application/json")]
    [InlineData("http://x/bundle.zip", "application/zip")]
    [InlineData("http://x/blob", "application/octet-stream")]
    public async Task Download_picks_the_content_type_from_the_job_file_url(string url, string expectedContentType)
    {
        var id = Guid.NewGuid();
        _service.GetJobAsync(id, Arg.Any<CancellationToken>())
            .Returns(new QuestionTransferJobDto { Id = id, FileUrl = url, SourceKey = "s" });
        _service.GetJobFileStreamAsync(id, Arg.Any<CancellationToken>()).Returns(new MemoryStream([9]));

        var result = await NewController().Download(id, default);

        result.ShouldBeOfType<FileStreamResult>().ContentType.ShouldBe(expectedContentType);
    }

    [Fact]
    public async Task Download_404s_when_the_job_is_unknown()
    {
        _service.GetJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((QuestionTransferJobDto?)null);
        (await NewController().Download(Guid.NewGuid(), default)).ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task PreviewImport_rejects_a_missing_file_and_otherwise_returns_the_preview()
    {
        (await NewController().PreviewImport(new StartQuestionImportFormDto { File = EmptyFile() }, default))
            .Result.ShouldBeOfType<BadRequestObjectResult>();

        var preview = new QuestionTransferImportPreviewDto { QuestionCount = 4, AlreadyImportedCount = 1 };
        _service.PreviewImportAsync(Arg.Any<IFormFile>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(preview);

        (await NewController().PreviewImport(new StartQuestionImportFormDto { File = ZipFile("k") }, default))
            .Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(preview);
    }

    [Fact]
    public async Task ListJobs_and_ListSources_pass_through_to_the_service()
    {
        _service.ListJobsAsync(10, Arg.Any<CancellationToken>()).Returns(new List<QuestionTransferJobDto> { new() });
        _service.ListSourceKeysAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "a", "b" });

        (await NewController().ListJobs(10, default)).ShouldBeOfType<OkObjectResult>();
        (await NewController().ListSources(default)).ShouldBeOfType<OkObjectResult>();
    }
}
