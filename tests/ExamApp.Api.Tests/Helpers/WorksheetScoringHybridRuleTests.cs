using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #571: <see cref="WorksheetScoring"/> hibrit kuralı — MCQ sonuçları bugünkü gibi canlı karşılaştırmayla kalır (kayıtlı
/// <c>IsCorrect</c> sütunu MCQ'da yok sayılır); etkileşimli cevap (payload, şıksız) kayıtlı <c>IsCorrect</c> ile sayılır.
/// Bellek tarafı (<see cref="WorksheetScoring.Tally{T}"/>) ve sorgu tarafı (EF <c>Count(Expression)</c>) AYNI sayıları vermeli.
/// </summary>
public class WorksheetScoringHybridRuleTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    // ---------------------------------------------------------------- saf kural

    [Fact]
    public void IsCorrect_McqSelectionMatchesCorrect_IsCorrectEvenWhenStoredFlagDisagrees()
    {
        WorksheetScoring.IsCorrect(selectedAnswerId: 5, correctAnswerId: 5, hasPayload: false, storedIsCorrect: false).ShouldBeTrue();
    }

    [Fact]
    public void IsCorrect_McqSelectionDiffersFromCorrect_IsWrongEvenWhenStoredFlagSaysCorrect()
    {
        // Canlı karşılaştırma kazanır: kayıtlı IsCorrect=true (eski/bozuk satır) MCQ'yu doğru yapmaz.
        WorksheetScoring.IsCorrect(selectedAnswerId: 5, correctAnswerId: 6, hasPayload: false, storedIsCorrect: true).ShouldBeFalse();
    }

    [Fact]
    public void IsCorrect_McqSelectionWithoutCorrectAnswerId_IsWrong()
    {
        WorksheetScoring.IsCorrect(selectedAnswerId: 5, correctAnswerId: null, hasPayload: false, storedIsCorrect: true).ShouldBeFalse();
    }

    [Fact]
    public void IsCorrect_PayloadOnlyAnswer_FollowsStoredGradeOfTheController()
    {
        WorksheetScoring.IsCorrect(selectedAnswerId: null, correctAnswerId: null, hasPayload: true, storedIsCorrect: true).ShouldBeTrue();
        WorksheetScoring.IsCorrect(selectedAnswerId: null, correctAnswerId: null, hasPayload: true, storedIsCorrect: false).ShouldBeFalse();
    }

    [Fact]
    public void IsCorrect_NoSelectionAndNoPayload_IsNeverCorrectEvenWhenStoredFlagIsTrue()
    {
        WorksheetScoring.IsCorrect(selectedAnswerId: null, correctAnswerId: 5, hasPayload: false, storedIsCorrect: true).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData(3, false, false)]
    [InlineData(3, true, false)]
    public void IsBlank_OnlyWhenNothingWasAnswered(int? selected, bool hasPayload, bool expected)
    {
        WorksheetScoring.IsBlank(selected, hasPayload).ShouldBe(expected);
    }

    [Fact]
    public void Tally_MixedRows_UsesLiveMcqComparisonAndStoredPayloadGrade()
    {
        var rows = new[]
        {
            new WorksheetScoring.AnswerFacts(SelectedAnswerId: 1, HasPayload: false, StoredIsCorrect: false, CorrectAnswerId: 1), // MCQ doğru
            new WorksheetScoring.AnswerFacts(SelectedAnswerId: 2, HasPayload: false, StoredIsCorrect: true, CorrectAnswerId: 1),  // MCQ yanlış (kayıt yalan)
            new WorksheetScoring.AnswerFacts(SelectedAnswerId: null, HasPayload: true, StoredIsCorrect: true, CorrectAnswerId: null), // etkileşim doğru
            new WorksheetScoring.AnswerFacts(SelectedAnswerId: null, HasPayload: true, StoredIsCorrect: false, CorrectAnswerId: null), // etkileşim yanlış
            new WorksheetScoring.AnswerFacts(SelectedAnswerId: null, HasPayload: false, StoredIsCorrect: false, CorrectAnswerId: 1), // boş
        };

        var (correct, wrong, blank) = WorksheetScoring.Tally(rows, r => r);

        correct.ShouldBe(2);
        wrong.ShouldBe(2);
        blank.ShouldBe(1);
    }

    [Theory]
    [InlineData(2, 5, 40)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33)]
    public void ScorePercent_RoundsDownAndHandlesEmptySessions(int correct, int total, int expected)
    {
        WorksheetScoring.ScorePercent(correct, total).ShouldBe(expected);
    }

    // ---------------------------------------------------------------- bellek = sorgu (SQLite, gerçek EF çevirisi)

    [Fact]
    public async Task SqlExpressions_MatchInMemoryTally_ForEveryAnswerShape()
    {
        var instanceId = await SeedMixedShapesAsync();

        await using var ctx = _db.NewContext();
        var rows = await ctx.TestInstanceQuestions.AsNoTracking()
            .Include(q => q.WorksheetQuestion).ThenInclude(wq => wq.Question)
            .Where(q => q.WorksheetInstanceId == instanceId)
            .ToListAsync();
        var memory = WorksheetScoring.Tally(rows, r => new WorksheetScoring.AnswerFacts(
            r.SelectedAnswerId, r.AnswerPayload != null, r.IsCorrect, r.WorksheetQuestion.Question.CorrectAnswerId));

        var total = await ctx.TestInstanceQuestions.CountAsync(q => q.WorksheetInstanceId == instanceId);
        var correct = await ctx.TestInstanceQuestions.Where(q => q.WorksheetInstanceId == instanceId)
            .CountAsync(WorksheetScoring.IsCorrectRow);
        var wrong = await ctx.TestInstanceQuestions.Where(q => q.WorksheetInstanceId == instanceId)
            .CountAsync(WorksheetScoring.IsWrongRow);
        var blank = await ctx.TestInstanceQuestions.Where(q => q.WorksheetInstanceId == instanceId)
            .CountAsync(WorksheetScoring.IsBlankRow);

        total.ShouldBe(rows.Count);
        correct.ShouldBe(memory.Correct);
        wrong.ShouldBe(memory.Wrong);
        blank.ShouldBe(memory.Blank);
        (correct + wrong + blank).ShouldBe(rows.Count);
    }

    /// <summary>
    /// Altı cevap şekli: MCQ doğru / MCQ yanlış (kayıt yalan) / MCQ yanlış ama sorunun doğru şıkkı YOK / etkileşim doğru /
    /// etkileşim yanlış / boş. Beklenen: doğru 2, yanlış 3, boş 1.
    /// </summary>
    private async Task<int> SeedMixedShapesAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 9001, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Karışık", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        ctx.AddRange(student, worksheet);
        await ctx.SaveChangesAsync();

        var mcqOk = await NewQuestionAsync(ctx, "mcq", hasCorrect: true);
        var mcqNoKey = await NewQuestionAsync(ctx, "mcq", hasCorrect: false);
        var drag = await NewQuestionAsync(ctx, "dragDropLabeling", hasCorrect: false);
        var blankQ = await NewQuestionAsync(ctx, "mcq", hasCorrect: true);

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Completed,
            StartTime = DateTime.UtcNow.AddMinutes(-30), EndTime = DateTime.UtcNow.AddMinutes(-1),
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var shapes = new List<WorksheetInstanceQuestion>();
        var pos = 0;
        WorksheetInstanceQuestion Row(WorksheetQuestion wq, int? selected, string? payload, bool stored) => new()
        {
            WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id,
            SelectedAnswerId = selected, AnswerPayload = payload, IsCorrect = stored,
        };

        var wqOk = await NewWorksheetQuestionAsync(ctx, worksheet.Id, mcqOk.Question.Id, ++pos);
        var wqNoKey = await NewWorksheetQuestionAsync(ctx, worksheet.Id, mcqNoKey.Question.Id, ++pos);
        var wqDrag = await NewWorksheetQuestionAsync(ctx, worksheet.Id, drag.Question.Id, ++pos);
        var wqBlank = await NewWorksheetQuestionAsync(ctx, worksheet.Id, blankQ.Question.Id, ++pos);
        var wqLie = await NewWorksheetQuestionAsync(ctx, worksheet.Id, mcqOk.Question.Id, ++pos);
        var wqDragNo = await NewWorksheetQuestionAsync(ctx, worksheet.Id, drag.Question.Id, ++pos);

        shapes.Add(Row(wqOk, mcqOk.CorrectAnswerId, null, stored: false));              // MCQ doğru → doğru
        shapes.Add(Row(wqNoKey, mcqNoKey.WrongAnswerId, null, stored: false));          // doğru şık yok → yanlış
        shapes.Add(Row(wqDrag, null, """{"placements":[]}""", stored: true));           // etkileşim doğru → doğru
        shapes.Add(Row(wqBlank, null, null, stored: false));                            // boş
        shapes.Add(Row(wqLie, mcqOk.WrongAnswerId, null, stored: true));                // MCQ yanlış, kayıt "doğru" → yanlış
        shapes.Add(Row(wqDragNo, null, """{"placements":[]}""", stored: false));        // etkileşim yanlış → yanlış
        ctx.TestInstanceQuestions.AddRange(shapes);
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    private sealed record QuestionSeed(Question Question, int? CorrectAnswerId, int WrongAnswerId);

    private static async Task<QuestionSeed> NewQuestionAsync(AppDbContext ctx, string type, bool hasCorrect)
    {
        var question = new Question { Text = "S", Point = 10, InteractionType = type == "mcq" ? null : type };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        var right = new Answer { QuestionId = question.Id, Text = "doğru", Tag = "A" };
        var wrong = new Answer { QuestionId = question.Id, Text = "yanlış", Tag = "B" };
        ctx.Answers.AddRange(right, wrong);
        await ctx.SaveChangesAsync();
        if (hasCorrect)
        {
            question.CorrectAnswerId = right.Id;
            await ctx.SaveChangesAsync();
        }
        return new QuestionSeed(question, hasCorrect ? right.Id : null, wrong.Id);
    }

    private static async Task<WorksheetQuestion> NewWorksheetQuestionAsync(AppDbContext ctx, int worksheetId, int questionId, int order)
    {
        var wq = new WorksheetQuestion { TestId = worksheetId, QuestionId = questionId, Order = order };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();
        return wq;
    }

    // ---------------------------------------------------------------- review #571: silinmiş soru (QuestionKnown = false)

    [Fact]
    public void Tally_DeletedQuestionPayloadRow_IsWrongEvenWhenStoredFlagIsTrue()
    {
        var rows = new[]
        {
            new WorksheetScoring.AnswerFacts(SelectedAnswerId: null, HasPayload: true, StoredIsCorrect: true, CorrectAnswerId: null, QuestionKnown: false),
        };

        var (correct, wrong, blank) = WorksheetScoring.Tally(rows, r => r);

        correct.ShouldBe(0);
        wrong.ShouldBe(1);
        blank.ShouldBe(0);
    }

    /// <summary>Bilinen MCQ doğru (1), silinmiş soruya ait etkileşim cevabı (kayıtlı IsCorrect=true), boş satır (1).</summary>
    private async Task<(int StudentId, int GradeId, int InstanceId)> SeedDeletedQuestionRowAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 9002, StudentNumber = "S2", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Silinmiş soru", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var known = new Question { Text = "bilinen", Point = 10 };
        var deleted = new Question { Text = "silinecek", Point = 10, InteractionType = "dragDropLabeling" };
        ctx.AddRange(student, worksheet, known, deleted);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = known.Id, Text = "A", Tag = "A" };
        ctx.Answers.Add(right);
        await ctx.SaveChangesAsync();
        known.CorrectAnswerId = right.Id;
        await ctx.SaveChangesAsync();

        var wqKnown = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = known.Id, Order = 1 };
        var wqDeleted = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = deleted.Id, Order = 2 };
        var wqBlank = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = known.Id, Order = 3 };
        ctx.TestQuestions.AddRange(wqKnown, wqDeleted, wqBlank);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Completed,
            StartTime = DateTime.UtcNow.AddMinutes(-30), EndTime = DateTime.UtcNow.AddMinutes(-1),
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        ctx.TestInstanceQuestions.AddRange(
            new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wqKnown.Id, SelectedAnswerId = right.Id, IsCorrect = true },
            new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wqDeleted.Id, AnswerPayload = """{"version":1,"type":"dragDropLabeling","placements":[]}""", IsCorrect = true },
            new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wqBlank.Id });
        await ctx.SaveChangesAsync();

        // Soruyu soft-delete et (sorgu filtresi navigation'ı boş bırakır).
        await ctx.Questions.IgnoreQueryFilters().Where(q => q.Id == deleted.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(q => q.IsDeleted, true));

        return (student.Id, grade.Id, instance.Id);
    }

    [Fact]
    public async Task InMemoryTally_DeletedQuestionRow_IsWrongAndTotalEqualsCorrectWrongBlank()
    {
        var seeded = await SeedDeletedQuestionRowAsync();

        await using var ctx = _db.NewContext();
        // Ürün yükleyicileriyle aynı biçim (review #571): satırlar soru zinciri Include EDİLMEDEN okunur (zorunlu gezinme +
        // soft-delete filtresi INNER JOIN'le silinmiş soru satırını düşürür — EF davranışı), doğru şık ayrı sözlükten gelir;
        // sözlükte olmayan soru = bilinmeyen.
        var rows = await ctx.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetInstanceId == seeded.InstanceId)
            .ToListAsync();
        rows.Count.ShouldBe(3);
        var wqIds = rows.Select(r => r.WorksheetQuestionId).ToList();
        var correctMap = await ctx.TestQuestions.AsNoTracking()
            .Where(wq => wqIds.Contains(wq.Id))
            .Select(wq => new { wq.Id, wq.Question.CorrectAnswerId })
            .ToDictionaryAsync(x => x.Id, x => x.CorrectAnswerId);
        var (correct, wrong, blank) = WorksheetScoring.Tally(rows, r => new WorksheetScoring.AnswerFacts(
            r.SelectedAnswerId, r.AnswerPayload != null, r.IsCorrect,
            correctMap.GetValueOrDefault(r.WorksheetQuestionId), QuestionKnown: correctMap.ContainsKey(r.WorksheetQuestionId)));

        correct.ShouldBe(1);
        wrong.ShouldBe(1);
        blank.ShouldBe(1);
        (correct + wrong + blank).ShouldBe(rows.Count);
    }

    [Fact]
    public async Task GetCompletedTests_DeletedQuestionRow_AgreesWithInMemoryTally()
    {
        // Beklenen (bellek tarafıyla aynı): doğru 1, yanlış 1, toplam 3 → yanlış = toplam − doğru − boş.
        var seeded = await SeedDeletedQuestionRowAsync();

        Paged<InstanceSummaryDto> list;
        await using (var ctx = _db.NewContext())
        {
            list = await new TestSessionService(ctx).GetCompletedTestsAsync(
                new StudentProfileDto { Id = seeded.StudentId, GradeId = seeded.GradeId }, 1, 10);
        }

        var item = list.Items.ShouldHaveSingleItem();
        item.TotalQuestions.ShouldBe(3);
        item.CorrectAnswers.ShouldBe(1);
        item.WrongAnswers.ShouldBe(1);
        item.WrongAnswers.ShouldBe(item.TotalQuestions - item.CorrectAnswers - 1); // 1 boş satır
    }
}
