using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services;
using ExamApp.Api.Services.QuestionTransfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Gerçek Postgres üzerinde: dersi boş testin seçim listesi sorgusu ve yeniden denemeli yürütme stratejisiyle test yazımı.
/// SQLite ikisini de sınamaz: strateji orada yeniden denemez, sorgu da farklı SQL üretir.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WorksheetTransferPostgresTests : IntegrationTestBase
{
    private const string SourceKey = "origin-a";

    public WorksheetTransferPostgresTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private static byte[] EmptyZip()
    {
        using var output = new MemoryStream();
        using (new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
        }
        return output.ToArray();
    }

    private Task<(int GradeId, int QuestionId)> SeedAsync() =>
        WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5. Sınıf", Code = "postgres-g5" };
            var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = 1 };
            db.Add(grade);
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            db.Add(new QuestionTransferImportMap { SourceKey = SourceKey, ExternalQuestionKey = "question:1", TargetQuestionId = question.Id });
            await db.SaveChangesAsync();
            return (grade.Id, question.Id);
        });

    [Fact]
    public async Task Selection_list_shows_a_worksheet_without_a_subject()
    {
        var (gradeId, questionId) = await SeedAsync();
        await WithDbAsync(async db =>
        {
            var worksheet = new Worksheet { Name = "Derssiz etkinlik", GradeId = gradeId, Kind = WorksheetKind.Activity };
            db.Worksheets.Add(worksheet);
            await db.SaveChangesAsync();
            db.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 });
            await db.SaveChangesAsync();
        });

        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuestionTransferService>();
        var item = (await service.ListWorksheetsAsync(WorksheetKind.Activity, null, null, "etkinlik", 50, default)).ShouldHaveSingleItem();

        item.SubjectName.ShouldBeNull();
        item.GradeName.ShouldBe("5. Sınıf");
        item.Kind.ShouldBe(nameof(WorksheetKind.Activity));
        item.QuestionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Worksheet_is_written_once_through_the_retrying_strategy()
    {
        var (gradeId, questionId) = await SeedAsync();
        const string manifest = """
            {"worksheets":[{"externalKey":"worksheet:7","id":7,"kind":"Test","name":"Taşınan test","grade":{"code":"postgres-g5","name":"5. Sınıf"},
            "items":[{"questionKey":"question:1","order":1}]}]}
            """;
        using var doc = JsonDocument.Parse(manifest);

        async Task<Models.Dtos.QuestionTransferWorksheetResultDto> ImportAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var zip = new ZipArchive(new MemoryStream(EmptyZip()), ZipArchiveMode.Read);
            return await new QuestionTransferWorksheetImporter(db, scope.ServiceProvider.GetRequiredService<IMinIoService>())
                .ImportAsync(zip, doc.RootElement, SourceKey, ownerUserId: 42);
        }

        var first = await ImportAsync();
        var second = await ImportAsync();

        first.Imported.ShouldBe(1, JsonSerializer.Serialize(first));
        second.AlreadyImported.ShouldBe(1);
        await WithDbAsync(async db =>
        {
            var worksheet = await db.Worksheets.AsNoTracking().SingleAsync(w => w.Name == "Taşınan test");
            worksheet.GradeId.ShouldBe(gradeId);
            worksheet.CreateUserId.ShouldBe(42);
            (await db.TestQuestions.AsNoTracking().SingleAsync(i => i.TestId == worksheet.Id)).QuestionId.ShouldBe(questionId);
        });
    }
}
