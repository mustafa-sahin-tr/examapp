using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Storage;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Test ve etkinlik taşıma: paket ekranın indirdiği yoldan (<see cref="QuestionTransferService.WritePackageAsync"/>) üretilir,
/// başka bir veritabanına alınır. Depo iki ortamda ortaktır; kimlikler ise ortama göre değişir.
/// </summary>
[Collection(HangfireJobStorageCollection.Name)]
public class WorksheetTransferRoundTripTests : IDisposable
{
    private const string PackageUrl = "/img/exam-questions/incoming-package.zip";
    private const string OpenTextPlan = """{"version":1,"type":"openText","minWords":3,"maxWords":40,"lines":4}""";
    private const string OpenTextSolution = """{"sampleAnswer":"Su ısınınca buharlaşır.","rubric":"Isı + buharlaşma."}""";
    private const int ImportingAdmin = 42;

    private readonly TestDb _source = TestDb.Create();
    private readonly TestDb _target = TestDb.Create();
    private readonly Dictionary<string, byte[]> _storage = new(StringComparer.Ordinal);
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly JobStorage? _previousStorage;

    public WorksheetTransferRoundTripTests()
    {
        var storage = Substitute.For<JobStorage>();
        storage.GetConnection().Returns(Substitute.For<IStorageConnection>());
        try
        {
            _previousStorage = JobStorage.Current;
        }
        catch (InvalidOperationException)
        {
            _previousStorage = null;
        }
        JobStorage.Current = storage;

        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(call => Task.FromResult<Stream?>(_storage.TryGetValue(call.Arg<string>(), out var bytes) ? new MemoryStream(bytes) : null));
        _minio.FileExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_storage.ContainsKey(call.Arg<string>())));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(call =>
            {
                var stream = call.ArgAt<Stream>(0);
                if (stream.CanSeek)
                    stream.Position = 0;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var url = $"/img/exam-questions/{call.ArgAt<string>(1)}";
                _storage[url] = copy.ToArray();
                return Task.FromResult(url);
            });
    }

    public void Dispose()
    {
        JobStorage.Current = _previousStorage;
        _source.Dispose();
        _target.Dispose();
    }

    private QuestionTransferService Service(AppDbContext ctx) =>
        new(ctx, _minio, Substitute.For<IBackgroundJobClient>(), new ConfigurationBuilder().Build());

    private static async Task<int> GradeAsync(TestDb db, string name = "5. Sınıf", string? code = "G5")
    {
        await using var ctx = db.NewContext();
        var grade = new Grade { Name = name, Code = code };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        return grade.Id;
    }

    private static async Task<int> SubjectAsync(TestDb db, string name = "Matematik", string? code = "MAT")
    {
        await using var ctx = db.NewContext();
        var subject = new Subject { Name = name, Code = code };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        return subject.Id;
    }

    private async Task<int> QuestionAsync(TestDb db, string text, byte[]? image = null, int createdBy = 0, Action<Question>? configure = null)
    {
        await using var ctx = db.NewContext();
        ctx.SetCurrentUser(createdBy);
        var question = new Question { Text = text, Point = 5, DifficultyLevel = 1 };
        if (image is not null)
        {
            question.ImageUrl = $"/img/exam-questions/seed-{Guid.NewGuid():N}.jpg";
            _storage[question.ImageUrl] = image;
        }
        configure?.Invoke(question);
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();

        if (question.InteractionType is null)
        {
            var answers = new[]
            {
                new Answer { QuestionId = question.Id, Text = "A", Order = 1 },
                new Answer { QuestionId = question.Id, Text = "B", Order = 2 },
            };
            ctx.Answers.AddRange(answers);
            await ctx.SaveChangesAsync();
            question.CorrectAnswerId = answers[0].Id;
            await ctx.SaveChangesAsync();
        }
        return question.Id;
    }

    private Task<int> OpenTextQuestionAsync(TestDb db, string text) =>
        QuestionAsync(db, text, configure: q =>
        {
            q.InteractionType = OpenTextGrader.Type;
            q.InteractionPlan = OpenTextPlan;
            q.InteractionSolution = OpenTextSolution;
        });

    /// <summary>Maddeler ters sırayla eklenir: sıra satır kimliğinden değil <see cref="WorksheetQuestion.Order"/>'dan okunmalı.</summary>
    private static async Task<int> WorksheetAsync(TestDb db, string name, int gradeId, int[] questionIds, WorksheetKind kind = WorksheetKind.Test, Action<Worksheet>? configure = null)
    {
        await using var ctx = db.NewContext();
        var worksheet = new Worksheet { Name = name, GradeId = gradeId, Kind = kind };
        configure?.Invoke(worksheet);
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        for (var i = questionIds.Length - 1; i >= 0; i--)
        {
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionIds[i], Order = i + 1 });
            await ctx.SaveChangesAsync();
        }
        return worksheet.Id;
    }

    private async Task<byte[]> ExportAsync(params int[] worksheetIds)
    {
        QuestionTransferJobDto started;
        await using (var ctx = _source.NewContext())
            started = await Service(ctx).StartExportAsync(new StartQuestionExportDto { WorksheetIds = worksheetIds.ToList() }, new QuestionTransferOwner(1, true), default);

        await using (var ctx = _source.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunExportAsync(started.Id, ownerIsAdmin: true);

        await using var read = _source.NewContext();
        var job = await read.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == started.Id);
        job.Status.ShouldBe(QuestionTransferJobStatus.Completed, job.Message);

        using var output = new MemoryStream();
        await Service(read).WritePackageAsync(job.SourceKey, output, default);
        return output.ToArray();
    }

    private static string OriginOf(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var doc = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        return QuestionContentHash.ReadPackageSourceKey(doc.RootElement)!;
    }

    private Task<QuestionTransferImportResultDto> ImportAsync(byte[] package, params IInterceptor[] interceptors) =>
        ImportWithAsync(package, () => _target.NewContext(interceptors));

    private async Task<QuestionTransferImportResultDto> ImportWithAsync(byte[] package, Func<AppDbContext> newContext)
    {
        _storage[PackageUrl] = package;
        var job = new QuestionTransferJob { Id = Guid.NewGuid(), Kind = QuestionTransferJobKind.Import, SourceKey = OriginOf(package), FileUrl = PackageUrl, Message = "Queued" };
        await using (var seed = _target.NewContext())
        {
            seed.SetCurrentUser(ImportingAdmin);
            seed.Add(job);
            await seed.SaveChangesAsync();
        }
        await using (var ctx = newContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(job.Id, ownerIsAdmin: true);

        await using var check = _target.NewContext();
        var saved = await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
        saved.Status.ShouldBe(QuestionTransferJobStatus.Completed, saved.Message);
        return JsonSerializer.Deserialize<QuestionTransferImportResultDto>(saved.ResultJson!)!;
    }

    private async Task<QuestionTransferImportPreviewDto> PreviewAsync(byte[] package)
    {
        await using var ctx = _target.NewContext();
        return await Service(ctx).PreviewImportAsync(new FormFile(new MemoryStream(package), 0, package.Length, "File", "package.zip"), null, default);
    }

    private async Task<Worksheet> TargetWorksheetAsync(string name)
    {
        await using var ctx = _target.NewContext();
        return await ctx.Worksheets.AsNoTracking().SingleAsync(w => w.Name == name);
    }

    private async Task<List<string>> TargetItemTextsAsync(string name)
    {
        await using var ctx = _target.NewContext();
        return await ctx.TestQuestions.AsNoTracking()
            .Where(i => i.Worksheet.Name == name)
            .OrderBy(i => i.Order)
            .Select(i => i.Question.Text)
            .ToListAsync();
    }

    private async Task<int> TargetQuestionCountAsync()
    {
        await using var ctx = _target.NewContext();
        return await ctx.Questions.CountAsync();
    }

    private async Task<bool> TargetHasWorksheetAsync(string name)
    {
        await using var ctx = _target.NewContext();
        return await ctx.Worksheets.AnyAsync(w => w.Name == name);
    }

    [Fact]
    public async Task Test_arrives_with_its_questions_in_the_same_order()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var first = await QuestionAsync(_source, "Birinci soru");
        var second = await QuestionAsync(_source, "İkinci soru");
        var third = await QuestionAsync(_source, "Üçüncü soru");
        var worksheet = await WorksheetAsync(_source, "Kesirler testi", grade, [third, first, second]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Imported.ShouldBe(3);
        result.Worksheets.Imported.ShouldBe(1, JsonSerializer.Serialize(result.Worksheets));
        result.Worksheets.Blocked.ShouldBe(0);
        (await TargetItemTextsAsync("Kesirler testi")).ShouldBe(["Üçüncü soru", "Birinci soru", "İkinci soru"]);
    }

    [Fact]
    public async Task Question_already_in_the_target_is_used_and_not_copied()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        byte[] image = [9, 8, 7];
        var inSource = await QuestionAsync(_source, "Ortak soru", image);
        var fresh = await QuestionAsync(_source, "Yeni soru");
        var existing = await QuestionAsync(_target, "Ortak soru", image);
        var worksheet = await WorksheetAsync(_source, "Ortak test", grade, [inSource, fresh]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Matched.ShouldBe(1);
        result.Imported.ShouldBe(1);
        (await TargetQuestionCountAsync()).ShouldBe(2);
        await using var ctx = _target.NewContext();
        var firstItem = await ctx.TestQuestions.AsNoTracking().Where(i => i.Worksheet.Name == "Ortak test").OrderBy(i => i.Order).FirstAsync();
        firstItem.QuestionId.ShouldBe(existing);
    }

    [Fact]
    public async Task Conflicting_question_stops_the_test_but_the_other_questions_still_arrive()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        byte[] image = [1, 1, 1];
        var conflicting = await QuestionAsync(_source, "Kaynaktaki metin", image);
        var fine = await QuestionAsync(_source, "Sorunsuz soru");
        await QuestionAsync(_target, "Hedefteki başka metin", image);
        var worksheet = await WorksheetAsync(_source, "Çakışan test", grade, [conflicting, fine]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Conflicts.ShouldBe(1);
        result.Imported.ShouldBe(1);
        result.Worksheets.Imported.ShouldBe(0);
        result.Worksheets.Blocked.ShouldBe(1);
        result.Worksheets.BlockedSamples.ShouldHaveSingleItem().ShouldContain("Çakışan test");
        (await TargetHasWorksheetAsync("Çakışan test")).ShouldBeFalse();
    }

    [Fact]
    public async Task Second_import_counts_the_test_as_already_imported_and_does_not_change_it()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Tekrar gelen test", grade, [question]);
        var package = await ExportAsync(worksheet);
        await ImportAsync(package);

        await using (var edit = _target.NewContext())
        {
            var imported = await edit.Worksheets.SingleAsync(w => w.Name == "Tekrar gelen test");
            imported.Name = "Hedefte değişti";
            await edit.SaveChangesAsync();
        }

        var second = await ImportAsync(package);

        second.Worksheets.AlreadyImported.ShouldBe(1);
        second.Worksheets.Imported.ShouldBe(0);
        (await TargetHasWorksheetAsync("Hedefte değişti")).ShouldBeTrue();
        (await TargetHasWorksheetAsync("Tekrar gelen test")).ShouldBeFalse();
    }

    [Fact]
    public async Task Test_exported_again_under_a_new_transfer_is_not_duplicated()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "İki kez aktarılan", grade, [question]);

        await ImportAsync(await ExportAsync(worksheet));
        var second = await ImportAsync(await ExportAsync(worksheet));

        second.Worksheets.AlreadyImported.ShouldBe(1);
        await using var ctx = _target.NewContext();
        (await ctx.Worksheets.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Imported_test_is_closed_owned_by_the_importer_and_keeps_its_settings()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Herkese açık test", grade, [question], configure: w =>
        {
            w.TeacherSharing = WorksheetTeacherSharing.PublicAssignable;
            w.StudentVisibility = WorksheetStudentVisibility.Normal;
            w.Description = "Açıklama";
            w.Subtitle = "Alt başlık";
            w.MaxDurationSeconds = 1200;
            w.IsPracticeTest = true;
            w.CommentsEnabled = false;
        });

        await ImportAsync(await ExportAsync(worksheet));

        var imported = await TargetWorksheetAsync("Herkese açık test");
        imported.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        imported.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
        imported.CreateUserId.ShouldBe(ImportingAdmin);
        imported.Description.ShouldBe("Açıklama");
        imported.Subtitle.ShouldBe("Alt başlık");
        imported.MaxDurationSeconds.ShouldBe(1200);
        imported.IsPracticeTest.ShouldBeTrue();
        imported.CommentsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Moving_a_test_does_not_change_who_owns_the_questions()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var shared = await QuestionAsync(_source, "Ortak soru");
        var fresh = await QuestionAsync(_source, "Yeni soru");
        var existing = await QuestionAsync(_target, "Ortak soru", createdBy: 9);
        var worksheet = await WorksheetAsync(_source, "Sahiplik testi", grade, [shared, fresh]);

        await ImportAsync(await ExportAsync(worksheet));

        await using var ctx = _target.NewContext();
        (await ctx.Questions.AsNoTracking().SingleAsync(q => q.Id == existing)).CreateUserId.ShouldBe(9);
        (await ctx.Questions.AsNoTracking().SingleAsync(q => q.Text == "Yeni soru")).CreateUserId.ShouldNotBe(ImportingAdmin);
    }

    [Fact]
    public async Task Test_is_not_imported_when_its_grade_is_not_in_the_target()
    {
        var grade = await GradeAsync(_source, "12. Sınıf", "G12");
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Sınıfsız test", grade, [question]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Imported.ShouldBe(1);
        result.Worksheets.Blocked.ShouldBe(1);
        result.Worksheets.BlockedSamples.ShouldHaveSingleItem().ShouldContain("grade");
        (await TargetHasWorksheetAsync("Sınıfsız test")).ShouldBeFalse();
    }

    [Fact]
    public async Task Subject_missing_in_the_target_stays_empty_and_is_counted()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var subject = await SubjectAsync(_source);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Derssiz test", grade, [question], configure: w => w.SubjectId = subject);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Worksheets.Imported.ShouldBe(1);
        result.Worksheets.MissingTaxonomy.ShouldBe(1);
        (await TargetWorksheetAsync("Derssiz test")).SubjectId.ShouldBeNull();
    }

    [Fact]
    public async Task Subject_is_bound_to_the_target_id()
    {
        var grade = await GradeAsync(_source);
        await SubjectAsync(_source, "Önce gelen", "OTHER");
        var subject = await SubjectAsync(_source);
        var targetGrade = await GradeAsync(_target);
        var targetSubject = await SubjectAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Dersli test", grade, [question], configure: w => w.SubjectId = subject);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Worksheets.MissingTaxonomy.ShouldBe(0);
        var imported = await TargetWorksheetAsync("Dersli test");
        imported.SubjectId.ShouldBe(targetSubject);
        imported.GradeId.ShouldBe(targetGrade);
    }

    [Fact]
    public async Task Open_text_question_stops_a_test_but_not_an_activity()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var openText = await OpenTextQuestionAsync(_source, "Neden buharlaşır?");
        var test = await WorksheetAsync(_source, "Açık uçlu test", grade, [openText]);
        var activity = await WorksheetAsync(_source, "Açık uçlu etkinlik", grade, [openText], WorksheetKind.Activity);

        var result = await ImportAsync(await ExportAsync(test, activity));

        result.Worksheets.Imported.ShouldBe(1, JsonSerializer.Serialize(result));
        result.Worksheets.Blocked.ShouldBe(1);
        (await TargetHasWorksheetAsync("Açık uçlu test")).ShouldBeFalse();
        (await TargetWorksheetAsync("Açık uçlu etkinlik")).Kind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public async Task Unpublished_question_stops_the_test()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Kaldırılmış soru", configure: q => q.IsUnpublished = true);
        var worksheet = await WorksheetAsync(_source, "Kaldırılmış sorulu test", grade, [question]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Worksheets.Blocked.ShouldBe(1);
        (await TargetHasWorksheetAsync("Kaldırılmış sorulu test")).ShouldBeFalse();
    }

    [Fact]
    public async Task Question_with_an_ended_licence_stops_the_test()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Lisansı biten soru", configure: q => q.LicenseEndsAt = DateTime.UtcNow.AddDays(-1));
        var worksheet = await WorksheetAsync(_source, "Lisansı biten test", grade, [question]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Worksheets.Blocked.ShouldBe(1);
        (await TargetHasWorksheetAsync("Lisansı biten test")).ShouldBeFalse();
    }

    [Fact]
    public async Task Removed_item_does_not_travel()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var kept = await QuestionAsync(_source, "Kalan soru");
        var removed = await QuestionAsync(_source, "Çıkarılan soru");
        var worksheet = await WorksheetAsync(_source, "Madde çıkarılan test", grade, [kept, removed]);
        await using (var ctx = _source.NewContext())
        {
            var item = await ctx.TestQuestions.SingleAsync(i => i.QuestionId == removed);
            item.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Imported.ShouldBe(1);
        (await TargetItemTextsAsync("Madde çıkarılan test")).ShouldBe(["Kalan soru"]);
    }

    [Fact]
    public async Task Two_items_that_are_the_same_question_here_are_kept_once_with_a_warning()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var first = await QuestionAsync(_source, "Aynı içerik");
        var copy = await QuestionAsync(_source, "Aynı içerik");
        var other = await QuestionAsync(_source, "Başka içerik");
        var worksheet = await WorksheetAsync(_source, "Kopyalı test", grade, [first, other, copy]);

        var result = await ImportAsync(await ExportAsync(worksheet));

        result.Worksheets.Imported.ShouldBe(1);
        result.Worksheets.Warnings.ShouldHaveSingleItem().ShouldContain("Kopyalı test");
        (await TargetItemTextsAsync("Kopyalı test")).ShouldBe(["Aynı içerik", "Başka içerik"]);
    }

    [Fact]
    public async Task Worksheet_image_travels_with_the_package()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        byte[] cover = [5, 5, 5];
        const string coverUrl = "/img/exam-questions/cover.jpg";
        _storage[coverUrl] = cover;
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Kapaklı test", grade, [question], configure: w => w.ImageUrl = coverUrl);

        await ImportAsync(await ExportAsync(worksheet));

        var imported = await TargetWorksheetAsync("Kapaklı test");
        imported.ImageUrl.ShouldNotBeNull();
        imported.ImageUrl.ShouldNotBe(coverUrl);
        _storage[imported.ImageUrl].ShouldBe(cover);
        // Kapak başka bucket'ta olursa imzalanmaz, ekranda görünmez.
        await _minio.Received().UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), MinioBucketPolicies.WorksheetsBucket, "image/jpeg");
    }

    [Fact]
    public async Task Database_error_while_saving_a_test_does_not_leave_the_job_running()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Kaydedilemeyen test", grade, [question]);

        var result = await ImportAsync(await ExportAsync(worksheet), new FailOnNewWorksheet());

        result.Imported.ShouldBe(1);
        result.Worksheets.Imported.ShouldBe(0);
        result.Worksheets.Blocked.ShouldBe(1);
        (await TargetHasWorksheetAsync("Kaydedilemeyen test")).ShouldBeFalse();
    }

    [Fact]
    public async Task Preview_counts_match_the_import_and_preview_writes_nothing()
    {
        var grade = await GradeAsync(_source);
        var missingGrade = await GradeAsync(_source, "12. Sınıf", "G12");
        var subject = await SubjectAsync(_source);
        await GradeAsync(_target);
        byte[] image = [3, 3, 3];
        var conflicting = await QuestionAsync(_source, "Kaynaktaki metin", image);
        await QuestionAsync(_target, "Hedefteki başka metin", image);
        var plain = await QuestionAsync(_source, "Sorunsuz soru");
        var shared = await QuestionAsync(_source, "Ortak soru");
        await QuestionAsync(_target, "Ortak soru");
        var unpublished = await QuestionAsync(_source, "Kaldırılmış soru", configure: q => q.IsUnpublished = true);
        var ended = await QuestionAsync(_source, "Lisansı biten soru", configure: q => q.LicenseEndsAt = DateTime.UtcNow.AddDays(-1));
        var openText = await OpenTextQuestionAsync(_source, "Neden buharlaşır?");

        int[] worksheets =
        [
            await WorksheetAsync(_source, "Alınır", grade, [plain, shared]),
            await WorksheetAsync(_source, "Dersi eksik", grade, [plain], configure: w => w.SubjectId = subject),
            await WorksheetAsync(_source, "Çakışan", grade, [conflicting]),
            await WorksheetAsync(_source, "Sınıfı eksik", missingGrade, [plain]),
            await WorksheetAsync(_source, "Kaldırılmış", grade, [unpublished]),
            await WorksheetAsync(_source, "Lisansı bitmiş", grade, [ended]),
            await WorksheetAsync(_source, "Açık uçlu test", grade, [openText]),
            await WorksheetAsync(_source, "Açık uçlu etkinlik", grade, [openText], WorksheetKind.Activity),
        ];
        var package = await ExportAsync(worksheets);

        // Önizleme hedefin hash'lerine bakar: eksikse bunu bildirir ve arka planda doldurur.
        await using (var fill = _target.NewContext())
            await new QuestionContentHashService(fill, _minio, Microsoft.Extensions.Logging.Abstractions.NullLogger<QuestionContentHashService>.Instance).FillMissingAsync();

        var preview = await PreviewAsync(package);
        preview.TargetUnhashedCount.ShouldBe(0);

        await using (var ctx = _target.NewContext())
        {
            (await ctx.Worksheets.CountAsync()).ShouldBe(0);
            (await ctx.WorksheetTransferImportMaps.CountAsync()).ShouldBe(0);
        }

        var result = await ImportAsync(package);

        result.Worksheets.Imported.ShouldBe(3, JsonSerializer.Serialize(result.Worksheets));
        result.Worksheets.Blocked.ShouldBe(5);
        result.Worksheets.MissingTaxonomy.ShouldBe(1);
        preview.Worksheets.Imported.ShouldBe(result.Worksheets.Imported, JsonSerializer.Serialize(preview.Worksheets));
        preview.Worksheets.Blocked.ShouldBe(result.Worksheets.Blocked);
        preview.Worksheets.MissingTaxonomy.ShouldBe(result.Worksheets.MissingTaxonomy);

        var again = await PreviewAsync(package);
        again.Worksheets.AlreadyImported.ShouldBe(3);
        again.Worksheets.Imported.ShouldBe(0);
    }

    [Fact]
    public async Task Package_holds_the_worksheets_and_only_their_questions()
    {
        var grade = await GradeAsync(_source);
        var selected = await QuestionAsync(_source, "Seçilen testin sorusu");
        var other = await QuestionAsync(_source, "Başka testin sorusu");
        var worksheet = await WorksheetAsync(_source, "Seçilen", grade, [selected]);
        await WorksheetAsync(_source, "Seçilmeyen", grade, [other]);

        var package = await ExportAsync(worksheet);

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        manifest.RootElement.GetProperty("version").GetInt32().ShouldBe(4);
        manifest.RootElement.GetProperty("worksheets").EnumerateArray().Select(w => w.GetProperty("name").GetString()).ShouldBe(["Seçilen"]);
        manifest.RootElement.GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("id").GetInt32()).ShouldBe([selected]);
        zip.GetEntry(QuestionTransferWorksheetImporter.WorksheetsFileName).ShouldBeNull();
    }

    [Fact]
    public async Task Export_list_filters_and_shows_empty_subject_without_failing()
    {
        var grade = await GradeAsync(_source);
        var subject = await SubjectAsync(_source);
        var question = await QuestionAsync(_source, "Soru");
        var removed = await QuestionAsync(_source, "Çıkarılan");
        await WorksheetAsync(_source, "Dersli test", grade, [question], configure: w => w.SubjectId = subject);
        var withoutSubject = await WorksheetAsync(_source, "Derssiz etkinlik", grade, [question, removed], WorksheetKind.Activity);
        await using (var edit = _source.NewContext())
        {
            var item = await edit.TestQuestions.SingleAsync(i => i.QuestionId == removed);
            item.IsDeleted = true;
            await edit.SaveChangesAsync();
        }

        await using var ctx = _source.NewContext();
        var all = await Service(ctx).ListWorksheetsAsync(null, null, null, null, 50, default);
        all.Count.ShouldBe(2);

        var activity = (await Service(ctx).ListWorksheetsAsync(WorksheetKind.Activity, null, null, null, 50, default)).ShouldHaveSingleItem();
        activity.Id.ShouldBe(withoutSubject);
        activity.Kind.ShouldBe(nameof(WorksheetKind.Activity));
        activity.SubjectName.ShouldBeNull();
        activity.GradeName.ShouldBe("5. Sınıf");
        activity.QuestionCount.ShouldBe(1);

        (await Service(ctx).ListWorksheetsAsync(null, null, subject, null, 50, default)).ShouldHaveSingleItem().Name.ShouldBe("Dersli test");
        (await Service(ctx).ListWorksheetsAsync(null, null, null, "etkinlik", 50, default)).ShouldHaveSingleItem().Name.ShouldBe("Derssiz etkinlik");
    }

    [Theory]
    [InlineData(typeof(Worksheet))]
    [InlineData(typeof(WorksheetQuestion))]
    public async Task Transient_failure_while_saving_a_test_is_retried_without_leaving_an_extra_row(Type failingEntity)
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Yeniden denenen test", grade, [question]);
        var interceptor = new FailOnceWhenAdding(failingEntity);

        var result = await ImportWithAsync(await ExportAsync(worksheet), () => _target.NewContextWithTransientRetry(interceptor));

        interceptor.Fired.ShouldBeTrue();
        result.Worksheets.Imported.ShouldBe(1, JsonSerializer.Serialize(result.Worksheets));
        await using var ctx = _target.NewContext();
        (await ctx.Worksheets.CountAsync()).ShouldBe(1);
        (await ctx.TestQuestions.CountAsync()).ShouldBe(1);
        (await ctx.WorksheetTransferImportMaps.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Storage_error_on_a_cover_stops_only_that_test_and_hides_the_detail()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        const string coverUrl = "/img/exam-questions/broken-cover.jpg";
        _storage[coverUrl] = [5, 5, 5];
        var question = await QuestionAsync(_source, "Tek soru");
        var covered = await WorksheetAsync(_source, "Kapağı yüklenemeyen", grade, [question], configure: w => w.ImageUrl = coverUrl);
        var plain = await WorksheetAsync(_source, "Kapaksız", grade, [question]);
        var package = await ExportAsync(covered, plain);
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), MinioBucketPolicies.WorksheetsBucket, Arg.Any<string>())
            .Returns<Task<string>>(_ => throw new IOException("secret-storage-host refused"));

        var result = await ImportAsync(package);

        result.Imported.ShouldBe(1);
        result.Worksheets.Imported.ShouldBe(1);
        result.Worksheets.Blocked.ShouldBe(1);
        result.Worksheets.BlockedSamples.ShouldHaveSingleItem().ShouldNotContain("secret-storage-host");
        (await TargetHasWorksheetAsync("Kapaksız")).ShouldBeTrue();
    }

    [Fact]
    public async Task Deleted_worksheet_does_not_send_its_questions()
    {
        var grade = await GradeAsync(_source);
        var kept = await QuestionAsync(_source, "Kalan testin sorusu");
        var hidden = await QuestionAsync(_source, "Silinen testin sorusu");
        var keptWorksheet = await WorksheetAsync(_source, "Kalan", grade, [kept]);
        var deleted = await WorksheetAsync(_source, "Silinen", grade, [hidden]);
        await using (var edit = _source.NewContext())
        {
            (await edit.Worksheets.SingleAsync(w => w.Id == deleted)).IsDeleted = true;
            await edit.SaveChangesAsync();
        }

        var package = await ExportAsync(keptWorksheet, deleted);

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        manifest.RootElement.GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("id").GetInt32()).ShouldBe([kept]);
        manifest.RootElement.GetProperty("worksheets").EnumerateArray().Select(w => w.GetProperty("name").GetString()).ShouldBe(["Kalan"]);
    }

    [Fact]
    public async Task Exporting_worksheets_always_uses_a_new_source_so_an_earlier_selection_is_not_overwritten()
    {
        var grade = await GradeAsync(_source);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Test", grade, [question]);

        await using var ctx = _source.NewContext();
        var first = await Service(ctx).StartExportAsync(new StartQuestionExportDto { SourceKey = "sabit", WorksheetIds = [worksheet] }, new QuestionTransferOwner(1, true), default);
        var second = await Service(ctx).StartExportAsync(new StartQuestionExportDto { SourceKey = "sabit", WorksheetIds = [worksheet] }, new QuestionTransferOwner(1, true), default);

        first.SourceKey.ShouldStartWith("transfer-");
        second.SourceKey.ShouldNotBe(first.SourceKey);
    }

    [Fact]
    public async Task Package_download_fails_when_the_worksheet_file_cannot_be_read()
    {
        var grade = await GradeAsync(_source);
        var question = await QuestionAsync(_source, "Tek soru");
        var worksheet = await WorksheetAsync(_source, "Test", grade, [question]);
        QuestionTransferJobDto started;
        await using (var ctx = _source.NewContext())
            started = await Service(ctx).StartExportAsync(new StartQuestionExportDto { WorksheetIds = [worksheet] }, new QuestionTransferOwner(1, true), default);
        await using (var ctx = _source.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunExportAsync(started.Id, ownerIsAdmin: true);
        _minio.GetFileStreamAsync(Arg.Is<string>(u => u.EndsWith("/worksheets.zip", StringComparison.Ordinal))).Returns(Task.FromResult<Stream?>(null));

        await using var read = _source.NewContext();
        using var output = new MemoryStream();
        await Should.ThrowAsync<InvalidOperationException>(() => Service(read).WritePackageAsync(started.SourceKey, output, default));
    }

    [Fact]
    public async Task Settings_follow_the_rules_of_creating_a_test_by_hand()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Tek soru");
        var longTest = await WorksheetAsync(_source, "Çok uzun test", grade, [question], configure: w => w.MaxDurationSeconds = WorksheetDurationLimits.MaxSeconds + 1000);
        var activity = await WorksheetAsync(_source, "Süreli etkinlik", grade, [question], WorksheetKind.Activity, w =>
        {
            w.MaxDurationSeconds = 600;
            w.IsPracticeTest = true;
        });

        await ImportAsync(await ExportAsync(longTest, activity));

        (await TargetWorksheetAsync("Çok uzun test")).MaxDurationSeconds.ShouldBe(WorksheetDurationLimits.MaxSeconds);
        var importedActivity = await TargetWorksheetAsync("Süreli etkinlik");
        importedActivity.MaxDurationSeconds.ShouldBe(0);
        importedActivity.IsPracticeTest.ShouldBeFalse();
    }

    private async Task FillTargetHashesAsync()
    {
        await using var fill = _target.NewContext();
        await new QuestionContentHashService(fill, _minio, Microsoft.Extensions.Logging.Abstractions.NullLogger<QuestionContentHashService>.Instance).FillMissingAsync();
    }

    [Fact]
    public async Task Preview_sees_the_ended_licence_that_import_will_copy_onto_a_matched_question()
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var question = await QuestionAsync(_source, "Ortak soru", configure: q => q.LicenseEndsAt = DateTime.UtcNow.AddDays(-1));
        await QuestionAsync(_target, "Ortak soru");
        await FillTargetHashesAsync();
        var worksheet = await WorksheetAsync(_source, "Lisansı paketten biten", grade, [question]);
        var package = await ExportAsync(worksheet);

        var preview = await PreviewAsync(package);
        var result = await ImportAsync(package);

        result.Worksheets.Blocked.ShouldBe(1, JsonSerializer.Serialize(result.Worksheets));
        preview.Worksheets.Blocked.ShouldBe(1, JsonSerializer.Serialize(preview.Worksheets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_stops_every_test_using_a_question_unpublished_through_another_copy(bool alreadyInTarget)
    {
        var grade = await GradeAsync(_source);
        await GradeAsync(_target);
        var published = await QuestionAsync(_source, "Aynı içerik");
        var unpublished = await QuestionAsync(_source, "Aynı içerik", configure: q => q.IsUnpublished = true);
        if (alreadyInTarget)
            await QuestionAsync(_target, "Aynı içerik");
        await FillTargetHashesAsync();
        var first = await WorksheetAsync(_source, "Yayındaki kopyayı kullanan", grade, [published]);
        var second = await WorksheetAsync(_source, "Kaldırılan kopyayı kullanan", grade, [unpublished]);
        var package = await ExportAsync(first, second);

        var preview = await PreviewAsync(package);
        var result = await ImportAsync(package);

        result.Worksheets.Blocked.ShouldBe(2, JsonSerializer.Serialize(result.Worksheets));
        preview.Worksheets.Blocked.ShouldBe(2, JsonSerializer.Serialize(preview.Worksheets));
    }

    private static byte[] CraftedPackage(string worksheetsJson)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write($$"""{"originKey":"crafted","questions":[],"worksheets":{{worksheetsJson}}}""");
        }
        return output.ToArray();
    }

    public static TheoryData<string> BrokenWorksheets() => new()
    {
        """[{"externalKey":"worksheet:1","kind":"Test","name":"Bozuk","grade":{"code":"G5"},"items":[{"questionKey":"question:1","order":1.5},{"questionKey":"question:2","order":2}]}]""",
        """[{"externalKey":"worksheet:1","kind":"Test","name":"Bozuk","grade":{"code":"G5"},"items":[5]}]""",
        $$"""[{"externalKey":"{{new string('k', 300)}}","kind":"Test","name":"Bozuk","grade":{"code":"G5"},"items":[{"questionKey":"question:1","order":1}]}]""",
        $$"""[{"externalKey":"worksheet:1","kind":"Test","name":"{{new string('n', 5000)}}","grade":{"code":"G5"},"items":[{"questionKey":"question:1","order":1}]}]""",
    };

    [Theory]
    [MemberData(nameof(BrokenWorksheets))]
    public async Task Broken_package_entry_is_blocked_in_preview_and_import_alike(string worksheetsJson)
    {
        await GradeAsync(_target);
        var package = CraftedPackage(worksheetsJson);

        var preview = await PreviewAsync(package);
        var result = await ImportAsync(package);

        preview.Worksheets.Blocked.ShouldBe(1);
        result.Worksheets.Blocked.ShouldBe(1);
        result.Worksheets.BlockedSamples.ShouldHaveSingleItem().Length.ShouldBeLessThan(400);
        await using var ctx = _target.NewContext();
        (await ctx.Worksheets.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Package_with_too_many_worksheets_writes_nothing()
    {
        await GradeAsync(_target);
        var entries = Enumerable.Range(1, QuestionTransferWorksheetImporter.MaxWorksheetsPerPackage + 1)
            .Select(i => $$"""{"externalKey":"worksheet:{{i}}","kind":"Test","name":"T{{i}}","grade":{"code":"G5"},"items":[]}""");
        var package = CraftedPackage($"[{string.Join(',', entries)}]");

        var preview = await PreviewAsync(package);
        var result = await ImportAsync(package);

        preview.Worksheets.Blocked.ShouldBe(QuestionTransferWorksheetImporter.MaxWorksheetsPerPackage + 1);
        result.Worksheets.Blocked.ShouldBe(QuestionTransferWorksheetImporter.MaxWorksheetsPerPackage + 1);
        result.Worksheets.BlockedSamples.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Entries_sharing_an_image_id_upload_it_once()
    {
        var grade = await GradeAsync(_target);
        var question = await QuestionAsync(_target, "Hedefteki soru");
        await using (var ctx = _target.NewContext())
        {
            ctx.Add(new QuestionTransferImportMap { SourceKey = "crafted", ExternalQuestionKey = "question:1", TargetQuestionId = question });
            await ctx.SaveChangesAsync();
        }
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var image = zip.CreateEntry("worksheets/7.jpg").Open())
                await image.WriteAsync(new byte[] { 1, 2, 3 });
            var entries = Enumerable.Range(1, 3).Select(i => $$"""{"externalKey":"worksheet:{{i}}","id":7,"kind":"Test","name":"T{{i}}","grade":{"code":"G5"},"items":[{"questionKey":"question:1","order":1}]}""");
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            await writer.WriteAsync($$"""{"originKey":"crafted","questions":[],"worksheets":[{{string.Join(',', entries)}}]}""");
        }

        var result = await ImportAsync(output.ToArray());

        result.Worksheets.Imported.ShouldBe(3, JsonSerializer.Serialize(result.Worksheets));
        await _minio.Received(1).UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), MinioBucketPolicies.WorksheetsBucket, Arg.Any<string>());
    }

    private sealed class FailOnceWhenAdding(Type entity) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries().Any(e => e.State == EntityState.Added && e.Entity.GetType() == entity))
            {
                Fired = true;
                throw new TransientTestException("simulated");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailOnNewWorksheet : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<Worksheet>().Any(e => e.State == EntityState.Added)
                ? throw new DbUpdateException("simulated failure")
                : ValueTask.FromResult(result);
    }
}
