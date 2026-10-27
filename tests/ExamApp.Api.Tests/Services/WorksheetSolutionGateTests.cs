using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #580: onaylanmamış (AI / cevap anahtarı) çözümlü soru içeren ETKİNLİK sayfası öğrenciye atanamaz, öğrenci yeni oturum
/// başlatamaz ve keşfet listesinde görmez; onaydan sonra hepsi açılır. Test (MCQ) sayfaları ve silinmiş sorular etkilenmez.
/// </summary>
public class WorksheetSolutionGateTests : IDisposable
{
    private const int OwnerUserId = 1;
    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record World(int GradeId, int StudentId, int ActivityId, int TestId, int PendingQuestionId, int CleanTestId);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerUserId);
        var grade = new Grade { Name = "5" };
        var school = new School { Name = "Okul" };
        ctx.AddRange(grade, school);
        await ctx.SaveChangesAsync();

        var activity = new Worksheet { Name = "Etkinlik", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Activity };
        var test = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Test };
        var clean = new Worksheet { Name = "Temiz", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Test };
        var student = new Student
        {
            UserId = 40, StudentNumber = "n", SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow,
            GradeId = grade.Id,
        };
        var owner = new Teacher { UserId = OwnerUserId, SchoolId = school.Id };
        var pending = new Question
        {
            Text = "D/Y", InteractionType = "matrixChoice", InteractionSolution = """{"r1":["c1"]}""",
            SolutionSource = SolutionSource.AI, SolutionStatus = SolutionStatus.Pending,
        };
        var approved = new Question
        {
            Text = "eşle", InteractionType = "match", InteractionSolution = """{"l1":"r1"}""", SolutionSource = SolutionSource.Human,
        };
        ctx.AddRange(activity, test, clean, student, owner, pending, approved);
        await ctx.SaveChangesAsync();
        ctx.TestQuestions.AddRange(
            new WorksheetQuestion { TestId = activity.Id, QuestionId = pending.Id, Order = 1 },
            new WorksheetQuestion { TestId = activity.Id, QuestionId = approved.Id, Order = 2 },
            // Aynı onaysız soru "Havuzdan ekle" ile bir test sayfasına da bağlanmış (review C2: kapı tür fark etmez).
            new WorksheetQuestion { TestId = test.Id, QuestionId = pending.Id, Order = 1 },
            new WorksheetQuestion { TestId = clean.Id, QuestionId = approved.Id, Order = 1 });
        await ctx.SaveChangesAsync();
        return new World(grade.Id, student.Id, activity.Id, test.Id, pending.Id, clean.Id);
    }

    private async Task ApproveAsync(int questionId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Questions.Where(q => q.Id == questionId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.SolutionStatus, SolutionStatus.Approved));
    }

    private async Task<ResponseBaseDto> AssignAsync(int worksheetId, int studentId)
    {
        await using var ctx = _db.NewContext();
        var service = new WorksheetAssignmentService(ctx, new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));
        return await service.AssignAsUserAsync(ctx, new WorksheetAssignmentRequestDto
        {
            WorksheetId = worksheetId, StudentId = studentId, StartAt = Start,
        }, OwnerUserId);
    }

    [Fact]
    public async Task Assign_ActivityWithPendingSolution_IsRefused_UntilApproved()
    {
        var w = await SeedAsync();

        var refused = await AssignAsync(w.ActivityId, w.StudentId);
        refused.Success.ShouldBeFalse();
        refused.Message.ShouldContain("onaylanmadan");
        // Review C2: tür fark etmez — aynı onaysız soru test sayfasında da atamayı engeller.
        (await AssignAsync(w.TestId, w.StudentId)).Success.ShouldBeFalse();
        (await AssignAsync(w.CleanTestId, w.StudentId)).Success.ShouldBeTrue();

        await ApproveAsync(w.PendingQuestionId);
        (await AssignAsync(w.ActivityId, w.StudentId)).Success.ShouldBeTrue();
        (await AssignAsync(w.TestId, w.StudentId)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Assign_DeletedPendingQuestionDoesNotBlock()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var q = await ctx.Questions.AsTracking().SingleAsync(x => x.Id == w.PendingQuestionId);
            ctx.Questions.Remove(q); // soft-delete
            await ctx.SaveChangesAsync();
        }

        (await AssignAsync(w.ActivityId, w.StudentId)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Start_ActivityWithPendingSolution_IsForbidden_ExistingSessionStillOpens()
    {
        var w = await SeedAsync();
        var student = new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId };

        await using (var ctx = _db.NewContext())
        {
            var ex = await Should.ThrowAsync<UnauthorizedAccessException>(() => new TestSessionService(ctx).StartTestAsync(w.ActivityId, student));
            ex.Message.ShouldContain("onay");
            await Should.ThrowAsync<UnauthorizedAccessException>(() => new TestSessionService(ctx).StartTestAsync(w.TestId, student));
        }
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).StartTestAsync(w.CleanTestId, student)).Success.ShouldBeTrue();

        await ApproveAsync(w.PendingQuestionId);
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).StartTestAsync(w.ActivityId, student)).Success.ShouldBeTrue();

        // Oturum açıldıktan sonra çözüm yeniden onay beklese de öğrenci kendi oturumuna döner.
        await using (var ctx = _db.NewContext())
        {
            await ctx.Questions.Where(q => q.Id == w.PendingQuestionId)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.SolutionStatus, SolutionStatus.Pending));
        }
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).StartTestAsync(w.ActivityId, student)).ShouldNotBeNull();
    }

    [Fact]
    public async Task DiscoverList_HidesPendingActivity_UnlessTheStudentAlreadyHasASession()
    {
        var w = await SeedAsync();
        var student = new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId };
        var filter = new ExamFilterDto { pageNumber = 1, pageSize = 10 };

        async Task<List<string>> NamesAsync()
        {
            await using var ctx = _db.NewContext();
            var exams = new ExamService(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), Substitute.For<IAuthApiClient>());
            return (await exams.GetWorksheetsForStudentsAsync(filter, student)).Items.Select(i => i.Name).OrderBy(n => n).ToList();
        }

        (await NamesAsync()).ShouldBe(["Temiz"]);

        await ApproveAsync(w.PendingQuestionId);
        (await NamesAsync()).ShouldBe(["Etkinlik", "Temiz", "Test"]);

        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).StartTestAsync(w.ActivityId, student)).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Questions.Where(q => q.Id == w.PendingQuestionId)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.SolutionStatus, SolutionStatus.Pending));
        }
        // Etkinlikte oturumu olan öğrenci onu görmeye devam eder; oturumu olmadığı test sayfası yine gizlenir.
        (await NamesAsync()).ShouldBe(["Etkinlik", "Temiz"]);
    }

    [Fact]
    public async Task PracticePool_ExcludesPendingSolutionQuestions()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Worksheets.ExecuteUpdateAsync(s => s.SetProperty(x => x.StudentVisibility, WorksheetStudentVisibility.Normal));
            // Havuz doğru şıkka dayanır: iki soruya da doğru şık ver.
            foreach (var q in await ctx.Questions.AsTracking().ToListAsync())
            {
                var answer = new Answer { Text = "a", QuestionId = q.Id };
                ctx.Answers.Add(answer);
                await ctx.SaveChangesAsync();
                q.CorrectAnswerId = answer.Id;
            }
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var ids = await ExamApp.Api.Services.Practice.PracticeQuestionPool.Build(check, w.GradeId, [], []).Select(q => q.Id).ToListAsync();
        ids.ShouldNotContain(w.PendingQuestionId);
        ids.Count.ShouldBe(1);
    }
}
