using ExamApp.Api.Data;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class WorksheetDetailServiceFromMistakesTests : IDisposable
{
    private const int StudentUserId = 7001;

    private readonly TestDb _db = TestDb.Create();

    private static WorksheetDetailService NewService(AppDbContext ctx) => new(ctx);

    private sealed record World(
        int SourceWorksheetId, int StudentId, int OtherStudentId,
        int Q1, int Q2, int Q3, int Q4,
        int Wq1, int Wq2, int Wq3, int Wq4,
        int Q1Correct, int Q1Wrong, int Q2Correct, int Q2Wrong,
        int Q3Correct, int Q3Wrong, int Q4Correct, int Q4Wrong);

    private async Task<World> SeedAsync(bool duplicateQ1 = false)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 1, StudentNumber = "s1", SchoolName = "Sch", GradeId = grade.Id };
        var other = new Student { UserId = 2, StudentNumber = "s2", SchoolName = "Sch", GradeId = grade.Id };
        var ws = new Worksheet { Name = "Kaynak", Description = "d", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var q1 = new Question { Text = "Q1", DifficultyLevel = 2 };
        var q2 = new Question { Text = "Q2", DifficultyLevel = 2 };
        var q3 = new Question { Text = "Q3", DifficultyLevel = 2 };
        var q4 = new Question { Text = "Q4", DifficultyLevel = 2 };
        ctx.AddRange(student, other, ws, q1, q2, q3, q4);
        await ctx.SaveChangesAsync();

        (int correct, int wrong) AddAnswers(Question q)
        {
            var a = new Answer { QuestionId = q.Id, Text = "correct", Tag = "A", Order = 0 };
            var b = new Answer { QuestionId = q.Id, Text = "wrong", Tag = "B", Order = 1 };
            ctx.Answers.AddRange(a, b);
            ctx.SaveChanges();
            q.CorrectAnswerId = a.Id;
            ctx.SaveChanges();
            return (a.Id, b.Id);
        }

        var (q1c, q1w) = AddAnswers(q1);
        var (q2c, q2w) = AddAnswers(q2);
        var (q3c, q3w) = AddAnswers(q3);
        var (q4c, q4w) = AddAnswers(q4);

        var wq1 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q1.Id, Order = 1 };
        var wq2 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q2.Id, Order = 2 };
        var wq3 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q3.Id, Order = 3 };
        var wq4 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q4.Id, Order = 4 };
        ctx.TestQuestions.AddRange(wq1, wq2, wq3, wq4);
        if (duplicateQ1)
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q1.Id, Order = 5 });
        await ctx.SaveChangesAsync();

        return new World(ws.Id, student.Id, other.Id,
            q1.Id, q2.Id, q3.Id, q4.Id,
            wq1.Id, wq2.Id, wq3.Id, wq4.Id,
            q1c, q1w, q2c, q2w, q3c, q3w, q4c, q4w);
    }

    private async Task<int> AddInstanceAsync(int worksheetId, int studentId, WorksheetInstanceStatus status,
        params (int worksheetQuestionId, int? selectedAnswerId)[] answers)
    {
        await using var ctx = _db.NewContext();
        var instance = new WorksheetInstance
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            Status = status,
            StartTime = DateTime.UtcNow.AddMinutes(-30),
            EndTime = status == WorksheetInstanceStatus.Completed ? DateTime.UtcNow : (DateTime?)null,
            WorksheetInstanceQuestions = answers
                .Select(a => new WorksheetInstanceQuestion
                {
                    WorksheetQuestionId = a.worksheetQuestionId,
                    SelectedAnswerId = a.selectedAnswerId,
                })
                .ToList(),
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_InstanceNotFound_ReturnsNull()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId: 9999, studentId: 1, userId: StudentUserId))
            .ShouldBeNull();
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_InstanceOwnedByAnotherStudent_ThrowsUnauthorized()
    {
        var w = await SeedAsync();
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Wrong));

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.OtherStudentId, StudentUserId));
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_InstanceNotCompleted_ThrowsInvalidOperation()
    {
        var w = await SeedAsync();
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Started,
            (w.Wq1, w.Q1Wrong));

        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId));
        ex.Message.ShouldContain("tamamlan");
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_NoWrongAnswers_ThrowsInvalidOperation()
    {
        var w = await SeedAsync();
        // Q1 correct, Q2 empty, Q3 correct, Q4 empty -> nothing wrong-marked
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Correct), (w.Wq2, null), (w.Wq3, w.Q3Correct), (w.Wq4, null));

        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId));
        ex.Message.ShouldContain("Yanlış");
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_OnlyWrongMarkedQuestions_AreAddedInOrder()
    {
        var w = await SeedAsync();
        // Q1 wrong, Q2 correct, Q3 empty, Q4 wrong -> expect [Q1, Q4]
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Wrong), (w.Wq2, w.Q2Correct), (w.Wq3, null), (w.Wq4, w.Q4Wrong));

        int newWorksheetId;
        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId);
            result.ShouldNotBeNull();
            newWorksheetId = result!.WorksheetId;
        }

        await using var check = _db.NewContext();
        var qIds = await check.TestQuestions
            .Where(wq => wq.TestId == newWorksheetId)
            .OrderBy(wq => wq.Order)
            .Select(wq => wq.QuestionId)
            .ToListAsync();
        qIds.ShouldBe(new[] { w.Q1, w.Q4 });

        var created = await check.Worksheets.FirstAsync(x => x.Id == newWorksheetId);
        created.IsPracticeTest.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_SameQuestionWrongTwice_IsDeduplicated()
    {
        var w = await SeedAsync(duplicateQ1: true);
        int dupWqId;
        await using (var ctx = _db.NewContext())
        {
            dupWqId = await ctx.TestQuestions
                .Where(wq => wq.TestId == w.SourceWorksheetId && wq.QuestionId == w.Q1 && wq.Order == 5)
                .Select(wq => wq.Id)
                .FirstAsync();
        }

        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Wrong), (dupWqId, w.Q1Wrong));

        int newWorksheetId;
        await using (var ctx = _db.NewContext())
            newWorksheetId = (await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId))!.WorksheetId;

        await using var check = _db.NewContext();
        (await check.TestQuestions.CountAsync(wq => wq.TestId == newWorksheetId)).ShouldBe(1);
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_StampsCreateUserIdFromCaller()
    {
        var w = await SeedAsync();
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Wrong), (w.Wq2, w.Q2Correct));

        int newWorksheetId;
        await using (var ctx = _db.NewContext())
            newWorksheetId = (await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId))!.WorksheetId;

        await using var check = _db.NewContext();
        var created = await check.Worksheets.FirstAsync(x => x.Id == newWorksheetId);
        created.CreateUserId.ShouldBe(StudentUserId);
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_AddsStudentScopedAssignmentForTheOwner()
    {
        var w = await SeedAsync();
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Wrong), (w.Wq2, w.Q2Correct));

        int newWorksheetId;
        await using (var ctx = _db.NewContext())
            newWorksheetId = (await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId))!.WorksheetId;

        await using var check = _db.NewContext();
        var assignment = await check.WorksheetAssignments
            .SingleAsync(a => a.WorksheetId == newWorksheetId);
        assignment.StudentId.ShouldBe(w.StudentId);
        assignment.GradeId.ShouldBeNull();
    }

    [Fact]
    public async Task CreateWorksheetFromMistakes_CalledTwiceForSameInstance_IsIdempotent()
    {
        var w = await SeedAsync();
        var instanceId = await AddInstanceAsync(w.SourceWorksheetId, w.StudentId, WorksheetInstanceStatus.Completed,
            (w.Wq1, w.Q1Wrong), (w.Wq2, w.Q2Correct), (w.Wq3, null), (w.Wq4, w.Q4Wrong));

        int first, second;
        await using (var ctx = _db.NewContext())
            first = (await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId))!.WorksheetId;
        await using (var ctx = _db.NewContext())
            second = (await NewService(ctx).CreateWorksheetFromMistakesAsync(instanceId, w.StudentId, StudentUserId))!.WorksheetId;

        second.ShouldBe(first);

        await using var check = _db.NewContext();
        (await check.Worksheets.CountAsync(x => x.IsPracticeTest)).ShouldBe(1);
        (await check.WorksheetAssignments.CountAsync(a => a.WorksheetId == first)).ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
