using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Catalog;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>Açılışta MEB paketi: yalnız paket değişince içe aktarılır, hata olursa iz bırakmaz ki sonraki açılış yeniden denesin.</summary>
public class CatalogPackageImporterTests : IDisposable
{
    private const string GradeCode = "1-sinif";
    private const string SubjectCode = "matematik";

    private readonly TestDb _db = TestDb.Create();
    private readonly string _dir = Directory.CreateTempSubdirectory("catalog-package-").FullName;
    private string PackagePath => Path.Combine(_dir, "maarif-catalog.json");

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CatalogBundleDto Bundle(string topicName = "Sayılar") => new()
    {
        Grades = [new() { Code = GradeCode, Name = "1. Sınıf" }],
        Subjects = [new() { Code = SubjectCode, Name = "Matematik" }],
        GradeSubjects = [new() { Grade = GradeCode, Subject = SubjectCode }],
        Topics = [new() { Code = "1-sinif.matematik.sayilar", Name = topicName, Grade = GradeCode, Subject = SubjectCode }],
        LearningOutcomes =
        [
            new() { CurriculumVersion = "Maarif", Code = "MAT.1.1.1", Description = "Sayabilme", Grade = GradeCode, Subject = SubjectCode },
        ],
    };

    private void WritePackage(CatalogBundleDto bundle) =>
        File.WriteAllText(PackagePath, JsonSerializer.Serialize(bundle, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private Task<CatalogPackageImportOutcome> RunAsync()
    {
        var ctx = _db.NewContext();
        var transfer = new CatalogTransferService(ctx, Substitute.For<IBackgroundJobClient>());
        return new CatalogPackageImporter(ctx, transfer, NullLogger<CatalogPackageImporter>.Instance).ImportIfChangedAsync(PackagePath);
    }

    [Fact]
    public async Task First_start_imports_the_package_and_records_it()
    {
        WritePackage(Bundle());

        var outcome = await RunAsync();

        outcome.Status.ShouldBe(CatalogPackageImportStatus.Imported);
        await using var ctx = _db.NewContext();
        (await ctx.LearningOutcomes.CountAsync()).ShouldBe(1);
        var applied = await ctx.AppliedDataPackages.SingleAsync();
        applied.Name.ShouldBe("maarif-catalog");
        applied.Sha256.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Same_package_is_not_imported_again()
    {
        WritePackage(Bundle());
        await RunAsync();

        var outcome = await RunAsync();

        outcome.Status.ShouldBe(CatalogPackageImportStatus.Unchanged);
        outcome.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Changed_package_is_imported_and_its_hash_replaces_the_old_one()
    {
        WritePackage(Bundle());
        await RunAsync();
        string firstHash;
        await using (var ctx = _db.NewContext())
            firstHash = (await ctx.AppliedDataPackages.SingleAsync()).Sha256;

        WritePackage(Bundle(topicName: "Doğal Sayılar"));
        var outcome = await RunAsync();

        outcome.Status.ShouldBe(CatalogPackageImportStatus.Imported);
        outcome.Result!.Topics.Updated.ShouldBe(1);
        await using var check = _db.NewContext();
        (await check.Topics.SingleAsync()).Name.ShouldBe("Doğal Sayılar");
        (await check.AppliedDataPackages.SingleAsync()).Sha256.ShouldNotBe(firstHash);
    }

    [Fact]
    public async Task Rejected_package_leaves_no_record_so_the_next_start_retries()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Grades.Add(new Grade { Name = "1. Sınıf", Code = "birinci-sinif" });
            await ctx.SaveChangesAsync();
        }
        WritePackage(Bundle());

        var outcome = await RunAsync();

        outcome.Status.ShouldBe(CatalogPackageImportStatus.Failed);
        outcome.Result!.Errors.ShouldNotBeEmpty();
        await using var check = _db.NewContext();
        (await check.AppliedDataPackages.AnyAsync()).ShouldBeFalse();
        (await check.LearningOutcomes.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_file_is_skipped()
    {
        var outcome = await RunAsync();

        outcome.Status.ShouldBe(CatalogPackageImportStatus.Missing);
        await using var ctx = _db.NewContext();
        (await ctx.AppliedDataPackages.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public void Default_path_is_the_package_copied_next_to_the_api()
    {
        var path = new CatalogPackageOptions().ResolvePath();

        path.ShouldEndWith(Path.Combine("Data", "Packages", "maarif-catalog.json"));
        path.ShouldStartWith(AppContext.BaseDirectory);
    }
}
