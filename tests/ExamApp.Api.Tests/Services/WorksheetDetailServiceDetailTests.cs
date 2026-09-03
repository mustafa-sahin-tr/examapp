using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class WorksheetDetailServiceDetailTests : IDisposable
{
    private const int OwnerTeacherUserId = 5000;
    private const int OtherTeacherUserId = 5001;

    private readonly TestDb _db = TestDb.Create();

    private static WorksheetDetailService NewService(AppDbContext ctx) => new(ctx);

    private sealed record World(
        int GradeId, int WorksheetId,
        int St1, int St2, int St3, int StNoAccess,
        int Q1, int Q2, int Q3, int Q4, int Q5,
        int Wq1, int Wq2, int Wq3, int Wq4, int Wq5,
        int T1, int T2,
        Dictionary<int, (int correct, int wrong)> Ans);

    /// <summary>
    /// 5-question worksheet owned by <see cref="OwnerTeacherUserId"/>.
    /// Q1,Q2 -> Topic T1; Q3,Q5 -> Topic T2; Q4 -> unclassified.
    /// Difficulty: Q1=2 (easy), Q2=5 (medium), Q3=8 (hard), Q4=3 (easy), Q5=5 (medium).
    /// Q1,Q3 carry a ClassificationSource; the rest are unclassified.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerTeacherUserId);

        var grade = new Grade { Name = "5" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();

        var t1 = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        var t2 = new Topic { Name = "Geometri", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.AddRange(t1, t2);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "Test", Description = "d", GradeId = grade.Id, SubjectId = subject.Id, MaxDurationSeconds = 600 };
        var q1 = new Question { Text = "Q1", TopicId = t1.Id, DifficultyLevel = 2, ClassificationSource = ClassificationSource.AI };
        var q2 = new Question { Text = "Q2", TopicId = t1.Id, DifficultyLevel = 5 };
        var q3 = new Question { Text = "Q3", TopicId = t2.Id, DifficultyLevel = 8, ClassificationSource = ClassificationSource.AI };
        var q4 = new Question { Text = "Q4", TopicId = null, DifficultyLevel = 3 };
        var q5 = new Question { Text = "Q5", TopicId = t2.Id, DifficultyLevel = 5 };
        ctx.AddRange(ws, q1, q2, q3, q4, q5);
        await ctx.SaveChangesAsync();

        var st1 = new Student { UserId = 101, StudentNumber = "s1", SchoolName = "Sch", GradeId = grade.Id };
        var st2 = new Student { UserId = 102, StudentNumber = "s2", SchoolName = "Sch", GradeId = grade.Id };
        var st3 = new Student { UserId = 103, StudentNumber = "s3", SchoolName = "Sch", GradeId = grade.Id };
        var st4 = new Student { UserId = 104, StudentNumber = "s4", SchoolName = "Sch", GradeId = grade.Id };
        ctx.AddRange(st1, st2, st3, st4);
        await ctx.SaveChangesAsync();

        var ans = new Dictionary<int, (int, int)>();
        foreach (var q in new[] { q1, q2, q3, q4, q5 })
        {
            var a = new Answer { QuestionId = q.Id, Text = "correct", Tag = "A", Order = 0 };
            var b = new Answer { QuestionId = q.Id, Text = "wrong", Tag = "B", Order = 1 };
            ctx.Answers.AddRange(a, b);
            await ctx.SaveChangesAsync();
            q.CorrectAnswerId = a.Id;
            await ctx.SaveChangesAsync();
            ans[q.Id] = (a.Id, b.Id);
        }

        var wq1 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q1.Id, Order = 1 };
        var wq2 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q2.Id, Order = 2 };
        var wq3 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q3.Id, Order = 3 };
        var wq4 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q4.Id, Order = 4 };
        var wq5 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q5.Id, Order = 5 };
        ctx.TestQuestions.AddRange(wq1, wq2, wq3, wq4, wq5);
        await ctx.SaveChangesAsync();

        return new World(grade.Id, ws.Id, st1.Id, st2.Id, st3.Id, st4.Id,
            q1.Id, q2.Id, q3.Id, q4.Id, q5.Id,
            wq1.Id, wq2.Id, wq3.Id, wq4.Id, wq5.Id,
            t1.Id, t2.Id, ans);
    }

    private async Task AddInstanceAsync(int worksheetId, int studentId, WorksheetInstanceStatus status,
        params (int worksheetQuestionId, int? selectedAnswerId)[] answers)
    {
        await using var ctx = _db.NewContext();
        ctx.TestInstances.Add(new WorksheetInstance
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
        });
        await ctx.SaveChangesAsync();
    }

    private async Task AddAssignmentAsync(int worksheetId, int? studentId, int? gradeId, int? createUserId = null)
    {
        await using var ctx = _db.NewContext();
        if (createUserId.HasValue)
            ctx.SetCurrentUser(createUserId.Value);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            GradeId = gradeId,
            StartAt = DateTime.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task AddReminderAsync(int worksheetId, int studentId, WorksheetReminderStatus status)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetReminders.Add(new WorksheetReminder
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            ScheduledFor = DateTime.UtcNow.AddDays(1),
            RemindBeforeMinutes = 30,
            Status = status,
        });
        await ctx.SaveChangesAsync();
    }

    private int C(World w, int qId) => w.Ans[qId].correct;
    private int X(World w, int qId) => w.Ans[qId].wrong;

    /// <summary>st1 -> 50% completed, st2 -> 100% completed, st3 -> in-progress attempt (must be ignored by insights).</summary>
    private async Task SeedStandardAttemptsAsync(World w)
    {
        await AddInstanceAsync(w.WorksheetId, w.St1, WorksheetInstanceStatus.Completed,
            (w.Wq1, C(w, w.Q1)), (w.Wq2, X(w, w.Q2)), (w.Wq3, null), (w.Wq4, C(w, w.Q4)));
        await AddInstanceAsync(w.WorksheetId, w.St2, WorksheetInstanceStatus.Completed,
            (w.Wq1, C(w, w.Q1)), (w.Wq2, C(w, w.Q2)), (w.Wq3, C(w, w.Q3)), (w.Wq4, C(w, w.Q4)));
        await AddInstanceAsync(w.WorksheetId, w.St3, WorksheetInstanceStatus.Started,
            (w.Wq1, X(w, w.Q1)), (w.Wq2, X(w, w.Q2)), (w.Wq3, C(w, w.Q3)));
    }

    private Task<WorksheetDetailDto?> AsTeacher(AppDbContext ctx, World w, int userId = OwnerTeacherUserId) =>
        NewService(ctx).GetWorksheetDetailAsync(w.WorksheetId, "Teacher", null, userId);

    private Task<WorksheetDetailDto?> AsStudent(AppDbContext ctx, World w, int studentId) =>
        NewService(ctx).GetWorksheetDetailAsync(w.WorksheetId, "Student", studentId, 0);

    // ---- access control ----

    [Fact]
    public async Task GetWorksheetDetail_TeacherNotOwnerAndNoAssignment_ReturnsDtoWithoutTeacherInsights()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w, OtherTeacherUserId);

        dto.ShouldNotBeNull();
        dto!.TeacherInsights.ShouldBeNull();
        dto.TopicBreakdown.ShouldNotBeEmpty(); // base info still present
    }

    [Fact]
    public async Task GetWorksheetDetail_TeacherOwnsViaAssignmentCreatedByThem_ReturnsTeacherInsights()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);
        await AddAssignmentAsync(w.WorksheetId, studentId: null, gradeId: w.GradeId, createUserId: OtherTeacherUserId);

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w, OtherTeacherUserId);

        dto.ShouldNotBeNull();
        dto!.TeacherInsights.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetail_StudentWithoutAssignmentOrInstance_ReturnsDtoWithBaseInfo()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await AsStudent(ctx, w, w.StNoAccess);

        dto.ShouldNotBeNull();
        dto!.Worksheet.Id.ShouldBe(w.WorksheetId);
        dto.TopicBreakdown.ShouldNotBeEmpty();
        dto.Attempts.ShouldBeEmpty();
        dto.CompletedResult.ShouldBeNull();
        dto.TeacherInsights.ShouldBeNull();
    }

    // ---- stats ----

    [Fact]
    public async Task GetWorksheetDetail_NoCompletedInstances_AverageScorePercentIsNull()
    {
        var w = await SeedAsync();
        await AddInstanceAsync(w.WorksheetId, w.St1, WorksheetInstanceStatus.Started, (w.Wq1, C(w, w.Q1)));

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        dto!.Stats.AverageScorePercent.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetail_WithCompletedInstances_AveragesOnlyCompleted()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        // (50 + 100) / 2 -> 75; the started attempt is ignored
        dto!.Stats.AverageScorePercent.ShouldBe(75);
    }

    // ---- topic breakdown ----

    [Fact]
    public async Task GetWorksheetDetail_TopicBreakdown_GroupsByTopicAndWeightsSumToAbout100()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        var breakdown = dto!.TopicBreakdown;
        breakdown.Sum(b => b.WeightPercent).ShouldBeInRange(98, 102);
        breakdown.Single(b => b.TopicId == w.T1).QuestionCount.ShouldBe(2);

        var unclassified = breakdown.Single(b => b.TopicId == null);
        unclassified.Name.ShouldBe("Sınıflandırılmamış");
        unclassified.QuestionCount.ShouldBe(1);
    }

    // ---- teacher insights ----

    [Fact]
    public async Task GetWorksheetDetail_HardestQuestions_OrderedByCorrectPercentAscendingMaxFive()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        var hardest = dto!.TeacherInsights!.HardestQuestions;
        hardest.Count.ShouldBeLessThanOrEqualTo(5);
        hardest.ShouldAllBe(h => h.AnsweredCount > 0);
        hardest.Select(h => h.CorrectPercent).ShouldBe(hardest.Select(h => h.CorrectPercent).OrderBy(p => p));
        hardest[0].QuestionId.ShouldBe(w.Q2); // 1 of 2 completed answers correct
        hardest[0].CorrectPercent.ShouldBe(50);
    }

    [Fact]
    public async Task GetWorksheetDetail_HardestQuestions_IgnoreInProgressInstances()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w); // st3 (Started) also answered Q1 wrong

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        var q1 = dto!.TeacherInsights!.HardestQuestions.Single(h => h.QuestionId == w.Q1);
        q1.AnsweredCount.ShouldBe(2);   // only the 2 completed instances
        q1.CorrectPercent.ShouldBe(100);
    }

    [Fact]
    public async Task GetWorksheetDetail_HardestQuestions_ExcludesUnansweredQuestions()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w); // nobody ever answers Q5

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        dto!.TeacherInsights!.HardestQuestions.ShouldNotContain(h => h.QuestionId == w.Q5);
    }

    [Fact]
    public async Task GetWorksheetDetail_DifficultyDistribution_BucketsByLevel()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        var d = dto!.TeacherInsights!.DifficultyDistribution;
        d.Easy.ShouldBe(2);   // levels 2 and 3
        d.Medium.ShouldBe(2); // levels 5 and 5
        d.Hard.ShouldBe(1);   // level 8
    }

    [Fact]
    public async Task GetWorksheetDetail_StudentRole_TeacherInsightsIsNull()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsStudent(ctx, w, w.St1);

        dto!.TeacherInsights.ShouldBeNull();
    }

    // ---- attempts ----

    [Fact]
    public async Task GetWorksheetDetail_TeacherRole_AttemptsAreEmpty()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        dto!.Attempts.ShouldBeEmpty();
        dto.CompletedResult.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetail_StudentRole_AttemptsContainOnlyOwnCompletedInstances()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsStudent(ctx, w, w.St1);

        var attempt = dto!.Attempts.ShouldHaveSingleItem();
        attempt.ScorePercent.ShouldBe(50);
        attempt.CorrectCount.ShouldBe(2);
        attempt.TotalCount.ShouldBe(4);
    }

    // ---- completed result + rank ----

    [Fact]
    public async Task GetWorksheetDetail_GradeScopedAssignment_RanksStudentWithinCohort()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);
        await AddAssignmentAsync(w.WorksheetId, studentId: null, gradeId: w.GradeId);

        await using var ctx = _db.NewContext();
        var dto = await AsStudent(ctx, w, w.St1);

        var rank = dto!.CompletedResult!.Rank;
        rank.ShouldNotBeNull();
        rank!.TotalStudents.ShouldBe(2);   // st1 + st2 completed; st3 in-progress excluded
        rank.Position.ShouldBe(2);         // st1 (50%) trails st2 (100%)
    }

    [Fact]
    public async Task GetWorksheetDetail_NoAssignment_RankIsNullButResultIsReturned()
    {
        var w = await SeedAsync();
        await SeedStandardAttemptsAsync(w);

        await using var ctx = _db.NewContext();
        var dto = await AsStudent(ctx, w, w.St1);

        dto!.CompletedResult.ShouldNotBeNull();
        dto.CompletedResult!.Rank.ShouldBeNull();
        dto.CompletedResult.ScorePercent.ShouldBe(50);
        dto.CompletedResult.EmptyCount.ShouldBe(1);
        dto.CompletedResult.WrongCount.ShouldBe(1);
    }

    // ---- planned reminder ----

    [Fact]
    public async Task GetWorksheetDetail_StudentWithActiveReminder_PopulatesPlannedReminder()
    {
        var w = await SeedAsync();
        await AddReminderAsync(w.WorksheetId, w.St1, WorksheetReminderStatus.Pending);

        await using var ctx = _db.NewContext();
        var dto = await AsStudent(ctx, w, w.St1);

        dto!.PlannedReminder.ShouldNotBeNull();
        dto.PlannedReminder!.RemindBeforeMinutes.ShouldBe(30);
        dto.PlannedReminder.Status.ShouldBe("Pending");
    }

    [Fact]
    public async Task GetWorksheetDetail_StudentWithCancelledReminder_PlannedReminderIsNull()
    {
        var w = await SeedAsync();
        await AddReminderAsync(w.WorksheetId, w.St1, WorksheetReminderStatus.Cancelled);

        await using var ctx = _db.NewContext();
        var dto = await AsStudent(ctx, w, w.St1);

        dto!.PlannedReminder.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetail_TeacherRole_PlannedReminderIsNull()
    {
        var w = await SeedAsync();
        await AddReminderAsync(w.WorksheetId, w.St1, WorksheetReminderStatus.Pending);

        await using var ctx = _db.NewContext();
        var dto = await AsTeacher(ctx, w);

        dto!.PlannedReminder.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetail_UnknownWorksheet_ReturnsNull()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetDetailAsync(999999, "Teacher", null, OwnerTeacherUserId)).ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
