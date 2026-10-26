using System.Data.Common;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573 (review): etkinlik korumaları —
/// (1) <c>CheckActivityAsync</c> türü oturum kilidinin İÇİNDE yeniden okur: ön kontrolden sonra tür değişirse NotAnActivity,
/// hiçbir satır yazılmaz; (2) sınav sırasında soft-delete edilen etkinlik de kontrol kapısına tabidir (tür filtresiz okunur):
/// kontrolsüz 409 ActivityCheckRequired, kontrolden sonra Completed; (3) canvas sonucu ilk kontrol sayaçlarını yalnız etkinlikte
/// taşır (kontrolsüz null).
/// </summary>
public class WorksheetKindLockAndActivityGuardTests : IDisposable
{
    private const int StudentUserId = 4901;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int InstanceId, int WorksheetId, int McqRowId, int CorrectAnswerId, int WrongAnswerId);

    private async Task<Seed> SeedAsync(WorksheetKind kind)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S4901", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Sayfa", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = kind };
        var question = new Question { Text = "2+2", Point = 10 };
        ctx.AddRange(student, worksheet, question);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = question.Id, Text = "4", Tag = "A" };
        var wrong = new Answer { QuestionId = question.Id, Text = "5", Tag = "B" };
        ctx.Answers.AddRange(right, wrong);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = right.Id;

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), MaxDurationSeconds = 0,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var row = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        ctx.TestInstanceQuestions.Add(row);
        await ctx.SaveChangesAsync();

        return new Seed(instance.Id, worksheet.Id, row.Id, right.Id, wrong.Id);
    }

    private static ActivityCheckRequestDto McqRequest(int rowId, int selected) => new()
    {
        Answers = new List<ActivityCheckAnswerDto> { new() { InstanceQuestionId = rowId, SelectedAnswerId = selected } },
    };

    private async Task<ActivityCheckResultDto> CheckAsync(Seed s, int selected, IInterceptor? interceptor = null)
    {
        await using var ctx = interceptor == null ? _db.NewContext() : _db.NewContext(interceptor);
        return await new TestSessionService(ctx).CheckActivityAsync(s.InstanceId, McqRequest(s.McqRowId, selected), StudentUserId);
    }

    private async Task<TestSessionResultDto> EndTestAsync(Seed s)
    {
        await using var ctx = _db.NewContext();
        return await new TestSessionService(ctx).EndTest(s.InstanceId, StudentUserId);
    }

    private async Task SoftDeleteWorksheetAsync(int worksheetId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Worksheets.IgnoreQueryFilters().Where(w => w.Id == worksheetId)
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.IsDeleted, true));
    }

    // ---------------------------------------------------------------- (1) tür kilidin içinde yeniden okunur

    /// <summary>
    /// Tür ön kontrol ile kilit arasında Activity → Test'e çevrilirse (yarış penceresi) kontrol reddedilir. Kilit yolu, ön
    /// kontrolden sonraki 2. "Worksheets.Kind" okumasında tür değişimini simüle eden bir komut interceptor'ı ile sınanır.
    /// </summary>
    private sealed class FlipKindOnSecondKindRead(int worksheetId) : DbCommandInterceptor
    {
        private int _kindReads;

        private void MaybeFlip(DbCommand command)
        {
            var text = command.CommandText;
            if (!text.Contains("\"Worksheets\"", StringComparison.Ordinal) || !text.Contains("\"Kind\"", StringComparison.Ordinal)
                || !text.Contains("\"TestInstances\"", StringComparison.Ordinal))
                return;
            if (++_kindReads != 2)
                return;

            using var update = command.Connection!.CreateCommand();
            update.Transaction = command.Transaction;
            update.CommandText = $"UPDATE \"Worksheets\" SET \"Kind\" = 0 WHERE \"Id\" = {worksheetId}";
            update.ExecuteNonQuery();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            MaybeFlip(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            MaybeFlip(command);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task CheckActivity_KindChangedBetweenPrecheckAndLock_IsNotAnActivityAndWritesNothing()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        var result = await CheckAsync(s, s.CorrectAnswerId, new FlipKindOnSecondKindRead(s.WorksheetId));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.NotAnActivity);

        await using var check = _db.NewContext();
        var row = await check.TestInstanceQuestions.AsNoTracking().SingleAsync(x => x.Id == s.McqRowId);
        row.SelectedAnswerId.ShouldBeNull();
        row.AnswerRevision.ShouldBe(0);
        var instance = await check.TestInstances.AsNoTracking().SingleAsync(x => x.Id == s.InstanceId);
        instance.CheckCount.ShouldBe(0);
        instance.FirstCheckedAt.ShouldBeNull();
    }

    [Fact]
    public async Task CheckActivity_ActivityWithoutKindChange_StillChecksNormally()
    {
        // Karşı kontrol: interceptor yokken aynı akış başarılıdır (yakalanan şey yarış penceresi, akışın kendisi değil).
        var s = await SeedAsync(WorksheetKind.Activity);

        var result = await CheckAsync(s, s.CorrectAnswerId);

        result.Success.ShouldBeTrue(result.Message);
    }

    // ---------------------------------------------------------------- (2) soft-delete edilen etkinlikte kontrol kapısı

    [Fact]
    public async Task EndTest_ActivityWorksheetSoftDeletedWithoutCheck_IsConflictAndStaysStarted()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        await SoftDeleteWorksheetAsync(s.WorksheetId);

        var result = await EndTestAsync(s);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
        await using var check = _db.NewContext();
        (await check.TestInstances.AsNoTracking().SingleAsync(x => x.Id == s.InstanceId)).Status
            .ShouldBe(WorksheetInstanceStatus.Started);
    }

    [Fact]
    public async Task EndTest_ActivityWorksheetSoftDeletedAfterCheck_CompletesTheInstance()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        (await CheckAsync(s, s.CorrectAnswerId)).Success.ShouldBeTrue();
        await SoftDeleteWorksheetAsync(s.WorksheetId);

        var result = await EndTestAsync(s);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        (await check.TestInstances.AsNoTracking().SingleAsync(x => x.Id == s.InstanceId)).Status
            .ShouldBe(WorksheetInstanceStatus.Completed);
    }

    // ---------------------------------------------------------------- (3) canvas sonucu: ilk kontrol sayaçları

    [Fact]
    public async Task CanvasResult_ActivityAfterCheck_CarriesFirstCheckCorrectAndTotal()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        (await CheckAsync(s, s.CorrectAnswerId)).Success.ShouldBeTrue();

        WorksheetInstanceResultDto? result;
        await using (var ctx = _db.NewContext())
            result = await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, StudentUserId);

        result.ShouldNotBeNull();
        result!.FirstCheckCorrectCount.ShouldBe(1);
        result.FirstCheckTotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task CanvasResult_ActivityWithoutCheck_FirstCheckCountsAreNull()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        WorksheetInstanceResultDto? result;
        await using (var ctx = _db.NewContext())
            result = await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, StudentUserId);

        result.ShouldNotBeNull();
        result!.FirstCheckCorrectCount.ShouldBeNull();
        result.FirstCheckTotalCount.ShouldBeNull();
    }

    [Fact]
    public async Task CanvasResult_TestWithStoredFirstCheckValues_ReportsNullBecauseOnlyActivitiesExposeThem()
    {
        var s = await SeedAsync(WorksheetKind.Test);
        await using (var seed = _db.NewContext())
            await seed.TestInstances.Where(ti => ti.Id == s.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.FirstCheckCorrectCount, (int?)3).SetProperty(ti => ti.FirstCheckTotalCount, (int?)5));

        WorksheetInstanceResultDto? result;
        await using (var ctx = _db.NewContext())
            result = await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, StudentUserId);

        result.ShouldNotBeNull();
        result!.FirstCheckCorrectCount.ShouldBeNull();
        result.FirstCheckTotalCount.ShouldBeNull();
    }
}
