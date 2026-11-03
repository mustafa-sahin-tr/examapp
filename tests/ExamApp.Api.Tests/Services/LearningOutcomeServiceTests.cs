using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #692: MEB kazanımlarının içe aktarımı, müfredat sürümü ve soruya bağlanması.</summary>
public class LearningOutcomeServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private async Task<(int GradeId, int SubjectId, int QuestionId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var question = new Question { Text = "?", SubjectId = subject.Id, Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        return (grade.Id, subject.Id, question.Id);
    }

    private static LearningOutcomeImportDto Import(params (string Code, string Description)[] items) => new()
    {
        CurriculumVersion = "Maarif",
        SourceReference = "official list",
        Items = items.Select(i => new LearningOutcomeImportItemDto
        {
            Code = i.Code,
            Description = i.Description,
            GradeName = "5",
            SubjectName = "matematik",
        }).ToList(),
    };

    private async Task<LearningOutcomeImportResultDto> RunImportAsync(LearningOutcomeImportDto request, CurriculumVersion version = CurriculumVersion.Maarif)
    {
        await using var ctx = _db.NewContext();
        return await new LearningOutcomeService(ctx).ImportAsync(request, version, 1);
    }

    private async Task<int> OutcomeIdAsync(string code, CurriculumVersion version = CurriculumVersion.Maarif)
    {
        await using var ctx = _db.NewContext();
        return await ctx.LearningOutcomes.Where(lo => lo.Code == code && lo.CurriculumVersion == version).Select(lo => lo.Id).SingleAsync();
    }

    [Fact]
    public async Task Import_RunTwice_CreatesNoDuplicates()
    {
        await SeedAsync();
        var request = Import(("MAT.5.1", "Birinci"), ("MAT.5.2", "İkinci"));

        var first = await RunImportAsync(request);
        var second = await RunImportAsync(request);

        first.Created.ShouldBe(2);
        second.Created.ShouldBe(0);
        second.Unchanged.ShouldBe(2);
        await using var ctx = _db.NewContext();
        (await ctx.LearningOutcomes.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Import_ChangedDescription_UpdatesSameRow()
    {
        await SeedAsync();
        await RunImportAsync(Import(("MAT.5.1", "Eski")));
        var id = await OutcomeIdAsync("MAT.5.1");

        var result = await RunImportAsync(Import(("MAT.5.1", "Yeni")));

        result.Updated.ShouldBe(1);
        await using var ctx = _db.NewContext();
        (await ctx.LearningOutcomes.SingleAsync(lo => lo.Id == id)).Description.ShouldBe("Yeni");
    }

    [Fact]
    public async Task Import_SameCodeInBothCurricula_Coexists()
    {
        await SeedAsync();
        await RunImportAsync(Import(("M.5.1", "Maarif")));
        await RunImportAsync(Import(("M.5.1", "Önceki")), CurriculumVersion.Previous);

        (await OutcomeIdAsync("M.5.1")).ShouldNotBe(await OutcomeIdAsync("M.5.1", CurriculumVersion.Previous));
    }

    [Fact]
    public async Task Import_BadRow_WritesNothingAndReportsLine()
    {
        await SeedAsync();
        var request = Import(("MAT.5.1", "Birinci"), ("MAT.5.1", "Kopya"), ("", "Kodsuz"));
        request.Items.Add(new LearningOutcomeImportItemDto { Code = "MAT.5.9", Description = "x", GradeName = "99", SubjectName = "Matematik" });

        var result = await RunImportAsync(request);

        result.Created.ShouldBe(0);
        result.Errors.Count.ShouldBe(3);
        result.Errors[0].ShouldStartWith("2:");
        result.Errors[1].ShouldStartWith("3:");
        result.Errors[2].ShouldStartWith("4:");
        await using var ctx = _db.NewContext();
        (await ctx.LearningOutcomes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Search_FiltersByGradeSubjectVersionAndText()
    {
        var (gradeId, subjectId, _) = await SeedAsync();
        await RunImportAsync(Import(("MAT.5.1", "Kesirleri sıralar"), ("MAT.5.2", "Açıları ölçer")));
        await RunImportAsync(Import(("M.5.1", "Önceki kesirler")), CurriculumVersion.Previous);

        await using var ctx = _db.NewContext();
        var service = new LearningOutcomeService(ctx);
        (await service.SearchAsync(gradeId, subjectId, null, null)).Count.ShouldBe(3);
        (await service.SearchAsync(gradeId, subjectId, CurriculumVersion.Maarif, null)).Count.ShouldBe(2);
        (await service.SearchAsync(gradeId, subjectId, CurriculumVersion.Maarif, "MAT.5.2")).Single().Description.ShouldBe("Açıları ölçer");
        (await service.SearchAsync(gradeId + 1, subjectId, null, null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SetForQuestion_ReplacesLinks_AndCanRelinkRemovedOutcome()
    {
        var (_, _, questionId) = await SeedAsync();
        await RunImportAsync(Import(("A", "a"), ("B", "b")));
        var a = await OutcomeIdAsync("A");
        var b = await OutcomeIdAsync("B");

        async Task<List<string>> SetAsync(params int[] ids)
        {
            await using var ctx = _db.NewContext();
            var service = new LearningOutcomeService(ctx);
            (await service.SetForQuestionAsync(questionId, ids, 1)).ShouldBeTrue();
            return (await service.GetForQuestionAsync(questionId)).Select(o => o.Code).ToList();
        }

        (await SetAsync(a, b)).ShouldBe(new[] { "A", "B" });
        (await SetAsync(b)).ShouldBe(new[] { "B" });
        (await SetAsync(a, b)).ShouldBe(new[] { "A", "B" });
        (await SetAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SetForQuestion_UnknownOutcome_WritesNothing()
    {
        var (_, _, questionId) = await SeedAsync();
        await RunImportAsync(Import(("A", "a")));
        var a = await OutcomeIdAsync("A");

        await using var ctx = _db.NewContext();
        var service = new LearningOutcomeService(ctx);
        (await service.SetForQuestionAsync(questionId, new[] { a, a + 100 }, 1)).ShouldBeFalse();
        (await service.GetForQuestionAsync(questionId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task QuestionWithoutOutcomes_HasEmptyList()
    {
        var (_, _, questionId) = await SeedAsync();

        await using var ctx = _db.NewContext();
        (await new LearningOutcomeService(ctx).GetForQuestionAsync(questionId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Transfer_ResolvesOutcomesByVersionAndCode_SkipsUnknown()
    {
        await SeedAsync();
        await RunImportAsync(Import(("A", "a")));
        await RunImportAsync(Import(("A", "önceki")), CurriculumVersion.Previous);
        var expected = await OutcomeIdAsync("A");
        var json = JsonDocument.Parse("""
            [
              { "code": "A", "curriculumVersion": "Maarif" },
              { "code": "A", "curriculumVersion": "Maarif" },
              { "code": "MISSING", "curriculumVersion": "Maarif" },
              { "code": "A", "curriculumVersion": "99" },
              "bad"
            ]
            """);

        await using var ctx = _db.NewContext();
        var ids = await QuestionTransferJobRunner.ResolveLearningOutcomesAsync(ctx, json.RootElement);

        ids.ShouldBe(new[] { expected });
    }

    [Theory]
    [InlineData("Maarif", true)]
    [InlineData("previous", true)]
    [InlineData("1", false)]
    [InlineData("Other", false)]
    [InlineData("", false)]
    public void CurriculumVersion_OnlyDefinedNames(string value, bool ok)
    {
        CurriculumVersionParser.TryParse(value, out _).ShouldBe(ok);
    }

    [Fact]
    public void Import_IsAdminOnly_AndListIsForAuthoringRoles()
    {
        typeof(AdminLearningOutcomesController).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles).ShouldContain("Admin");
        typeof(LearningOutcomesController).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles).ShouldContain("Teacher,Admin");
    }

    public void Dispose() => _db.Dispose();
}
