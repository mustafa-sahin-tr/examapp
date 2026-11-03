using System.Runtime.CompilerServices;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Catalog;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Repodaki MEB paketi gerçek katalog servisinden geçer: ekrandan yüklenince reddedilecek bir dosya birleşmesin.</summary>
public class MaarifCatalogFileTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    private static CatalogBundleDto ReadBundle()
    {
        var path = Path.Combine(RepoRoot(), "deploy", "data", "meb", "maarif-catalog.json");
        return JsonSerializer.Deserialize<CatalogBundleDto>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private Task<CatalogImportResultDto> ImportAsync(CatalogBundleDto bundle, bool dryRun)
    {
        var ctx = _db.NewContext();
        return new CatalogTransferService(ctx, Substitute.For<IBackgroundJobClient>()).ImportAsync(bundle, dryRun, userId: 1);
    }

    [Fact]
    public async Task Package_is_accepted_and_writes_everything_it_counted()
    {
        var bundle = ReadBundle();

        var preview = await ImportAsync(bundle, dryRun: true);

        preview.Errors.ShouldBeEmpty();
        preview.LearningOutcomes.Created.ShouldBe(bundle.LearningOutcomes.Count);

        var applied = await ImportAsync(bundle, dryRun: false);

        applied.Errors.ShouldBeEmpty();
        await using var ctx = _db.NewContext();
        (await ctx.LearningOutcomes.CountAsync()).ShouldBe(bundle.LearningOutcomes.Count);
        (await ctx.Grades.CountAsync()).ShouldBe(bundle.Grades.Count);
    }

    [Fact]
    public async Task Importing_the_package_twice_changes_nothing()
    {
        var bundle = ReadBundle();
        await ImportAsync(bundle, dryRun: false);

        var again = await ImportAsync(bundle, dryRun: true);

        again.Errors.ShouldBeEmpty();
        again.LearningOutcomes.Created.ShouldBe(0);
        again.LearningOutcomes.Updated.ShouldBe(0);
        again.Topics.Created.ShouldBe(0);
        again.SubTopics.Created.ShouldBe(0);
    }

    [Theory]
    [InlineData("MAT.5.1.1", "5-sinif", "matematik", "Altı basamaklı sayıları okuma ve yazmayı çok basamaklı sayılara genelleyebilme")]
    [InlineData("FİZ.11.1.1", "11-sinif", "fizik", "Serbest düşme hareketi yapan cisimlerin ivmesine yönelik tümevarımsal akıl yürütebilme")]
    [InlineData("T.K.5.21", "5-sinif", "turkce", "Konuşma sürecini değerlendirebilme")]
    public void Spot_checked_outcomes_keep_their_grade_subject_and_text(string code, string grade, string subject, string description)
    {
        var outcome = ReadBundle().LearningOutcomes.Single(o => o.Code == code);

        outcome.Grade.ShouldBe(grade);
        outcome.Subject.ShouldBe(subject);
        outcome.Description.ShouldBe(description);
        outcome.CurriculumVersion.ShouldBe(nameof(CurriculumVersion.Maarif));
    }

    [Fact]
    public void Subject_names_keep_their_correct_spelling()
    {
        var names = ReadBundle().Subjects.Select(s => s.Name).ToList();

        names.ShouldContain("T.C. İnkılap Tarihi ve Atatürkçülük");
        names.ShouldContain("Kur'an-ı Kerim");
    }

    [Fact]
    public void A_level_in_the_title_or_code_is_not_read_as_a_grade()
    {
        var outcomes = ReadBundle().LearningOutcomes;

        outcomes.ShouldNotContain(o => o.Subject == "temel-dini-bilgiler" && (o.Grade == "1-sinif" || o.Grade == "2-sinif"));
        outcomes.ShouldNotContain(o => o.Code.StartsWith("SMTHMK.") || o.Code == "KSE.5.1");
    }

    [Fact]
    public void Older_turkish_programme_adds_nothing_beside_the_newer_one()
    {
        var turkish = ReadBundle().LearningOutcomes.Where(o => o.Subject == "turkce").ToList();

        turkish.ShouldNotContain(o => o.SourceReference!.Contains("PID=1979"));
        turkish.ShouldNotContain(o => o.Code.StartsWith("T.5."));
    }

    [Fact]
    public void Core_subjects_have_their_themes()
    {
        var topics = ReadBundle().Topics;

        topics.ShouldContain(t => t.Grade == "1-sinif" && t.Subject == "hayat-bilgisi" && t.Name == "Ben ve Okulum");
        topics.ShouldContain(t => t.Subject == "sosyal-bilgiler" && t.Name == "Ortak Mirasımız");
    }

    [Fact]
    public void Newer_programme_wins_when_two_programmes_use_the_same_code()
    {
        var outcome = ReadBundle().LearningOutcomes.Single(o => o.Code == "T.K.5.21");

        outcome.SourceReference.ShouldNotBeNull();
        outcome.SourceReference.ShouldContain("PID=2352");
    }
}
