using System.IO.Compression;
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

    private QuestionTransferController NewController() => new(_service, _minio);

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
        _service.StartExportAsync(Arg.Any<StartQuestionExportDto>(), Arg.Any<CancellationToken>()).Returns(job);

        var result = await NewController().StartExport(new StartQuestionExportDto { QuestionIds = { 1, 2 } }, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(job);
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
        _service.StartImportAsync("from-manifest", "http://minio/obj.zip", Arg.Any<CancellationToken>()).Returns(job);

        var result = await NewController().StartImport(
            new StartQuestionImportFormDto { File = ZipFile("from-manifest") }, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(job);
        await _service.Received(1).StartImportAsync("from-manifest", "http://minio/obj.zip", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_falls_back_to_default_when_no_source_key_can_be_determined()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns("u");
        _service.StartImportAsync("default", "u", Arg.Any<CancellationToken>())
            .Returns(new QuestionTransferJobDto());

        await NewController().StartImport(new StartQuestionImportFormDto { File = ZipFile(sourceKeyInManifest: null) }, default);

        await _service.Received(1).StartImportAsync("default", "u", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_prefers_an_explicit_source_key_over_the_manifest()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns("u");
        _service.StartImportAsync("explicit", "u", Arg.Any<CancellationToken>()).Returns(new QuestionTransferJobDto());

        await NewController().StartImport(
            new StartQuestionImportFormDto { File = ZipFile("from-manifest"), SourceKey = "  explicit  " }, default);

        await _service.Received(1).StartImportAsync("explicit", "u", Arg.Any<CancellationToken>());
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
