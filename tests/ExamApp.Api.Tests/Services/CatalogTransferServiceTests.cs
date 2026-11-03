using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Catalog;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ReceivedExtensions;

namespace ExamApp.Api.Tests.Services;

public class CatalogTransferServiceTests : IDisposable
{
    private const string GradeCode = "1-sinif";
    private const string SubjectCode = "matematik";
    private const string TopicCode = "1-sinif.matematik.sayilar";
    private const string SubTopicCode = "1-sinif.matematik.sayilar.sayma";

    private readonly TestDb _db = TestDb.Create();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    private bool RefreshScheduled() => _jobs.ReceivedCalls().Any();

    private static CatalogBundleDto Bundle() => new()
    {
        Grades = [new() { Code = GradeCode, Name = "1. Sınıf" }],
        Subjects = [new() { Code = SubjectCode, Name = "Matematik" }],
        GradeSubjects = [new() { Grade = GradeCode, Subject = SubjectCode }],
        Topics =
        [
            new()
            {
                Code = TopicCode,
                Name = "Sayılar",
                Grade = GradeCode,
                Subject = SubjectCode,
                SubTopics =
                [
                    new() { Code = SubTopicCode, Name = "Sayma" },
                    new() { Code = TopicCode + ".karsilastirma", Name = "Karşılaştırma" },
                ],
            },
        ],
        LearningOutcomes =
        [
            new()
            {
                CurriculumVersion = "Maarif",
                Code = "MAT.1.1.1",
                Description = "Sayabilme",
                Grade = GradeCode,
                Subject = SubjectCode,
                SubTopic = SubTopicCode,
                SourceReference = "official document",
            },
        ],
    };

    private async Task<CatalogImportResultDto> ImportAsync(CatalogBundleDto bundle, bool dryRun = false)
    {
        await using var ctx = _db.NewContext();
        return await new CatalogTransferService(ctx, _jobs).ImportAsync(bundle, dryRun, userId: 1);
    }

    private async Task<CatalogBundleDto> ExportAsync()
    {
        await using var ctx = _db.NewContext();
        return await new CatalogTransferService(ctx, _jobs).ExportAsync();
    }

    [Fact]
    public async Task Taxonomy_change_refreshes_the_classifier_cache()
    {
        await ImportAsync(Bundle());

        Assert.True(RefreshScheduled());
    }

    [Fact]
    public async Task Dry_run_and_unchanged_import_do_not_refresh_the_classifier_cache()
    {
        await ImportAsync(Bundle(), dryRun: true);
        Assert.False(RefreshScheduled());

        await ImportAsync(Bundle());
        _jobs.ClearReceivedCalls();
        await ImportAsync(Bundle());

        Assert.False(RefreshScheduled());
    }

    [Fact]
    public async Task Import_creates_everything_with_codes_and_links_the_outcome_to_its_subtopic()
    {
        var result = await ImportAsync(Bundle());

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Grades.Created);
        Assert.Equal(1, result.Subjects.Created);
        Assert.Equal(1, result.GradeSubjects.Created);
        Assert.Equal(1, result.Topics.Created);
        Assert.Equal(2, result.SubTopics.Created);
        Assert.Equal(1, result.LearningOutcomes.Created);

        await using var ctx = _db.NewContext();
        Assert.Equal(GradeCode, (await ctx.Grades.SingleAsync()).Code);
        Assert.Equal(TopicCode, (await ctx.Topics.SingleAsync()).Code);
        var outcome = await ctx.LearningOutcomes.Include(lo => lo.SubTopic).SingleAsync();
        Assert.Equal(SubTopicCode, outcome.SubTopic!.Code);
        Assert.Equal(CurriculumVersion.Maarif, outcome.CurriculumVersion);
    }

    [Fact]
    public async Task Import_twice_creates_no_copies()
    {
        await ImportAsync(Bundle());

        var again = await ImportAsync(Bundle());

        Assert.Equal(0, again.Grades.Created + again.Subjects.Created + again.GradeSubjects.Created + again.Topics.Created + again.SubTopics.Created);
        Assert.Equal(0, again.Topics.Updated + again.SubTopics.Updated);
        Assert.Equal(1, again.Topics.Unchanged);
        Assert.Equal(2, again.SubTopics.Unchanged);
        Assert.Equal(1, again.LearningOutcomes.Unchanged);
    }

    [Fact]
    public async Task Same_code_with_a_new_name_renames_the_record()
    {
        await ImportAsync(Bundle());
        var renamed = Bundle();
        renamed.Topics[0].Name = "Doğal Sayılar";

        var result = await ImportAsync(renamed);

        Assert.Equal(1, result.Topics.Updated);
        await using var ctx = _db.NewContext();
        var topic = await ctx.Topics.SingleAsync();
        Assert.Equal("Doğal Sayılar", topic.Name);
        Assert.Equal(TopicCode, topic.Code);
    }

    [Fact]
    public async Task Rename_keeps_the_questions_link_because_the_row_is_the_same()
    {
        await ImportAsync(Bundle());
        int subTopicId;
        await using (var ctx = _db.NewContext())
            subTopicId = (await ctx.SubTopics.SingleAsync(s => s.Code == SubTopicCode)).Id;
        var renamed = Bundle();
        renamed.Topics[0].SubTopics[0].Name = "Ritmik Sayma";

        await ImportAsync(renamed);

        await using var check = _db.NewContext();
        Assert.Equal("Ritmik Sayma", (await check.SubTopics.SingleAsync(s => s.Id == subTopicId)).Name);
    }

    [Fact]
    public async Task Topic_moves_to_the_grade_the_file_names()
    {
        await ImportAsync(Bundle());
        var moved = Bundle();
        moved.Grades.Add(new() { Code = "2-sinif", Name = "2. Sınıf" });
        moved.Topics[0].Grade = "2-sinif";

        var result = await ImportAsync(moved);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Topics.Updated);
        await using var ctx = _db.NewContext();
        var topic = await ctx.Topics.Include(t => t.Grade).SingleAsync();
        Assert.Equal("2. Sınıf", topic.Grade.Name);
    }

    [Fact]
    public async Task Old_row_without_a_code_is_matched_by_name_and_gets_the_code()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Grades.Add(new Grade { Name = "1. Sınıf" });
            ctx.Subjects.Add(new Subject { Name = "Matematik" });
            await ctx.SaveChangesAsync();
        }

        var result = await ImportAsync(Bundle());

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Grades.Updated);
        Assert.Equal(0, result.Grades.Created);
        await using var check = _db.NewContext();
        Assert.Equal(GradeCode, (await check.Grades.SingleAsync()).Code);
    }

    [Fact]
    public async Task Same_name_with_another_code_stops_the_import()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Grades.Add(new Grade { Name = "1. Sınıf", Code = "another-code" });
            await ctx.SaveChangesAsync();
        }

        var result = await ImportAsync(Bundle());

        Assert.Contains(result.Errors, e => e.Contains("another-code", StringComparison.Ordinal));
        await using var check = _db.NewContext();
        Assert.Equal("another-code", (await check.Grades.SingleAsync()).Code);
        Assert.Equal(0, await check.Subjects.CountAsync());
    }

    [Fact]
    public async Task Rename_onto_a_name_another_record_uses_is_rejected()
    {
        var bundle = Bundle();
        bundle.Topics.Add(new() { Code = "other", Name = "Geometri", Grade = GradeCode, Subject = SubjectCode });
        await ImportAsync(bundle);
        var clash = Bundle();
        clash.Topics[0].Name = "Geometri";

        var result = await ImportAsync(clash);

        Assert.Contains(result.Errors, e => e.Contains("another record", StringComparison.Ordinal));
        await using var ctx = _db.NewContext();
        Assert.Equal("Sayılar", (await ctx.Topics.SingleAsync(t => t.Code == TopicCode)).Name);
    }

    [Fact]
    public async Task Same_named_rows_without_codes_stop_the_import_instead_of_guessing()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Grades.AddRange(new Grade { Name = "1. Sınıf" }, new Grade { Name = "1. sınıf" });
            await ctx.SaveChangesAsync();
        }

        var result = await ImportAsync(Bundle());

        Assert.Contains(result.Errors, e => e.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dry_run_reports_the_counts_and_writes_nothing()
    {
        var result = await ImportAsync(Bundle(), dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.Topics.Created);
        Assert.Equal(1, result.LearningOutcomes.Created);
        await using var ctx = _db.NewContext();
        Assert.Equal(0, await ctx.Grades.CountAsync());
        Assert.Equal(0, await ctx.Topics.CountAsync());
        Assert.Equal(0, await ctx.LearningOutcomes.CountAsync());
    }

    [Fact]
    public async Task Import_keeps_rows_the_file_does_not_mention()
    {
        await ImportAsync(Bundle());
        var other = Bundle();
        other.Topics[0].SubTopics.RemoveAt(1);
        other.Topics.Add(new() { Code = "geometri", Name = "Geometri", Grade = GradeCode, Subject = SubjectCode });

        var result = await ImportAsync(other);

        Assert.Equal(1, result.Topics.Created);
        await using var ctx = _db.NewContext();
        Assert.Equal(2, await ctx.Topics.CountAsync());
        Assert.Equal(2, await ctx.SubTopics.CountAsync());
    }

    [Fact]
    public async Task One_bad_row_writes_nothing()
    {
        var bundle = Bundle();
        bundle.Topics.Add(new() { Code = "x", Name = "X", Grade = "unknown-grade", Subject = SubjectCode });

        var result = await ImportAsync(bundle);

        Assert.Contains(result.Errors, e => e.StartsWith("topics[1]", StringComparison.Ordinal));
        await using var ctx = _db.NewContext();
        Assert.Equal(0, await ctx.Grades.CountAsync());
        Assert.Equal(0, await ctx.LearningOutcomes.CountAsync());
    }

    [Theory]
    [InlineData("Unknown", "MAT.1", SubTopicCode, "unknown curriculum")]
    [InlineData("Maarif", "", SubTopicCode, "required")]
    [InlineData("Maarif", "MAT.1", "missing", "unknown subTopic")]
    public async Task Invalid_outcome_is_reported_with_its_row(string version, string code, string subTopic, string expected)
    {
        var bundle = Bundle();
        bundle.LearningOutcomes.Add(new()
        {
            CurriculumVersion = version,
            Code = code,
            Description = "text",
            Grade = GradeCode,
            Subject = SubjectCode,
            SubTopic = subTopic,
        });

        var result = await ImportAsync(bundle);

        Assert.Contains(result.Errors, e => e.StartsWith("learningOutcomes[1]", StringComparison.Ordinal) && e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_codes_in_the_file_are_rejected()
    {
        var bundle = Bundle();
        bundle.LearningOutcomes.Add(bundle.LearningOutcomes[0]);
        bundle.Grades.Add(bundle.Grades[0]);

        var result = await ImportAsync(bundle);

        Assert.Contains(result.Errors, e => e.StartsWith("learningOutcomes[1]", StringComparison.Ordinal) && e.Contains("duplicate", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.StartsWith("grades[1]", StringComparison.Ordinal) && e.Contains("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Row_without_a_code_is_rejected()
    {
        var bundle = Bundle();
        bundle.Topics[0].Code = " ";

        var result = await ImportAsync(bundle);

        Assert.Contains(result.Errors, e => e.StartsWith("topics[0]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unsupported_format_version_is_rejected()
    {
        var bundle = Bundle();
        bundle.FormatVersion = 99;

        var result = await ImportAsync(bundle);

        Assert.Contains(result.Errors, e => e.StartsWith("formatVersion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outcome_update_changes_the_text_and_keeps_a_link_the_file_omits()
    {
        await ImportAsync(Bundle());
        var bundle = Bundle();
        bundle.LearningOutcomes[0].Description = "Corrected text";
        bundle.LearningOutcomes[0].SubTopic = null;

        var result = await ImportAsync(bundle);

        Assert.Equal(1, result.LearningOutcomes.Updated);
        await using var ctx = _db.NewContext();
        var outcome = await ctx.LearningOutcomes.SingleAsync();
        Assert.Equal("Corrected text", outcome.Description);
        Assert.NotNull(outcome.SubTopicId);
    }

    [Fact]
    public async Task Export_then_import_into_an_empty_database_reproduces_the_catalog()
    {
        await ImportAsync(Bundle());
        var exported = await ExportAsync();

        using var target = TestDb.Create();
        await using (var ctx = target.NewContext())
        {
            var result = await new CatalogTransferService(ctx, _jobs).ImportAsync(exported, dryRun: false, userId: 1);
            Assert.Empty(result.Errors);
        }

        await using var check = target.NewContext();
        var copy = await new CatalogTransferService(check, _jobs).ExportAsync();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(exported), System.Text.Json.JsonSerializer.Serialize(copy));
    }

    [Fact]
    public async Task Export_gives_rows_without_a_code_the_code_the_migration_would()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5. Sınıf" };
            var subject = new Subject { Name = "Türkçe" };
            ctx.AddRange(grade, subject);
            await ctx.SaveChangesAsync();
            ctx.Topics.Add(new Topic { Name = "Okuduğunu Anlama", GradeId = grade.Id, SubjectId = subject.Id });
            await ctx.SaveChangesAsync();
        }

        var exported = await ExportAsync();

        Assert.Equal("5-sinif.turkce.okudugunu-anlama", exported.Topics.Single().Code);
    }

    [Fact]
    public async Task Export_leaves_out_soft_deleted_rows()
    {
        await ImportAsync(Bundle());
        await using (var ctx = _db.NewContext())
        {
            var topic = await ctx.Topics.SingleAsync();
            topic.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var exported = await ExportAsync();

        Assert.Empty(exported.Topics);
    }

    [Theory]
    [InlineData("5. Sınıf", "5-sinif")]
    [InlineData("Türk Dili ve Edebiyatı", "turk-dili-ve-edebiyati")]
    [InlineData("T.C. İnkılap Tarihi ve Atatürkçülük", "t-c-inkilap-tarihi-ve-ataturkculuk")]
    [InlineData("  Çağdaş / Şiir -- Öğüt  ", "cagdas-siir-ogut")]
    [InlineData("Modül 1: Hücre", "modul-1-hucre")]
    public void Slug_is_ascii_and_stable(string name, string expected) => Assert.Equal(expected, CatalogCode.Slug(name));

    [Fact]
    public void Controller_is_admin_only()
    {
        var roles = typeof(AdminCatalogController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).Select(a => a.Roles);

        Assert.Contains("Admin", roles);
    }

    public void Dispose() => _db.Dispose();
}
