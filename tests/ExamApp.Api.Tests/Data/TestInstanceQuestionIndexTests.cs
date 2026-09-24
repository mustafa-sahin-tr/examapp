using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ExamApp.Api.Tests.Data;

/// <summary>
/// Issue #265: admin trendleri ve öğretmen aktivite uçlarının "çözülen soru" yüklemine birebir uyan kısmi, kapsayan
/// index modelde (ve dolayısıyla migration'da) tanımlı olmalı.
/// </summary>
public class TestInstanceQuestionIndexTests
{
    [Fact]
    public void Answered_update_time_partial_covering_index_is_in_the_npgsql_model()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused;Database=unused").Options;
        using var ctx = new AppDbContext(options);
        var model = ctx.GetService<IDesignTimeModel>().Model;

        var index = model.FindEntityType(typeof(WorksheetInstanceQuestion))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "IX_TestInstanceQuestions_UpdateTime_Answered");

        index.Properties.Select(p => p.Name).ShouldBe(new[] { nameof(WorksheetInstanceQuestion.UpdateTime) });
        index.IsUnique.ShouldBeFalse();
        index.GetFilter().ShouldBe(
            "NOT \"IsDeleted\" AND (\"SelectedAnswerId\" IS NOT NULL OR \"AnswerPayload\" IS NOT NULL) AND \"UpdateTime\" IS NOT NULL");
        index.GetIncludeProperties().ShouldBe(new[]
        {
            nameof(WorksheetInstanceQuestion.WorksheetInstanceId),
            nameof(WorksheetInstanceQuestion.IsCorrect),
            nameof(WorksheetInstanceQuestion.TimeTaken),
        });
    }
}
